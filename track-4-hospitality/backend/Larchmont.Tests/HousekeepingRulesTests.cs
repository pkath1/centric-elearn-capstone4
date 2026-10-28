using System.Net;
using System.Net.Http.Json;
using Larchmont.Api.Domain;
using static Larchmont.Tests.HousekeepingFixture;

namespace Larchmont.Tests;

// Status moves, roles and concurrency through PUT /api/rooms/{id}/status (spec: Business rules
// BR-01 to BR-11, BR-19; Acceptance Criteria AC-01 to AC-13, AC-39 to AC-41, AC-44).
public class HousekeepingRulesTests
{
    [Fact]
    public async Task AC_01_HousekeeperStartsCleaning()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty));
        var client = f.CreateClient();

        // Given a room in Dirty. When a Housekeeper sets Cleaning with the current rowVersion.
        var response = await PutStatus(client, 101, new { status = "Cleaning", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 200, status Cleaning, rowVersion 2, one history row Dirty to Cleaning.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Cleaning", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("rowVersion").GetInt32());
        var rows = f.Query(db => db.RoomStatusHistory.Count(h => h.RoomId == 101 && h.FromStatus == "Dirty" && h.ToStatus == "Cleaning"));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task AC_02_DirtyCannotGoStraightToReady()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty));

        // Given a room in Dirty. When a Housekeeper sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 422 with the exact allowed-moves message.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Dirty rooms can move to: Cleaning.", (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_03_HousekeeperCannotInspect()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: false));

        // Given a Cleaning room with simple_turn false. When a Housekeeper sets Inspected.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Inspected", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 403 and the detail names the Supervisor role.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var detail = (await Json(response)).GetProperty("detail").GetString();
        Assert.NotNull(detail);
        Assert.Contains("Supervisor", detail);
    }

    [Fact]
    public async Task AC_04_SupervisorInspects()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: false));

        // Given a Cleaning room with simple_turn false. When a Supervisor sets Inspected.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Inspected", changedByUserId = Supervisor, rowVersion = 1 });

        // Then 200 and status Inspected.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Inspected", (await Json(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC_05_SupervisorReleasesInspectedRoom()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Inspected));

        // Given an Inspected room. When a Supervisor sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = Supervisor, rowVersion = 1 });

        // Then 200, status Ready, and the history row carries the Supervisor's id and no reason.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", (await Json(response)).GetProperty("status").GetString());
        var row = f.Query(db => db.RoomStatusHistory.Single(h => h.RoomId == 101 && h.ToStatus == "Ready"));
        Assert.Equal(Supervisor, row.ChangedByUserId);
        Assert.Null(row.Reason);
    }

    [Fact]
    public async Task AC_06_SimpleTurnShortcutIsAudited()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: true));
        var client = f.CreateClient();

        // Given a Cleaning room with simple_turn true. When a Housekeeper sets Ready.
        var response = await PutStatus(client, 101, new { status = "Ready", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 200, status Ready, history reason "SimpleTurnShortcut", and the room is on the spot-check list.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", (await Json(response)).GetProperty("status").GetString());
        var row = f.Query(db => db.RoomStatusHistory.Single(h => h.RoomId == 101 && h.ToStatus == "Ready"));
        Assert.Equal("SimpleTurnShortcut", row.Reason);
        Assert.Equal(HousekeeperA, row.ChangedByUserId);
        var spotChecks = await Json(await client.GetAsync("/api/hotels/1/spot-checks"));
        Assert.Contains(spotChecks.GetProperty("items").EnumerateArray(), i => i.GetProperty("roomId").GetInt32() == 101);
    }

    [Fact]
    public async Task AC_07_HousekeeperCannotReadyNonSimpleRoom()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: false));

        // Given a Cleaning room with simple_turn false. When a Housekeeper sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 422 with the exact message.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Only a Supervisor can mark this room Ready after inspection. Cleaning rooms can move to: Inspected.",
            (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_08_SupervisorCannotSkipInspection()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Cleaning, simpleTurn: false));

        // Given a Cleaning room with simple_turn false. When a Supervisor sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = Supervisor, rowVersion = 1 });

        // Then 422 with the exact message.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Cleaning rooms can move to: Inspected.", (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_09_MaintenanceSetsOutOfOrder()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        f.Clock.UtcNow = At(11, 30);

        // Given a Ready room. When Maintenance sets OutOfOrder with reason "Leak in ensuite".
        var response = await PutStatus(f.CreateClient(), 101, new { status = "OutOfOrder", changedByUserId = Maintenance, rowVersion = 1, reason = "Leak in ensuite" });

        // Then 200, oooReason set, oooSince is the current UTC time.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Leak in ensuite", body.GetProperty("oooReason").GetString());
        Assert.Equal(At(11, 30), Utc(body.GetProperty("oooSince")));
    }

    [Fact]
    public async Task AC_10_OutOfOrderNeedsReason()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given a Ready room. When Maintenance sets OutOfOrder with an empty reason.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "OutOfOrder", changedByUserId = Maintenance, rowVersion = 1, reason = "" });

        // Then 400 and the error names the field "reason".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(NamesField(await Json(response), "reason"));
    }

    [Fact]
    public async Task AC_11_FrontDeskCannotSetOutOfOrder()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        // Given a Ready room. When a FrontDesk user sets OutOfOrder.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "OutOfOrder", changedByUserId = FrontDesk, rowVersion = 1, reason = "Leak" });

        // Then 403 and the room is unchanged.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var room = f.Query(db => db.Rooms.Single(r => r.Id == 101));
        Assert.Equal(RoomStatuses.Ready, room.Status);
        Assert.Equal(1, room.RowVersion);
    }

    [Fact]
    public async Task AC_12_OutOfOrderCannotGoToReady()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.OutOfOrder));

        // Given an OutOfOrder room. When Maintenance sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = Maintenance, rowVersion = 1 });

        // Then 422 with the exact message.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("OutOfOrder rooms can move to: Dirty.", (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_13_ReleasedRoomNeedsSupervisorInspection()
    {
        using var f = new HousekeepingFactory();
        // Standard Double, so the room type alone would qualify, but a released room is never a simple turn.
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.OutOfOrder, simpleTurn: false));
        var client = f.CreateClient();

        // Given an OutOfOrder room. Maintenance releases it to Dirty.
        var released = await PutStatus(client, 101, new { status = "Dirty", changedByUserId = Maintenance, rowVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        // A Housekeeper starts cleaning.
        var cleaning = await PutStatus(client, 101, new { status = "Cleaning", changedByUserId = HousekeeperA, rowVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, cleaning.StatusCode);

        // When a Housekeeper sets Ready.
        var response = await PutStatus(client, 101, new { status = "Ready", changedByUserId = HousekeeperA, rowVersion = 3 });

        // Then 422 and the room stays Cleaning.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var room = f.Query(db => db.Rooms.Single(r => r.Id == 101));
        Assert.Equal(RoomStatuses.Cleaning, room.Status);
    }

    [Fact]
    public async Task AC_39_StaleRowVersionIs409()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty, rowVersion: 3));
        var client = f.CreateClient();

        // Given two Supervisors both read room 101 at rowVersion 3.
        // When the first sets Cleaning with rowVersion 3.
        var first = await PutStatus(client, 101, new { status = "Cleaning", changedByUserId = Supervisor, rowVersion = 3 });
        // Then 200 with rowVersion 4.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(4, (await Json(first)).GetProperty("rowVersion").GetInt32());

        // When the second sets Inspected with the stale rowVersion 3.
        var second = await PutStatus(client, 101, new { status = "Inspected", changedByUserId = Supervisor, rowVersion = 3 });

        // Then 409 with the exact message.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Room 101 changed. Reload and try again.", (await Json(second)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_40_UnknownStatusListsAllowed()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty));

        // Given a status change request with status "Done".
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Done", changedByUserId = HousekeeperA, rowVersion = 1 });

        // Then 422 and the detail lists the allowed statuses.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Status must be one of: Dirty, Cleaning, Inspected, Ready, OutOfOrder.",
            (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AC_41_StatusChangeNeedsRowVersion()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Dirty));

        // Given a status change request with no rowVersion.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Cleaning", changedByUserId = HousekeeperA });

        // Then 400 and the error names the field "rowVersion".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(NamesField(await Json(response), "rowVersion"));
    }

    [Fact]
    public async Task AC_44_FrontDeskCannotSetReady()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Inspected));

        // Given an Inspected room. When a FrontDesk user sets Ready.
        var response = await PutStatus(f.CreateClient(), 101, new { status = "Ready", changedByUserId = FrontDesk, rowVersion = 1 });

        // Then 403 and the room stays Inspected.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoomStatuses.Inspected, f.Query(db => db.Rooms.Single(r => r.Id == 101)).Status);
    }
}
