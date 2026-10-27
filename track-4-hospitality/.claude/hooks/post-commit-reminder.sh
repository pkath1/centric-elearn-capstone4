#!/usr/bin/env bash
# PostToolUse for Bash. After a git commit command, adds a reminder to Claude's
# context to keep CLAUDE.md and the onboarding brief in step with the change.
input="$(cat)"
cmd=$(HOOK_INPUT="$input" node -e 'const j = JSON.parse(process.env.HOOK_INPUT || "{}"); process.stdout.write((j.tool_input || {}).command || "");')
case "$cmd" in
  *"git commit"*)
    printf '%s\n' '{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"If a commit was just made and it changed commands, stack, layout, or conventions, update CLAUDE.md and docs/onboarding-brief.md to match before the next task."}}'
    ;;
esac
exit 0
