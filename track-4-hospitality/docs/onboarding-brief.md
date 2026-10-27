# Onboarding brief: Larchmont Hotels

Written from reading the code in this folder. Every file and method named here was checked.

## Architecture in brief

Two folders do the work. `backend/` is a .NET 8 MVC API with its tests. `frontend/` is a React 19 single-page app built with Vite and TypeScript. In development, Vite forwards `/api` to the API on `http://localhost:5080` (`frontend/vite.config.ts`, line 8). The API allows CORS only from `http://localhost:5173`.

The API has four layers, each in its own folder under `backend/Larchmont.Api`:

| Layer | Folder | Job |
|---|---|---|
| Controllers | `Controllers/` | HTTP routes and model binding. Thin: they call one service each. |
| Services | `Services/` | Business rules. Each service has an interface and a class in the same file. |
| Repositories | `Repositories/` | Data access through `LarchmontDbContext` (EF Core). |
| Data and domain | `Data/`, `Domain/` | `LarchmontDbContext` maps tables and columns. `Domain/Entities.cs` holds the entities. |

Supporting folders: `Dtos/` (request and response records), `Mappers/` (entity to DTO extension methods), `Validators/` (FluentValidation), `Infrastructure/` (exception types, `ApiExceptionHandler`, password hashing).

### Startup (`Program.cs`)
1. Reads `ConnectionStrings:Larchmont`. Startup throws if it is missing.
2. Registers MVC controllers, ProblemDetails, Swagger, CORS, and `ApiExceptionHandler`.
3. Registers `LarchmontDbContext` with `UseMySql` and server version 8.4.0.
4. Registers validators with `AddValidatorsFromAssemblyContaining<Program>`.
5. Registers repositories and services as scoped, one line each (about lines 24 to 40). A new repository or service needs a line here.
6. Unless the environment is `Testing`, runs DbUp: `DeployChanges.To.MySqlDatabase(...).WithScriptsEmbeddedInAssembly(...)`. The API throws if the migration fails.
7. Pipeline: `UseExceptionHandler`, `UseCors`, Swagger, `MapControllers`.

## Entities

Defined in `Domain/Entities.cs`. Tables and columns come from `Data/LarchmontDbContext.cs`, which maps names to snake_case.

- **Room** (`rooms`): `HotelId`, `RoomTypeId`, `RoomNumber`, `Floor`, `Status`, `Notes`. `Status` is one of `Dirty`, `Cleaning`, `Inspected`, `Ready`, `OutOfOrder` (`RoomStatuses`, widened in V005). The unique key is (hotel, room number).
- **RoomStatusEvent** (`room_status_history`): one row per status change, with `FromStatus`, `ToStatus`, `ChangedByUserId`, and `ChangedAt`.
- **Booking** (`bookings`): reference, channel, hotel, guest, room type, optional room, rate plan, `CheckIn` and `CheckOut` (`DateOnly`), adults, status (`BookingStatuses`), total amount. `Nights` is calculated and not stored.
- **User** (`users`): email (unique), display name, role, optional hotel, password hash, active flag, last login.
- Also: Hotel, RoomType, RatePlan, RoomRate, Guest, BookingNote.

## Migrations

- Scripts live in `backend/db/migrations`: `V001__create_schema.sql`, `V002__reference_data.sql`, `V003__seed_rooms_guests_staff.sql`, `V004__seed_bookings.sql`, `V005__housekeeping_board.sql`.
- The csproj embeds `..\db\migrations\*.sql` as resources. DbUp runs them in filename order at startup.
- V001 to V005 are applied. Do not edit them. The next file is V006.

## Tests

`backend/Larchmont.Tests` uses xUnit, Moq (`UnitTests.cs`), and EF Core InMemory. `ApiFactory` swaps the MySQL context for an InMemory one and sets the environment to `Testing`, which skips DbUp. There are 20 tests and no database is needed to run them. Test names mix styles, for example `Hotels_List_ReturnsSeededHotel` and `Cancel_TwoDaysBeforeArrival_HasNoFee`. There are no category traits.

## Traced request: `PUT /api/rooms/{id}/status`

This request sets a room's housekeeping status. No screen in `frontend/` calls it yet, and `frontend/src/api/client.ts` only has a GET helper (`getJson`). So the trace starts at HTTP, not the browser.

1. **Vite dev proxy.** A request to `/api/...` from the browser on port 5173 goes to the API on port 5080 (`frontend/vite.config.ts`).
2. **Middleware.** `Program.cs` runs `UseExceptionHandler`, `UseCors`, Swagger, then `MapControllers`.
3. **Routing.** `RoomsController` in `Controllers/HotelControllers.cs` is declared with `[Route("api/rooms")]`. The action is `SetStatus(int id, RoomStatusRequest request)` with `[HttpPut("{id:int}/status")]`.
4. **Model binding.** The JSON body binds to `RoomStatusRequest(string Status, int? ChangedByUserId)` in `Dtos/Dtos.cs`. No validator runs on this request, so the service does the checks.
5. **Service.** The controller calls `IRoomService.SetStatusAsync`. The implementation is `RoomService.SetStatusAsync` in `Services/HotelServices.cs`:
   - If `Status` is not in `RoomStatuses.All`, it throws `BusinessRuleException` (422).
   - It loads the room with `_rooms.GetAsync(id)`. If missing, it throws `NotFoundException` (404).
   - It saves the old status, sets the new one, and queues a `RoomStatusEvent` with `ChangedAt = DateTime.UtcNow`.
   - It calls `_rooms.SaveAsync()`, then builds the response.
6. **Repository.** `RoomRepository` in `Repositories/HotelRepositories.cs`:
   - `GetAsync` runs `_db.Rooms.FirstOrDefaultAsync`.
   - `AddStatusEventAsync` runs `_db.RoomStatusHistory.Add` and does not save.
   - `SaveAsync` runs `_db.SaveChangesAsync`. One call writes an UPDATE to `rooms` and an INSERT to `room_status_history` together.
7. **Database.** The MySQL tables are `rooms` and `room_status_history` (`V001__create_schema.sql`, lines 59 to 81). `room_status_history` has a foreign key on `room_id` only. `changed_by_user_id` has no foreign key.
8. **Response.** `RoomService.ToDtoAsync` loads the room type name through `_types.GetAsync`. `Mappers/Mappers.cs` `RoomMapper.ToDto` builds the `RoomDto`, which is returned as JSON with 200.
9. **Errors.** Exceptions from any layer go to `ApiExceptionHandler` (`Infrastructure/Exceptions.cs`), which writes a ProblemDetails body with the matching status code.

### Gaps seen on this path
- Any status in the allowed list can move to any other. There are no transition rules.
- There is no concurrency token, so two writes can overwrite each other.
- The request has no validator, unlike rooms and hotels.
- `ChangedByUserId` is not checked against `users`, and the database has no foreign key for it.

## Known issues
- CI (`.github/workflows/ci.yml`) is broken: .NET 6 is set but the project targets net8.0, and the frontend job uses `working-directory: web` where the folder is `frontend`.
- Committed credentials are in `backend/Larchmont.Api/appsettings.Development.json`, and a root password is in `compose.yaml`. Values are not recorded here. They need rotating and moving to environment variables.
- Stay dates are `DateOnly`, timestamps are UTC. The brief says "dates stored UTC"; the code does that for timestamps only.
