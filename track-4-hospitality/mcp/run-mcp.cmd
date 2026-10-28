@echo off
rem Windows launcher: Claude Code starts MCP servers without Git Bash on PATH, so add it here.
set "PATH=%ProgramFiles%\Git\usr\bin;%PATH%"
cd /d "%~dp0"
bash run-mcp.sh
