#!/usr/bin/env bash
# PreToolUse guard for Edit|Write. Blocks edits to applied migrations and
# writes that contain hard-coded secret values. Exit 2 blocks the tool call.
input="$(cat)"
HOOK_INPUT="$input" node -e '
const j = JSON.parse(process.env.HOOK_INPUT || "{}");
const ti = j.tool_input || {};
const file = (ti.file_path || "").replace(/\\/g, "/");
const content = ti.content || ti.new_string || "";
if (/backend\/db\/migrations\/V00[1-4]__/.test(file)) {
  console.error("Blocked: V001 to V004 are applied migrations and must not be edited. Add a new V00n file instead.");
  process.exit(2);
}
if (/\.claude\/hooks\//.test(file)) process.exit(0);
const secret = /(password|pwd)\s*=\s*[^;\s\x27"]{3,}|api[_-]?key\s*[:=]\s*["\x27][A-Za-z0-9_\-]{16,}/i;
if (secret.test(content)) {
  console.error("Blocked: content looks like a hard-coded secret. Read it from an environment variable instead.");
  process.exit(2);
}
process.exit(0);
'
