using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larchmont.Api.Domain;
using static Larchmont.Tests.HousekeepingFixture;

namespace Larchmont.Tests;

// Booking rules, floor assignments, lock webhook and occupancy over HTTP
// (spec: BR-02, BR-09, BR-17, BR-18, BR-20, BR-21; Acceptance Criteria AC-15 to AC-19,
// AC-33 to AC-38, AC-43).
public class HousekeepingApiTests
{
    private static Booking NewBooking(int id, int? roomId, string status, DateOnly checkIn, DateOnly checkOut) => new()
    {
        Id = id, Reference = "HK-" + id, Channel = "Direct", HotelId = 1, GuestId = 1, RoomTypeId = 1,
        RoomId = roomId, RatePlanId = 1, CheckIn = checkIn, CheckOut = checkOut, Adults = 2,
        Status = status, TotalAmount = 190m, CreatedAt = At(9), UpdatedAt = At(9)
    };

    [Fact]
    public async Task AC_15_OutOfOrderRoomCannotBeAssigned()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.OutOfOrder),
            NewBooking(9001, null, BookingStatuses.Confirmed, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)));

        // Given a Confirmed Standard Double booking at hotel 1 and room 101 out of order.
        // When the booking is assigned to room 101.
        var response = await f.CreateClient().PostAsJsonAsync("/api/bookings/9001/assign-room", new { roomId = 101 });

        // Then 422 with the exact message, and the booking keeps no room.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Room 101 is out of order and cannot be assigned.", (await Json(response)).GetProperty("detail").GetString());
        Assert.Null(f.Query(db => db.Bookings.Single(b => b.Id == 9001)).RoomId);
    }

    [Fact]
    public async Task AC_16_CheckInNeedsReadyRoom()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Dirty),
            NewBooking(9002, 101, BookingStatuses.Confirmed, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)));

        // Given a Confirmed booking assigned to room 101 that is Dirty. When check-in is called.
        var response = await f.CreateClient().PostAsync("/api/bookings/9002/check-in", null);

        // Then 422 with the exact message, and the booking stays Confirmed.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Room 101 is Dirty. Check-in needs a Ready room.", (await Json(response)).GetProperty("detail").GetString());
        Assert.Equal(BookingStatuses.Confirmed, f.Query(db => db.Bookings.Single(b => b.Id == 9002)).Status);
    }

    [Fact]
    public async Task AC_17_CheckInWithReadyRoomSucceeds()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Ready),
            NewBooking(9003, 101, BookingStatuses.Confirmed, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)));

        // Given a Confirmed booking assigned to a Ready room. When check-in is called.
        var response = await f.CreateClient().PostAsync("/api/bookings/9003/check-in", null);

        // Then 200 and the booking is CheckedIn.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.CheckedIn, f.Query(db => db.Bookings.Single(b => b.Id == 9003)).Status);
    }

    [Fact]
    public async Task AC_18_CheckoutCreatesTurnaround()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Ready),
            NewBooking(9004, 101, BookingStatuses.CheckedIn, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)));

        // Given a CheckedIn booking in room 101 (Ready). When check-out is called.
        var response = await f.CreateClient().PostAsync("/api/bookings/9004/check-out", null);

        // Then 200, the booking is CheckedOut, the room is Dirty, and one turnaround exists with
        // the checkout stamp equal to the Dirty history row time and target 45.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.CheckedOut, f.Query(db => db.Bookings.Single(b => b.Id == 9004)).Status);
        Assert.Equal(RoomStatuses.Dirty, f.Query(db => db.Rooms.Single(r => r.Id == 101)).Status);
        var turnarounds = f.Query(db => db.RoomTurnarounds.Where(t => t.RoomId == 101).ToList());
        Assert.Single(turnarounds);
        var dirtyAt = f.Query(db => db.RoomStatusHistory.Single(h => h.RoomId == 101 && h.ToStatus == "Dirty").ChangedAt);
        Assert.Equal(dirtyAt, turnarounds[0].CheckoutAt);
        Assert.Equal(45, turnarounds[0].TargetMinutes);
    }

    [Fact]
    public async Task AC_19_SecondCheckoutDoesNotDuplicateTurnaround()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty));
        f.Seed(db => db.RoomTurnarounds.Add(new RoomTurnaround { Id = 1, RoomId = 101, CheckoutAt = At(10), TargetMinutes = 45, IsOpen = true }));

        // Given room 101 has an open turnaround. When the lock checkout event arrives for room 101.
        var response = await LockCheckout(f.CreateClient(), HousekeepingFactory.LockKey, new { hotelId = 1, roomNumber = "101" });

        // Then 200 with turnaroundCreated false, one turnaround for room 101, and the room is still Dirty.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await Json(response)).GetProperty("turnaroundCreated").GetBoolean());
        Assert.Equal(1, f.Query(db => db.RoomTurnarounds.Count(t => t.RoomId == 101)));
        Assert.Equal(RoomStatuses.Dirty, f.Query(db => db.Rooms.Single(r => r.Id == 101)).Status);
    }

    [Fact]
    public async Task AC_33_FloorAssignmentNeedsHotelHousekeeper()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(201, "201", 2, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given a Supervisor. When floor 2 at hotel 1 is set to user 5, an active Housekeeper at hotel 1.
        var ok = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor,
            assignments = new[] { new { floor = 2, housekeeperUserId = HousekeeperB } }
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // When the user is a Housekeeper at hotel 2 instead. Then 422 with the exact message.
        var wrongHotel = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor,
            assignments = new[] { new { floor = 2, housekeeperUserId = HousekeeperOtherHotel } }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongHotel.StatusCode);
        Assert.Equal("User 9 does not work at hotel 1.", (await Json(wrongHotel)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_34_ReassignReplacesFloorOwner()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping();
        var client = f.CreateClient();
        await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor, assignments = new[] { new { floor = 2, housekeeperUserId = HousekeeperB } }
        });

        // Given floor 2 at hotel 1 is assigned to user 5. When a Supervisor assigns floor 2 to user 6.
        var response = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor, assignments = new[] { new { floor = 2, housekeeperUserId = HousekeeperC } }
        });

        // Then 200, and floor 2 has exactly one active assignment, to user 6.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await Json(await client.GetAsync("/api/hotels/1/floor-assignments"));
        var floor2 = list.EnumerateArray().Where(a => a.GetProperty("floor").GetInt32() == 2).ToList();
        Assert.Single(floor2);
        Assert.Equal(HousekeeperC, floor2[0].GetProperty("housekeeperUserId").GetInt32());
    }

    [Fact]
    public async Task AC_35_FrontDeskCannotAssignFloors()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping();
        var client = f.CreateClient();

        // Given a FrontDesk user. When floor assignments are set.
        var response = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = FrontDesk, assignments = new[] { new { floor = 2, housekeeperUserId = HousekeeperB } }
        });

        // Then 403 and no assignment changed.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var list = await Json(await client.GetAsync("/api/hotels/1/floor-assignments"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task AC_36_LockWebhookNeedsApiKey()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given a Ready room. When the lock webhook is called without an X-Api-Key header.
        var response = await LockCheckout(f.CreateClient(), null, new { hotelId = 1, roomNumber = "101" });

        // Then 401, and no room or turnaround changes.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(RoomStatuses.Ready, f.Query(db => db.Rooms.Single(r => r.Id == 101)).Status);
        Assert.Equal(0, f.Query(db => db.RoomTurnarounds.Count()));
    }

    [Fact]
    public async Task AC_37_LockCheckoutMakesRoomDirty()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given room "101" at hotel 1 is Ready. When the lock webhook is called with a valid key
        // and checkedOutAt "2026-10-27T10:00:00Z".
        var response = await LockCheckout(f.CreateClient(), HousekeepingFactory.LockKey,
            new { hotelId = 1, roomNumber = "101", checkedOutAt = "2026-10-27T10:00:00Z" });

        // Then 200, the room is Dirty, and the turnaround checkout stamp is 10:00:00Z.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RoomStatuses.Dirty, (await Json(response)).GetProperty("status").GetString());
        Assert.Equal(RoomStatuses.Dirty, f.Query(db => db.Rooms.Single(r => r.Id == 101)).Status);
        Assert.Equal(At(10), f.Query(db => db.RoomTurnarounds.Single(t => t.RoomId == 101).CheckoutAt));
    }

    [Fact]
    public async Task AC_38_LockWebhookUnknownRoomIs404()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given no room "999" at hotel 1. When the lock webhook is called for it with a valid key.
        var response = await LockCheckout(f.CreateClient(), HousekeepingFactory.LockKey, new { hotelId = 1, roomNumber = "999" });

        // Then 404.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC_43_OutOfOrderRoomsLeaveCapacity()
    {
        using var f = new HousekeepingFactory();
        var rooms = Enumerable.Range(1001, 20)
            .Select(id => NewRoom(id, id.ToString(), 1, id <= 1002 ? RoomStatuses.OutOfOrder : RoomStatuses.Ready))
            .Cast<object>();
        // Ten Standard Double bookings of seven nights each: 70 room nights over 1 to 7 October.
        var bookings = Enumerable.Range(9101, 10)
            .Select(id => NewBooking(id, null, BookingStatuses.Confirmed, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)))
            .Cast<object>();
        f.SeedHousekeeping(rooms.Concat(bookings).ToArray());

        // Given hotel 1 with 20 rooms, 2 out of order, and 70 room nights sold over 7 days.
        // When the occupancy report is read for those 7 days.
        var response = await f.CreateClient().GetAsync("/api/reports/occupancy?hotelId=1&from=2026-10-01&to=2026-10-07");

        // Then roomsAvailable is 18, occupancyPct is 55.6 and roomsOutOfOrder is 2.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(18, body.GetProperty("roomsAvailable").GetInt32());
        Assert.Equal(70, body.GetProperty("roomNightsSold").GetInt32());
        Assert.Equal(55.6m, body.GetProperty("occupancyPct").GetDecimal());
        Assert.Equal(2, body.GetProperty("roomsOutOfOrder").GetInt32());
    }
}
