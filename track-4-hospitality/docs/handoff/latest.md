# Handoff 2026-10-27 14:36 | branch main | Goal: Track 4 capstone (housekeeping board), planning stage

## Start here
Paste: "Read @docs/handoff/latest.md, then continue from 'Next steps'. Do not re-explore code."

## Repo orientation
- Stack: .NET 8 MVC controllers, EF Core on MySQL, DbUp migrations in `backend/db/migrations` (V001-V004 applied; never edit; next is V005), xUnit + Moq + EF InMemory, React 19 + Vite + TS.
- Code: `backend/Larchmont.Api` (Domain/Entities.cs, Services, Repositories, Controllers, Validators, Dtos, Mappers, Data/LarchmontDbContext.cs, Program.cs manual AddScoped lines 24-40), `backend/Larchmont.Tests`, `frontend/src` (App.tsx, api/client.ts getJson only, hooks/useAsync.ts, components/StateView.tsx).
- Entities: Room (Status Clean/Dirty/OutOfOrder only), Booking, User (roles Admin/FrontDesk/Manager/Housekeeper). Dates UTC.
- `CLAUDE.md` is STALE (says ASP.NET 6, Dapper, SQL Server, CRA, `src/`, `client/`). Fix = capstone Task 1.
- Gaps found: `BookingService.CheckInAsync` (145-155) and `AssignRoomAsync` (183-197) ignore room status; `RoomService.SetStatusAsync` (Services/HotelServices.cs ~172) has no transition rules or concurrency; `CheckOutAsync` (157-181) sets Dirty, no checkout stamp.
- LH-56 root cause: `Services/ReportService.cs` line 44 `day <= b.CheckOut` should be `<`; also `BookingRepository.ListOverlappingAsync` `CheckOut >= from`. Expected: Edinburgh 2 Oct 5 rooms 25%; 1-7 Oct 45 nights 32.1%; Group 226 nights 26.9%.
- CI broken: dotnet 6.0.x (needs 8), `working-directory: web` (is `frontend`). `compose.yaml` has a root password literal. `appsettings.Development.json` has committed secrets (DB, channel API key, SMTP).

## Goal and context
Run app, add caveman skill, then plan implementation of all of `DELIVERABLES.md` from `docs/stakeholder-brief.md` (Sofia Lindqvist). Process rules in `../CAPSTONE.md` (spec first, 3 parallel agents, MCP mandatory, no extras).
Session 2 (2026-10-27): user asked to clone `https://github.com/niti007/centric-elearn-capstone` (track-4-hospitality). Done, no other work.

## Approved decisions
| Decision | User words | Consequence |
|---|---|---|
| Caveman as project skill, no plugin | "no plugin should be installed ..i want this to be added as a skill in my project" | `.claude/skills/caveman/SKILL.md` + SessionStart hook, committed 0a513df |
| Ponytail as project skill | (committed a5e5d4a "Add ponytail project skill and load it at session start") | `.claude/skills/ponytail/`; SessionStart hook says invoke caveman and ponytail |
| Git identity | chose "Use placeholder labuser" | repo-local `labuser` / `labuser@localhost` |
| Housekeeper Ready shortcut | chose "Simple-turn shortcut, audited" | Cleaning->Ready only if room flagged simple turn; logged; OOO returns always need inspection |
| Housekeeper location tracking | chose "Spec it, do not build" | Spec states why off-clock/break tracking must not be built (UK GDPR, review checklist); offer on-shift opt-in alternative |
| Lock checkout | earlier chose "Webhook endpoint + polling" | `POST /api/integrations/lock/checkout` + board polling |
| Sell gate | chose "Out-of-order blocks assign; non-Ready blocks check-in" | edit AssignRoomAsync / CheckInAsync |
| Handoff command | "create a slash command", project scope, command only | `.claude/commands/handoff.md`, no auto-load |
| Do not execute | "give it to me first ..do not execute anything yet" | capstone plan NOT yet approved |
| Clone repo | "clone this repo https://github.com/niti007/centric-elearn-capstone/tree/main/track-4-hospitality" | whole repo cloned (see Current state) |

