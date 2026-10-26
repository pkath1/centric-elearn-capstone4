# Handoff 2026-10-26 16:17 | branch main | Goal: Track 4 capstone (housekeeping board), planning stage

## Start here
Paste: "Read @docs/handoff/latest.md, then continue from 'Next steps'. Do not re-explore code."

## Repo orientation
- Stack: .NET 8 MVC controllers, EF Core on MySQL, DbUp migrations in `backend/db/migrations` (V001-V004 applied; never edit; next is V005), xUnit + Moq + EF InMemory, React 19 + Vite + TS.
- Code: `backend/Larchmont.Api` (Domain/Entities.cs, Services, Repositories, Controllers, Validators, Dtos, Mappers, Data/LarchmontDbContext.cs, Program.cs manual AddScoped lines 24-40), `backend/Larchmont.Tests`, `frontend/src` (App.tsx, api/client.ts getJson only, hooks/useAsync.ts, components/StateView.tsx).
- Entities: Room (Status Clean/Dirty/OutOfOrder only), Booking, User (roles Admin/FrontDesk/Manager/Housekeeper). Dates UTC.
- `CLAUDE.md` is STALE (says ASP.NET 6, Dapper, SQL Server, CRA). Fix = capstone Task 1.
- Gaps found: `BookingService.CheckInAsync` (145-155) and `AssignRoomAsync` (183-197) ignore room status; `RoomService.SetStatusAsync` (Services/HotelServices.cs ~172) has no transition rules or concurrency; `CheckOutAsync` (157-181) sets Dirty, no checkout stamp.
- LH-56 root cause: `Services/ReportService.cs` line 44 `day <= b.CheckOut` should be `<`; also `BookingRepository.ListOverlappingAsync` `CheckOut >= from`. Expected: Edinburgh 2 Oct 5 rooms 25%; 1-7 Oct 45 nights 32.1%; Group 226 nights 26.9%.
- CI broken: dotnet 6.0.x (needs 8), `working-directory: web` (is `frontend`). `compose.yaml` has a root password literal. `appsettings.Development.json` has committed secrets (DB, channel API key, SMTP).

## Goal and context
Run app, add caveman skill, then plan implementation of all of `DELIVERABLES.md` from `docs/stakeholder-brief.md` (Sofia Lindqvist). Process rules in `../CAPSTONE.md` (spec first, 3 parallel agents, MCP mandatory, no extras).

## Approved decisions
| Decision | User words | Consequence |
|---|---|---|
| Caveman as project skill, no plugin | "no plugin should be installed ..i want this to be added as a skill in my project" | `.claude/skills/caveman/SKILL.md` + SessionStart hook, committed 0a513df |
| Git identity | chose "Use placeholder labuser" | repo-local `labuser` / `labuser@localhost` |
| Housekeeper Ready shortcut | chose "Simple-turn shortcut, audited" | Cleaning->Ready only if room flagged simple turn; logged; OOO returns always need inspection |
| Housekeeper location tracking | chose "Spec it, do not build" | Spec states why off-clock/break tracking must not be built (UK GDPR, review checklist); offer on-shift opt-in alternative |
| Lock checkout | earlier chose "Webhook endpoint + polling" | `POST /api/integrations/lock/checkout` + board polling |
| Sell gate | chose "Out-of-order blocks assign; non-Ready blocks check-in" | edit AssignRoomAsync / CheckInAsync |
| Handoff command | "create a slash command", project scope, command only | `.claude/commands/handoff.md`, no auto-load |
| Do not execute | "give it to me first ..do not execute anything yet" | capstone plan NOT yet approved |

## Proposed, NOT approved
- Full 9-task capstone plan (saved at `C:\Users\labuser\.claude\plans\start-frontend-and-backend-sharded-adleman.md` was overwritten by handoff plan; capstone plan summary below under Next steps).
- SLA 45 min, 75 for Suite/Family; clock from stored checkout stamp (`room_turnarounds` table).
- V005 columns: rooms ooo_reason/ooo_since/ooo_expected_return/simple_turn/row_version; room_types.sla_minutes; floor_assignments; history reason; roles Supervisor, Maintenance.
- MCP server language (TS or .NET) not chosen.

## Progress
- Done: `.claude/skills/caveman/SKILL.md`, `.claude/settings.json` (committed 0a513df). `.claude/commands/handoff.md` created, UNCOMMITTED.
- Done: `db-setup/01-create-users.sql` run as MySQL root; DB `larchmont` and user `larchmont_app` exist.
- Not started: all capstone Tasks 1-9.

## Current state
- Branch main, only untracked `.claude/commands/` (+ this handoff). Not committed.
- Backend LISTENING :5080 (pid 3728), frontend :5173 (pid 6560); start was via env var `ConnectionStrings__Larchmont` set to a root connection string (value not recorded here). Migration success not verified from logs.
- No tests/build run this session.

## Problems and fixes
- Backend "Access denied larchmont_app": fixed by running 01-create-users.sql as root.
- Git commit "Author identity unknown": fixed repo-local identity.
- Plan mode kept rejecting plans; user wanted the plan shown as text first.

## Next steps
1. FIRST: show/confirm the capstone plan, get user approval (not yet given).
2. Task 1: onboarding-brief, correct CLAUDE.md, `.claude/rules/`, 2+ skills, 5+ agents (backend, frontend, test, reviewer, db-analyst, security), hooks (block write SQL/secrets, build+test after edit, update CLAUDE.md after commit).
3. Task 3: write spec `docs/specs/housekeeping-board.md` (template `docs/specs/_template.md`) BEFORE feature code; user supplies mentor/Sofia replies.
4. Task 2 MCP server + `db-setup/02-mcp-views.sql`, `03-create-mcp-user.sql`, `.mcp.json`, negative tests, `docs/mcp-security.md`.
5. Task 4 feature backend with 3 parallel agents + `docs/agent-map.md`, `docs/perf-note.md`.
6. Tasks 5 (LH-56), 6 (ChannelImportService characterisation + refactor), 7 (Board + SLA pages), 8 (25+ `AC_nn_` tests, fix wrong-checking test, no date dependence), 9 (review/security agents, CI fix, compose, README, reflection).
7. Keep `docs/prompt-journal.md` (15+ entries).
Open: MCP language; confirm git identity; whether to gitignore `docs/handoff/`.

## Key files
- `DELIVERABLES.md`, `../CAPSTONE.md`: requirements and process.
- `docs/stakeholder-brief.md`, `docs/bug-LH-56.md`, `docs/review-checklist.md`, `docs/ci-failing-run.log`, `docs/sample-channel-export.csv`, `docs/specs/_template.md`.
- `.claude/commands/handoff.md`, `.claude/skills/caveman/SKILL.md`, `.claude/settings.json`.
