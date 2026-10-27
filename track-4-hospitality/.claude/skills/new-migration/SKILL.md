---
name: new-migration
description: Add the next DbUp migration safely for the Larchmont MySQL schema (next file is V005 and onward). Use when a task needs a new table, column, index, or seed data in backend/db/migrations.
---

# New migration

Scripts in `backend/db/migrations` are applied by DbUp at API startup, in filename order. V001 to V004 are applied and must never change.

## Steps
1. List `backend/db/migrations` and take the highest number. The new file is one higher, formatted `V00n__snake_case_description.sql`.
2. Write only the change. Use MySQL 8.4 syntax, snake_case names, and the same style as V001.
3. For a new column, add a default or allow NULL, so existing rows still load.
4. Update `Domain/Entities.cs` and, if needed, `Data/LarchmontDbContext.cs` (max lengths, indexes).
5. Do not put real passwords, keys or connection strings in the file.
6. Build and test: `cd backend && dotnet build Larchmont.sln` then `cd backend && dotnet test Larchmont.sln`. Tests use InMemory and do not run the script, so state in the summary that the SQL itself was not executed against MySQL unless you ran it.
7. Report the file name and what it changes.

## Checks
- The file name is unique and above the current highest number.
- No existing `V00n` file was edited (`git diff` shows only the new file).
- The entity and mapping match the new columns.
