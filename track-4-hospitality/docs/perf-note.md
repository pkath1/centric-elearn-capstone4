# Performance and concurrency note (phase 6)

Every number below was measured on the scratch database `larchmont_scratch` (V001 to V005 applied, six hotels) through the running API on `http://localhost:5098`. Nothing was run against the live `larchmont` database. The API was started with `ASPNETCORE_ENVIRONMENT=Testing` (DbUp skipped) and `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information`. "SQL statements" is the number of `Executed DbCommand` lines the EF Core log wrote for one request. Transactions were read from the EF log at Debug level for the `Database.Transaction` category.

Three builds were measured:

- **pre-feature**: commit `a5e5d4a` (no housekeeping board, no `row_version`).
- **HEAD**: commit `4e86fe7` (feature as handed over, before phase 6).
- **after**: this branch (`phase/6-perf`, the commit that adds this note).

Scratch data sizes: hotel 1 had 20 rooms. 80 rooms were then added to hotel 1 with `POST /api/rooms`, so hotel 1 has 100 rooms and the whole scratch database has 200 rooms. Counts quoted "at 120 rooms (all hotels)" are the counts taken before that step.

## 1. Rooms list: N+1 check

Result: **not N+1, no fix made.** The list is two statements whatever the number of rooms: one `rooms` query (with the hotel filter) and one `room_types` query. The room type name is taken from a dictionary built once in `RoomService.ListAsync`. Entities have no navigation properties, so nothing lazy-loads.

| Request | Build | Rooms in scope | SQL statements |
|---|---|---|---|
| `GET /api/rooms?hotelId=1` | HEAD | 20 | 2 |
| `GET /api/rooms` | HEAD | 120 | 2 |
| `GET /api/rooms?hotelId=1` | HEAD | 100 | 2 |
| `GET /api/rooms` | HEAD | 200 | 2 |
| `GET /api/rooms?hotelId=1` | pre-feature | 100 | 2 |
| `GET /api/rooms` | pre-feature | 200 | 2 |
| `GET /api/rooms?hotelId=1` | after | 100 | 2 |
| `GET /api/rooms` | after | 200 | 2 |

The statements are the `rooms` select with `WHERE r.hotel_id = @p ORDER BY hotel_id, room_number` and the `room_types` select with no filter.

## 2. Lost update on room status

Method: two `PUT /api/rooms/1/status` calls fired at the same moment (background `curl` processes), both with the same `rowVersion` read just before, both `OutOfOrder` with a reason, changed by user 2 (Admin). Room 1 was reset to `Dirty` before each round. Ten rounds per build (`race.sh`).

**After (HEAD and this branch, `rowVersion` checked):**

| Round | Request A | Request B | Final status | Final rowVersion |
|---|---|---|---|---|
| 1 to 10 (both builds) | 200 in 10 of 10 | 409 in 10 of 10 | OutOfOrder in 10 of 10 | sent value + 1 in 10 of 10 |

Each round adds the reset row and one status row for the winner. The 409 request wrote nothing. The 409 may come from the service's version check or from the EF concurrency exception (`DbUpdateConcurrencyException` mapped to `ConflictException`); both return the same message, so the two paths were not told apart.

**Before (pre-feature `a5e5d4a`, no concurrency token):** the request has no `rowVersion`, so the client's value was ignored.

- Both requests returned 200 in 10 of 10 rounds.
- In each round the history shows a `Dirty` to `OutOfOrder` row for the first write to commit, then an `OutOfOrder` to `OutOfOrder` row for the second. The history rows do not say which request was which. The second write was applied on top of the first without any signal to its caller, who had sent it on the basis that the room was `Dirty` (rows 241 to 270 in `room_status_history`, 10 rounds of 3 rows).
- The two writes appear to have been serialised by the row lock in every round, so the second read the first one's result. A harder lost update, where both read the same stale value, was not observed in this sample. The code path is the evidence for it: the old `RoomService.SetStatusAsync` read the room, set the status and saved, with no version comparison.

## 3. Query counts for board and SLA breaches

| Request | Rooms in hotel 1 | SQL statements (HEAD) | SQL statements (after) |
|---|---|---|---|
| `GET /api/hotels/1/board` | 20 | 6 | - |
| `GET /api/hotels/1/board` | 100 | 6 | 6 |
| `GET /api/hotels/1/sla-breaches` | 20 | 4 | - |
| `GET /api/hotels/1/sla-breaches` | 100 | 4 | 4 |

