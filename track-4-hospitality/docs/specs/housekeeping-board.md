# Spec: Housekeeping and room-status board

Source: `docs/stakeholder-brief.md` (Sofia Lindqvist, Director of Rooms, 19 October 2026).
Status: draft. Open questions are pending a mentor reply.

## Overview
Larchmont's front desk sold and checked in guests to rooms still being cleaned, and sold a room that engineers had taken out of service. Housekeeping coordinates by walkie-talkie and a whiteboard. This feature puts every room into a tracked status, enforces a cleaning and inspection path, stops the desk selling or assigning rooms that are not Ready, measures turnaround against a target, and gives the desk a single "when will this room be ready?" answer.

For: Rooms directorate, supervisors, housekeepers, maintenance, front desk.

In scope:
- Room statuses Dirty, Cleaning, Inspected, Ready, OutOfOrder, with legal moves per role.
- Checkout creates a turnaround with a stored checkout stamp and an SLA.
- Breach list, ready-by answer, board by floor, floor assignments.
- Check-in and room assignment rules that depend on room status.
- Lock checkout webhook (see "Problems found in the brief": no lock integration exists in the code).
- Occupancy: out-of-order rooms leave the available capacity.

Out of scope:
- Tracking housekeepers' location, shifts or breaks (see "Not built, and why").
- The occupancy night-counting error in ticket LH-56 (separate fix, Task 5).
- Authentication. The API has none today; see Assumptions.
- Any change to the staff app beyond the screens listed under UI.

## Problems found in the brief

Each item quotes the brief, says what is wrong, and gives a recommendation. Decisions are listed in Open questions.

1. **The Ready contradiction.** Brief: "Nothing goes to Ready without a supervisor inspection, no exceptions." Brief also: "Housekeepers should be able to mark a room Ready themselves when it is a simple turn". These cannot both be true. Recommendation: allow the housekeeper shortcut only for simple turns, record every shortcut with a reason, and put it on a supervisor spot-check list. Sofia must confirm this is acceptable (Q2).

2. **Off-clock and break tracking.** Brief: "Some of them pick up extra shifts without clocking in, and I want to see them too, and during their breaks. If they do not like it, that is a conversation for HR." This cannot be built lawfully (see "Not built, and why").

3. **The lock integration does not exist.** Brief: "the lock system already knows. I want the board to turn the room Dirty the instant that happens, and to show on screen without anybody pressing refresh." No code in the repo talks to a lock system, and no vendor contract or payload is in the repo. Recommendation: build the webhook endpoint with an API key (AC-36 to AC-38) and board polling. "Instant" becomes a 15-second poll on the board. Sofia must confirm what the lock vendor can send (Q10).

4. **45 minutes versus 75 minutes.** Brief: "I want a turnaround target of 45 minutes from checkout to Ready, for every room type, everywhere." Brief also: "for those two types the target is 75 minutes." The second sentence overrides the first for Suite and Family. Recommendation: 45 minutes by default, 75 for Suite (STE) and Family Room (FAM), stored per room type so the number can change without code (Q4).

5. **When the clock starts.** Brief: "The clock should start when the guest leaves." The system cannot see when a guest physically leaves. It can see the lock event and the desk's checkout. Recommendation: the stamp is the lock event time when a lock event arrives, otherwise the desk check-out time. It is stored once and never recalculated (Q3).

6. **Out-of-order rooms and occupancy.** The brief does not say how out-of-order rooms affect occupancy. The brief's complaint is: "The Monday occupancy report looks wrong to me. It said Edinburgh was close to fifty percent last week." The code counts all rooms as available (`RoomRepository.CountForHotelAsync`), so out-of-order rooms inflate the denominator. The ticket LH-56 shows the larger cause: the report counts the checkout night as a sold night (`ReportService`, `day <= b.CheckOut`). This spec decides only the out-of-order part (BR-20, Q6).

7. **Missing Supervisor and Maintenance roles.** Brief: "a supervisor checks it and it becomes Inspected" and "Maintenance should be able to put a room out of order with a reason". The user role validator (`Validators.cs`, `UserRequestValidator`) allows only Admin, RoomsDirector, Manager, FrontDesk and Housekeeper. Recommendation: add Supervisor and Maintenance (V005 and the validator).

8. **Busiest floor is undefined.** Brief: "The busiest floors should be easy to see." No measure is given. Default in BR-22 (Q5).

9. **"Simple turn" is undefined.** Brief: "when it is a simple turn". No definition is given. Default in Assumptions (Q1).

10. **Who may set out of order.** Brief names Maintenance only. Default: Maintenance and Supervisor (Q8).

## Business rules

