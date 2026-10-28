# Test report: housekeeping board (phase 9)

Branch `phase/9-tests`. Spec: `docs/specs/housekeeping-board.md` (45 acceptance criteria).
Scope: test code under `backend/Larchmont.Tests`. No production code changed.

## Results

| Run | Passed | Failed | Total |
|---|---|---|---|
| Baseline (before this work) | 191 | 0 | 191 |
| After hardening, tie-break and wrong-check fix | 192 | 0 | 192 |
| After policy unit tests (final) | 201 | 0 | 201 |

Added: 1 BR-22 tie-break test, 4 policy facts and 5 policy theory cases in `RoomStatusPolicyTests`.
Changed: AC_32, AC_38 (hardened), `Create_WhenAdultsExceedRoomCapacity_IsRejected` (fixed).

## Coverage map (AC to test method)

All 45 criteria have a test method, named `AC_nn_<Behaviour>`. Files: R = HousekeepingRulesTests, B = HousekeepingBoardTests, A = HousekeepingApiTests.

| AC | Test | File |
|---|---|---|
| 01 | AC_01_HousekeeperStartsCleaning | R |
| 02 | AC_02_DirtyCannotGoStraightToReady | R |
| 03 | AC_03_HousekeeperCannotInspect | R |
| 04 | AC_04_SupervisorInspects | R |
| 05 | AC_05_SupervisorReleasesInspectedRoom | R |
| 06 | AC_06_SimpleTurnShortcutIsAudited | R |
| 07 | AC_07_HousekeeperCannotReadyNonSimpleRoom | R |
| 08 | AC_08_SupervisorCannotSkipInspection | R |
| 09 | AC_09_MaintenanceSetsOutOfOrder | R |
| 10 | AC_10_OutOfOrderNeedsReason | R |
| 11 | AC_11_FrontDeskCannotSetOutOfOrder | R |
| 12 | AC_12_OutOfOrderCannotGoToReady | R |
| 13 | AC_13_ReleasedRoomNeedsSupervisorInspection | R |
| 14 | AC_14_ReadyByShowsOutOfOrderReturn | B |
| 15 | AC_15_OutOfOrderRoomCannotBeAssigned | A |
| 16 | AC_16_CheckInNeedsReadyRoom | A |
| 17 | AC_17_CheckInWithReadyRoomSucceeds | A |
| 18 | AC_18_CheckoutCreatesTurnaround | A |
| 19 | AC_19_SecondCheckoutDoesNotDuplicateTurnaround | A |
| 20 | AC_20_Exactly45MinutesIsNotBreach | B |
| 21 | AC_21_46MinutesIsBreach | B |
| 22 | AC_22_SuiteTargetIs75Minutes | B |
| 23 | AC_23_FamilyTargetIs75Minutes | B |
| 24 | AC_24_BreachListWorstFirst | B |
| 25 | AC_25_EmptyBreachListReturnsEmpty | B |
| 26 | AC_26_OpenRoomPastTargetIsBreach | B |
| 27 | AC_27_ReadyByForDirtyRoom | B |
| 28 | AC_28_ReadyByOverdueRoom | B |
| 29 | AC_29_ReadyByUnknownRoomIs404 | B |
| 30 | AC_30_BoardGroupedByFloor | B |
| 31 | AC_31_BusiestFloorIsMostRoomsNeedingWork | B |
| 32 | AC_32_BoardUnknownHotelIs404 | B |
| 33 | AC_33_FloorAssignmentNeedsHotelHousekeeper | A |
| 34 | AC_34_ReassignReplacesFloorOwner | A |
| 35 | AC_35_FrontDeskCannotAssignFloors | A |
| 36 | AC_36_LockWebhookNeedsApiKey | A |
| 37 | AC_37_LockCheckoutMakesRoomDirty | A |
| 38 | AC_38_LockWebhookUnknownRoomIs404 | A |
| 39 | AC_39_StaleRowVersionIs409 | R |
| 40 | AC_40_UnknownStatusListsAllowed | R |
| 41 | AC_41_StatusChangeNeedsRowVersion | R |
| 42 | AC_42_SupervisorSpotCheckClearsList | B |
| 43 | AC_43_OutOfOrderRoomsLeaveCapacity | A |
| 44 | AC_44_FrontDeskCannotSetReady | R |
| 45 | AC_45_LockEventMovesOpenStampEarlier | A |

Weak or partial criteria (test exists, assertion is thinner than the spec):
- AC-03: asserts only that the Detail contains "Supervisor". The spec does not fix the wording.
- AC-20: the spec says "the board and breach list are read". Only the breach list is asserted. The board has no breach field, so there is nothing to check on it.
- AC-32 and AC-38: were 404-only. Now hardened (see below).

## What was hardened

- **AC_32** (`HousekeepingBoardTests`): the room 101 seed is now present. Asserts 404, Detail `Hotel 999 not found.`, no `floors` or `busiestFloor` in the body, and no change to room 101 (Ready, rowVersion 1, zero status history rows). The Detail is the current wording; the spec only says 404.
- **AC_38** (`HousekeepingApiTests`): asserts 404, Detail `Room 999 not found at hotel 1.`, room 101 unchanged (Ready, rowVersion 1), and zero turnaround and zero status history rows. The Detail is the current wording; the spec only says 404.
- **New `Board_BusiestFloorTie_GoesToLowerFloor`** (BR-22): two floors with two busy rooms each. Asserts busiestFloor 1 and busy only on floor 1. AC-31 has no tie, so before this test nothing covered the tie-break (see mutation M7).

## The wrong-check test

