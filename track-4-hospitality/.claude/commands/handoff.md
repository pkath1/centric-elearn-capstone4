---
description: Write a session handoff (decisions, progress, state, next steps) so a new session continues without re-reading the code
allowed-tools: Read, Write, Bash(git status:*), Bash(git log:*), Bash(git branch:*), Bash(git diff:*)
---

# /handoff

Write a handoff file so a brand-new session can continue with no lost context and no need to re-explore the repo.

Argument: `$ARGUMENTS` (optional note to include under "Goal")

## Steps

1. Run `git branch --show-current`, `git status --short`, `git log --oneline -10` and `git diff --stat`. If the repo disagrees with what the conversation claims, trust the repo and note the difference.
2. Review this whole conversation: the user's requests, the decisions they made, answers to questions, plans approved or rejected, files created or edited, commands run, errors and fixes.
3. Write the handoff to `docs/handoff/latest.md` (overwrite) and to a dated copy `docs/handoff/handoff-<YYYY-MM-DD-HHmm>.md`. Create `docs/handoff/` if missing.

## Format

Keep it short, about 150 lines at most. Repo-relative paths. Self-contained: no "as discussed above".

1. **Header**: date, branch, one-line goal.
2. **Start here**: a paste-ready opening message for the next session, for example "Read @docs/handoff/latest.md, then continue from 'Next steps'."
3. **Repo orientation** (so the next session skips exploration):
   - Stack: .NET 8 MVC controllers, EF Core on MySQL, DbUp migrations in `backend/db/migrations` (never edit applied ones, add the next `V00n`), xUnit + Moq, React 19 + Vite + TypeScript.
   - Code: `backend/Larchmont.Api` (Domain, Services, Repositories, Controllers, Validators, Dtos, Mappers), `backend/Larchmont.Tests`, `frontend/src`.
   - Entities are `Room`, `Booking`, `User`. Dates are stored UTC.
   - `CLAUDE.md` may be stale; check whether capstone Task 1 has corrected it.
4. **Goal and context**: the original ask and how it changed.
5. **Approved decisions**: table of decision | quote from the user | consequence. Include user constraints such as "do not execute yet" or "do not commit".
6. **Proposed, NOT approved**: anything suggested but not confirmed.
7. **Progress**: done / in progress / not started, with file paths.
8. **Current state**: branch, uncommitted files, test and build status, running processes and ports, env var names that matter (names only, never values).
9. **Problems and fixes**: error, cause, resolution, and dead ends not to repeat.
10. **Next steps**: ordered, with the very first action stated explicitly.
11. **Key files**: path plus one-line purpose.

## Rules

- Never invent approvals. Record only what the user actually said, quoting their words. Keep "approved" and "proposed" separate.
- Redact secrets: no passwords, API keys, tokens or connection strings, in any section.
- Do not commit the file. Change nothing else.

## Report

Print the output path and a summary of at most 5 lines. Do not paste the whole file. Mention that `docs/handoff/` can be added to `.gitignore` if the user does not want it tracked.