- BR-01 Every room has exactly one status: Dirty, Cleaning, Inspected, Ready or OutOfOrder. Existing rooms with the old value Clean are read as Ready.
- BR-02 A guest checkout moves the room to Dirty from any status except OutOfOrder, and creates a turnaround record with a stored checkout stamp. A room that is OutOfOrder stays OutOfOrder at checkout.
- BR-03 A Housekeeper may move Dirty to Cleaning.
- BR-04 Only a Supervisor may move Cleaning to Inspected, or Inspected back to Cleaning (failed inspection).
- BR-05 Ready is reached only from Inspected, moved by a Supervisor. The only exception is BR-06.
- BR-06 Simple-turn shortcut: a Housekeeper may move Cleaning to Ready only when the room's simple_turn flag is true. Each shortcut is written to the status history with reason "SimpleTurnShortcut" and the user, and is added to the supervisor spot-check list until a Supervisor marks it checked.
- BR-07 A room that has been OutOfOrder never uses the shortcut in BR-06. Released from OutOfOrder it goes to Dirty, then Cleaning, then Inspected by a Supervisor, then Ready.
- BR-08 Maintenance or a Supervisor may move a room to OutOfOrder from any status except OutOfOrder itself. The reason is required, non-blank, and at most 255 characters. The expected return time is optional.
- BR-09 An OutOfOrder room is not for sale. It cannot be assigned to a booking and cannot be checked into.
- BR-10 An illegal move returns 422. The message names the statuses the room can move to from its current status, for the caller's role.
- BR-11 A move by a role that is not allowed for that move returns 403, and names the roles that may make it.
- BR-12 Turnaround target: 45 minutes per room type. Suite (STE) and Family Room (FAM) have 75 minutes. The target is stored on the room type.
- BR-13 The turnaround clock starts at the stored checkout stamp and stops when the room reaches Ready. A room that has not reached Ready keeps running.
- BR-14 A breach is a turnaround where elapsed minutes are more than the target. Exactly on the target is not a breach. The breach list is ordered by overrun (elapsed minus target), largest first.
- BR-15 Ready-by answer for one room: Dirty or Cleaning gives checkout stamp plus target, or now plus 15 minutes if that is already past (flagged overdue). Ready gives the Ready time. OutOfOrder gives the expected return time and no ready time. Inspected gives now.
- BR-16 The board shows one hotel at a time, grouped by floor, with every room on it.
- BR-17 Each floor of a hotel has at most one assigned housekeeper at a time. The assignee must be an active Housekeeper whose hotel is that hotel.
- BR-18 The lock checkout webhook needs the API key from configuration. A checkout for a room that already has an open turnaround does not create a second one.
- BR-19 Room status changes are concurrency-checked by row version. A stale version returns 409.
- BR-20 Out-of-order rooms are not counted in the rooms-available figure of the occupancy report.
- BR-21 A booking can be checked in only when its assigned room is Ready.
- BR-22 Busiest floor on the board is the floor with the most rooms in Dirty or Cleaning. Ties go to the lower floor number.
- BR-23 Every status change writes one status history row with the previous and new status, the user (when known), the time (UTC) and the reason (when given).
- BR-24 The system does not record where a housekeeper is, or whether they are on shift or on break.

### Status moves and roles

| From | To | Allowed roles | Notes |
|---|---|---|---|
| (checkout) | Dirty | system | BR-02, creates turnaround |
| Dirty | Cleaning | Housekeeper, Supervisor | sets simple_turn at start (Q1) |
| Cleaning | Inspected | Supervisor | BR-04 |
| Cleaning | Ready | Housekeeper | only if simple_turn, BR-06 |
| Inspected | Ready | Supervisor | BR-05 |
| Inspected | Cleaning | Supervisor | failed inspection |
| Any except OutOfOrder | OutOfOrder | Maintenance, Supervisor | reason required, BR-08 |
| OutOfOrder | Dirty | Maintenance, Supervisor | release, BR-07 |

Any other move is illegal and returns 422 (BR-10).

## Users

| Role | Status: may move | Other |
|---|---|---|
| Housekeeper | Dirty to Cleaning; Cleaning to Ready (simple turn only) | View own floor board. Cannot set OutOfOrder or Inspected. |
| Supervisor | Dirty to Cleaning; Cleaning to Inspected; Inspected to Ready or Cleaning; set and release OutOfOrder | Assign floors; mark spot-checks; view breach list and board. |
| Maintenance | Set and release OutOfOrder | Cannot inspect or set Ready. |
| FrontDesk | None | View board, ready-by answer; check-in and assign rooms (subject to BR-09 and BR-21). |
| Manager, RoomsDirector | None | View board, breach list, occupancy. |
| Admin | All moves | Everything above. |

The role of the caller is the role on the user record named in `changedByUserId`. See Assumptions for why this is weak until authentication exists.

## API

Times are UTC ISO 8601 strings. Errors use ProblemDetails, as the existing `ApiExceptionHandler` produces. New error type: `ForbiddenException` maps to 403 (new).

