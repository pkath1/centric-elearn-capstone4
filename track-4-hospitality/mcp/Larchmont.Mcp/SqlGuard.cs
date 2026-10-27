namespace Larchmont.Mcp;

/// <summary>Result of <see cref="SqlGuard.Check"/>. Reason is safe to show to the client.</summary>
public sealed record GuardResult(bool Allowed, string Reason)
{
    public static readonly GuardResult Accepted = new(true, string.Empty);

    public static GuardResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// Second line of defence for run_readonly_query. The first line is the database grants (SELECT on views only).
/// This guard is a tokenizer plus allowlists. It is deliberately strict and refuses anything it does not recognise.
/// It has no database dependency, so it is unit-tested directly.
/// </summary>
public static class SqlGuard
{
    public const int MaxStatementLength = 4000;

    private enum TokenKind { Word, Number, Punct, String }

    private readonly record struct Token(TokenKind Kind, string Text)
    {
        public bool IsWord(string word) => Kind == TokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

        public bool IsPunct(string punct) => Kind == TokenKind.Punct && Text == punct;
    }

    private static readonly HashSet<string> DeniedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "REPLACE", "DROP", "CREATE", "ALTER", "TRUNCATE", "RENAME",
        "GRANT", "REVOKE", "CALL", "SET", "LOAD", "DO", "HANDLER", "PREPARE", "EXECUTE", "DEALLOCATE",
        "FLUSH", "LOCK", "UNLOCK", "KILL", "SHUTDOWN", "INTO", "OUTFILE", "DUMPFILE", "LOAD_FILE",
        "SLEEP", "BENCHMARK", "GET_LOCK", "RELEASE_LOCK", "IS_FREE_LOCK", "IS_USED_LOCK", "USE", "SHOW",
        "EXPLAIN", "DESCRIBE", "ANALYZE", "OPTIMIZE", "REPAIR", "FOR", "SHARE", "MODE", "PROCEDURE",
        "INSTALL", "UNINSTALL", "RESET", "PURGE", "BINLOG", "CHANGE", "START", "STOP", "BEGIN", "COMMIT",
        "ROLLBACK", "SAVEPOINT", "XA", "SIGNAL", "RESIGNAL",
    };

