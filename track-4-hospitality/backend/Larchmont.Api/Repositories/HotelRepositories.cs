using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Larchmont.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Larchmont.Api.Repositories;

public interface IHotelRepository
{
    Task<List<Hotel>> ListAsync();
    Task<Hotel?> GetAsync(int id);
    Task<Hotel?> GetByCodeAsync(string code);
    Task AddAsync(Hotel hotel);
    Task SaveAsync();
}

public class HotelRepository : IHotelRepository
{
    private readonly LarchmontDbContext _db;
    public HotelRepository(LarchmontDbContext db) => _db = db;

    public Task<List<Hotel>> ListAsync() => _db.Hotels.OrderBy(h => h.Name).ToListAsync();
    public Task<Hotel?> GetAsync(int id) => _db.Hotels.FirstOrDefaultAsync(h => h.Id == id);
    public Task<Hotel?> GetByCodeAsync(string code) => _db.Hotels.FirstOrDefaultAsync(h => h.Code == code);
    public async Task AddAsync(Hotel hotel) { _db.Hotels.Add(hotel); await _db.SaveChangesAsync(); }
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IRoomTypeRepository
{
    Task<List<RoomType>> ListAsync();
    Task<RoomType?> GetAsync(int id);
    Task<RoomType?> GetByCodeAsync(string code);
    Task AddAsync(RoomType type);
    Task SaveAsync();
}

public class RoomTypeRepository : IRoomTypeRepository
{
    private readonly LarchmontDbContext _db;
    public RoomTypeRepository(LarchmontDbContext db) => _db = db;

    public Task<List<RoomType>> ListAsync() => _db.RoomTypes.OrderBy(t => t.Id).ToListAsync();
    public Task<RoomType?> GetAsync(int id) => _db.RoomTypes.FirstOrDefaultAsync(t => t.Id == id);
    public Task<RoomType?> GetByCodeAsync(string code) => _db.RoomTypes.FirstOrDefaultAsync(t => t.Code == code);
    public async Task AddAsync(RoomType type) { _db.RoomTypes.Add(type); await _db.SaveChangesAsync(); }
    public Task SaveAsync() => _db.SaveChangesAsync();
}

public interface IRoomRepository
{
    Task<List<Room>> ListAsync(int? hotelId, string? status, int? floor);
    Task<Room?> GetAsync(int id);
    Task<Room?> GetByNumberAsync(int hotelId, string roomNumber);
    Task<int> CountAvailableForHotelAsync(int? hotelId);
    Task<int> CountOutOfOrderForHotelAsync(int? hotelId);
    Task AddAsync(Room room);
    Task AddStatusEventAsync(RoomStatusEvent ev);
    Task<RoomStatusEvent?> GetLastEntryAsync(int roomId, string toStatus);
    Task AddTurnaroundAsync(RoomTurnaround turnaround);
    Task<RoomTurnaround?> GetOpenTurnaroundAsync(int roomId);
    Task AddSpotCheckAsync(RoomSpotCheck spotCheck);
    Task SaveAsync();
    Task SaveChangedAsync(Room room);
}

public class RoomRepository : IRoomRepository
{
    private readonly LarchmontDbContext _db;
    public RoomRepository(LarchmontDbContext db) => _db = db;

    public Task<List<Room>> ListAsync(int? hotelId, string? status, int? floor)
    {
        var q = _db.Rooms.AsQueryable();
        if (hotelId.HasValue) q = q.Where(r => r.HotelId == hotelId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
        if (floor.HasValue) q = q.Where(r => r.Floor == floor);
        return q.OrderBy(r => r.HotelId).ThenBy(r => r.RoomNumber).ToListAsync();
    }

    public Task<Room?> GetAsync(int id) => _db.Rooms.FirstOrDefaultAsync(r => r.Id == id);

    public Task<Room?> GetByNumberAsync(int hotelId, string roomNumber) =>
        _db.Rooms.FirstOrDefaultAsync(r => r.HotelId == hotelId && r.RoomNumber == roomNumber);

    // BR-20: out-of-order rooms are not available for sale, so they leave the capacity.
    public Task<int> CountAvailableForHotelAsync(int? hotelId)
    {
        var q = _db.Rooms.Where(r => r.Status != RoomStatuses.OutOfOrder);
        if (hotelId.HasValue) q = q.Where(r => r.HotelId == hotelId);
        return q.CountAsync();
    }

    public Task<int> CountOutOfOrderForHotelAsync(int? hotelId)
    {
        var q = _db.Rooms.Where(r => r.Status == RoomStatuses.OutOfOrder);
        if (hotelId.HasValue) q = q.Where(r => r.HotelId == hotelId);
        return q.CountAsync();
    }

    public async Task AddAsync(Room room) { _db.Rooms.Add(room); await _db.SaveChangesAsync(); }
    public Task AddStatusEventAsync(RoomStatusEvent ev) { _db.RoomStatusHistory.Add(ev); return Task.CompletedTask; }

    public Task<RoomStatusEvent?> GetLastEntryAsync(int roomId, string toStatus) =>
        _db.RoomStatusHistory.Where(e => e.RoomId == roomId && e.ToStatus == toStatus)
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync();

    public Task AddTurnaroundAsync(RoomTurnaround turnaround) { _db.RoomTurnarounds.Add(turnaround); return Task.CompletedTask; }

    public Task<RoomTurnaround?> GetOpenTurnaroundAsync(int roomId) =>
        _db.RoomTurnarounds.FirstOrDefaultAsync(t => t.RoomId == roomId && t.IsOpen);

    public Task AddSpotCheckAsync(RoomSpotCheck spotCheck) { _db.RoomSpotChecks.Add(spotCheck); return Task.CompletedTask; }

    public Task SaveAsync() => _db.SaveChangesAsync();

    // Two writes of the same room in parallel: the row_version token catches the second one (BR-19).
    public async Task SaveChangedAsync(Room room)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException($"Room {room.RoomNumber} changed. Reload and try again.");
        }
    }
}