### Changed: `PUT /api/rooms/{id}/status`
Request: `{ "status": "Cleaning", "changedByUserId": 5, "rowVersion": 3, "reason": null }`. `reason` is optional, and required when status is OutOfOrder. `rowVersion` is required.
Response 200: room DTO with `status`, `rowVersion`, `simpleTurn`, `oooReason`, `oooSince`, `oooExpectedReturn`.
Errors:
- 400 validation (missing `rowVersion`, `changedByUserId`, or blank `reason` for OutOfOrder).
- 403 role may not make this move; Detail names the allowed roles.
- 404 room not found.
- 409 `rowVersion` does not match the stored row; Detail "Room {n} changed. Reload and try again."
- 422 illegal move (BR-10) or unknown status (AC-40).

### New: `POST /api/rooms/{id}/out-of-order`
Request: `{ "changedByUserId": 7, "reason": "Leak in ensuite", "expectedReturn": "2026-11-03T12:00:00Z", "rowVersion": 4 }`.
Response 200: room DTO. Same error codes as above (400, 403, 404, 409). The 422 applies to rooms that are already OutOfOrder.

### New: `GET /api/rooms/{id}/ready-by`
Response 200: `{ "roomId", "roomNumber", "status", "readyBy": "...", "overdue": false, "outOfOrder": false, "returnsAt": null }`.
- Dirty or Cleaning: `readyBy` per BR-15, `overdue` true when the target has passed.
- Inspected: `readyBy` is now.
- Ready: `readyBy` is the Ready time.
- OutOfOrder: `outOfOrder` true, `returnsAt` = expected return (null if not known), `readyBy` null.
Errors: 404 room not found.

### New: `GET /api/hotels/{id}/board`
Response 200: `{ "hotelId", "busiestFloor", "floors": [ { "floor", "busy": true, "rooms": [ room DTO with status, roomNumber, roomType, checkoutAt, targetMinutes, readyBy, housekeeperUserId ] } ] }`.
Floors ascending; rooms by room number within a floor. `busy` true on the busiest floor only (BR-22).
Errors: 404 hotel not found.

### New: `GET /api/hotels/{id}/sla-breaches`
Response 200: `{ "items": [ { "roomId", "roomNumber", "roomType", "checkoutAt", "readyAt": null, "targetMinutes", "elapsedMinutes", "overrunMinutes" } ] }`. Open rooms (no Ready time) have `readyAt` null and elapsed measured to now. Ordered by `overrunMinutes` descending.
Errors: 404 hotel not found.

### New: `GET /api/hotels/{id}/spot-checks`
Response 200: `{ "items": [ { "roomId", "roomNumber", "readyAt", "changedByUserId" } ] }`. Simple-turn shortcut moves not yet checked.

### New: `POST /api/rooms/{id}/spot-check`
Request: `{ "changedByUserId": 2 }`. Caller must be a Supervisor (403 otherwise). Response 200. 404 if the room has no open spot-check. Room leaves the list.

### New: `PUT /api/hotels/{id}/floor-assignments`
Request: `{ "changedByUserId": 2, "assignments": [ { "floor": 2, "housekeeperUserId": 5 } ] }`.
Response 200: the full assignment list for the hotel. Replaces the floor owner for each floor sent (BR-17).
Errors: 400 floor outside 0 to 60 (matches existing room validator) or missing user id; 403 caller not a Supervisor; 404 hotel or user not found; 422 user is not an active Housekeeper at this hotel.

### New: `GET /api/hotels/{id}/floor-assignments`
Response 200: list of floor and housekeeper pairs for the hotel.

### New: `POST /api/integrations/lock/checkout`
Header: `X-Api-Key` (required). The value must match the `Lock:ApiKey` setting, read from an environment variable in production. It is not committed.
Request: `{ "hotelId": 1, "roomNumber": "101", "checkedOutAt": "2026-10-27T10:00:00Z" }`. `checkedOutAt` is optional; when absent, the server uses its own UTC now. Payload shape is an assumption until the vendor confirms (Q10).
Response 200: `{ "roomId", "status": "Dirty", "turnaroundCreated": true }`. `turnaroundCreated` false when an open turnaround already existed (BR-18).
Errors: 400 missing or invalid body; 401 missing or wrong key; 404 hotel or room not found; 422 `checkedOutAt` in the future.

### Changed: booking endpoints
- `POST /api/bookings/{id}/check-in`: 422 when the assigned room is not Ready (BR-21, AC-16).
- `POST /api/bookings/{id}/check-out`: as before, plus creates the turnaround record (BR-02).
- `POST /api/bookings/{id}/assign-room`: 422 when the room is OutOfOrder (BR-09, AC-15).