`UnitTests.cs`, `BookingServiceTests.Create_WhenAdultsExceedRoomCapacity_IsRejected`. The test was meant to check that 5 adults in a Standard Double (max 2) is refused. It asserted `Assert.ThrowsAnyAsync<Exception>`, and it did not mock the guest, hotel or rate lookups. Moq returns null for the guest, so `BookingService` threw `NotFoundException` ("Guest 1 not found.") before the capacity rule ran. Any exception would have passed it, so it could not fail on a broken capacity rule.

Fix: the test now mocks every lookup that runs before the rule, expects `BusinessRuleException` with `Standard Double sleeps at most 2.`, and verifies `AddAsync` is never called. The same file also had a stale header comment in `HousekeepingRulesTests.cs` that said its tests "fail on purpose until the rules land". That was removed.

Checked and not changed: `UnitTests.Occupancy_WithNoBookings_IsZeroPercent` (weak but correct, since zero bookings gives zero percent); `HousekeepingApiTests.AC_43` and `CheckoutNightTests` (LH-56 coverage is in `CheckoutNightTests`, as the brief says).

## Naming

All 45 AC tests follow `AC_nn_<Behaviour>`. The three new non-AC tests follow the existing `Area_Behaviour` style (`Board_BusiestFloorTie_GoesToLowerFloor`, and the `RoomStatusPolicyTests` names). No `[Trait("Category", ...)]` was added; that is a known issue and was left alone.

## Mutation spot-check

Each mutation was a single text change to a production file, followed by a full `dotnet test Larchmont.sln`, then `git checkout` of that file. Production files were restored after every run.

| # | Rule | Mutation (file) | Caught by | Result |
|---|---|---|---|---|
| M1 | rowVersion check (BR-19) | Stale-version check removed (`HotelServices.cs`, ChangeStatusAsync) | AC_39_StaleRowVersionIs409 | Killed |
| M2 | OutOfOrder needs a reason (BR-08) | Policy reason check removed (`RoomStatusPolicy.cs`, EnsureMoveAllowed) | Before: SURVIVED. The API validator rejects a blank reason first, so AC_10 passed. After: `RoomStatusPolicyTests.OutOfOrder_BlankReason_IsRejectedByPolicyNamingReason`, `OutOfOrder_NullReason_IsRejectedByPolicy` | Killed by new tests |
| M3 | Ready needs inspection or a simple turn (BR-05, BR-06) | Simple-turn gate removed from Cleaning to Ready (`RoomStatusPolicy.cs`, Moves table) | AC_07, AC_08, AC_13 | Killed (3 tests) |
| M4 | Turnaround SLA, exactly on target is not a breach (BR-14) | `overrun <= 0` changed to `overrun < 0` (`HousekeepingServices.cs`) | AC_20, AC_22 | Killed (2 tests) |
| M5 | Floor assignment role check (BR-11, BR-17) | Caller Supervisor check removed in `FloorAssignmentService.SetAsync` | AC_35_FrontDeskCannotAssignFloors. The first attempt did not apply (CRLF line endings), and was rerun with a fixed pattern | Killed |
| M6 | Floor assignee must be a Housekeeper at this hotel (BR-17) | `user.HotelId != hotelId` removed | AC_33, `FloorAssignments_OneFloorRejected_NoFloorChanges`, `FloorAssignmentService_OneInvalidFloor_NeverCallsRepository` | Killed (3 tests) |
| M7 | Busiest floor tie goes to the lower floor (BR-22) | `count > most` changed to `count > 0 && count >= most` | Before: SURVIVED (200 of 200 passed with the pre-change board test file). After: `Board_BusiestFloorTie_GoesToLowerFloor` | Killed by new test |
| M8 | Check-in needs a Ready room (BR-21) | Ready check removed in `BookingService.CheckInAsync` | AC_16_CheckInNeedsReadyRoom | Killed |

No mutation is left surviving. M2 and M7 were survivors and each got a test.

## Remaining gaps (not covered, not fixed)

Spec items with no test:
- `POST /api/rooms/{id}/out-of-order` has no test. The AC tests reach OutOfOrder only through `PUT .../status`. The 400, 403, 404, 409 and 422 cases of this endpoint, and the optional `expectedReturn` (BR-08), are unchecked.
- Lock webhook with a wrong (non-empty) key: 401 is not tested. Only the missing header is (AC-36).
- Lock webhook with `checkedOutAt` in the future: 422 is not tested.
- `POST /api/rooms/{id}/spot-check` by a non-Supervisor: 403 is not tested.
- Floor range 0 to 60 (400) is not tested.
- BR-02: an OutOfOrder room stays OutOfOrder at checkout (through the lock webhook) is not tested.
- BR-15: ready-by for Inspected (now) and for Ready (the Ready time) is not tested.
- BR-11 role matrix: Admin (all moves), and Manager or RoomsDirector getting 403 on moves, are not tested.
- BR-22 with no busy rooms anywhere (busiestFloor null) is not specified and not tested.
- BR-23: history rows are only asserted in some tests, not for every change.
- Frontend (board polling, breach list, states) has no tests here. The scope was backend only.

Test-design limits:
- EF Core InMemory cannot roll back, so the all-or-nothing floor transaction (BR-17) is tested at service level only (`PerformanceTests`). The real MySQL transaction is not exercised.
- The clock is fixed (`FixedClock`) and all dates are fixed, so no test depends on today's date.
- The mutation run covered 8 rules. It is a spot-check, not a full mutation sweep.

## Not verified

- The frontend build and its tests were not run; they are outside this scope.
- The full baseline suite count of 191 was confirmed in this worktree. Later changes were checked only by `dotnet test Larchmont.sln` (201 passing).