The count does not change with the number of rooms, so there is no N+1 in either query. The board's six statements are: hotel, `rooms`, `room_types`, open `room_turnarounds`, latest Ready stamps, and `floor_assignments`. The SLA list's four are: hotel, `rooms`, `room_types`, and `room_turnarounds`.

What did change is the SLA turnaround query (fix B.1). It used to read the whole turnaround history of the hotel:

```
FROM room_turnarounds WHERE EXISTS (SELECT 1 FROM rooms ... WHERE hotel_id = @p)
```

After the fix it reads only open turnarounds and closed ones checked out within `SlaLookbackDays` (30):

```
FROM room_turnarounds WHERE (is_open OR checkout_at >= @closedSince) AND EXISTS (...)
```

The scratch database holds only a few turnarounds, so no timing is quoted: a timing on this data would be noise.

## 4. Schema against the EF model

Compared: V005 and V001 to V004 as applied on `larchmont_scratch` (`information_schema`) against `LarchmontDbContext.cs` and `Domain/Entities.cs`, for `rooms`, `room_types`, `room_status_history`, `room_turnarounds`, `floor_assignments`, `room_spot_checks`, `users`, `hotels` and `bookings`.

Columns: all columns of the nine tables are mapped, with matching names, and the string lengths match (checked by reading the two column lists side by side, not by a tool). The two generated columns are not mapped, as expected: `room_turnarounds.open_room_id` and `floor_assignments.active_floor`.

Indexes and keys:

- Mapped in EF and in SQL (names differ): `rooms (hotel_id, room_number)` unique, `room_turnarounds (room_id, checkout_at)`, `floor_assignments (housekeeper_user_id)`, `room_spot_checks (status_history_id)` unique, `room_spot_checks (room_id, checked_at)`, `users (email)` unique, `hotels (code)` unique, `bookings (reference)` unique, `bookings (hotel_id, check_in)`.
- In SQL only: `ux_room_turnarounds_open_room (open_room_id)` unique and `ux_floor_assignments_active (hotel_id, active_floor)` unique. These are the database guards for "one open turnaround per room" and "one active owner per floor". EF cannot see them.

Check constraints: SQL has `ck_rooms_status` (status in Dirty, Cleaning, Inspected, Ready, OutOfOrder). EF has none. The `RoomStatuses.All` list in the code is the same five values.

Real requests on the scratch database:

| Question | Request | Result |
|---|---|---|
| Does EF try to write the generated column? | Lock checkout on room 102 (INSERT into `room_turnarounds`) | 200, `turnaroundCreated: true`. `open_room_id` read back as 2 (generated by MySQL). No error. |
| Can a status outside the CHECK reach the database? | `PUT /api/rooms/2/status` with `Clean` (the pre-V005 name) | 422 "Status must be one of: Dirty, Cleaning, Inspected, Ready, OutOfOrder." The service refuses it before SQL runs. No database error. |
| Does a fractional-second time survive? | Lock checkout with `checkedOutAt` = `2026-10-26T10:00:00.7Z` on room 106 | 200. Stored `checkout_at` = `2026-10-26 10:00:01`. Mismatch found (see below). |
| Does the unique open-turnaround key surface as a 500? | Two concurrent lock checkouts on each of rooms 103, 104, 105 (no open turnaround) | 200 and 200 each time, one row each time. The second request saw the first's open turnaround, so the unique key was not hit. Not reproduced. |

Findings:

- **No runtime error from any mismatch found.** The CHECK and the generated columns are safe for the paths the API uses.
- **Precision mismatch (not fixed).** EF writes `datetime(6)`; the `DATETIME` columns (`checkout_at`, `ready_at`, `ooo_since`, `changed_at` and others) have no fractional seconds, so MySQL rounds to the nearest second. A stamp of `10:00:00.7` is stored as `10:00:01`. The SLA minutes are computed from stored values, so this can move an elapsed time across the BR-14 boundary by up to one second. Fixing it needs a new migration (`DATETIME(6)`), which was not in this phase's list. Recommended for a later migration.
- **Check-then-insert on open turnarounds (not fixed, not reproduced).** The lock service reads the open turnaround and then inserts one. Two overlapping calls could both insert; the unique key on `open_room_id` would then reject the second with a `DbUpdateException`, which `ApiExceptionHandler` turns into a 500, not a 409. Three concurrent pairs did not overlap. Left as a known risk.

