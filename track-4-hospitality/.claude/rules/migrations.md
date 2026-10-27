---
paths:
  - "backend/db/migrations/**"
---

# Migration rules

- Never edit `V001__create_schema.sql`, `V002__reference_data.sql`, `V003__seed_rooms_guests_staff.sql` or `V004__seed_bookings.sql`. They are applied. Hooks block edits to them.
- New schema changes go in a new file. The next file is `V005__<snake_case_description>.sql`. Use the `new-migration` skill.
- DbUp runs embedded scripts in filename order at API startup. Skipped when the environment is `Testing`.
- Target MySQL 8.4. Column names are snake_case, matching the `LarchmontDbContext` mapping.
- Keep seed data out of schema files. No real passwords, keys or connection strings in SQL.
- After adding a column, update the matching entity in `Domain/Entities.cs` and the mapping in `Data/LarchmontDbContext.cs`.
