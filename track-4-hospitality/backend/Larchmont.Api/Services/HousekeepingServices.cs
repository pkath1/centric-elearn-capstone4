using FluentValidation;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;

namespace Larchmont.Api.Services;

public interface IHousekeepingBoardService
{
    Task<HotelBoardDto> GetBoardAsync(int hotelId);
    Task<SlaBreachListDto> GetSlaBreachesAsync(int hotelId);
    Task<ReadyByDto> GetReadyByAsync(int roomId);
    Task<SpotCheckListDto> GetSpotChecksAsync(int hotelId);
    Task<SpotCheckDto> MarkSpotCheckedAsync(int roomId, SpotCheckRequest request);
}

public class HousekeepingBoardService : IHousekeepingBoardService
{
    private const int ReadyByFallbackMinutes = 15;
    private static readonly string[] BusyStatuses = { RoomStatuses.Dirty, RoomStatuses.Cleaning };

    private readonly IHotelRepository _hotels;
    private readonly IRoomRepository _rooms;
    private readonly IRoomTypeRepository _types;
    private readonly IRoomTurnaroundRepository _turnarounds;
    private readonly IFloorAssignmentRepository _assignments;
    private readonly IRoomSpotCheckRepository _spotChecks;
    private readonly IUserRepository _users;
    private readonly IClock _clock;
    private readonly IValidator<SpotCheckRequest> _spotCheckValidator;

    public HousekeepingBoardService(IHotelRepository hotels, IRoomRepository rooms, IRoomTypeRepository types,
        IRoomTurnaroundRepository turnarounds, IFloorAssignmentRepository assignments,
        IRoomSpotCheckRepository spotChecks, IUserRepository users, IClock clock,
        IValidator<SpotCheckRequest> spotCheckValidator)
    {
        _hotels = hotels;
        _rooms = rooms;
        _types = types;
        _turnarounds = turnarounds;
        _assignments = assignments;
        _spotChecks = spotChecks;
        _users = users;
        _clock = clock;
        _spotCheckValidator = spotCheckValidator;
    }