### Changed: `GET /api/reports/occupancy`
Response adds `roomsOutOfOrder` (count, for information). `roomsAvailable` excludes OutOfOrder rooms (BR-20). Night counting is unchanged here; it belongs to LH-56.

## Data model (planned V005)

Words only; the SQL is written in Task 4.

- Rooms: the status column widens to the five values in BR-01. Existing rows with Clean are set to Ready. New columns: `ooo_reason` (text, 255, nullable), `ooo_since` (UTC, nullable), `ooo_expected_return` (UTC, nullable), `simple_turn` (flag, default false), `row_version` (integer, incremented on every change, used as the concurrency token).
- Room types: new `sla_minutes` integer. Set to 75 for Suite (STE) and Family Room (FAM), 45 for the rest.
- Room turnarounds (new table): one row per checkout. Holds room, booking, checkout stamp (UTC), ready stamp (UTC, nullable), target minutes copied from the room type at checkout, and an open flag. At most one open row per room.
- Floor assignments (new table): hotel, floor, housekeeper user, active flag. At most one active row per hotel and floor (BR-17).
- Room status history: new nullable `reason` column. Existing rows are unchanged.
- Users: role values Supervisor and Maintenance added to the allowed list (existing rows unchanged).
- Spot-check (new table or flag on the history row): room, status history row, checked-by user, checked-at. Simple-turn shortcut moves with no checked row appear on the list (AC-42).

## UI

Two screens, both per hotel. Both need loading, empty and error states.

- **Live board by floor.** Hotel selector (default: first hotel the user may see). Floors as columns or sections, rooms as tiles with room number, room type, status and ready-by time. The busiest floor is highlighted (BR-22). Refreshes every 15 seconds (polling; no push). States: loading ("Loading board"), empty ("No rooms at this hotel"), error ("Board unavailable. Retry").
- **SLA breach list.** Same hotel selector. Table ordered worst first: room, type, checkout time, target, elapsed, overrun. States: loading, empty ("No breaches right now"), error.
- The ready-by answer appears in the room tile, and as a lookup box on the board: enter a room number, see "Ready at 14:20" or "Out of order until 3 November, 12:00".

## Acceptance Criteria

Times below are UTC on 27 October 2026 unless stated. Each criterion maps to one planned test (Traceability).

