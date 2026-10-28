# Debug report: LH-56, occupancy report counts the checkout night

## Symptom

The occupancy report counts the night of check-out as a sold room night. Edinburgh (hotel 1) shows about 50% occupancy when the night audit shows about 32%. Full figures are in `docs/bug-LH-56.md`.

| Request | Before fix | Expected (night audit) |
|---|---|---|
| Edinburgh, 2 Oct 2026 (20 rooms) | 9 sold, 45% | 5 sold, 25% |
| Edinburgh, 1 to 7 Oct 2026 (140 room nights) | 69 nights, 49.3% | 45 nights, 32.1% |
| Group, 1 to 7 Oct 2026 (120 rooms, 840 room nights) | 334 nights, 39.8% | 226 nights, 26.9% |

## How reproduced

- **larchmont-db MCP tools: not available in this session.** The tools I had were file, search, and shell tools only. I did not query the live database.
- **Seed-data recount (read-only).** I wrote a small Python script in `C:\Users\labuser\lh56-scripts\` (outside the repo). It reads `backend/db/migrations/V004__seed_bookings.sql` as text, parses the booking rows, keeps the statuses `Confirmed`, `CheckedIn` and `CheckedOut` (the same set as `BookingStatuses.Staying`), and counts nights with the old rule (`CheckIn <= day <= CheckOut`) and the new rule (`CheckIn <= day < CheckOut`). It matched the 290 booking rows in V004 (see the limit in "Remaining risk"). Output before the fix matched the bug report exactly: 9, 69 and 334. The new rule gave 5, 45 and 226. The percentages are 25.0, 32.1 and 26.9 with 20, 140 and 840 room nights.
- **Automated test, before the fix.** The new tests in `backend/Larchmont.Tests/CheckoutNightTests.cs` failed on the old code (see "Test evidence").

## First explanation tried and discarded

My first suspect was the denominator. `docs/specs/housekeeping-board.md` (line 39) says out-of-order rooms may inflate the rooms-available figure. I discarded this because the bug's denominators are consistent with 20 rooms for Edinburgh (140 room nights for 7 days, and 9 sold on 2 Oct gives exactly 45%). So capacity was not the problem. The numerator was. I did not check the out-of-order count for Edinburgh in the seed data, so that part is not verified. Out-of-order handling is BR-20 and is a separate change.

## Root cause

Two night-counting checks used an inclusive upper bound on check-out:

1. `Services/ReportService.cs`, `GetOccupancyAsync`: `day <= b.CheckOut` counted the check-out day as sold.
2. `Repositories/BookingRepository.cs`, `ListOverlappingAsync`: `b.CheckOut >= from` returned bookings that check out on the first day of the range. Such a booking has no night inside the range, so it should not be loaded at all. With the old day-loop rule it added a phantom night on `from`. With the day-loop fix alone it would add nothing, so the repository change is a second, tighter fix and not the main one.

The other overlap queries already use the correct exclusive form: `RoomHasOverlapAsync` (line 54) and `ListForRoomAsync` (line 74) use `b.CheckOut > ...`.

## Callers checked

`ListOverlappingAsync` has two callers:

- `ReportService.GetOccupancyAsync` (hotel filter, occupancy report). Changed as intended.
- `ReportService.GetRevenueAsync` (all hotels). Its own filter is `CheckIn >= from && CheckIn <= to`. The validator requires `CheckOut > CheckIn` (`Validators`, lines 68 and 78), so every booking that passes the filter has `CheckOut > from`. The new `>` therefore removes no revenue rows for valid data. Revenue is unchanged.

`ReportService.cs` line 44 is the only place that counts nights.

## Fix

- `Services/ReportService.cs`: `day <= b.CheckOut` changed to `day < b.CheckOut`, with a comment.
- `Repositories/BookingRepository.cs`: `b.CheckOut >= from` changed to `b.CheckOut > from`.

No schema change, no migration, no change to V001 to V005.

Note on the spec reference: the bug ticket asks to see "AC-43 for the formula". AC-43 in `docs/specs/housekeeping-board.md` covers out-of-order rooms only. It does not define night counting. The night rule used here comes from the ticket's expected values and the stay semantics (a guest sleeps the night of check-in).

## Test evidence

New tests in `backend/Larchmont.Tests/CheckoutNightTests.cs`:

- `Occupancy_BookingCheckingOutOnDay_DoesNotCountThatDay`: a booking 1 to 2 Oct. Expects 1 room sold on 1 Oct and 0 on 2 Oct, 1 night total, 2.5%.
- `ListOverlapping_BookingCheckingOutOnFromDay_IsExcluded`: an InMemory database with the real `BookingRepository`. Checks a booking that checks out on the `from` day is excluded. Also checks that a booking starting on the `from` day and one starting on the `to` day are included, and one starting after `to` is excluded.

Before the fix (`dotnet test`, 74 existing tests plus 2 new):
- `Occupancy_BookingCheckingOutOnDay_DoesNotCountThatDay`: failed. Expected 0, actual 1.
- `ListOverlapping_BookingCheckingOutOnFromDay_IsExcluded`: failed. `DoesNotContain` matched the booking that checks out on the `from` day.
- Full suite: 74 passed, 2 failed (within the 76 tests in the run; the 74 were the baseline before my test file existed).

Baseline before any change (no new file): 74 passed.

After the fix: `dotnet build Larchmont.sln` gave 0 warnings and 0 errors. `dotnet test Larchmont.sln` gave 76 passed, 0 failed.

## Remaining risk

- **Not verified against the live database.** The MCP tools were not available. The recount used V004 seed text. Its row parser matched 290 booking rows. The results match the ticket's numbers exactly, which is good evidence the parser and seed agree with the reported data. A later data change or any booking row the parser skipped could still change the live numbers.
- **Endpoint not re-run after the fix.** I did not start the API and call `GET /api/reports/occupancy` against a real database. The HTTP path uses the same two code locations that the tests cover.
- **Out-of-order rooms (BR-20).** Not changed here. Edinburgh out-of-order rooms in the seed data were not checked.
- **Revenue report.** Behaviour is unchanged for valid bookings, based on the validator rule. Not tested with a zero-night booking because the validator rejects one.
- **Existing tests in `HousekeepingApiTests.AC_43` and `UnitTests` (`Occupancy_WithNoBookings`)** pass after the change. They do not test the checkout night, so they did not catch LH-56.