    public async Task<HotelBoardDto> GetBoardAsync(int hotelId)
    {
        var now = _clock.UtcNow;
        await EnsureHotelAsync(hotelId);

        var rooms = await _rooms.ListAsync(hotelId, null, null);
        var typeNames = await TypeNamesAsync();
        var openByRoom = OpenByRoom(await _turnarounds.ListOpenForHotelAsync(hotelId));
        var readyRoomIds = rooms.Where(r => r.Status == RoomStatuses.Ready).Select(r => r.Id).ToList();
        var readyAt = await _turnarounds.LatestReadyAtByRoomAsync(readyRoomIds);
        var ownerByFloor = (await _assignments.ListActiveAsync(hotelId))
            .GroupBy(a => a.Floor)
            .ToDictionary(g => g.Key, g => g.First().HousekeeperUserId);

        var ordered = rooms
            .OrderBy(r => r.Floor)
            .ThenBy(r => r.RoomNumber.Length)
            .ThenBy(r => r.RoomNumber, StringComparer.Ordinal)
            .ToList();

        // Busiest floor (BR-22): most rooms Dirty or Cleaning; ties go to the lower floor.
        var busyCounts = ordered.Where(r => BusyStatuses.Contains(r.Status))
            .GroupBy(r => r.Floor)
            .ToDictionary(g => g.Key, g => g.Count());
        int? busiestFloor = null;
        var most = 0;
        foreach (var floor in ordered.Select(r => r.Floor).Distinct().OrderBy(f => f))
        {
            var count = busyCounts.GetValueOrDefault(floor);
            if (count > most)
            {
                most = count;
                busiestFloor = floor;
            }
        }

        var floors = ordered
            .GroupBy(r => r.Floor)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var boardRooms = g.Select(r =>
                {
                    openByRoom.TryGetValue(r.Id, out var open);
                    readyAt.TryGetValue(r.Id, out var lastReady);
                    var (readyBy, _) = ComputeReadyBy(r, open, readyAt.ContainsKey(r.Id) ? lastReady : null, now);
                    return new BoardRoomDto(
                        r.Id,
                        r.RoomNumber,
                        typeNames.GetValueOrDefault(r.RoomTypeId, ""),
                        r.Status,
                        AsUtc(open?.CheckoutAt),
                        open?.TargetMinutes,
                        AsUtc(readyBy),
                        ownerByFloor.TryGetValue(g.Key, out var owner) ? owner : null);
                }).ToList();
                return new BoardFloorDto(g.Key, busiestFloor == g.Key, boardRooms);
            })
            .ToList();

        return new HotelBoardDto(hotelId, busiestFloor, floors);
    }

    public async Task<SlaBreachListDto> GetSlaBreachesAsync(int hotelId)
    {
        var now = _clock.UtcNow;
        await EnsureHotelAsync(hotelId);

        var roomsById = (await _rooms.ListAsync(hotelId, null, null)).ToDictionary(r => r.Id);
        var typeNames = await TypeNamesAsync();
        var turnarounds = await _turnarounds.ListForHotelAsync(hotelId);

        var breaches = new List<(SlaBreachDto Item, string RoomNumber)>();
        foreach (var t in turnarounds)
        {
            if (!roomsById.TryGetValue(t.RoomId, out var room)) continue;
            var end = t.ReadyAt ?? now;
            // BR-14: whole elapsed minutes must be more than the target; exactly on target is not a breach.
            var elapsed = (int)Math.Floor((end - t.CheckoutAt).TotalMinutes);
            var overrun = elapsed - t.TargetMinutes;
            if (overrun <= 0) continue;
            breaches.Add((new SlaBreachDto(
                room.Id,
                room.RoomNumber,
                typeNames.GetValueOrDefault(room.RoomTypeId, ""),
                AsUtc(t.CheckoutAt),
                AsUtc(t.ReadyAt),
                t.TargetMinutes,
                elapsed,
                overrun), room.RoomNumber));
        }

        var items = breaches
            .OrderByDescending(b => b.Item.OverrunMinutes)
            .ThenBy(b => b.RoomNumber.Length)
            .ThenBy(b => b.RoomNumber, StringComparer.Ordinal)
            .Select(b => b.Item)
            .ToList();
        return new SlaBreachListDto(items);
    }

    public async Task<ReadyByDto> GetReadyByAsync(int roomId)
    {
        var now = _clock.UtcNow;
        var room = await _rooms.GetAsync(roomId) ?? throw new NotFoundException($"Room {roomId} not found.");

        if (room.Status == RoomStatuses.OutOfOrder)
            return new ReadyByDto(room.Id, room.RoomNumber, room.Status, null, false, true, AsUtc(room.OooExpectedReturn));

        var open = await _turnarounds.GetOpenForRoomAsync(roomId);
        var lastReady = room.Status == RoomStatuses.Ready
            ? await _turnarounds.LatestReadyAtForRoomAsync(roomId)
            : null;
        var (readyBy, overdue) = ComputeReadyBy(room, open, lastReady, now);
        return new ReadyByDto(room.Id, room.RoomNumber, room.Status, AsUtc(readyBy), overdue, false, null);
    }

    public async Task<SpotCheckListDto> GetSpotChecksAsync(int hotelId)
    {
        await EnsureHotelAsync(hotelId);

        var checks = await _spotChecks.ListOpenForHotelAsync(hotelId);
        var roomsById = (await _rooms.ListAsync(hotelId, null, null)).ToDictionary(r => r.Id);
        var historyById = (await _spotChecks.GetHistoryAsync(checks.Select(c => c.StatusHistoryId).Distinct().ToList()))
            .ToDictionary(h => h.Id);

        var items = new List<SpotCheckDto>();
        foreach (var check in checks)
        {
            if (!roomsById.TryGetValue(check.RoomId, out var room)) continue;
            if (!historyById.TryGetValue(check.StatusHistoryId, out var history)) continue;
            items.Add(new SpotCheckDto(room.Id, room.RoomNumber, AsUtc(history.ChangedAt), history.ChangedByUserId));
        }
        return new SpotCheckListDto(items);
    }

    public async Task<SpotCheckDto> MarkSpotCheckedAsync(int roomId, SpotCheckRequest request)
    {
        await _spotCheckValidator.ValidateAndThrowAsync(request);
        var caller = await _users.GetAsync(request.ChangedByUserId)
            ?? throw new NotFoundException($"User {request.ChangedByUserId} not found.");
        if (!caller.Active || !HousekeepingRoles.Supervisors.Contains(caller.Role))
            throw new ForbiddenException("Only a Supervisor can mark a spot-check.");

        var room = await _rooms.GetAsync(roomId) ?? throw new NotFoundException($"Room {roomId} not found.");
        var check = await _spotChecks.GetOpenForRoomAsync(roomId)
            ?? throw new NotFoundException($"Room {room.RoomNumber} has no open spot-check.");
        var history = (await _spotChecks.GetHistoryAsync(new[] { check.StatusHistoryId })).FirstOrDefault();

        check.CheckedAt = _clock.UtcNow;
        check.CheckedByUserId = caller.Id;
        await _spotChecks.SaveAsync();

        return new SpotCheckDto(room.Id, room.RoomNumber, AsUtc(history?.ChangedAt ?? check.CreatedAt), history?.ChangedByUserId);
    }

    private async Task EnsureHotelAsync(int hotelId)
    {
        if (await _hotels.GetAsync(hotelId) == null) throw new NotFoundException($"Hotel {hotelId} not found.");
    }

    private async Task<Dictionary<int, string>> TypeNamesAsync() =>
        (await _types.ListAsync()).ToDictionary(t => t.Id, t => t.Name);

    private static Dictionary<int, RoomTurnaround> OpenByRoom(IEnumerable<RoomTurnaround> open) =>
        open.GroupBy(t => t.RoomId).ToDictionary(g => g.Key, g => g.OrderBy(t => t.CheckoutAt).First());

    // BR-15. Dirty or Cleaning: checkout stamp plus target, or now plus 15 minutes (overdue) when that is past.
    // Inspected: now. Ready: the Ready time. OutOfOrder and anything else: no ready time.
    private static (DateTime? ReadyBy, bool Overdue) ComputeReadyBy(Room room, RoomTurnaround? open, DateTime? readyAt, DateTime now)
    {
        switch (room.Status)
        {
            case RoomStatuses.Dirty:
            case RoomStatuses.Cleaning:
                if (open == null) return (null, false);
                var due = open.CheckoutAt.AddMinutes(open.TargetMinutes);
                return due >= now ? (due, false) : (now.AddMinutes(ReadyByFallbackMinutes), true);
            case RoomStatuses.Inspected:
                return (now, false);
            case RoomStatuses.Ready:
                return (readyAt, false);
            default:
                return (null, false);
        }
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}

