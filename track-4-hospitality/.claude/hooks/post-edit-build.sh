#!/usr/bin/env bash
# PostToolUse for Edit|Write on .cs files. Builds the backend quietly and
# fails loudly (exit 2 feeds the errors back to Claude) if the build breaks.
input="$(cat)"
file=$(HOOK_INPUT="$input" node -e 'const j = JSON.parse(process.env.HOOK_INPUT || "{}"); process.stdout.write(((j.tool_input || {}).file_path || "").replace(/\\/g, "/"));')
case "$file" in
  *.cs) ;;
  *) exit 0 ;;
esac
root="${CLAUDE_PROJECT_DIR:-$(pwd)}"
out=$(dotnet build "$root/backend/Larchmont.sln" -nologo -v q 2>&1)
status=$?
if [ "$status" -ne 0 ]; then
  echo "$out" | grep -iE "error" | head -20 >&2
  echo "dotnet build failed after editing $file. Fix the build before continuing." >&2
  exit 2
fi
exit 0