## Proposed, NOT approved
- Nothing pending approval. The capstone plan is `C:\Users\labuser\.claude\plans\start-frontend-and-backend-sharded-adleman.md` (intact); changes to it are in `go-through-c-users-labuser-claude-plans-deep-hare.md`.
- SLA 45 min, 75 for Suite/Family; clock from stored checkout stamp (`room_turnarounds` table).
- V005 columns: rooms ooo_reason/ooo_since/ooo_expected_return/simple_turn/row_version; room_types.sla_minutes; floor_assignments; history reason; roles Supervisor, Maintenance.
- MCP server language (TS or .NET) not chosen.

## Progress
- Done: caveman skill + `.claude/settings.json` (0a513df); `/handoff` command + first handoff (64d66a3); ponytail skill (a5e5d4a).
- Done: `db-setup/01-create-users.sql` run as MySQL root; DB `larchmont` and user `larchmont_app` exist.
- Done: repo cloned to `C:\Users\labuser\centric-elearn-capstone-clone` (not built or run).
- Not started: all capstone Tasks 1-9.

## Current state
- Branch main, working tree clean; HEAD a5e5d4a. This handoff is untracked until committed.
- Clone at `C:\Users\labuser\centric-elearn-capstone-clone` is a separate copy of the upstream repo; the working repo is `C:\Users\labuser\centric-elearn-capstone4`. Not verified whether the clone differs from the working copy.
- Backend :5080 and frontend :5173 were running last session (pids 3728, 6560); not rechecked. Started with env var `ConnectionStrings__Larchmont` (root connection string, value not recorded).
- No tests/build run.

## Problems and fixes
- Backend "Access denied larchmont_app": fixed by running 01-create-users.sql as root.
- Git commit "Author identity unknown": fixed repo-local identity.
- Plan mode kept rejecting plans; user wanted the plan shown as text first.
- Git cannot clone a subfolder, so the full repo was cloned.

## Next steps
1. FIRST: show/confirm the capstone plan, get user approval (not yet given).
2. Task 1: onboarding-brief, correct CLAUDE.md, `.claude/rules/`, 2+ skills, 5+ agents (backend, frontend, test, reviewer, db-analyst, security), hooks (block write SQL/secrets, build+test after edit, update CLAUDE.md after commit).
3. Task 3: write spec `docs/specs/housekeeping-board.md` (template `docs/specs/_template.md`) BEFORE feature code; user supplies mentor/Sofia replies.
4. Task 2 MCP server + `db-setup/02-mcp-views.sql`, `03-create-mcp-user.sql`, `.mcp.json`, negative tests, `docs/mcp-security.md`.
5. Task 4 feature backend with 3 parallel agents + `docs/agent-map.md`, `docs/perf-note.md`.
6. Tasks 5 (LH-56), 6 (ChannelImportService characterisation + refactor), 7 (Board + SLA pages), 8 (25+ `AC_nn_` tests, fix wrong-checking test, no date dependence), 9 (review/security agents, CI fix, compose, README, reflection).
7. Keep `docs/prompt-journal.md` (15+ entries).
Open: MCP language; confirm git identity; whether to gitignore `docs/handoff/`; which copy (working vs clone) to use.

## Key files
- `DELIVERABLES.md`, `../CAPSTONE.md`: requirements and process.
- `docs/stakeholder-brief.md`, `docs/bug-LH-56.md`, `docs/review-checklist.md`, `docs/ci-failing-run.log`, `docs/sample-channel-export.csv`, `docs/specs/_template.md`.
- `.claude/commands/handoff.md`, `.claude/skills/caveman/SKILL.md`, `.claude/skills/ponytail/SKILL.md`, `.claude/settings.json`.
