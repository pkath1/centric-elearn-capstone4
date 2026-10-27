using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Larchmont.Api.Repositories;

public interface IRoomTurnaroundRepository
{
    // Every open turnaround at the hotel (one query).
    Task<List<RoomTurnaround>> ListOpenForHotelAsync(int hotelId);
    // Every turnaround at the hotel, open and closed (one query).
    Task<List<RoomTurnaround>> ListForHotelAsync(int hotelId);
    // Latest Ready stamp for each of the given rooms that has one (one query).
    Task<Dictionary<int, DateTime>> LatestReadyAtByRoomAsync(IReadOnlyCollection<int> roomIds);
    Task<RoomTurnaround?> GetOpenForRoomAsync(int roomId);
    Task<DateTime?> LatestReadyAtForRoomAsync(int roomId);
    // Adds without saving; call SaveAsync.
    void Add(RoomTurnaround turnaround);
    Task SaveAsync();
}

public class RoomTurnaroundRepository : IRoomTurnaroundRepository
{
    private readonly LarchmontDbContext _db;
    public RoomTurnaroundRepository(LarchmontDbContext db) => _db = db;

    public Task<List<RoomTurnaround>> ListOpenForHotelAsync(int hotelId) =>
        _db.RoomTurnarounds
            .Where(t => t.IsOpen && _db.Rooms.Any(r => r.Id == t.RoomId && r.HotelId == hotelId))
            .ToListAsync();

    public Task<List<RoomTurnaround>> ListForHotelAsync(int hotelId) =>
        _db.RoomTurnarounds
            .Where(t => _db.Rooms.Any(r => r.Id == t.RoomId && r.HotelId == hotelId))
            .ToListAsync();

    public async Task<Dictionary<int, DateTime>> LatestReadyAtByRoomAsync(IReadOnlyCollection<int> roomIds)
    {
        var rows = await _db.RoomTurnarounds
            .Where(t => t.ReadyAt != null && roomIds.Contains(t.RoomId))
            .GroupBy(t => t.RoomId)
            .Select(g => new { RoomId = g.Key, ReadyAt = g.Max(t => t.ReadyAt) })
            .ToListAsync();
        return rows.Where(r => r.ReadyAt != null).ToDictionary(r => r.RoomId, r => r.ReadyAt!.Value);
    }

    public Task<RoomTurnaround?> GetOpenForRoomAsync(int roomId) =>
        _db.RoomTurnarounds.Where(t => t.RoomId == roomId && t.IsOpen)
            .OrderBy(t => t.CheckoutAt)
            .FirstOrDefaultAsync();

    public Task<DateTime?> LatestReadyAtForRoomAsync(int roomId) =>
        _db.RoomTurnarounds.Where(t => t.RoomId == roomId && t.ReadyAt != null)
            .OrderByDescending(t => t.ReadyAt)
            .Select(t => t.ReadyAt)
            .FirstOrDefaultAsync();

    public void Add(RoomTurnaround turnaround) => _db.RoomTurnarounds.Add(turnaround);
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IFloorAssignmentRepository
{
    Task<List<FloorAssignment>> ListActiveAsync(int hotelId);
    // Ends the active owner of the floor (if another user) and makes userId the active owner.
    // Saves in two steps so the active-floor unique key is free before the new row is inserted.
    Task ReplaceActiveAsync(int hotelId, int floor, int housekeeperUserId);
}

public class FloorAssignmentRepository : IFloorAssignmentRepository
{
    private readonly LarchmontDbContext _db;
    public FloorAssignmentRepository(LarchmontDbContext db) => _db = db;

    public Task<List<FloorAssignment>> ListActiveAsync(int hotelId) =>
        _db.FloorAssignments.Where(f => f.HotelId == hotelId && f.Active)
            .OrderBy(f => f.Floor)
            .ToListAsync();

    public async Task ReplaceActiveAsync(int hotelId, int floor, int housekeeperUserId)
    {
        var current = await _db.FloorAssignments
            .FirstOrDefaultAsync(f => f.HotelId == hotelId && f.Floor == floor && f.Active);
        if (current != null)
        {
            if (current.HousekeeperUserId == housekeeperUserId) return;
            current.Active = false;
            await _db.SaveChangesAsync();
        }
        _db.FloorAssignments.Add(new FloorAssignment
        {
            HotelId = hotelId,
            Floor = floor,
            HousekeeperUserId = housekeeperUserId,
            Active = true
        });
        await _db.SaveChangesAsync();
    }
}

public interface IRoomSpotCheckRepository
{
    // Open spot-checks (not yet checked) for rooms at the hotel (one query).
    Task<List<RoomSpotCheck>> ListOpenForHotelAsync(int hotelId);
    Task<RoomSpotCheck?> GetOpenForRoomAsync(int roomId);
    // Status history rows by id (one query).
    Task<List<RoomStatusEvent>> GetHistoryAsync(IReadOnlyCollection<long> historyIds);
    // Adds without saving; call SaveAsync.
    void Add(RoomSpotCheck spotCheck);
    Task SaveAsync();
}

public class RoomSpotCheckRepository : IRoomSpotCheckRepository
{
    private readonly LarchmontDbContext _db;
    public RoomSpotCheckRepository(LarchmontDbContext db) => _db = db;

    public Task<List<RoomSpotCheck>> ListOpenForHotelAsync(int hotelId) =>
        _db.RoomSpotChecks
            .Where(s => s.CheckedAt == null && _db.Rooms.Any(r => r.Id == s.RoomId && r.HotelId == hotelId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

    public Task<RoomSpotCheck?> GetOpenForRoomAsync(int roomId) =>
        _db.RoomSpotChecks.Where(s => s.RoomId == roomId && s.CheckedAt == null)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync();

    public Task<List<RoomStatusEvent>> GetHistoryAsync(IReadOnlyCollection<long> historyIds) =>
        _db.RoomStatusHistory.Where(h => historyIds.Contains(h.Id)).ToListAsync();

    public void Add(RoomSpotCheck spotCheck) => _db.RoomSpotChecks.Add(spotCheck);
    public Task SaveAsync() => _db.SaveChangesAsync();
}
