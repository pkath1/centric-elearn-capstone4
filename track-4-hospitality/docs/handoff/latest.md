# Handoff 2026-10-27 ~16:35 | branch feature/housekeeping-board | Goal: Track 4 capstone (housekeeping board). Phases 0-5 merged. Phase 6 finished but NOT gated. Resume: gate Phase 6, then Phase 7.

## Start here
Paste: "Read @docs/handoff/latest.md, then continue from 'Next steps'. Do not re-explore code."
Role: you (Sonnet) are the ORCHESTRATOR. Haiku subagents (Agent tool with `model: haiku`, project agents in `.claude/agents/`) work in hand-made git worktrees; you brief, gate, merge. Caveman and ponytail skills are active (SessionStart hook).

## Repo orientation
- Git root is `centric-elearn-capstone4`; project is `track-4-hospitality/`. Stack: .NET 8 MVC + EF Core + MySQL, DbUp migrations in `backend/db/migrations` (V001-V005 applied, next is V006), xUnit/Moq/InMemory, React 19 + Vite + TS. Dates UTC. `CLAUDE.md` is correct (rewritten in phase 1).
- Backend `backend/Larchmont.Api` (Services, Repositories, Controllers, Validators, Dtos, Infrastructure); tests `backend/Larchmont.Tests` (65 pass on feature branch); MCP server `mcp/Larchmont.Mcp` (+ `mcp/tests`, 95 pass); frontend `frontend/src`.
- Spec (source of truth): `docs/specs/housekeeping-board.md` (45 ACs). Plans: `C:\Users\labuser\.claude\plans\start-frontend-and-backend-sharded-adleman.md` (11 phases) and `go-through-c-users-labuser-claude-plans-deep-hare.md` (changes).
- Known bug LH-56 (phase 7): `Services/ReportService.cs` line ~44 `day <= b.CheckOut` should be `<`; also `BookingRepository.ListOverlappingAsync` `CheckOut >= from`. Expected: Edinburgh 2 Oct 5 rooms 25%; 1-7 Oct 45 nights 32.1% (reports 69 nights 49.3% now); Group 226 nights 26.9%.
- Still to fix (phase 10): CI uses dotnet 6.0.x (needs 8) and `working-directory: web` (is `frontend`); `compose.yaml` has a root password literal; `appsettings.Development.json` has committed secrets (DB, API key, SMTP): move to env and rotate.

## Goal and context
Deliver all of `DELIVERABLES.md` (9 tasks in `../CAPSTONE.md`): spec first, parallel agents, MCP mandatory, no extras, no committed secrets. Nothing merges to `main` without the user's approval.

## Approved decisions
| Decision | User words | Consequence |
|---|---|---|
| Proceed on spec defaults | "ok to procced on defaults with an explaination why" | Sofia never replied; defaults are unconfirmed assumptions (the spec says so) |
| MCP in .NET | chose ".NET" | `mcp/Larchmont.Mcp`, ModelContextProtocol + MySqlConnector |
| Scratch DB | "Use scratch db only with user root and password..." | `larchmont_scratch`; live `larchmont` untouched |
| Root creds in env file | "you can store root user and its password in env file" | `C:\Users\labuser\.larchmont-root.env` (outside repo); MCP creds in `.larchmont-mcp.env` |
| Remove root deny lines once | "remove the two deny lines" | removed for the MCP user script, then restored (deny rules for `mysql -u root` are active) |
| Lock stamp: option A | "go ahead with option A" | a lock event may move an OPEN checkout stamp earlier; writes a history row `LockCheckoutStampMovedEarlier` (AC-45) |
| Commit often | "keep comitting all changes to my branch as you make" | commit after every merge. NOT pushed (origin is github pkath1/centric-elearn-capstone4; ask before pushing) |
| Stop after phase 6 | "stop and commit everything .. start from phase 7 tomorrow" | do NOT start phase 7 until the user says so |

## Proposed, NOT approved
- Nothing pending approval. The 12 open questions for Sofia stay open in the spec.

## Progress
- Done and merged into the feature branch: P0 setup, P1 onboarding and Claude config, P2 spec, P3 V005 schema + stubs, P4 MCP server (gate failed once: views cannot read session variables; fixed with the `__MCP_HOTEL_ID__` placeholder), P5 rules/api/frontend/tests (merge fixes: duplicate `HousekeepingRoles`, `ApiExceptionHandler` now serialises `errors`, Suite tests go through Supervisor), option A.
- FINISHED BUT NOT GATED: Phase 6 (perf and concurrency). Branch `phase/6-perf`, commit `96996ba` (message says WIP/UNVERIFIED), worktree `C:\Users\labuser\wt6\perf`. Agent reports 74 tests pass (65 + 9 new), build clean. Contents: `docs/perf-note.md`, `Larchmont.Tests/PerformanceTests.cs`, breach query bounded to 30 days, floor assignments in one transaction, services now call the FluentValidation validators. Agent numbers (scratch DB, SQL statements per request): rooms list 2 at 20 to 200 rooms (NOT an N+1, no fix needed); board 6 and breaches 4 at 20 and 100 rooms; status race pre-feature code 200/200 in 10 of 10 rounds, now 200/409 in 10 of 10; floor PUT 11 statements without a transaction vs 8 in one transaction. The orchestrator has NOT verified any of this.
- Agent findings NOT fixed: DATETIME(0) columns vs EF datetime(6) (10:00:00.7 is stored as 10:00:01; needs a V006 migration); a check-then-insert race on open turnarounds could give a 500 (not reproduced); validators changed some 400/404/409 codes (lock 400 keys are now PascalCase RoomNumber/HotelId; changedByUserId<=0 or rowVersion<=0 is now 400); floor-assignment rollback on MySQL not triggered.
- Not started: P7 LH-56, P8 importer refactor, P9 tests, P10 review/CI/compose/README, P11 prompt journal + reflection + tick `DELIVERABLES.md` + final handoff.

