using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larchmont.Api.Domain;
using static Larchmont.Tests.HousekeepingFixture;

namespace Larchmont.Tests;

// Turnaround clock, breach list, ready-by answer, board and spot-checks
// (spec: BR-12 to BR-16, BR-22; Acceptance Criteria AC-14, AC-20 to AC-32, AC-42).
// The clock is a FixedClock; every "now" is set by the test, never read from the machine.
public class HousekeepingBoardTests
{
    // Moves a room using its stored rowVersion, asserting that the move itself succeeds.
    private static async Task<JsonElement> Move(HousekeepingFactory f, HttpClient client, int roomId, string status, int userId)
    {
        var rowVersion = f.Query(db => db.Rooms.Single(r => r.Id == roomId).RowVersion);
        var response = await PutStatus(client, roomId, new { status, changedByUserId = userId, rowVersion });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    // Lock checkout for a room number at hotel 1, with a checkout stamp the test chooses.
    private static async Task Checkout(HttpClient client, string roomNumber, DateTime checkedOutAt)
    {
        var response = await LockCheckout(client, HousekeepingFactory.LockKey,
            new { hotelId = 1, roomNumber, checkedOutAt });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> Breaches(HttpClient client)
    {
        var response = await client.GetAsync("/api/hotels/1/sla-breaches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("items");
    }

    private static JsonElement? Find(JsonElement items, int roomId)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("roomId").GetInt32() == roomId) return item;
        }
        return null;
    }

