using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Larchmont.Api.Repositories;

public interface IRoomTurnaroundRepository
{
    // Every open turnaround at the hotel (one query).
    Task<List<RoomTurnaround>> ListOpenForHotelAsync(int hotelId);
    // Every open turnaround at the hotel, plus closed ones checked out at or after closedSince (one query).
    Task<List<RoomTurnaround>> ListForHotelAsync(int hotelId, DateTime closedSince);
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

    public Task<List<RoomTurnaround>> ListForHotelAsync(int hotelId, DateTime closedSince) =>
        _db.RoomTurnarounds
            .Where(t => (t.IsOpen || t.CheckoutAt >= closedSince)
                        && _db.Rooms.Any(r => r.Id == t.RoomId && r.HotelId == hotelId))
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
    // Makes each listed user the active owner of their floor, ending the previous owner. All floors are
    // written in one database transaction: either every floor changes or none does (BR-17).
    Task ReplaceActiveForFloorsAsync(int hotelId, IReadOnlyList<(int Floor, int HousekeeperUserId)> assignments);
}

public class FloorAssignmentRepository : IFloorAssignmentRepository
{
    private readonly LarchmontDbContext _db;
    public FloorAssignmentRepository(LarchmontDbContext db) => _db = db;

    public Task<List<FloorAssignment>> ListActiveAsync(int hotelId) =>
        _db.FloorAssignments.Where(f => f.HotelId == hotelId && f.Active)
            .OrderBy(f => f.Floor)
            .ToListAsync();

    public async Task ReplaceActiveForFloorsAsync(int hotelId, IReadOnlyList<(int Floor, int HousekeeperUserId)> assignments)
    {
        var floors = assignments.Select(a => a.Floor).ToList();
        await using var tx = await _db.Database.BeginTransactionAsync();

        var current = await _db.FloorAssignments
            .Where(f => f.HotelId == hotelId && f.Active && floors.Contains(f.Floor))
            .ToListAsync();
        var toAdd = new List<FloorAssignment>();
        var ended = false;
        foreach (var a in assignments)
        {
            var owner = current.FirstOrDefault(c => c.Floor == a.Floor);
            if (owner?.HousekeeperUserId == a.HousekeeperUserId) continue;
            if (owner != null)
            {
                owner.Active = false;
                ended = true;
            }
            toAdd.Add(new FloorAssignment
            {
                HotelId = hotelId,
                Floor = a.Floor,
                HousekeeperUserId = a.HousekeeperUserId,
                Active = true
            });
        }

        // Two saves inside the one transaction: the old owners must be inactive before the new rows are
        // inserted, because the active-floor unique key rejects two active rows for one floor.
        if (ended) await _db.SaveChangesAsync();
        _db.FloorAssignments.AddRange(toAdd);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
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
