# Agent map

Who did what, and what the orchestrator had to correct. The orchestrator was the main Claude Code session (Sonnet). Work was done by subagents (Haiku unless noted) in hand-made git worktrees, one branch per phase (`phase/<n>-...`). The orchestrator briefed each agent, then gated the result (diff, build, tests, spec check) before merging into `feature/housekeeping-board`. Nothing merges to `main` without the user's approval.

Project agents live in `.claude/agents/`: `backend`, `frontend`, `test`, `reviewer`, `security`, `db-analyst`.

Sources: `docs/handoff/*.md` for phases 0 to 5 (the per-agent detail for those phases was not recorded beyond the handoffs), and the session itself for phases 6 to 11.

## By phase

| Phase | Work | Agent(s) | What the orchestrator corrected or found at the gate |
|---|---|---|---|
| 0 | Setup, repo orientation | orchestrator | n/a |
| 1 | Onboarding brief, corrected `CLAUDE.md`, rules, skills, agents, hooks | orchestrator and agents (detail not recorded) | `CLAUDE.md` rewritten to match the real stack (not CRA, not Dapper, DbUp migrations). |
| 2 | Spec `docs/specs/housekeeping-board.md` (45 ACs) | orchestrator | Proceeded on stated defaults after no reply from the stakeholder; the spec lists 12 open questions. |
| 3 | V005 schema and service stubs | agent (detail not recorded) | n/a |
| 4 | MCP server (`mcp/Larchmont.Mcp`, 95 tests) | agent (detail not recorded) | Gate failed once: MySQL views cannot read session variables. Fixed with a `__MCP_HOTEL_ID__` placeholder substituted when the views are installed. |
| 5 | Rules, API, frontend, tests (5a to 5d) | backend, frontend and test agents in separate worktrees | Merge fixes: a duplicate `HousekeepingRoles` type, `ApiExceptionHandler` did not serialise validation `errors`, and the Suite tests had to go through the Supervisor path. |
| 5 (change) | Lock event may move an open checkout stamp earlier (option A) | orchestrator | User chose option A; adds a history row (AC-45). |
| 6 | Performance and concurrency | backend agent (Haiku) | Commit was labelled WIP/UNVERIFIED. Orchestrator re-ran build and tests (74 pass), read the diff, checked validator status-code changes against the spec, and grepped for secrets. The measured SQL statement counts and the 200/409 race result were not re-run by the orchestrator. Handoff files edited on the phase branch were kept out of the merge. |
| 7 | Bug LH-56 (occupancy counted the checkout night) | backend agent (Haiku) | The MCP tools were not available to the agent, so it recounted from the V004 seed SQL with a read-only script; numbers matched the ticket. The ticket pointed at AC-43 for the night rule, but AC-43 is about out-of-order rooms. Regression tests fail on the old code (agent report; orchestrator re-ran only the fixed suite). |
| 8 | Importer characterisation tests, then refactor | backend agent (Haiku) | Commit order checked (tests, refactor, note); refactor commit touches only the service file; 191 tests pass. Agent pinned 12 existing importer bugs without fixing them. |
| 9 | Test hardening and mutation spot-check | test agent (Haiku) | Found a unit test that passed on the wrong exception, two mutation survivors (killed with new tests), and weak AC_32 and AC_38 (hardened). 201 tests pass. |
| 10 | Review | reviewer and security agents in parallel (Haiku, read-only) | Security agent found a second committed credential file the known-issues list missed. Reviewer found the out-of-order SLA problem (needs a spec decision). Orchestrator fixed CI, compose and Swagger; the rest is listed in `docs/review-report.md`. |
| 11 | Documents and final checks | orchestrator | A delegation to extract prompts from raw session transcripts was blocked by the auto-mode safety check (the transcripts contain credentials). The journal was rebuilt from the handoffs and the session instead. |

## What the agents got wrong or could not do

- `isolation: worktree` on the Agent tool failed (wrong base commit, then refused). Worktrees were created by hand with `git worktree add`.
- A subagent is stuck while the session is in plan mode; the plan has to be exited first.
- Haiku agents write long, confident reports. Every gate re-ran build and tests rather than trusting the report. Claims that the orchestrator did not re-verify are marked as such in the notes (`docs/perf-note.md`, `docs/debug-report.md`, `docs/refactor-note.md`, `docs/test-report.md`).
- Agents followed the task's co-author trailer, not the harness default; this is only about commit trailers.
- Agent scope rules (backend agent must not touch tests) were overridden by explicit task wording in phases 7 and 8; the agents said so in their reports.

## Parallel use

Phase 5 used separate worktrees for backend, frontend and tests (the handoff does not say how many ran at the same time), and phase 10 ran the reviewer and security agents at the same time. Phases 6 to 9 ran one agent each, in order, because each depended on the previous merge.
