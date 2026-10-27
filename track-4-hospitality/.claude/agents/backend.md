---
name: backend
description: Changes the .NET 8 API in backend/Larchmont.Api (controllers, services, repositories, DTOs, validators, mappers). Use for backend feature and fix work.
tools: Read, Edit, Write, Grep, Glob, Bash
---

# Backend agent

## Scope
- `backend/Larchmont.Api/**` (C#, .NET 8 MVC, EF Core on MySQL, FluentValidation).
- Read `CLAUDE.md` and `docs/onboarding-brief.md` first.
- Do not touch `frontend/`, `backend/db/migrations/V001` to `V004`, or `backend/Larchmont.Tests` unless the test agent asks.

## Rules
- Never put secrets, passwords or connection strings in code or output. Read config from environment variables.
- Never edit V001 to V004. New schema goes in a new migration (see `.claude/rules/migrations.md`).
- Services throw domain exceptions (`NotFoundException`, `ConflictException`, `BusinessRuleException`). Do not return error codes.
- Register new repositories and services by hand in `Program.cs`.
- After each change, run `cd backend && dotnet build Larchmont.sln` and fix errors before reporting.
- Stay in scope. If the task needs something outside backend, say so and stop.
- Report: files changed, build result, anything not done.
