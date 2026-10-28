# Prompt journal

This journal was reconstructed after the work, not written live. Entries 1 to 10 come from the user's words as recorded in `docs/handoff/handoff-2026-10-26-1617.md` and `docs/handoff/latest.md` (earlier sessions); entries 11 to 26 come from the final session. Prompts are quoted as typed, including typos. Where the handoff shortened a prompt, it is shortened here. Any password the user typed is replaced with `<hidden>`. The raw session transcripts were not used: they contain credentials, and an attempt to have an agent read them was blocked by the auto-mode safety check (see entry 26 and `docs/agent-map.md`). "Mode" is given only where the session itself showed it; otherwise it says "not recorded".

## Earlier sessions (from the handoffs)

### 1. Caveman as a project skill
- **Prompt:** "no plugin should be installed ..i want this to be added as a skill in my project"
- **Mode:** not recorded.
- **What came back:** `.claude/skills/caveman/SKILL.md` plus a SessionStart hook that loads it, committed as 0a513df. No plugin was installed.
- **Decision:** Accepted. The user rejected the plugin route and asked for a project skill.

### 2. Stop before executing the plan
- **Prompt:** "give it to me first ..do not execute anything yet"
- **Mode:** plan (inferred from the instruction; not recorded in the handoff).
- **What came back:** The 11-phase capstone plan, written out for review, with nothing run.
- **Decision:** Accepted as given; the plan stayed unapproved until the user answered the questions in it.

### 3. Proceed on defaults
- **Prompt:** "ok to procced on defaults with an explaination why"
- **Mode:** not recorded.
- **What came back:** The stakeholder never replied, so the spec went ahead on stated defaults, each with its reason, and 12 open questions stayed in the spec.
- **Decision:** Accepted. The defaults are unconfirmed assumptions and the spec says so.

### 4. MCP server language (answer to a question)
- **Prompt:** chose ".NET" from the options offered.
- **Mode:** not recorded.
- **What came back:** `mcp/Larchmont.Mcp`, a .NET stdio server using ModelContextProtocol and MySqlConnector.
- **Decision:** Accepted. This was a choice between options, not a typed prompt.

### 5. Scratch database only
- **Prompt:** "Use scratch db only with user root and password..." (shortened in the handoff; password not recorded here)
- **Mode:** not recorded.
- **What came back:** A `larchmont_scratch` database for all testing; the live `larchmont` database stayed untouched.
- **Decision:** Accepted. This choice is why later phases ran against scratch.

### 6. Where root credentials may live
- **Prompt:** "you can store root user and its password in env file"
- **Mode:** not recorded.
- **What came back:** `~/.larchmont-root.env` and `~/.larchmont-mcp.env`, both outside the repository.
- **Decision:** Accepted, with the condition that the values are never printed. Later turns kept to that.

### 7. Lifting the root deny rules for one script
- **Prompt:** "remove the two deny lines"
- **Mode:** not recorded.
- **What came back:** The two `mysql ... -u root` deny rules were removed so the MCP user script could run, then restored afterwards.
- **Decision:** Accepted for that one task only. The rules were put back (compare entries 18 to 20).

### 8. Lock stamp rule (option A)
- **Prompt:** "go ahead with option A"
- **Mode:** not recorded.
- **What came back:** A lock event may move an open checkout stamp earlier and writes a `LockCheckoutStampMovedEarlier` history row (AC-45).
- **Decision:** Accepted. The later review found the rule has no lower bound on the lock time (finding R6 in `docs/review-report.md`).

### 9. Commit often
- **Prompt:** "keep comitting all changes to my branch as you make"
- **Mode:** not recorded.
- **What came back:** A commit after every phase merge, on the feature branch, not pushed.
- **Decision:** Accepted. The push was held until the user asked for it (entry 25).

### 10. Stop after phase 6
- **Prompt:** "stop and commit everything .. start from phase 7 tomorrow"
- **Mode:** not recorded.
- **What came back:** Everything committed, a handoff written, work stopped with phase 6 finished but not gated.
- **Decision:** Accepted. The next session started from that handoff.

## Final session

### 11. Why the MCP server fails
- **Prompt:** "why is mcp server larchmont db failing ?"
- **Mode:** not recorded.
- **What came back:** The first answer was that `LARCHMONT_MCP_CONNECTION` was not set in the shell that started Claude Code, so the server exited at startup.
- **Decision:** Accepted, but only partly right. It was one real cause; the later log (entries 15 to 24) showed a second one, that `bash` was not on the Windows PATH.

### 12. Resume from the handoff
- **Prompt:** "Read @docs/handoff/latest.md, then continue from 'Next steps'."
- **Mode:** plan, then auto (plan mode was active for the first part of the turn; the plan was approved and auto mode followed).
- **What came back:** Phase 6 gated: build clean, 74 tests pass, the diff read, a secret grep run, then merged. Two gaps were stated: the perf figures were not re-run, and the live MySQL race result was the agent's.
- **Decision:** Accepted. The plan had flagged that the phase branch also edited handoff files, and those were kept out of the merge.