- AC-01 Given a room in Dirty, When a Housekeeper sets status Cleaning with the current rowVersion, Then the response is 200, status is Cleaning, rowVersion is one higher, and one status history row Dirty to Cleaning exists.
- AC-02 Given a room in Dirty, When a Housekeeper sets status Ready, Then the response is 422 and Detail is "Dirty rooms can move to: Cleaning."
- AC-03 Given a Cleaning room with simple_turn false, When a Housekeeper sets status Inspected, Then the response is 403 and Detail names the Supervisor role.
- AC-04 Given a Cleaning room with simple_turn false, When a Supervisor sets status Inspected, Then the response is 200 and status is Inspected.
- AC-05 Given an Inspected room, When a Supervisor sets status Ready, Then the response is 200, status is Ready, and the history row has the Supervisor's user id and no reason.
- AC-06 Given a Cleaning room with simple_turn true, When a Housekeeper sets status Ready, Then the response is 200, status is Ready, the history row has reason "SimpleTurnShortcut", and the room appears in the spot-check list.
- AC-07 Given a Cleaning room with simple_turn false, When a Housekeeper sets status Ready, Then the response is 422 and Detail is "Only a Supervisor can mark this room Ready after inspection. Cleaning rooms can move to: Inspected."
- AC-08 Given a Cleaning room with simple_turn false, When a Supervisor sets status Ready, Then the response is 422 and Detail is "Cleaning rooms can move to: Inspected."
- AC-09 Given a Ready room, When a Maintenance user sets status OutOfOrder with reason "Leak in ensuite", Then the response is 200, `oooReason` is "Leak in ensuite", and `oooSince` is set to the current UTC time.
- AC-10 Given a Ready room, When a Maintenance user sets status OutOfOrder with an empty reason, Then the response is 400 and the error names the field "reason".
- AC-11 Given a Ready room, When a FrontDesk user sets status OutOfOrder, Then the response is 403 and the room is unchanged.
- AC-12 Given an OutOfOrder room, When a Maintenance user sets status Ready, Then the response is 422 and Detail is "OutOfOrder rooms can move to: Dirty."
- AC-13 Given an OutOfOrder room, When a Maintenance user sets status Dirty, then Cleaning by a Housekeeper, and then a Housekeeper sets status Ready, Then the Ready move is 422 and the room stays Cleaning, because it was released from OutOfOrder and simple_turn is false.
- AC-14 Given an OutOfOrder room with expected return 2026-11-03T12:00:00Z, When `GET /api/rooms/{id}/ready-by` is called, Then the response is 200 with `outOfOrder` true, `returnsAt` "2026-11-03T12:00:00Z" and `readyBy` null.
- AC-15 Given a Confirmed booking at hotel 1 for a Standard Double and room 101 (same hotel and type) in OutOfOrder, When `POST /api/bookings/{id}/assign-room` is called with roomId 101, Then the response is 422 and Detail is "Room 101 is out of order and cannot be assigned."
- AC-16 Given a Confirmed booking assigned to room 101 that is Dirty, When `POST /api/bookings/{id}/check-in` is called, Then the response is 422 and Detail is "Room 101 is Dirty. Check-in needs a Ready room." and the booking stays Confirmed.
- AC-17 Given a Confirmed booking assigned to room 101 that is Ready, When check-in is called, Then the response is 200 and the booking status is CheckedIn.
- AC-18 Given a CheckedIn booking in room 101 (Ready), When `POST /api/bookings/{id}/check-out` is called, Then the response is 200, the booking is CheckedOut, the room is Dirty, and one room turnaround row exists with checkout stamp equal to the Dirty history row time and target 45.
- AC-19 Given room 101 has an open turnaround, When the lock checkout webhook is called for room 101 with a valid key, Then the response is 200 with `turnaroundCreated` false, the turnaround count for room 101 is still 1, and the room is still Dirty.
- AC-20 Given a Standard Double with checkout stamp 10:00:00 and Ready at 10:45:00, When the board and breach list are read, Then the room is not in the breach list.
- AC-21 Given a Standard Double with checkout stamp 10:00:00 and Ready at 10:46:00, When the breach list is read, Then the room is listed with `overrunMinutes` 1.
- AC-22 Given a Suite with checkout stamp 10:00:00, When it is marked Ready at 11:15:00, Then it is not in the breach list; and Given the same room marked Ready at 11:16:00, When the breach list is read, Then it is listed with `overrunMinutes` 1.
- AC-23 Given a Family Room with checkout stamp 10:00:00 and Ready at 11:16:00, When the breach list is read, Then the room is listed with `targetMinutes` 75 and `overrunMinutes` 1.
- AC-24 Given three open breaches with overruns of 10, 40 and 25 minutes, When the breach list is read, Then the items are in the order 40, 25, 10.
- AC-25 Given no open breaches at hotel 1, When `GET /api/hotels/1/sla-breaches` is called, Then the response is 200 with `items` an empty array.
- AC-26 Given a Standard Double Dirty since checkout stamp 11:00:00 and the time is 11:50:00, When the breach list is read, Then the room is listed with `readyAt` null, `elapsedMinutes` 50 and `overrunMinutes` 5.
- AC-27 Given a Standard Double Dirty with checkout stamp 20 minutes ago, When `GET /api/rooms/{id}/ready-by` is called, Then the response is 200 with `readyBy` 25 minutes from now and `overdue` false.
- AC-28 Given a Cleaning room whose target passed 10 minutes ago, When `GET /api/rooms/{id}/ready-by` is called, Then the response is 200 with `overdue` true and `readyBy` 15 minutes from now.
- AC-29 Given no room with id 999999, When `GET /api/rooms/999999/ready-by` is called, Then the response is 404 with Detail "Room 999999 not found."
- AC-30 Given hotel 1 with rooms on floors 1 to 3, When `GET /api/hotels/1/board` is called, Then the response is 200, floors are in ascending order, every room appears exactly once, and rooms within a floor are in room-number order.
- AC-31 Given floor 3 has 5 rooms in Dirty or Cleaning and floor 1 has 4, When the board is read, Then `busiestFloor` is 3 and floor 3 has `busy` true and no other floor does.
- AC-32 Given no hotel with id 999, When `GET /api/hotels/999/board` is called, Then the response is 404.
- AC-33 Given a Supervisor, When floor assignments are set for hotel 1 with floor 2 to user 5 (an active Housekeeper at hotel 1), Then the response is 200. When the user is a Housekeeper at hotel 2 instead, Then the response is 422 and Detail is "User 9 does not work at hotel 1."
- AC-34 Given floor 2 at hotel 1 is assigned to user 5, When a Supervisor assigns floor 2 to user 6, Then the response is 200 and floor 2 has exactly one active assignment, to user 6.
- AC-35 Given a FrontDesk user, When floor assignments are set, Then the response is 403 and no assignment changes.
- AC-36 Given a room in Ready, When the lock checkout webhook is called without an `X-Api-Key` header, Then the response is 401 and no room or turnaround changes.
- AC-37 Given room "101" at hotel 1 is Ready, When the lock checkout webhook is called with a valid key and `checkedOutAt` "2026-10-27T10:00:00Z", Then the response is 200, the room is Dirty, and the turnaround checkout stamp is 10:00:00Z.
- AC-38 Given no room "999" exists at hotel 1, When the lock checkout webhook is called for it with a valid key, Then the response is 404.
- AC-39 Given two Supervisors both read room 101 at rowVersion 3, When the first sets Cleaning with rowVersion 3 and then the second sets Inspected with rowVersion 3, Then the first returns 200 with rowVersion 4 and the second returns 409 with Detail "Room 101 changed. Reload and try again."
- AC-40 Given a status change request with status "Done", When it is sent, Then the response is 422 and Detail is "Status must be one of: Dirty, Cleaning, Inspected, Ready, OutOfOrder."
- AC-41 Given a status change request with no `rowVersion`, When it is sent, Then the response is 400 and the error names the field "rowVersion".
- AC-42 Given a room on the spot-check list, When a Supervisor calls `POST /api/rooms/{id}/spot-check`, Then the response is 200 and the room no longer appears in `GET /api/hotels/{id}/spot-checks`.
- AC-43 Given hotel 1 with 20 rooms, 2 of them OutOfOrder, and 70 room nights sold over 7 days, When `GET /api/reports/occupancy?hotelId=1` is called for those 7 days, Then `roomsAvailable` is 18 per day and `occupancyPct` is 55.6, and `roomsOutOfOrder` is 2.
- AC-44 Given an Inspected room, When a FrontDesk user sets status Ready, Then the response is 403 and the room stays Inspected.