    private static readonly HashSet<string> OtherSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        "information_schema", "mysql", "performance_schema", "sys",
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN", "JOIN", "LEFT",
        "RIGHT", "INNER", "OUTER", "CROSS", "ON", "USING", "AS", "GROUP", "BY", "ORDER", "HAVING", "LIMIT",
        "OFFSET", "ASC", "DESC", "DISTINCT", "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END", "WITH",
        "RECURSIVE", "TRUE", "FALSE", "INTERVAL", "DAY", "MINUTE", "HOUR", "EXISTS", "ANY", "SOME",
        "SIGNED", "UNSIGNED", "DECIMAL", "INT", "DATETIME", "DATE", "CHAR",
    };

    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "MIN", "MAX", "AVG", "COALESCE", "IFNULL", "NULLIF", "IF", "CAST", "TIMESTAMPDIFF",
        "DATEDIFF", "CURDATE", "UTC_TIMESTAMP", "UTC_DATE", "NOW", "CONCAT", "UPPER", "LOWER", "ROUND", "ABS",
        "GREATEST", "LEAST", "CHAR_LENGTH", "DATE_ADD", "DATE_SUB",
    };

    /// <summary>Checks one statement. Returns Accepted only for a single read-only SELECT over whitelisted views.</summary>
    public static GuardResult Check(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject("The statement is empty.");
        }

        if (sql.Length > MaxStatementLength)
        {
            return GuardResult.Reject($"The statement is longer than {MaxStatementLength} characters.");
        }

        var (tokens, lexError) = Lex(sql);
        if (lexError is not null)
        {
            return GuardResult.Reject(lexError);
        }

        // One statement only. A semicolon is allowed only as the very last token.
        var semicolons = tokens.Count(t => t.IsPunct(";"));
        if (semicolons > 0)
        {
            if (semicolons > 1 || !tokens[^1].IsPunct(";"))
            {
                return GuardResult.Reject("Multiple statements are not allowed. Send exactly one statement.");
            }

            tokens.RemoveAt(tokens.Count - 1);
        }

        if (tokens.Count == 0)
        {
            return GuardResult.Reject("The statement is empty.");
        }

        if (!(tokens[0].IsWord("SELECT") || tokens[0].IsWord("WITH")) || !tokens.Any(t => t.IsWord("SELECT")))
        {
            return GuardResult.Reject("Only SELECT (or WITH ... SELECT) is allowed.");
        }

        // Names the statement introduces itself: CTE names (name AS (...)) and aliases.
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.Word && tokens[i + 1].IsWord("AS") && tokens[i + 2].IsPunct("("))
            {
                cteNames.Add(tokens[i].Text);
            }
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Word)
            {
                continue;
            }

            if (token.IsWord("AS") && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Word)
            {
                aliases.Add(tokens[i + 1].Text);
            }
            else if (i > 0 && tokens[i - 1].IsPunct(")"))
            {
                aliases.Add(token.Text);
            }
            else if (i > 0 && tokens[i - 1].Kind == TokenKind.Word
                     && (McpSchema.ViewNames.Contains(tokens[i - 1].Text) || cteNames.Contains(tokens[i - 1].Text))
                     && !Keywords.Contains(token.Text))
            {
                aliases.Add(token.Text);
            }
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Word)
            {
                continue;
            }

            var upper = token.Text.ToUpperInvariant();
            var next = i + 1 < tokens.Count ? tokens[i + 1] : (Token?)null;

            if (DeniedWords.Contains(upper))
            {
                return GuardResult.Reject($"'{upper}' is not allowed. Only read-only SELECT statements over the mcp views are accepted.");
            }

            if (McpSchema.PersonalColumns.Contains(token.Text))
            {
                return GuardResult.Reject($"'{token.Text}' holds personal data or free text and cannot be queried.");
            }

            if (OtherSchemas.Contains(token.Text))
            {
                return GuardResult.Reject($"Other schemas are not allowed ('{token.Text}').");
            }

            if (next is { } dot && dot.IsPunct(".")
                && i + 3 < tokens.Count && tokens[i + 2].Kind == TokenKind.Word && tokens[i + 3].IsPunct("."))
            {
                return GuardResult.Reject("Three-part names (schema.table.column) are not allowed.");
            }

            if (next is { } open && open.IsPunct("("))
            {
                if (Keywords.Contains(upper) || AllowedFunctions.Contains(upper))
                {
                    continue;
                }

                return GuardResult.Reject($"Function '{token.Text}' is not allowed.");
            }

            if (McpSchema.BaseTables.Contains(token.Text))
            {
                return GuardResult.Reject($"Base table '{token.Text}' is not allowed. Query the mcp views instead (v_mcp_*).");
            }

            if (upper is "FROM" or "JOIN")
            {
                if (next is not { } target)
                {
                    return GuardResult.Reject("FROM must be followed by a view name.");
                }

                if (target.IsPunct("("))
                {
                    continue;
                }

                var isView = target.Kind == TokenKind.Word
                             && (McpSchema.ViewNames.Contains(target.Text) || cteNames.Contains(target.Text));
                if (!isView)
                {
                    return GuardResult.Reject("Only the mcp views (v_mcp_*) may follow FROM or JOIN.");
                }

                continue;
            }

            if (Keywords.Contains(upper) || AllowedFunctions.Contains(upper))
            {
                continue;
            }

            if (McpSchema.ViewNames.Contains(token.Text) || McpSchema.ViewColumns.Contains(token.Text)
                || cteNames.Contains(token.Text) || aliases.Contains(token.Text))
            {
                continue;
            }

            return GuardResult.Reject($"'{token.Text}' is not an allowed name. Only the mcp views and their columns may be used.");
        }

        return GuardResult.Accepted;
    }

    private static (List<Token> Tokens, string? Error) Lex(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        var n = sql.Length;

        char Peek(int index) => index < n ? sql[index] : '\0';

        while (i < n)
        {
            var c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                var j = i + 1;
                var closed = false;
                while (j < n)
                {
                    var d = sql[j];
                    if (d == '\\')
                    {
                        return (tokens, "Backslash escapes are not allowed.");
                    }

                    if (d == c)
                    {
                        if (j + 1 < n && sql[j + 1] == c)
                        {
                            j += 2;
                            continue;
                        }

                        closed = true;
                        break;
                    }

                    j++;
                }

                if (!closed)
                {
                    return (tokens, "Unterminated string literal.");
                }

                tokens.Add(new Token(TokenKind.String, string.Empty));
                i = j + 1;
                continue;
            }

            switch (c)
            {
                case '`':
                    return (tokens, "Quoted identifiers (backticks) are not allowed.");
                case '@':
                    return (tokens, "User variables (@name) are not allowed.");
                case '?':
                    return (tokens, "Placeholders (?) are not allowed.");
                case '\\':
                    return (tokens, "Backslashes are not allowed.");
                case '#':
                    return (tokens, "Comments are not allowed.");
                case ':':
                    return (tokens, "':' is not allowed.");
                case '$':
                    return (tokens, "'$' is not allowed.");
                case '-' when Peek(i + 1) == '-':
                case '/' when Peek(i + 1) == '*':
                case '*' when Peek(i + 1) == '/':
                    return (tokens, "Comments are not allowed.");
            }

            if (IsAsciiLetter(c) || c == '_')
            {
                var j = i + 1;
                while (j < n && (IsAsciiLetter(sql[j]) || IsAsciiDigit(sql[j]) || sql[j] == '_'))
                {
                    j++;
                }

                tokens.Add(new Token(TokenKind.Word, sql[i..j]));
                i = j;
                continue;
            }

            if (IsAsciiDigit(c))
            {
                var j = i + 1;
                while (j < n && (IsAsciiDigit(sql[j]) || sql[j] == '.'))
                {
                    j++;
                }

                if (j < n && (IsAsciiLetter(sql[j]) || sql[j] == '_'))
                {
                    return (tokens, "Malformed number.");
                }

                tokens.Add(new Token(TokenKind.Number, sql[i..j]));
                i = j;
                continue;
            }

            if ("(),.=<>!+-*/%|&^~;".IndexOf(c) >= 0)
            {
                tokens.Add(new Token(TokenKind.Punct, c.ToString()));
                i++;
                continue;
            }

            return (tokens, $"Character '{c}' is not allowed.");
        }

        return (tokens, null);
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