### 13. Start phase 7
- **Prompt:** "GO"
- **Mode:** auto.
- **What came back:** The LH-56 fix (two comparison operators), two regression tests that fail on the old code, `docs/debug-report.md`. The agent had no MCP tools, so it recounted from the seed SQL.
- **Decision:** Accepted and merged. The missing MCP evidence was reported to the user and left open.

### 14. Start phase 8
- **Prompt:** "GO"
- **Mode:** auto.
- **What came back:** 115 characterisation tests, then a refactor of `ChannelImportService.cs` in a separate commit, then a note listing 12 existing bugs the agent pinned and did not fix.
- **Decision:** Accepted. The commit order and unchanged test file were checked before merging.

### 15. Fix MCP first
- **Prompt:** "deal with mcp first"
- **Mode:** auto.
- **What came back:** The server started by hand with the right environment and answered a real query (100 rooms), so the server, user and views were fine. The cause was that Claude Code was launched without the variables.
- **Decision:** Accepted, and it did not fix the problem in the user's session (see 17 and 23).

### 16. Start phase 9
- **Prompt:** "Start phase 9"
- **Mode:** auto.
- **What came back:** A test agent hardened AC_32 and AC_38, found a unit test that passed on the wrong exception, and ran an 8-rule mutation check with two survivors that were then killed. 201 tests pass.
- **Decision:** Accepted. The scope was narrowed first, because 45 AC tests already existed.

### 17. MCP still failing, "use root"
- **Prompt:** "mcp server is still showing failed ..fix that using user as root and password as <hidden>"
- **Mode:** auto.
- **What came back:** A refusal to use root: the server's own config check rejects the root account by design. The real cause in that moment was `mcp/.env` pointing at the live database with a different password. It was repointed at scratch with the working `larchmont_mcp` connection.
- **Decision:** Rewritten. The goal (a working MCP server) was met without the root account; the request as typed was not followed.

### 18. The deny rule
- **Prompt:** "the deny rule on mysql -u root"
- **Mode:** auto.
- **What came back:** The two rules were found in `.claude/settings.json` and the options were set out. Nothing changed.
- **Decision:** The prompt was a fragment, so the reply asked what was meant instead of guessing.

### 19. Remove the rules
- **Prompt:** "1."
- **Mode:** auto.
- **What came back:** The edit to `.claude/settings.json` was blocked by the auto-mode safety check as self-modification of permissions. The user was told to make the edit.
- **Decision:** The block was accepted and not worked around.

### 20. Make the edit
- **Prompt:** "Edit the file. In .claude/settings.json, delete the two lines "Bash(mysql*-u root*)" and "Bash(mysql*-uroot*)". Also remove the trailing comma after "Bash(mysql*alter*)" on the line above, so the JSON stays valid."
- **Mode:** auto.
- **What came back:** The same edit, now with the user's explicit instruction, succeeded and the JSON was validated.
- **Decision:** Accepted. The rules were restored at the start of phase 10.

### 21. Confirm the live MCP goal
- **Prompt:** "yes" and then "yes go"
- **Mode:** auto.
- **What came back:** A read-only root check showed the live `larchmont` database has no V005 tables, so the view script would fail there. Work stopped and the options were listed. The root file's password was not the one the user had given.
- **Decision:** Accepted. Stopping was the right call; applying V005 to live data was not authorised.

### 22. Root with a password again
- **Prompt:** "change user to root and password to <hidden>" and then "1"
- **Mode:** auto.
- **What came back:** A harmless `SELECT 1` showed that password was rejected by MySQL (`Access denied`). No file was changed and the root guard in the server was not removed.
- **Decision:** Rejected by the system, not by the user: the credential did not work.

### 23. Still failed after reconnect
- **Prompt:** "yes after reconect shows as failed"
- **Mode:** auto.
- **What came back:** The MCP log showed `'bash' is not recognized as an internal or external command`. A Windows launcher (`mcp/run-mcp.cmd`) was added and tested with a minimal PATH.
- **Decision:** Accepted. This was the real cause; the earlier hand tests passed only because they ran inside Git Bash.

### 24. MCP up, next phase
- **Prompt:** "mcp is up now proceed with next phase"
- **Mode:** auto.
- **What came back:** The root deny rules restored, the launcher fix committed, `CLAUDE.md` updated, then phase 10: two review agents in parallel, fixes to CI, compose and Swagger, and `docs/review-report.md`.
- **Decision:** Accepted. Rotation of the committed credentials was held for the user's decision.

### 25. Commit and push
- **Prompt:** "commit and push changes to my branch aftert his" (sent while phase 10 was running)
- **Mode:** auto.
- **What came back:** Phase 10 merged, committed, and `feature/housekeeping-board` pushed to origin as a new branch (not forced, not `main`). The sign-in prompt took a while and the push finished after it was completed.
- **Decision:** Accepted. The earlier handoff said to ask before pushing; this was the explicit go-ahead.

### 26. Start phase 11
- **Prompt:** "start phase 11 once done commit and push to same branch"
- **Mode:** auto.
- **What came back:** Missing documents written (`agent-map.md`, this journal, the reflection), a CI job for the MCP tests, a checklist table and CI-log explanation in `docs/review-report.md`. A delegation to extract prompts from the raw transcripts was blocked by the safety check, so this journal was built from the handoffs and the session.
- **Decision:** Accepted, with the journal limits stated in the header.