## Assumptions & Open Questions

### Assumptions (decided by me, to be confirmed)
- **Roles and identity.** `Program.cs` has no authentication, and `changedByUserId` is trusted. Role checks look up the user's role from the users table using that id. This is weak: anyone can send any id. Authentication is needed before go-live, and is outside this spec.
- **Stack.** The spec follows the code: MVC controllers, EF Core on MySQL, DbUp migrations, FluentValidation. The top-level `track-4-hospitality/CLAUDE.md` describes minimal APIs, Dapper and SQL Server, which does not match the repo. The project CLAUDE.md also names `src/Larchmont.Web`, which does not exist. The code is treated as correct for this spec.
- **Timestamps.** Stored as UTC, as the code already does (`DateTime.UtcNow`). The root CLAUDE.md says "local hotel time", which conflicts. Times in the UI are shown in the browser's local time. Stay dates are unaffected.
- **Simple turn (Q1).** A room is a simple turn when its room type is Standard Double (STD), Standard Twin (TWN) or Superior Double (SUP) and it was not released from OutOfOrder. simple_turn is set when the room moves from Dirty to Cleaning.
- **Checkout stamp.** Stored once, at the first of the lock event time or the desk check-out time. Not recalculated afterwards.
- **Existing rooms.** Rooms with status Clean become Ready in V005. Rooms are not re-inspected.
- **Ready-by when overdue.** Now plus 15 minutes, flagged overdue.
- **Board refresh.** Polling every 15 seconds. No push channel.
- **Occupancy.** Only the out-of-order part of the report is changed here. The night-counting error is LH-56.
- **Lock key.** The new key comes from configuration or an environment variable. The existing development settings file already holds a committed key; that key must not be reused, and should be rotated.

### Open questions for Sofia (pending mentor reply)

- Q1 What is a "simple turn"? Default: a Standard Double, Standard Twin or Superior Double that was not out of order, decided when cleaning starts. Is that the right set of room types?
- Q2 May housekeepers mark a simple turn Ready themselves, given you said nothing goes to Ready without inspection? Default: yes. Every such move is recorded and goes on a spot-check list that a supervisor clears. Should the supervisor check every one, or a sample?
- Q3 When does the clock start? Default: the earlier of the lock system's checkout time and the time the desk checks the guest out. Is that the moment you mean by "when the guest leaves"?
- Q4 Are 75 minutes for Suites and Family Rooms the final targets for those types, and is 45 for every other type? Default: yes.
- Q5 What makes a floor "busiest"? Default: the floor with the most rooms in Dirty or Cleaning. Would you rather use the number of arrivals due that day?
- Q6 Should out-of-order rooms be left out of the rooms-available figure in the occupancy report? Default: yes, and they are shown as a separate count.
- Q7 Should a newly created room start as Dirty, not Ready? Default: Dirty, so it has to be cleaned and inspected before sale.
- Q8 Who may set a room out of order? Default: Maintenance and Supervisors. Should front desk be able to report a fault and have it held?
- Q9 Is one housekeeper per floor per hotel enough, or do you want different housekeepers on different shifts? Default: one at a time per floor.
- Q10 What does the lock vendor send, and can it push an event to us? Default: a checkout event with hotel, room number and time, sent to the webhook with an API key. Without a vendor contract the payload in this spec is an assumption.
- Q11 Would you accept an on-shift opt-in ("start shift" and "end shift" buttons, no location, no break tracking) as the alternative to the tracking you asked for? Default: we offer it, and do not build it in this spec.
- Q12 Is a fifteen-second refresh on the board fast enough to meet "without anybody pressing refresh"? Default: yes.

