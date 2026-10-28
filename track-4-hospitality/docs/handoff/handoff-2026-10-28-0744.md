# Handoff 2026-10-28 07:44 | branch feature/housekeeping-board | Goal: Track 4 capstone (housekeeping board). Phases 0-11 done and merged on the feature branch. Left: user decisions (credential rotation, live DB, merge to main).

## Start here
Paste: "Read @docs/handoff/latest.md, then continue from 'Next steps'. Do not re-explore code."
Role: you are the ORCHESTRATOR. Subagents (Agent tool, `model: haiku`, project agents in `.claude/agents/`) work in hand-made git worktrees; you brief, gate, merge. Caveman and ponytail skills are active (SessionStart hook).

## Repo orientation
- Git root is `centric-elearn-capstone4`; project is `track-4-hospitality/`. Stack: .NET 8 MVC + EF Core + MySQL, DbUp migrations in `backend/db/migrations` (V001-V005 applied, never edit them; next is V006), xUnit/Moq/InMemory, React 19 + Vite + TS. Dates UTC. `CLAUDE.md` is correct and current.
- Backend `backend/Larchmont.Api` (Domain, Services, Repositories, Controllers, Validators, Dtos, Infrastructure); tests `backend/Larchmont.Tests` (201 pass); MCP server `mcp/Larchmont.Mcp` + `mcp/tests` (95 pass, no DB needed); frontend `frontend/src`.
- Spec (source of truth): `docs/specs/housekeeping-board.md` (45 ACs, 12 open questions for the stakeholder). Plans: `C:\Users\labuser\.claude\plans\` (outside the repo).

## Goal and context
Deliver every item in `DELIVERABLES.md` (9 tasks in `../CAPSTONE.md`): spec first, parallel agents, MCP mandatory, no extras, no committed secrets. Nothing merges to `main` without the user's approval.

## Approved decisions
| Decision | User words | Consequence |
|---|---|---|
| Proceed on spec defaults | "ok to procced on defaults with an explaination why" | Defaults are unconfirmed assumptions. |
| MCP in .NET | chose ".NET" | `mcp/Larchmont.Mcp` |
| Scratch DB only | "Use scratch db only with user root and password..." | `larchmont_scratch`; live `larchmont` untouched |
| Root creds in env file | "you can store root user and its password in env file" | `~/.larchmont-root.env`, `~/.larchmont-mcp.env` (outside repo) |
| Lock stamp: option A | "go ahead with option A" | open checkout stamp may move earlier, with a history row |
| Commit often | "keep comitting all changes to my branch as you make" | commit after every merge |
| Phase 7 | "GO" | LH-56 fixed, merged |
| Phase 8 | "GO" | importer characterised and refactored, merged |
| MCP first | "deal with mcp first" | MCP fixed (see Problems) |
| Phase 9 | "Start phase 9" | tests hardened, merged |
| Remove root deny rules | "1." then the user's exact edit instruction | removed, then RESTORED at phase 10 start; settings.json now has them again |
| Root-check on live DB | "yes" / "yes go" | read-only check only; live has no V005; nothing applied |
| Phase 10 | "mcp is up now proceed with next phase" | review, CI/compose/Swagger fixes, merged |
| Push | "commit and push changes to my branch aftert his" | `feature/housekeeping-board` pushed to origin (not forced) |
| Phase 11 | "start phase 11 once done commit and push to same branch" | docs written; commit and push the feature branch only |

## Proposed, NOT approved
- Rotating committed credentials (see Next steps) and purging git history: proposed, NOT approved, nothing done.
- Applying V005 to the live `larchmont` database and installing MCP views there: offered as an option, NOT chosen.
- A V006 migration (DATETIME(0) vs datetime(6); `rooms.status` default Dirty): recommended for later, NOT approved.
- Fixes for review findings R1-R13 and S1-S7 (`docs/review-report.md`): NOT started; most need a spec or user decision.
- Opening a pull request or merging to `main`: NOT requested.

## Progress
- Done and merged into the feature branch: P0 setup, P1 onboarding and Claude config, P2 spec, P3 V005 schema, P4 MCP server, P5 feature (rules, API, frontend, tests), P6 perf/concurrency (`docs/perf-note.md`), P7 LH-56 (`docs/debug-report.md`), P8 importer (`docs/refactor-note.md`), P9 tests (`docs/test-report.md`), P10 review (`docs/review-report.md`), P11 documents: `docs/agent-map.md`, `docs/prompt-journal.md` (26 entries, rebuilt from handoffs and the session), `docs/ai-usage-reflection.md`, CI `mcp` job, checklist table, `DELIVERABLES.md` ticked (all but one).
- Not ticked in `DELIVERABLES.md`: `compose.yaml` "complete and free of secrets": it is secret-free but has only a MySQL service.
- Not started: all open review findings; credential rotation; history purge.

## Current state
- Branch feature/housekeeping-board, last merge `1cecec5` (phase 11). Pushed up to `d30a028`; phase 11 merge and this handoff are committed and pushed together at the end of this session (check `git status -sb` for ahead/behind).
- Uncommitted: nothing meaningful. `docs/overview.html` is untracked and was not created by the agent; ask the user whether to add it.
- Build clean; `cd backend && dotnet test Larchmont.sln` 201 pass; `dotnet test mcp/Larchmont.Mcp.sln` 95 pass. Frontend build last run in earlier phases.
- Worktrees (not needed any more, can be removed with `git worktree remove`): `C:\Users\labuser\wt6\perf`, `wt7\lh56`, `wt8\importer`, `wt9\tests`, `wt10\review`, `wt11\docs`.
- MCP: `.mcp.json` runs `cmd /c mcp\run-mcp.cmd` -> `run-mcp.sh` -> loads gitignored `mcp/.env` (scratch DB, user `larchmont_mcp`, hotel 1). The user confirmed `larchmont-db` is connected. Old `mcp/.env` backed up at `~/mcp-dotenv.backup`.
- Env var names: `ConnectionStrings__Larchmont`, `LARCHMONT_MCP_CONNECTION`, `LARCHMONT_MCP_HOTEL_ID`, `Lock__ApiKey`, `MYSQL_ROOT_PASSWORD` (compose). Never print values.

## Problems and fixes
- MCP failed to connect (two causes): (1) env vars not in Claude Code's environment, fixed by `mcp/.env` loaded by the launcher; (2) `'bash' is not recognized` because Claude Code starts servers without Git Bash on PATH, fixed by `mcp/run-mcp.cmd`. Hand tests inside Git Bash hid cause 2.
- `mcp/.env` had pointed at live `larchmont` with a different password: queries failed. Now scratch.
- The MCP server refuses the root account by design. A request to use root was not followed; the root password the user gave (name not stored here) was rejected by MySQL.
- Live `larchmont` has only V001-V004 tables; the MCP views need V005. Do not apply V005 to live without a fresh yes.
- Edit of `.claude/settings.json` was blocked once by the auto-mode safety check; it worked after the user's explicit instruction. A subagent that would read raw session transcripts was blocked (they contain credentials); the journal was built from handoffs instead.
- `isolation: worktree` on the Agent tool fails: create worktrees by hand with `git worktree add -b phase/<n> <dir> feature/housekeeping-board`. A subagent is stuck in plan mode.
- Git pushes can wait on a GitHub sign-in prompt; run them in the background and wait.
- CI cannot run from this repo layout (workflows must be at the repo root); the workflow is fixed for when `track-4-hospitality/` is the root.

## Next steps
1. FIRST: `git status -sb` and `git log --oneline -3`; confirm the branch is pushed and clean (except `docs/overview.html`).
2. Ask the user about credential rotation (do not act without a yes): `larchmont_app` password (`db-setup/01-create-users.sql`), values in `backend/Larchmont.Api/appsettings.Development.json` (DB, channel API key, SMTP), the old compose root password, then a history purge (git-filter-repo or BFG, rewrites shared history). At the very end also delete `~/.larchmont-root.env` and `~/.larchmont-mcp.env` and rotate the root password.
3. Ask whether to fix the open review findings or leave them documented. Highest value: R1/R2 (out-of-order SLA; needs the stakeholder rule), R4/R5 (500 on concurrent writes), R3, R6; add V006 for R11 and the datetime precision.
4. Ask whether to complete `compose.yaml` (API, frontend services) or leave the last `DELIVERABLES.md` box unticked.
5. Ask whether to open a PR to `main`. Merge to `main` only on the user's approval.
6. Optional: re-run the Phase 7 numbers through the live MCP server (`run_readonly_query` over the views) for the missing MCP evidence in `docs/debug-report.md`; run `npm run build` in `frontend`.

## Key files
- `DELIVERABLES.md` (checklist), `CLAUDE.md`, `docs/specs/housekeeping-board.md`, `docs/review-report.md` (open findings), `docs/test-report.md`, `docs/agent-map.md`, `docs/prompt-journal.md`, `docs/ai-usage-reflection.md`.
- `mcp/Larchmont.Mcp/` (server, `SqlGuard.cs`, `McpConfig.cs`), `mcp/run-mcp.cmd`, `mcp/run-mcp.sh`, `.mcp.json`, `db-setup/02-mcp-views.sql`, `db-setup/03-create-mcp-user.sql`, `docs/mcp-security.md`.
- `backend/Larchmont.Api/Services/{RoomStatusPolicy,LockIntegrationService,HousekeepingServices,HotelServices,BookingService,ReportService,ChannelImportService}.cs`, `db/migrations/V005__housekeeping_board.sql`.
- `.claude/settings.json` (hooks, deny rules), `.claude/agents/*`, `.claude/skills/*`, `.github/workflows/ci.yml`, `compose.yaml`.