    [Fact]
    public async Task AC_14_ReadyByShowsOutOfOrderReturn()
    {
        using var f = new HousekeepingFactory();
        var room = NewRoom(101, "101", 1, RoomStatuses.OutOfOrder);
        room.OooReason = "Leak in ensuite";
        room.OooSince = At(9);
        room.OooExpectedReturn = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        f.SeedHousekeeping(room);

        // Given an OutOfOrder room with expected return 2026-11-03T12:00:00Z.
        // When the ready-by answer is requested.
        var response = await f.CreateClient().GetAsync("/api/rooms/101/ready-by");

        // Then 200, outOfOrder true, returnsAt is the expected return, readyBy null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.True(body.GetProperty("outOfOrder").GetBoolean());
        Assert.Equal(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), Utc(body.GetProperty("returnsAt")));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("readyBy").ValueKind);
    }

    [Fact]
    public async Task AC_20_Exactly45MinutesIsNotBreach()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready, simpleTurn: true));
        var client = f.CreateClient();

        // Given a Standard Double with checkout stamp 10:00 and Ready at 10:45.
        f.Clock.UtcNow = At(10);
        await Checkout(client, "101", At(10));
        await Move(f, client, 101, RoomStatuses.Cleaning, HousekeeperA);
        f.Clock.UtcNow = At(10, 45);
        await Move(f, client, 101, RoomStatuses.Ready, HousekeeperA);
        Assert.Equal(At(10, 45), f.Query(db => db.RoomTurnarounds.Single(t => t.RoomId == 101).ReadyAt));

        // When the breach list is read. Then the room is not in it.
        var items = await Breaches(client);
        Assert.Null(Find(items, 101));
    }

    [Fact]
    public async Task AC_21_46MinutesIsBreach()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready, simpleTurn: true));
        var client = f.CreateClient();

        // Given a Standard Double with checkout stamp 10:00 and Ready at 10:46.
        f.Clock.UtcNow = At(10);
        await Checkout(client, "101", At(10));
        await Move(f, client, 101, RoomStatuses.Cleaning, HousekeeperA);
        f.Clock.UtcNow = At(10, 46);
        await Move(f, client, 101, RoomStatuses.Ready, HousekeeperA);

        // When the breach list is read. Then the room is listed with overrunMinutes 1.
        var item = Find(await Breaches(client), 101);
        Assert.NotNull(item);
        Assert.Equal(45, item!.Value.GetProperty("targetMinutes").GetInt32());
        Assert.Equal(46, item.Value.GetProperty("elapsedMinutes").GetInt32());
        Assert.Equal(1, item.Value.GetProperty("overrunMinutes").GetInt32());
    }

    [Fact]
    public async Task AC_22_SuiteTargetIs75Minutes()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(201, "201", 2, RoomStatuses.Ready, roomTypeId: 2, simpleTurn: true),
            NewRoom(202, "202", 2, RoomStatuses.Ready, roomTypeId: 2, simpleTurn: true));
        var client = f.CreateClient();

        // Given a Suite with checkout stamp 10:00 (two suites, one Ready at 11:15, one at 11:16).
        f.Clock.UtcNow = At(10);
        await Checkout(client, "201", At(10));
        await Checkout(client, "202", At(10));
        await Move(f, client, 201, RoomStatuses.Cleaning, HousekeeperA);
        await Move(f, client, 202, RoomStatuses.Cleaning, HousekeeperA);

        // When 201 is marked Ready at 11:15 and 202 at 11:16, and the breach list is read.
        f.Clock.UtcNow = At(11, 15);
        await Move(f, client, 201, RoomStatuses.Inspected, Supervisor);
        await Move(f, client, 201, RoomStatuses.Ready, Supervisor);
        f.Clock.UtcNow = At(11, 16);
        await Move(f, client, 202, RoomStatuses.Inspected, Supervisor);
        await Move(f, client, 202, RoomStatuses.Ready, Supervisor);
        var items = await Breaches(client);

        // Then 201 (75 minutes) is not listed, and 202 is listed with targetMinutes 75 and overrunMinutes 1.
        Assert.Null(Find(items, 201));
        var item = Find(items, 202);
        Assert.NotNull(item);
        Assert.Equal(75, item!.Value.GetProperty("targetMinutes").GetInt32());
        Assert.Equal(1, item.Value.GetProperty("overrunMinutes").GetInt32());
    }

    [Fact]
    public async Task AC_23_FamilyTargetIs75Minutes()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(301, "301", 3, RoomStatuses.Ready, roomTypeId: 3, simpleTurn: true));
        var client = f.CreateClient();

        // Given a Family Room with checkout stamp 10:00 and Ready at 11:16.
        f.Clock.UtcNow = At(10);
        await Checkout(client, "301", At(10));
        await Move(f, client, 301, RoomStatuses.Cleaning, HousekeeperA);
        f.Clock.UtcNow = At(11, 16);
        await Move(f, client, 301, RoomStatuses.Inspected, Supervisor);
        await Move(f, client, 301, RoomStatuses.Ready, Supervisor);

        // When the breach list is read. Then listed with targetMinutes 75 and overrunMinutes 1.
        var item = Find(await Breaches(client), 301);
        Assert.NotNull(item);
        Assert.Equal(75, item!.Value.GetProperty("targetMinutes").GetInt32());
        Assert.Equal(1, item.Value.GetProperty("overrunMinutes").GetInt32());
    }

    [Fact]
    public async Task AC_24_BreachListWorstFirst()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(401, "401", 4, RoomStatuses.Ready),
            NewRoom(402, "402", 4, RoomStatuses.Ready),
            NewRoom(403, "403", 4, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given three open breaches with overruns of 10, 40 and 25 minutes at now 12:00.
        // 401 checked out 11:05 (elapsed 55), 402 at 10:35 (elapsed 85), 403 at 10:50 (elapsed 70).
        await Checkout(client, "401", At(11, 5));
        await Checkout(client, "402", At(10, 35));
        await Checkout(client, "403", At(10, 50));

        // When the breach list is read. Then the items are ordered 40, 25, 10.
        var items = (await Breaches(client)).EnumerateArray().ToList();
        Assert.Equal(new[] { 402, 403, 401 }, items.Select(i => i.GetProperty("roomId").GetInt32()).ToArray());
        Assert.Equal(new[] { 40, 25, 10 }, items.Select(i => i.GetProperty("overrunMinutes").GetInt32()).ToArray());
    }

    [Fact]
    public async Task AC_25_EmptyBreachListReturnsEmpty()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given no open breaches at hotel 1. When the breach list is read.
        var items = await Breaches(f.CreateClient());

        // Then 200 with items an empty array.
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task AC_26_OpenRoomPastTargetIsBreach()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given a Standard Double Dirty since checkout stamp 11:00, and the time is 11:50.
        await Checkout(client, "101", At(11));
        f.Clock.UtcNow = At(11, 50);

        // When the breach list is read. Then listed with readyAt null, elapsedMinutes 50, overrunMinutes 5.
        var item = Find(await Breaches(client), 101);
        Assert.NotNull(item);
        Assert.Equal(JsonValueKind.Null, item!.Value.GetProperty("readyAt").ValueKind);
        Assert.Equal(50, item.Value.GetProperty("elapsedMinutes").GetInt32());
        Assert.Equal(5, item.Value.GetProperty("overrunMinutes").GetInt32());
    }

    [Fact]
    public async Task AC_27_ReadyByForDirtyRoom()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given a Standard Double Dirty with checkout stamp 20 minutes before now (12:00).
        await Checkout(client, "101", At(11, 40));

        // When the ready-by answer is requested. Then readyBy is 25 minutes from now, not overdue.
        var response = await client.GetAsync("/api/rooms/101/ready-by");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(At(12, 25), Utc(body.GetProperty("readyBy")));
        Assert.False(body.GetProperty("overdue").GetBoolean());
    }

    [Fact]
    public async Task AC_28_ReadyByOverdueRoom()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given a Cleaning room whose target (checkout 11:05 plus 45) passed 10 minutes ago.
        await Checkout(client, "101", At(11, 5));
        await Move(f, client, 101, RoomStatuses.Cleaning, HousekeeperA);

        // When the ready-by answer is requested. Then overdue true and readyBy 15 minutes from now.
        var response = await client.GetAsync("/api/rooms/101/ready-by");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.True(body.GetProperty("overdue").GetBoolean());
        Assert.Equal(At(12, 15), Utc(body.GetProperty("readyBy")));
    }

    [Fact]
    public async Task AC_29_ReadyByUnknownRoomIs404()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping();

        // Given no room with id 999999. When the ready-by answer is requested.
        var response = await f.CreateClient().GetAsync("/api/rooms/999999/ready-by");

        // Then 404 with the exact detail.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Room 999999 not found.", (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_30_BoardGroupedByFloor()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(301, "301", 3, RoomStatuses.Ready),
            NewRoom(102, "102", 1, RoomStatuses.Ready),
            NewRoom(101, "101", 1, RoomStatuses.Ready),
            NewRoom(302, "302", 3, RoomStatuses.Dirty),
            NewRoom(201, "201", 2, RoomStatuses.Cleaning));

        // Given hotel 1 with rooms on floors 1 to 3. When the board is read.
        var response = await f.CreateClient().GetAsync("/api/hotels/1/board");

        // Then 200, floors ascending, every room once, rooms by room number within a floor.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var floors = (await Json(response)).GetProperty("floors").EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 2, 3 }, floors.Select(x => x.GetProperty("floor").GetInt32()).ToArray());
        var numbers = floors.Select(x => x.GetProperty("rooms").EnumerateArray()
            .Select(r => r.GetProperty("roomNumber").GetString()!).ToArray()).ToArray();
        Assert.Equal(new[] { "101", "102" }, numbers[0]);
        Assert.Equal(new[] { "201" }, numbers[1]);
        Assert.Equal(new[] { "301", "302" }, numbers[2]);
    }

    [Fact]
    public async Task AC_31_BusiestFloorIsMostRoomsNeedingWork()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Dirty), NewRoom(102, "102", 1, RoomStatuses.Dirty),
            NewRoom(103, "103", 1, RoomStatuses.Cleaning), NewRoom(104, "104", 1, RoomStatuses.Dirty),
            NewRoom(201, "201", 2, RoomStatuses.Ready), NewRoom(202, "202", 2, RoomStatuses.Ready),
            NewRoom(301, "301", 3, RoomStatuses.Dirty), NewRoom(302, "302", 3, RoomStatuses.Dirty),
            NewRoom(303, "303", 3, RoomStatuses.Cleaning), NewRoom(304, "304", 3, RoomStatuses.Dirty),
            NewRoom(305, "305", 3, RoomStatuses.Cleaning));

        // Given floor 3 has 5 rooms in Dirty or Cleaning and floor 1 has 4. When the board is read.
        var response = await f.CreateClient().GetAsync("/api/hotels/1/board");

        // Then busiestFloor is 3 and only floor 3 has busy true.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(3, body.GetProperty("busiestFloor").GetInt32());
        var busy = body.GetProperty("floors").EnumerateArray()
            .ToDictionary(x => x.GetProperty("floor").GetInt32(), x => x.GetProperty("busy").GetBoolean());
        Assert.True(busy[3]);
        Assert.False(busy[1]);
        Assert.False(busy[2]);
    }

    [Fact]
    public async Task AC_32_BoardUnknownHotelIs404()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        var client = f.CreateClient();

        // Given no hotel with id 999 (hotel 1 and its room 101 exist). When the board is read.
        var response = await client.GetAsync("/api/hotels/999/board");

        // Then 404 with ProblemDetails Detail "Hotel 999 not found." (current wording of EnsureHotelAsync;
        // the spec only says 404), and no board body is returned.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Hotel 999 not found.", body.GetProperty("detail").GetString());
        Assert.False(body.TryGetProperty("floors", out _));
        Assert.False(body.TryGetProperty("busiestFloor", out _));

        // A read changes nothing: room 101 is still Ready, at rowVersion 1, with no status history.
        var room = f.Query(db => db.Rooms.Single(r => r.Id == 101));
        Assert.Equal(RoomStatuses.Ready, room.Status);
        Assert.Equal(1, room.RowVersion);
        Assert.Equal(0, f.Query(db => db.RoomStatusHistory.Count()));
    }

    // BR-22: ties go to the lower floor. AC-31 has no tie, so this is the only check of the tie-break.
    [Fact]
    public async Task Board_BusiestFloorTie_GoesToLowerFloor()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Dirty), NewRoom(102, "102", 1, RoomStatuses.Cleaning),
            NewRoom(103, "103", 1, RoomStatuses.Ready),
            NewRoom(201, "201", 2, RoomStatuses.Dirty), NewRoom(202, "202", 2, RoomStatuses.Dirty),
            NewRoom(203, "203", 2, RoomStatuses.Ready));

        // Given floor 1 and floor 2 each have two rooms in Dirty or Cleaning.
        var body = await Json(await f.CreateClient().GetAsync("/api/hotels/1/board"));

        // Then busiestFloor is 1, and only floor 1 has busy true.
        Assert.Equal(1, body.GetProperty("busiestFloor").GetInt32());
        var busy = body.GetProperty("floors").EnumerateArray()
            .ToDictionary(x => x.GetProperty("floor").GetInt32(), x => x.GetProperty("busy").GetBoolean());
        Assert.True(busy[1]);
        Assert.False(busy[2]);
    }

    [Fact]
    public async Task AC_42_SupervisorSpotCheckClearsList()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: true));
        var client = f.CreateClient();
        // A housekeeper marks the simple turn Ready, which puts it on the spot-check list (BR-06).
        await Move(f, client, 101, RoomStatuses.Ready, HousekeeperA);

        // Given a room on the spot-check list. When a Supervisor spot-checks it.
        var check = await client.PostAsJsonAsync("/api/rooms/101/spot-check", new { changedByUserId = Supervisor });

        // Then 200, and the room no longer appears in the spot-check list.
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var list = await Json(await client.GetAsync("/api/hotels/1/spot-checks"));
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("roomId").GetInt32() == 101);
    }
}
