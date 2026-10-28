# Larchmont Hotels - agent briefing

Paths below are relative to this folder (`track-4-hospitality/`), which holds `.claude/`.

## Stack
- API: .NET 8 MVC controllers (`[ApiController]`, not minimal APIs) in `backend/Larchmont.Api`.
- Data: EF Core 8 on MySQL (Pomelo provider). Dapper and SQL Server are not used.
- Migrations: DbUp, embedded from `backend/db/migrations/*.sql`, run on API startup (skipped when environment is `Testing`).
- Tests: xUnit, Moq, EF Core InMemory (`backend/Larchmont.Tests`).
- Frontend: React 19 + Vite 6 + TypeScript in `frontend/` (not CRA, not `client/`). Vite proxies `/api` to `http://localhost:5080`.

## Commands
- Build API: `cd backend && dotnet build Larchmont.sln`
- Test: `cd backend && dotnet test Larchmont.sln`
- Run API: `cd backend && dotnet run --project Larchmont.Api` (serves `http://localhost:5080`). Needs the `ConnectionStrings__Larchmont` environment variable; startup throws if it is missing.
- MCP tests: `dotnet test mcp/Larchmont.Mcp.sln`
- Frontend install: `cd frontend && npm ci`
- Frontend dev server: `cd frontend && npm run dev` (port 5173)
- Frontend build: `cd frontend && npm run build` (`tsc -b && vite build`)

## Layout
- `backend/Larchmont.Api/Controllers` - MVC controllers, routes `api/...`
- `backend/Larchmont.Api/Services` - business logic (interfaces and implementations together)
- `backend/Larchmont.Api/Repositories` - EF Core data access
- `backend/Larchmont.Api/Domain/Entities.cs` - entities (Room, Booking, User, and others)
- `backend/Larchmont.Api/Data/LarchmontDbContext.cs` - table and column mapping (snake_case)
- `backend/Larchmont.Api/Validators`, `Dtos`, `Mappers`, `Infrastructure` (exceptions, password hashing)
- `backend/db/migrations` - DbUp scripts V001 to V005 (applied; never edit). The next file is V006
- `mcp/` - .NET stdio MCP server (`Larchmont.Mcp`, tests in `mcp/tests`); launched by `mcp/run-mcp.cmd` on Windows (`.mcp.json`; it needs Git for Windows and calls `mcp/run-mcp.sh`), which loads gitignored `mcp/.env` (copy `mcp/.env.example`; needs `LARCHMONT_MCP_CONNECTION` and `LARCHMONT_MCP_HOTEL_ID`). Views in `db-setup/02-mcp-views.sql`, user in `db-setup/03-create-mcp-user.sql`, see `docs/mcp-security.md`
- `frontend/src` - `api/` (fetch helpers), `pages/`, `components/`, `hooks/`

## Conventions
- Services throw `NotFoundException` (404), `ConflictException` (409), `BusinessRuleException` (422). `ApiExceptionHandler` turns them into ProblemDetails.
- Validation uses FluentValidation validators registered from the assembly.
- Services use repositories, not `LarchmontDbContext` directly.
- Repositories and services are registered by hand in `Program.cs`. Add new ones there.
- Timestamps use UTC (`DateTime.UtcNow`). Stay dates are `DateOnly` with no timezone.
- Migrations: never edit V001 to V005. The next file is V006. See `.claude/rules/migrations.md`.

## Known issues
- CI (`.github/workflows/ci.yml`) now uses .NET 8 and `frontend`, but has not run on GitHub yet.
- `compose.yaml` reads `MYSQL_ROOT_PASSWORD` from the environment (fixed). Committed credentials still exist in `backend/Larchmont.Api/appsettings.Development.json` and `db-setup/01-create-users.sql`, and all old ones are in git history. Do not copy them. They need rotating, moving to environment variables, and a history purge. See `docs/review-report.md`.
- Swagger is enabled only in the Development environment.
- Tests have no `Category` traits, so filters such as `--filter Category=Unit` select nothing.