public interface IFloorAssignmentService
{
    Task<List<FloorAssignmentDto>> ListAsync(int hotelId);
    Task<List<FloorAssignmentDto>> SetAsync(int hotelId, SetFloorAssignmentsRequest request);
}

public class FloorAssignmentService : IFloorAssignmentService
{
    private readonly IHotelRepository _hotels;
    private readonly IUserRepository _users;
    private readonly IFloorAssignmentRepository _assignments;
    private readonly IValidator<SetFloorAssignmentsRequest> _validator;

    public FloorAssignmentService(IHotelRepository hotels, IUserRepository users,
        IFloorAssignmentRepository assignments, IValidator<SetFloorAssignmentsRequest> validator)
    {
        _hotels = hotels;
        _users = users;
        _assignments = assignments;
        _validator = validator;
    }

    public async Task<List<FloorAssignmentDto>> ListAsync(int hotelId)
    {
        await EnsureHotelAsync(hotelId);
        return await ActiveListAsync(hotelId);
    }

    public async Task<List<FloorAssignmentDto>> SetAsync(int hotelId, SetFloorAssignmentsRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        await EnsureHotelAsync(hotelId);

        var caller = await _users.GetAsync(request.ChangedByUserId)
            ?? throw new NotFoundException($"User {request.ChangedByUserId} not found.");
        if (!caller.Active || !HousekeepingRoles.Supervisors.Contains(caller.Role))
            throw new ForbiddenException("Only a Supervisor can assign floors.");

        // Check every assignee before changing anything (BR-17).
        foreach (var item in request.Assignments)
        {
            var user = await _users.GetAsync(item.HousekeeperUserId)
                ?? throw new NotFoundException($"User {item.HousekeeperUserId} not found.");
            if (user.Role != "Housekeeper" || !user.Active || user.HotelId != hotelId)
                throw new BusinessRuleException($"User {item.HousekeeperUserId} does not work at hotel {hotelId}.");
        }

        foreach (var item in request.Assignments)
            await _assignments.ReplaceActiveAsync(hotelId, item.Floor, item.HousekeeperUserId);

        return await ActiveListAsync(hotelId);
    }

    private async Task<List<FloorAssignmentDto>> ActiveListAsync(int hotelId) =>
        (await _assignments.ListActiveAsync(hotelId))
            .Select(a => new FloorAssignmentDto(a.Floor, a.HousekeeperUserId))
            .ToList();

    private async Task EnsureHotelAsync(int hotelId)
    {
        if (await _hotels.GetAsync(hotelId) == null) throw new NotFoundException($"Hotel {hotelId} not found.");
    }
}

internal static class HousekeepingRoles
{
    // Roles that may mark spot-checks and assign floors. Admin has all moves (spec, Users table).
    public static readonly string[] Supervisors = { "Supervisor", "Admin" };
}

public interface ILockIntegrationService
{
    Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request);
}

public class LockIntegrationService : ILockIntegrationService
{
    public Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request) => throw new NotImplementedException();
}
