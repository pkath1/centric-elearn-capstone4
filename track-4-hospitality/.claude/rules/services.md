---
paths:
  - "backend/Larchmont.Api/Services/**"
---

# Service rules

- Each service has an interface and a class in the same file. Register new ones in `Program.cs` as scoped.
- Use repositories for data access. Do not inject `LarchmontDbContext` into services.
- Throw `NotFoundException` (404), `ConflictException` (409) or `BusinessRuleException` (422) from `Infrastructure/Exceptions.cs`. Do not return error codes.
- Validate requests with the FluentValidation validator for the request type, through `ValidateAndThrowAsync`.
- Timestamps are UTC: `DateTime.UtcNow`. Stay dates are `DateOnly`.
- Keep status changes and their history row in the same `SaveAsync` call, as `RoomService.SetStatusAsync` does.
- Add or change a test in `backend/Larchmont.Tests` for every rule you change.