## Not built, and why

**Tracking housekeepers off the clock, on breaks, or by location.** Brief: "Some of them pick up extra shifts without clocking in, and I want to see them too, and during their breaks." And: "show where each housekeeper is on the floor plan, all the time." This must not be built.
- It tracks staff personal data outside working time, which is not a lawful purpose under UK GDPR. Tracking during breaks is the same problem.
- `docs/review-checklist.md`, Security and privacy: "Anything that tracks staff or guests has a lawful purpose and stops when the person is off duty." This fails it.
- Sofia's own words allow for this ("If they do not like it, that is a conversation for HR"). That conversation should happen before any tracking is built, not after.

**Alternative offered (Q11).** On-shift opt-in. A housekeeper taps "start shift" and "end shift" in the staff app. Only the shift state and the floor assigned by BR-17 are shown, and only while the housekeeper is on shift and has opted in. No location and no break data are recorded. The supervisor sees who is on shift and their floor, which meets the "send the nearest person" need in the room-level view.

## Traceability

Brief quotes are shortened. Test names follow `AC_nn_<Behaviour>`.

| Brief item | Business rule | Acceptance criteria | Endpoint / screen | Test |
|---|---|---|---|---|
| "Every room is in one of four states" | BR-01 | AC-01 | PUT /api/rooms/{id}/status | AC_01_HousekeeperStartsCleaning |
| "When a guest checks out the room becomes Dirty" / legal moves | BR-10 | AC-02 | PUT /api/rooms/{id}/status | AC_02_DirtyCannotGoStraightToReady |
| "a supervisor checks it" | BR-04, BR-11 | AC-03 | PUT /api/rooms/{id}/status | AC_03_HousekeeperCannotInspect |
| "a supervisor checks it and it becomes Inspected" | BR-04 | AC-04 | PUT /api/rooms/{id}/status | AC_04_SupervisorInspects |
| "Nothing goes to Ready without a supervisor inspection, no exceptions." | BR-05 | AC-05 | PUT /api/rooms/{id}/status | AC_05_SupervisorReleasesInspectedRoom |
| "Housekeepers should be able to mark a room Ready themselves when it is a simple turn" | BR-06 | AC-06 | PUT /api/rooms/{id}/status; GET spot-checks | AC_06_SimpleTurnShortcutIsAudited |
| "Nothing goes to Ready without a supervisor inspection" (non-simple room) | BR-05, BR-06 | AC-07 | PUT /api/rooms/{id}/status | AC_07_HousekeeperCannotReadyNonSimpleRoom |
| "Nothing goes to Ready without a supervisor inspection" (supervisor skips) | BR-05 | AC-08 | PUT /api/rooms/{id}/status | AC_08_SupervisorCannotSkipInspection |
| "Maintenance should be able to put a room out of order with a reason" | BR-08 | AC-09 | PUT /api/rooms/{id}/status | AC_09_MaintenanceSetsOutOfOrder |
| "with a reason" (blank reason) | BR-08 | AC-10 | PUT /api/rooms/{id}/status | AC_10_OutOfOrderNeedsReason |
| "Maintenance should be able to put a room out of order" (role limit) | BR-11 | AC-11 | PUT /api/rooms/{id}/status | AC_11_FrontDeskCannotSetOutOfOrder |
| "a room that is out of order is not for sale. Full stop." | BR-09 | AC-12 | PUT /api/rooms/{id}/status | AC_12_OutOfOrderCannotGoToReady |
| "it should come back through the same cleaning steps afterwards" | BR-07 | AC-13 | PUT /api/rooms/{id}/status | AC_13_ReleasedRoomNeedsSupervisorInspection |
| "be told that it is out of order and when it comes back" | BR-15 | AC-14 | GET /api/rooms/{id}/ready-by | AC_14_ReadyByShowsOutOfOrderReturn |
| "The desk must never be able to assign a guest to an out-of-order room." | BR-09 | AC-15 | POST /api/bookings/{id}/assign-room | AC_15_OutOfOrderRoomCannotBeAssigned |
| "sold and checked in a guest to a room that was still being cleaned" | BR-21 | AC-16 | POST /api/bookings/{id}/check-in | AC_16_CheckInNeedsReadyRoom |
| "the desk cannot sell a room that is not ready" (check-in succeeds when ready) | BR-21 | AC-17 | POST /api/bookings/{id}/check-in | AC_17_CheckInWithReadyRoomSucceeds |
| "The clock should start when the guest leaves." | BR-02, BR-13 | AC-18 | POST /api/bookings/{id}/check-out | AC_18_CheckoutCreatesTurnaround |
| "the lock system already knows" (duplicate event) | BR-18 | AC-19 | POST /api/integrations/lock/checkout | AC_19_SecondCheckoutDoesNotDuplicateTurnaround |
| "a turnaround target of 45 minutes from checkout to Ready" (boundary) | BR-12, BR-14 | AC-20 | GET /api/hotels/{id}/sla-breaches | AC_20_Exactly45MinutesIsNotBreach |
| "Anything slower is a breach" | BR-14 | AC-21 | GET /api/hotels/{id}/sla-breaches | AC_21_46MinutesIsBreach |
| "for those two types the target is 75 minutes" (Suite) | BR-12 | AC-22 | GET /api/hotels/{id}/sla-breaches | AC_22_SuiteTargetIs75Minutes |
| "for those two types the target is 75 minutes" (Family) | BR-12 | AC-23 | GET /api/hotels/{id}/sla-breaches | AC_23_FamilyTargetIs75Minutes |
| "should appear on a list I can look at each morning, worst first" | BR-14 | AC-24 | GET /api/hotels/{id}/sla-breaches; SLA breach list screen | AC_24_BreachListWorstFirst |
| "show me the late rooms list" (empty) | BR-14 | AC-25 | GET /api/hotels/{id}/sla-breaches | AC_25_EmptyBreachListReturnsEmpty |
| "show me the late rooms list" (open room past target) | BR-13, BR-14 | AC-26 | GET /api/hotels/{id}/sla-breaches | AC_26_OpenRoomPastTargetIsBreach |
| "when will this room be ready? Not a status, a time." (Dirty) | BR-15 | AC-27 | GET /api/rooms/{id}/ready-by | AC_27_ReadyByForDirtyRoom |
| "when will this room be ready? Not a status, a time." (overdue) | BR-15 | AC-28 | GET /api/rooms/{id}/ready-by | AC_28_ReadyByOverdueRoom |
| "They should be able to ask that for a single room" (unknown room) | BR-15 | AC-29 | GET /api/rooms/{id}/ready-by | AC_29_ReadyByUnknownRoomIs404 |
| "The board should be organised by floor" | BR-16 | AC-30 | GET /api/hotels/{id}/board; live board screen | AC_30_BoardGroupedByFloor |
| "The busiest floors should be easy to see." | BR-22 | AC-31 | GET /api/hotels/{id}/board; live board screen | AC_31_BusiestFloorIsMostRoomsNeedingWork |
| "Show me the board for one hotel" (unknown hotel) | BR-16 | AC-32 | GET /api/hotels/{id}/board | AC_32_BoardUnknownHotelIs404 |
| "each housekeeper should be given a floor to look after" | BR-17 | AC-33 | PUT /api/hotels/{id}/floor-assignments | AC_33_FloorAssignmentNeedsHotelHousekeeper |
| "each housekeeper should be given a floor" (reassign) | BR-17 | AC-34 | PUT /api/hotels/{id}/floor-assignments | AC_34_ReassignReplacesFloorOwner |
| "each housekeeper should be given a floor" (role limit) | BR-11 | AC-35 | PUT /api/hotels/{id}/floor-assignments | AC_35_FrontDeskCannotAssignFloors |
| "the lock system already knows ... instant" (no key) | BR-18 | AC-36 | POST /api/integrations/lock/checkout | AC_36_LockWebhookNeedsApiKey |
| "the lock system already knows ... turn the room Dirty" | BR-02, BR-18 | AC-37 | POST /api/integrations/lock/checkout; board screen | AC_37_LockCheckoutMakesRoomDirty |
| "the lock system already knows" (unknown room) | BR-18 | AC-38 | POST /api/integrations/lock/checkout | AC_38_LockWebhookUnknownRoomIs404 |
| Two people changing the same room (concurrency) | BR-19 | AC-39 | PUT /api/rooms/{id}/status | AC_39_StaleRowVersionIs409 |
| Legal moves must name what is allowed (unknown status) | BR-10 | AC-40 | PUT /api/rooms/{id}/status | AC_40_UnknownStatusListsAllowed |
| Concurrency token required on every change | BR-19 | AC-41 | PUT /api/rooms/{id}/status | AC_41_StatusChangeNeedsRowVersion |
| "the supervisors cannot be everywhere" (spot-check of shortcuts) | BR-06 | AC-42 | POST /api/rooms/{id}/spot-check; GET /api/hotels/{id}/spot-checks | AC_42_SupervisorSpotCheckClearsList |
| "The Monday occupancy report looks wrong to me." (out-of-order share) | BR-20 | AC-43 | GET /api/reports/occupancy | AC_43_OutOfOrderRoomsLeaveCapacity |
| "nothing goes to Ready without a supervisor inspection" (front desk cannot set Ready) | BR-11 | AC-44 | PUT /api/rooms/{id}/status | AC_44_FrontDeskCannotSetReady |
