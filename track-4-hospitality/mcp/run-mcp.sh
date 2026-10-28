#!/usr/bin/env bash
# Loads the gitignored mcp/.env, then starts the MCP server. stdout is the MCP protocol, so messages go to stderr.
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ ! -f "$dir/.env" ]; then
  echo "Missing $dir/.env. Copy mcp/.env.example to mcp/.env and fill it in." >&2
  exit 2
fi
set -a
. "$dir/.env"
set +a
exec dotnet run --project "$dir/Larchmont.Mcp" -v q
