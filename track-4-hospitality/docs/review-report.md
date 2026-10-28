# Review report (phase 10)

Branch `feature/housekeeping-board` against `main`. Two read-only agents (reviewer and security) read the code; neither ran the app, a build or a race. Findings marked CONFIRMED mean the agent followed the code path; PLAUSIBLE means inferred. The orchestrator did not re-verify each finding. No credential values appear in this file.

## Fixed in this phase

| Item | Change |
|---|---|
| CI used .NET 6 and `working-directory: web` | `.github/workflows/ci.yml` now uses `8.0.x` and `frontend`. Not run: CI executes only on GitHub. |
| `compose.yaml` held a root password literal | It now reads `MYSQL_ROOT_PASSWORD` from the environment and refuses to start without it. README says so. |
| Swagger on in every environment (security 4) | `Program.cs` enables Swagger only when the environment is Development. `dotnet run` uses the Development profile, so the README link still works. |

## Needs a decision or an external action (not done)

| # | Source | Finding | Why not done |
|---|---|---|---|
| S1 | security High, CONFIRMED | `db-setup/01-create-users.sql` has a literal password for the app user (plus a commit message that mentions it). | Replacing the literal without rotating leaves the old password live. Rotation changes a live MySQL user. Needs the user's go. |
| S2 | security High, CONFIRMED | `appsettings.Development.json` (DB connection, channel API key, SMTP password) and the old `compose.yaml` password are in git history. | Needs rotation of all of them, then a history purge (git-filter-repo or BFG) that rewrites shared history. Needs the user's go. |
| S3 | security High, CONFIRMED | No authentication; `changedByUserId` comes from the request body, so the audit trail can be forged. | Documented as out of scope in the spec. Needs authentication before go-live. |
| S4 | security Medium, CONFIRMED | App DB account has ALL PRIVILEGES on the database, so it can also edit `mcp_query_audit`. | Needs a live grant change and a separate migration account for DbUp. |
| S5 | security Medium, PLAUSIBLE | `SqlGuard` lexes double-quoted identifiers as strings (only matters with ANSI_QUOTES); `WITH RECURSIVE` and cross joins are allowed; any word after AS is accepted as an alias. | DB grants (SELECT on views only) are the real control. Worth hardening, not proven exploitable. |
| S6 | security Low | MCP user is `@'%'`; the user-setup doc puts the password on a sed command line; ProblemDetails copy ids into messages (user id enumeration); deny rules in `.claude/settings.json` are prefix-based and bypassable with pipes. | Low risk for a local capstone. Listed so the choice is visible. |
| S7 | security Low | .NET 8 support ends about 2026-11-10; EF Core, Pomelo and DbUp packages are old. | Upgrade is a separate piece of work. |
| R1 | reviewer High, CONFIRMED | Out-of-order rooms keep an open turnaround, so they show as SLA breaches and come back already overdue. | The spec is ambiguous (BR-02 and BR-13). This is the open question for Sofia (OOO checkout). Needs a rule before code. |
| R2 | reviewer Medium, CONFIRMED | A Dirty room can have no turnaround (new room, release from OutOfOrder), so ready-by is blank and it is never a breach. | Same rule decision as R1. |
| R3 | reviewer Medium, CONFIRMED | `ChangeStatusAsync` does not check that the acting user is Active; other services do. | Changes status codes (403 or 422) beyond the spec. Needs a spec line. |
| R4 | reviewer Medium, CONFIRMED | Desk and lock checkout do not catch a row-version conflict, so a concurrent status change returns 500. | Needs a decision: retry once or return 409. Add a unit-level test. |
| R5 | reviewer Medium, CONFIRMED | Two supervisors setting the same floor at once can return 500 (unique key hit) instead of 409. | Map `DbUpdateException` to 409 or lock the rows. Add a test. |
| R6 | reviewer Medium, CONFIRMED | Lock `checkedOutAt` has no lower bound; one early value moves the stamp permanently (option A). | Needs a window (for example 24 hours) agreed with the vendor (Q10). |
| R7 | reviewer Medium, PLAUSIBLE | A lock event for a room with a CheckedIn booking still sets it Dirty. | Depends on the vendor payload (Q10). |
| R8 | reviewer Medium, CONFIRMED | The board UI reads `rowVersion` just before each write, which hides stale data from BR-19. | Needs `rowVersion` in the board DTO and a frontend change. |
| R9 | reviewer Medium, CONFIRMED | No test reaches the real concurrency-conflict path (AC_39 is sequential). | Unit test with a repository stub that throws; EF InMemory cannot show it. |
| R10 | reviewer Low, CONFIRMED | Polling hook blanks the board on one failed poll. | UI polish. |
| R11 | reviewer Low, CONFIRMED | V005 default for `rooms.status` is `Ready`, but Q7 says new rooms start Dirty. | Needs a V006 migration; V005 is never edited. Also the place to fix DATETIME(0) vs datetime(6). |
| R12 | reviewer Low, CONFIRMED | A simple-turn shortcut saves its spot-check row in a second save. | Move into one save or one transaction. |
| R13 | reviewer Low, CONFIRMED | Dead code (`RolesFor`, two unused `Add` methods), `ILockIntegrationService` in the wrong file, one literal role string. | Cleanup only. |

## Passed

Frontend (no unsafe HTML or token storage), no raw SQL in the API, lock API key check (constant time, closed by default), no edits to V001 to V004, no service uses the DbContext directly, no local-time code, and a test exists for each of the 45 ACs.

## Not checked

The reviewer did not read the MCP server, views, hooks, `.mcp.json`, or the importer refactor. The security reviewer did not run SHOW GRANTS, check MySQL `sql_mode`, run the guard dynamically, or inspect other tracks' config files. Neither ran the application. Whether any committed credential is still live is unknown.

## Orchestrator note

Items R1 to R9 are the ones most likely to matter in production. R1, R2 and R6 to R7 depend on answers to the open questions for Sofia, so they were left as findings and not guessed in code.