## Current state
- Branch feature/housekeeping-board; last merged work commit is 4e86fe7 (option A), then handoff commits. Backend 65/65 and MCP 95/95 pass on the feature branch; frontend builds.
- Scratch DB `larchmont_scratch` was changed by the phase 6 agent: 80 rooms added to hotel 1, extra turnarounds, floor assignments, status history rows. It can be rebuilt from V001-V005 (drop and recreate needs root).
- Servers I started (API :5080 against scratch, Vite :5173) die with the desktop. Restart: build `ConnectionStrings__Larchmont` in a shell from the `larchmont_app` password in `db-setup/01-create-users.sql`, db `larchmont_scratch`; `dotnet run --project Larchmont.Api` with `ASPNETCORE_URLS=http://localhost:5080`; `npm run dev` in `frontend`. A leftover API process (pid 7084) may lock the main checkout's binaries; stop it before building.
- Env var names: `ConnectionStrings__Larchmont`, `LARCHMONT_MCP_CONNECTION`, `LARCHMONT_MCP_HOTEL_ID`, `Lock__ApiKey` (unset means the lock endpoint answers 401). Never print values.
- UI test users on scratch: Admin id 2 (all moves), Housekeepers 21/22 at Bath, FrontDesk 20. No Supervisor or Maintenance users exist in the seed.
- Leftover folders (not registered worktrees): `C:\Users\labuser\wt\phase3`, `wt\5c-frontend`, `wt6\evidence`; screenshots in `C:\Users\labuser\shots\`.

## Problems and fixes
- Agent tool `isolation: worktree` fails (wrong base commit, then refuses): create worktrees by hand with `git worktree add -b phase/<n> /c/Users/labuser/<dir>/<name> feature/housekeeping-board`.
- A subagent is stuck if the session is in plan mode: call ExitPlanMode first.
- `mysql -u root` is blocked by our own deny rule; the user must approve or remove it. Do not bypass.
- MySQL: `larchmont_app` host is `%`, not `localhost`. Views cannot use `@vars`.
- The shell tool fails on large heredocs with apostrophes; write long files with the Write tool.
- Post-edit build hook builds the main checkout, so it fails if another process locks its binaries.

## Next steps
1. FIRST: gate Phase 6. `git diff feature/housekeeping-board..phase/6-perf`; in `C:\Users\labuser\wt6\perf` run `cd backend && dotnet build Larchmont.sln && dotnet test Larchmont.sln` (expect 74 pass); re-run a subset of the perf measurements on `larchmont_scratch`; check the validator behaviour changes against the spec status codes (AC-10, AC-41, lock 400s); secret grep; then `git merge --no-ff phase/6-perf` into the feature branch and commit. Decide whether to add a V006 migration for DATETIME(6).
2. Phase 7 (LH-56), only after the user says go: 1 Haiku. Use the MCP server to show the wrong numbers, fix `ReportService.cs` and `BookingRepository.ListOverlappingAsync`, add a regression test that fails on the old code and passes on the fix, write `docs/debug-report.md` including the first explanation tried and discarded.
3. Phase 8: characterisation tests for `ChannelImportService.cs` (`docs/sample-channel-export.csv`), commit, then refactor, then `docs/refactor-note.md`.
4. Phase 9: reach 25+ `AC_` tests (74 on the phase 6 branch), fix the existing test that checks the wrong thing (`UnitTests.cs` or `ApiTests.cs`), harden weak `AC_32` and `AC_38` (they only assert 404), mutation spot-check.
5. Phase 10: reviewer and security agents -> `docs/review-report.md`; fix CI, compose, README; move committed secrets to env and rotate. Phase 11: `docs/prompt-journal.md` (15+ entries), `docs/ai-usage-reflection.md`, tick `DELIVERABLES.md`, final handoff; the user approves the merge to `main`.
Open: ask before pushing; delete the two env files and rotate the root password at the end; OOO room checkout creates a turnaround and shows as an SLA breach (question for Sofia).

## Key files
- `docs/specs/housekeeping-board.md`, `docs/mcp-security.md`, `docs/onboarding-brief.md`, `DELIVERABLES.md`, `../CAPSTONE.md`; on phase/6-perf: `docs/perf-note.md`.
- `backend/Larchmont.Api/Services/{RoomStatusPolicy,LockIntegrationService,HousekeepingServices,BookingService,ReportService}.cs`, `Controllers/HousekeepingControllers.cs`, `db/migrations/V005__housekeeping_board.sql`.
- `mcp/Larchmont.Mcp/SqlGuard.cs`, `db-setup/02-mcp-views.sql`, `db-setup/03-create-mcp-user.sql`, `.mcp.json`.
- `.claude/settings.json` (hooks and deny rules), `.claude/agents/*`, `.claude/skills/*`.
