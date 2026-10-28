# Deliverables checklist

Tick every box before you submit. Paths are relative to this folder. Submit with no `node_modules`, `bin`, `obj` or `dist`.

## Documents
- [x] `docs/onboarding-brief.md`
- [x] `docs/specs/housekeeping-board.md`
- [x] `docs/prompt-journal.md` (at least 15 entries)
- [x] `docs/mcp-security.md`
- [x] `docs/agent-map.md`
- [x] `docs/debug-report.md`
- [x] `docs/refactor-note.md`
- [x] `docs/perf-note.md` - Performance and concurrency note (before and after). Proof of performance, schema and concurrency fixes needs a test against MySQL or the EF Core SQL log (the existing tests use an in-memory database).
- [x] `docs/review-report.md`
- [x] `docs/ai-usage-reflection.md`

## Claude Code setup
- [x] `CLAUDE.md` (corrected)
- [x] `.claude/rules/*` or a nested `CLAUDE.md`
- [x] `.claude/skills/` (at least 2)
- [x] `.claude/agents/` (at least 5: backend, frontend, test, reviewer, db-analyst)
- [x] `.claude/settings.json` with hooks
- [x] `.mcp.json`

## MCP server
- [x] `mcp/` (the server) and `mcp/tests/`
- [x] `db-setup/03-create-mcp-user.sql`
- [x] `db-setup/02-mcp-views.sql`

## Pipeline and run files
- [x] `.github/workflows/ci.yml` (fixed, plus a job that runs the MCP tests; not yet run on GitHub, see docs/review-report.md)
- [ ] `compose.yaml` (complete and free of secrets) - secret-free, but it has only a MySQL service
- [x] `README.md` (updated)

## This track: housekeeping and room-status board
- [x] Room states Dirty, Cleaning, Inspected, Ready, with rules; invalid moves return a clear 4xx
- [x] Out-of-order rooms cannot be sold or assigned
- [x] Inspection step before Ready
- [x] Turnaround SLA with a breach list
- [x] Assignment of housekeepers by floor
- [x] `GET /api/rooms/{id}/ready-by` tells the desk when a room will be ready
- [x] Screen: live board by floor
- [x] Screen: SLA breach list
- [x] Loading, empty and error states on both screens
- [x] MCP tools: `rooms_breaching_sla`, `ooo_impact_on_occupancy`, `housekeeper_load`, `arrivals_without_ready_room`, plus a guarded `run_readonly_query`
- [x] Ticket `docs/bug-LH-56.md` resolved with a regression test that fails on the original code
- [x] Channel import service refactored behind characterisation tests
- [x] At least 25 tests named after acceptance criteria (for example `AC_04_CannotMarkReadyWithoutInspection`)
- [x] Findings from the review checklist fixed or explained, and the failing CI run in `docs/ci-failing-run.log` explained and fixed (open items listed in `docs/review-report.md`; CI fix untested on GitHub)
