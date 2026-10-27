using System.Net;
using System.Net.Http.Json;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;
using Larchmont.Api.Services;
using Larchmont.Api.Validators;
using Moq;
using static Larchmont.Tests.HousekeepingFixture;

namespace Larchmont.Tests;

// Performance and concurrency fixes from phase 6 (evidence: docs/perf-note.md).
public class PerformanceTests
{
    // SLA breach list (SlaLookbackDays): closed turnarounds older than the lookback are not scanned or listed.
    [Fact]
    public async Task SlaBreaches_ClosedTurnaroundOlderThanLookback_IsNotListed()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            NewRoom(101, "101", 1, RoomStatuses.Ready),
            NewRoom(102, "102", 1, RoomStatuses.Ready),
            NewRoom(103, "103", 1, RoomStatuses.Dirty),
            // Closed 31 days ago, 100 minutes to Ready against a 45 minute target: outside the lookback.
            new RoomTurnaround { Id = 1, RoomId = 101, CheckoutAt = At(12).AddDays(-31), ReadyAt = At(12).AddDays(-31).AddMinutes(100), TargetMinutes = 45, IsOpen = false },
            // Closed 29 days ago, same overrun: inside the lookback, so listed.
            new RoomTurnaround { Id = 2, RoomId = 102, CheckoutAt = At(12).AddDays(-29), ReadyAt = At(12).AddDays(-29).AddMinutes(100), TargetMinutes = 45, IsOpen = false },
            // Open since 31 days ago: open turnarounds are always considered.
            new RoomTurnaround { Id = 3, RoomId = 103, CheckoutAt = At(12).AddDays(-31), TargetMinutes = 45, IsOpen = true });

        var body = await Json(await f.CreateClient().GetAsync("/api/hotels/1/sla-breaches"));

        var roomIds = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("roomId").GetInt32()).ToList();
        Assert.DoesNotContain(101, roomIds);
        Assert.Contains(102, roomIds);
        Assert.Contains(103, roomIds);
    }

    [Fact]
    public void SlaLookbackDays_IsThirtyDays()
    {
        Assert.Equal(30, HousekeepingBoardService.SlaLookbackDays);
    }

    // Floor assignments (BR-17): a request with one bad floor changes no floor.
    [Fact]
    public async Task FloorAssignments_OneFloorRejected_NoFloorChanges()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(new FloorAssignment { Id = 1, HotelId = 1, Floor = 2, HousekeeperUserId = HousekeeperA, Active = true });
        var client = f.CreateClient();

        // Given floor 2 belongs to housekeeper A. When floor 1 is valid and floor 2 names a Glasgow housekeeper.
        var response = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor,
            assignments = new[]
            {
                new { floor = 1, housekeeperUserId = HousekeeperB },
                new { floor = 2, housekeeperUserId = HousekeeperOtherHotel }
            }
        });

        // Then 422, and the active owners are exactly as before.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var list = await Json(await client.GetAsync("/api/hotels/1/floor-assignments"));
        var owners = list.EnumerateArray().Select(a => (a.GetProperty("floor").GetInt32(), a.GetProperty("housekeeperUserId").GetInt32())).ToList();
        Assert.Equal(new[] { (2, HousekeeperA) }, owners);
    }

    [Fact]
    public async Task FloorAssignments_TwoFloors_BothChangeInOneSetCall()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(
            new FloorAssignment { Id = 1, HotelId = 1, Floor = 1, HousekeeperUserId = HousekeeperA, Active = true },
            new FloorAssignment { Id = 2, HotelId = 1, Floor = 2, HousekeeperUserId = HousekeeperA, Active = true });
        var client = f.CreateClient();

        var response = await client.PutAsJsonAsync("/api/hotels/1/floor-assignments", new
        {
            changedByUserId = Supervisor,
            assignments = new[]
            {
                new { floor = 1, housekeeperUserId = HousekeeperB },
                new { floor = 2, housekeeperUserId = HousekeeperC }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var owners = f.Query(db => db.FloorAssignments.Where(a => a.Active)
            .OrderBy(a => a.Floor).Select(a => new { a.Floor, a.HousekeeperUserId }).ToList());
        Assert.Equal(new[] { (1, HousekeeperB), (2, HousekeeperC) },
            owners.Select(o => (o.Floor, o.HousekeeperUserId)).ToArray());
    }

    // Service level check of the all-or-nothing rule. The InMemory store cannot roll back, so the
    // rollback itself is not exercised here; the service must not write anything when one floor is invalid,
    // and must hand every floor to the repository in one call (which runs one transaction on MySQL).
    [Fact]
    public async Task FloorAssignmentService_OneInvalidFloor_NeverCallsRepository()
    {
        var hotels = new Mock<IHotelRepository>();
        var users = new Mock<IUserRepository>();
        var assignments = new Mock<IFloorAssignmentRepository>();
        hotels.Setup(h => h.GetAsync(1)).ReturnsAsync(new Hotel { Id = 1 });
        users.Setup(u => u.GetAsync(Supervisor)).ReturnsAsync(new User { Id = Supervisor, Role = "Supervisor", Active = true, HotelId = 1 });
        users.Setup(u => u.GetAsync(HousekeeperB)).ReturnsAsync(new User { Id = HousekeeperB, Role = "Housekeeper", Active = true, HotelId = 1 });
        users.Setup(u => u.GetAsync(HousekeeperOtherHotel)).ReturnsAsync(new User { Id = HousekeeperOtherHotel, Role = "Housekeeper", Active = true, HotelId = 2 });
        var sut = new FloorAssignmentService(hotels.Object, users.Object, assignments.Object, new SetFloorAssignmentsRequestValidator());

        await Assert.ThrowsAsync<BusinessRuleException>(() => sut.SetAsync(1, new SetFloorAssignmentsRequest(Supervisor,
            new[] { new FloorAssignmentRequest(1, HousekeeperB), new FloorAssignmentRequest(2, HousekeeperOtherHotel) })));

        assignments.Verify(a => a.ReplaceActiveForFloorsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<(int Floor, int HousekeeperUserId)>>()), Times.Never);
    }

    [Fact]
    public async Task FloorAssignmentService_ValidFloors_ReplacedInOneRepositoryCall()
    {
        var hotels = new Mock<IHotelRepository>();
        var users = new Mock<IUserRepository>();
        var assignments = new Mock<IFloorAssignmentRepository>();
        hotels.Setup(h => h.GetAsync(1)).ReturnsAsync(new Hotel { Id = 1 });
        users.Setup(u => u.GetAsync(Supervisor)).ReturnsAsync(new User { Id = Supervisor, Role = "Supervisor", Active = true, HotelId = 1 });
        users.Setup(u => u.GetAsync(HousekeeperB)).ReturnsAsync(new User { Id = HousekeeperB, Role = "Housekeeper", Active = true, HotelId = 1 });
        users.Setup(u => u.GetAsync(HousekeeperC)).ReturnsAsync(new User { Id = HousekeeperC, Role = "Housekeeper", Active = true, HotelId = 1 });
        assignments.Setup(a => a.ListActiveAsync(1)).ReturnsAsync(new List<FloorAssignment>());
        var sut = new FloorAssignmentService(hotels.Object, users.Object, assignments.Object, new SetFloorAssignmentsRequestValidator());

        await sut.SetAsync(1, new SetFloorAssignmentsRequest(Supervisor,
            new[] { new FloorAssignmentRequest(1, HousekeeperB), new FloorAssignmentRequest(2, HousekeeperC) }));

        assignments.Verify(a => a.ReplaceActiveForFloorsAsync(1,
            It.Is<IReadOnlyList<(int Floor, int HousekeeperUserId)>>(l => l.Count == 2)), Times.Once);
    }

    // Validators replace the inline checks. The 400 body must still name the field.
    [Fact]
    public async Task LockCheckout_WhitespaceRoomNumber_Is400NamingRoomNumber()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));

        var response = await LockCheckout(f.CreateClient(), HousekeepingFactory.LockKey, new { hotelId = 1, roomNumber = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(NamesField(await Json(response), "roomNumber"));
    }

    [Fact]
    public async Task LockCheckout_HotelIdZero_Is400NamingHotelId()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping();

        var response = await LockCheckout(f.CreateClient(), HousekeepingFactory.LockKey, new { hotelId = 0, roomNumber = "101" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(NamesField(await Json(response), "hotelId"));
    }

    [Fact]
    public async Task SetStatus_ReasonLengthCountedAfterTrim()
    {
        using var f = new HousekeepingFactory();
        f.SeedHousekeeping(NewRoom(101, "101", 1, RoomStatuses.Ready));
        var client = f.CreateClient();
        var padded255 = "  " + new string('x', 255) + "  ";

        // 255 characters once trimmed is accepted, as before the validator change.
        var accepted = await PutStatus(client, 101, new { status = "OutOfOrder", changedByUserId = Maintenance, rowVersion = 1, reason = padded255 });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // 256 characters is refused with the field named (validation runs before the room is read).
        var refused = await PutStatus(client, 101, new { status = "OutOfOrder", changedByUserId = Maintenance, rowVersion = 2, reason = new string('x', 256) });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True(NamesField(await Json(refused), "reason"));
    }
}
