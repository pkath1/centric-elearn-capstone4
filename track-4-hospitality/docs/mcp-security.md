# MCP server security: Larchmont housekeeping and occupancy

Status: `02-mcp-views.sql` (with the hotel id substituted) has been applied to `larchmont_scratch` and all seven views query. `03-create-mcp-user.sql` has not been run (no root available to the author); the coordinator applies it, so the grants and the least-privilege user are not yet tested against a server.

## What it is

`mcp/Larchmont.Mcp` is a read-only stdio MCP server (.NET 8, official `ModelContextProtocol` package, `MySqlConnector`). It lets an AI assistant answer housekeeping and occupancy questions for one hotel. It exposes four fixed tools and one guarded free-form query tool:

| Tool | Purpose | Input |
|---|---|---|
| `rooms_breaching_sla` | Open turnarounds past target, worst first | `limit` (1 to 200) |
| `ooo_impact_on_occupancy` | Out-of-order share and bookings on out-of-order rooms | `fromDate`, `toDate` (yyyy-MM-dd) |
| `housekeeper_load` | Rooms per housekeeper per floor, by status | optional `floor` |
| `arrivals_without_ready_room` | Today's confirmed arrivals whose room is not Ready | none |
| `run_readonly_query` | One guarded SELECT over the `v_mcp_*` views | `sql` |

## Threat model

Assets: guest names, contact details and addresses; staff contact details and password hashes; free-text notes; the audit trail; the availability of the database.

Actors and threats:
1. **The AI assistant, following a prompt injection** (for example text inside a booking or note asks it to dump data). It can send any SQL the tool accepts.
2. **A mistaken or over-broad query** (for example a `SELECT *` over a large table, or a long-running join).
3. **A leaked connection string or password** (from a shell history, a committed file, or a log).
4. **Someone with the MCP user's credentials** connecting directly to MySQL, outside the server.
5. **Cross-hotel reads**: the assistant asks about a hotel other than the one it is scoped to.

Out of scope for this design: the operating system account that runs the server, the MySQL server itself, and network-level controls.

## Controls and where each is enforced

Database grants are the first line. The server guard is the second. Neither is trusted alone.

| # | Control | Enforced in | What it stops |
|---|---|---|---|
| C1 | The MCP user has SELECT on the seven `v_mcp_*` views only, and no grant on any base table, `users` or credentials | Database: `03-create-mcp-user.sql` | Reading base tables, staff credentials, or anything not in a view, even if the guard is bypassed |
| C2 | Views omit personal columns and free-text notes (no guest name, email, phone, address, staff email, password hash, `last_login_at`, `notes`, `reason`, `ooo_reason`) | Database: `02-mcp-views.sql` | Personal data leaving the database through any view |
| C3 | Views run with the definer's privileges (`SQL SECURITY DEFINER`), so the MCP user needs no base-table grants | Database: `02-mcp-views.sql` | The need to grant base tables to make views work |
| C4 | Hotel scope: every view has `WHERE hotel_id = <id>`, with the id substituted into the view definitions at install time by `sed` (the file must not be run unsubstituted). The clients supply no hotel and cannot change it | Database: `02-mcp-views.sql` | Cross-hotel reads, including from `run_readonly_query` |
| C5 | Audit: the MCP user can INSERT into `mcp_query_audit` and cannot read, update or delete it | Database: `03-create-mcp-user.sql` | Silent use. Every call, including refused ones, leaves a row |
| C6 | Account limits: `MAX_QUERIES_PER_HOUR 1000`, `MAX_USER_CONNECTIONS 4` | Database: `03-create-mcp-user.sql` | Runaway loops and connection exhaustion |
| C7 | Single statement, read-only: one statement, only `SELECT` or `WITH ... SELECT` | Server: `SqlGuard` | Writes, DDL, stacked statements |
| C8 | Comments refused (`--`, `#`, `/* */`, `/*!`) | Server: `SqlGuard` | A comment hiding a second statement or a payload |
| C9 | Allowlist of names: only `v_mcp_*` views, their columns, CTE names, aliases, and a short list of functions. Base tables, other schemas (`information_schema`, `mysql`, `performance_schema`, `sys`, `db.table`) are refused | Server: `SqlGuard` | Reading base tables or other databases |
| C10 | Personal-data denylist by column name, checked even if a view later exposed the column | Server: `SqlGuard`, `McpSchema.PersonalColumns` | Requests for personal columns |
| C11 | Refused: `INTO` (OUTFILE, DUMPFILE, variables), `LOAD_FILE`, `SLEEP`, `BENCHMARK`, locks, `FOR UPDATE`, `SET`, `CALL`, `GRANT`, `SHOW`, `EXPLAIN`, `USE` | Server: `SqlGuard` | File write and read, delays, lock abuse, admin statements |
| C12 | User variables (`@`), placeholders (`?`), backticks, backslashes and `:` are refused | Server: `SqlGuard` | Using session state (user variables), placeholders and escape tricks to alter a statement's meaning |
| C13 | Length cap of 4000 characters | Server: `SqlGuard` | Very large payloads |
| C14 | Row cap: at most 200 rows read; the cursor is cancelled at the cap and `truncated` is reported | Server: `QueryRunner` | Bulk export through the tool |
| C15 | Timeout: 5 seconds (`CommandTimeout`) and `max_execution_time` 5000 ms set on the session | Server: `QueryRunner`, plus the database | Long-running and resource-heavy queries |
| C16 | Audit row per call: tool, `USER()`, hotel, statement (truncated), row count, outcome, detail, UTC time. Successful calls fail closed if the audit row cannot be written | Server: `QueryRunner` | Silent use and unaudited data |
| C17 | Startup refuses to run without `LARCHMONT_MCP_CONNECTION` or `LARCHMONT_MCP_HOTEL_ID`, and refuses a `root` account | Server: `McpConfig` | Running with the wrong or an over-privileged account |
| C18 | Secrets never printed: the connection string is redacted in `ToString()`, and errors name variables or exception types, never driver messages | Server: `McpConfig`, `QueryRunner` | Credentials in logs or in tool output |
| C19 | stdout is reserved for the protocol. Logs go to stderr at Warning and above | Server: `Program.cs` | Log lines corrupting the protocol |
| C20 | The connection string is never committed. `.mcp.json` passes variable names only (`${LARCHMONT_MCP_CONNECTION}`), so values come from the developer's environment | `.mcp.json`, environment | Secrets in the repository |
| C21 | Fixed SQL for the four business tools. Clients never supply SQL there; the only client input is typed and bounded | Server: `HousekeepingTools` | Injection through the business tools |

