# AI usage reflection

## Where Claude Code helped

- **Reading the codebase before changing it.** The briefing in `CLAUDE.md` was wrong about the stack (it described a different project shape). Correcting it first meant the later agents worked from the real layout: .NET 8 MVC, EF Core on MySQL, DbUp migrations, React with Vite.
- **Spec first.** A 45-criterion spec with business rules and 12 open questions gave every agent and every test one source of truth. It also made later disagreements easy to settle by pointing at a line.
- **Parallel agents with gates.** Separate worktrees and one branch per phase let agents work without colliding. Each merge was checked by re-running build and tests, which caught real problems (a duplicate type, validation errors not serialised, a test that passed on the wrong exception).
- **Tests that check themselves.** Characterisation tests before the importer refactor (115 cases), a regression test that failed on the old LH-56 code, and a mutation spot-check that found two rules no test protected.
- **Review in parallel.** A reviewer agent and a security agent, run at the same time, found things a single pass had not: out-of-order rooms staying on the SLA clock, a second committed credential file, and the audit table being editable by the app account.

## Where it was wrong or fell short

- **Confident reports.** Agents reported success in a confident tone: "74 tests pass", "no mutation survives". Some of that was only partly true. One phase commit was labelled WIP/UNVERIFIED, and perf numbers could not be re-run by the orchestrator. Every claim was re-checked where it could be, and the notes say which ones were not.
- **A wrong first diagnosis.** For MCP, the first answer was that an environment variable was unset. That was true but not the whole story: after it was fixed, the Windows launch still failed because `bash` was not on the PATH Claude Code uses. The log, not the guess, found it, and the hand tests had passed only because they ran inside Git Bash.
- **A wrong first explanation for LH-56.** The agent first suspected the occupancy denominator (out-of-order rooms) and discarded it after the numbers did not fit; the cause was an off-by-one at checkout.
- **Tool and safety limits.** Worktree isolation on the agent tool failed and had to be done by hand. Some requests were correctly blocked: using the root account for the MCP server, editing the permission rules, and having an agent read raw session transcripts. These were not bugs; they were guards doing their job, and they cost time.
- **The request for root.** A request to run the MCP server as root was refused, because the server rejects root by design and root defeats the read-only design. The password supplied did not log in either.
- **Gaps left open.** The importer has 12 known bugs, pinned but not fixed. Committed credentials are still in git history and still need rotating. The MCP evidence for LH-56 came from the seed SQL, not the live server. CI has not run on GitHub.

## What I would do differently

1. Start the prompt journal on day one. This one was rebuilt from handoffs afterwards, so it is thinner than a live one would be.
2. Test the MCP launch the way Claude Code launches it (a bare Windows shell), not from a developer terminal, before calling it fixed.
3. Rotate the committed credentials at the start, not the end. They are now in pushed history.
4. Decide the out-of-order SLA rule with the stakeholder before building the board, so the review would not find it late.
5. Put the 2 or 3 riskiest rules under a mutation check from the start, not as a spot-check at phase 9.
6. Keep one instruction per agent and ask for evidence (the command and its output), not a summary.