## Fixes made in phase 6 and the tests that cover them

| Fix | Code | Test (backend/Larchmont.Tests/PerformanceTests.cs unless noted) |
|---|---|---|
| B.1 SLA lookback of 30 days (open turnarounds always; closed ones only when checked out within 30 days) | `HousekeepingBoardService.SlaLookbackDays`, `ITurnaroundRepository.ListForHotelAsync(hotelId, closedSince)` | `SlaBreaches_ClosedTurnaroundOlderThanLookback_IsNotListed`, `SlaLookbackDays_IsThirtyDays`; existing AC_20 to AC_26 still pass |
| B.2 Floor assignments in one transaction | `FloorAssignmentRepository.ReplaceActiveForFloorsAsync` (one `BeginTransactionAsync`, two saves inside it, one commit); the service makes one call | `FloorAssignments_OneFloorRejected_NoFloorChanges`, `FloorAssignments_TwoFloors_BothChangeInOneSetCall`, `FloorAssignmentService_OneInvalidFloor_NeverCallsRepository`, `FloorAssignmentService_ValidFloors_ReplacedInOneRepositoryCall` |
| B.3 Validators instead of inline checks | `RoomService.SetStatusAsync`, `SetOutOfOrderAsync` use `RoomStatusRequestValidator` and `OutOfOrderRequestValidator`; `LockIntegrationService.CheckoutAsync` uses `LockCheckoutRequestValidator` | `LockCheckout_WhitespaceRoomNumber_Is400NamingRoomNumber`, `LockCheckout_HotelIdZero_Is400NamingHotelId`, `SetStatus_ReasonLengthCountedAfterTrim`; existing AC_10, AC_41 and lock tests |

### Floor assignments on MySQL (evidence for B.2)

Replacing both floors of hotel 1 (floor 1 to user 6, floor 2 to user 5, both previously held by other users):

| Build | SQL statements | Transaction in EF log |
|---|---|---|
| HEAD | 11 (one `UPDATE` and one `INSERT` per floor, each its own save) | none |
| after | 8 | `Beginning transaction`, `Began transaction`, one savepoint per save, `Committing transaction`, `Committed transaction` |

What is not shown on MySQL: a rollback. No failure can be caused through the API after the pre-checks (every user and floor is validated before the first write, and the validator rejects duplicate floors), so the rollback path was not triggered on the scratch database. The InMemory provider has no transactions, so `ApiFactory` ignores its transaction warning (test-only); the tests above therefore prove that an invalid floor writes nothing, and that all floors go through one repository call. The all-or-nothing rollback is by MySQL transaction semantics and is not measured here.

### Validators (B.3): behaviour changes to know about

These come from using the existing validators, and are not business-rule changes:

- Lock checkout 400 body now names `RoomNumber` and `HotelId` (PascalCase, as the other validators do) instead of `roomNumber` and `hotelId`. Field names match case-insensitively.
- `SetStatus` to `OutOfOrder` without a reason is refused by the validator (400, field `reason`) before the move table is read. Before, a missing reason on an illegal move gave 422. The message text is the validator's.
- `changedByUserId` of 0 or less, or `rowVersion` of 0 or less, now gives 400 from the validator. Before, `changedByUserId` 0 gave 404 (no such user) and `rowVersion` 0 gave 409 (stale) on the out-of-order route. Not tested directly.
- A reason of 256 characters with leading or trailing spaces is still checked after trimming, as before.

## Commands and results

- `cd backend && dotnet build Larchmont.sln`: succeeded, 0 warnings, 0 errors.
- `cd backend && dotnet test Larchmont.sln`: 74 passed (65 existing plus 9 in `PerformanceTests.cs`).
- Measurements: `curl` against the API on port 5098, EF SQL statements counted from the API console log.
- Race: 10 rounds per build with two concurrent `PUT` calls.

## Not done

- A MySQL rollback test for floor assignments (see above).
- The `DATETIME` precision migration and the open-turnaround check-then-insert fix (both recorded above, not in scope).
- Timings. Only statement counts and response codes were measured.