## Deliberately not allowed

- Any write, DDL, or privilege change, through any tool or path.
- Base tables, `users`, credentials, `mcp_query_audit` (no read), other schemas and other databases.
- Personal data: guest names and contact details, addresses, staff emails and display names, password hashes, login times, notes, and free-text reasons.
- Reading files, writing files, sleeping, benchmarking, taking locks, and user variables.
- More than one hotel per server process. The hotel is fixed by configuration.
- Any tool that acts on the hotel (check-in, assignment, status changes). The MCP surface is read-only.

## How to run

1. Apply the migrations (the API does this on start). Then, as an administrator:
   - Run `02-mcp-views.sql` with the hotel id substituted. The file must not be run unsubstituted (the placeholder `__MCP_HOTEL_ID__` is a syntax error): `sed "s/__MCP_HOTEL_ID__/1/g" db-setup/02-mcp-views.sql | mysql -h 127.0.0.1 -P 3306 -u ADMIN -p larchmont`. Use the hotel's `hotels.id` in place of `1`, and the same command with `larchmont_scratch` for the scratch copy.
   - Run `db-setup/03-create-mcp-user.sql` with the placeholders substituted at run time. The header of that file shows the Git Bash command. The password is read silently and never written to disk.
   - Check the grants: `SHOW GRANTS FOR 'larchmont_mcp'@'%';`
2. On the developer machine, set environment variables (names as in `.mcp.json`):
   - `LARCHMONT_MCP_CONNECTION`: a MySQL connection string for `larchmont_mcp`, set in the shell or the user's environment, never in a file in the repo.
   - `LARCHMONT_MCP_HOTEL_ID`: the same `hotels.id` used in step 1. It is written to the audit log only. It does not scope queries, so it must match the installed views.
3. Start the server directly to check the settings: `dotnet run --project mcp/Larchmont.Mcp`. With no variables set, it prints the names of the missing variables and exits with code 2.
4. Claude Code reads `.mcp.json` and starts the server over stdio.

## Tests

- `cd mcp && dotnet test Larchmont.Mcp.sln`: about 95 cases, with no database. They cover every refusal listed above, the positive cases for valid SELECTs on views, the allowlist invariants (no view exposes a personal column, no view is a base table), and the configuration checks.
- The database-backed tests are not written. The tests above cover the guard without a database.

## Known limits

1. **The guard is a tokenizer with allowlists, not a SQL parser.** It is strict and refuses what it does not recognise. That causes false refusals: for example, an alias that has the same name as a base table is refused. A bypass that a parser-based guard would stop is still possible in principle, which is why the database grants (C1) are the first line.
2. **One hotel per install, and the install is the scope.** The hotel id is in the view definitions. A second hotel needs a second install (or a re-run with another id, which replaces the views for every client of the database). `LARCHMONT_MCP_HOTEL_ID` is not checked against the views; a mismatch only makes the audit `hotel_id` wrong.
3. **The views were verified only on a scratch database, as the admin account, not as `larchmont_mcp`.** The grants in `03` have not been run.
4. **Time.** The views use `UTC_TIMESTAMP()` and `UTC_DATE()`, matching the UTC timestamps the spec stores. No server time zone is used.
5. **Occupancy is at booking level.** `ooo_impact_on_occupancy` counts bookings overlapping a window, not nights. Out-of-order status is the room's current status, not its status on each night. Ticket LH-56 (the night-counting error in the report) is a separate fix.
6. **One database user for every caller.** The audit row records `larchmont_mcp@host`, not the person or assistant who asked. The `tool` column and statement text are the only caller detail.
7. **Audit failure on refused calls is logged, not fatal.** If the audit row cannot be written for a refused statement, the refusal is still returned and the failure is logged by exception type on stderr. A successful call with no audit row returns no data.
8. **Row cap reads, it does not limit scanning.** The database may scan many rows for a query that returns 200. The 5-second timeout is the bound on that cost.
9. **`MAX_QUERIES_PER_HOUR` is shared.** One busy assistant session can use up the hourly allowance for all clients of the account.
10. **Password in the environment.** The connection string is held in the process environment, which other processes of the same user can read. Use a least-privilege account and rotate it if it leaks.
11. **Grant host is `'%'`.** Restrict it to the server's host in production.
12. **Definer.** The views run as the account that created them. That account should be a managed administrator, not `root`, and it should not be used by applications.
13. **Not yet covered.** There is no `permissions` deny rule or hook in this change that blocks write SQL typed into the Bash tool. That belongs in `.claude/settings.json`, which is outside this change.
