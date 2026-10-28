using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Larchmont.Api.Repositories;

public record BookingFilter(int? HotelId, int? GuestId, string? Status, DateOnly? From, DateOnly? To, int Page, int PageSize);

public interface IBookingRepository
{
    Task<(List<Booking> Items, int Total)> SearchAsync(BookingFilter filter);
    Task<Booking?> GetAsync(int id);
    Task<Booking?> GetByExternalRefAsync(string externalRef);
    Task<List<Booking>> ListForGuestAsync(int guestId);
    Task<bool> RoomHasOverlapAsync(int roomId, int excludeBookingId, DateOnly checkIn, DateOnly checkOut);
    Task<List<Booking>> ListOverlappingAsync(int? hotelId, DateOnly from, DateOnly to);
    Task<List<Booking>> ListArrivalsAsync(DateOnly date, int? hotelId);
    Task<List<Booking>> ListForRoomAsync(int roomId, DateOnly from, DateOnly to);
    Task<int> NextReferenceNumberAsync();
    Task<List<BookingNote>> ListNotesAsync(int bookingId);
    Task AddAsync(Booking booking);
    Task AddNoteAsync(BookingNote note);
    Task SaveAsync();
}

public class BookingRepository : IBookingRepository
{
    private readonly LarchmontDbContext _db;
    public BookingRepository(LarchmontDbContext db) => _db = db;

    public async Task<(List<Booking> Items, int Total)> SearchAsync(BookingFilter f)
    {
        var q = _db.Bookings.AsQueryable();
        if (f.HotelId.HasValue) q = q.Where(b => b.HotelId == f.HotelId);
        if (f.GuestId.HasValue) q = q.Where(b => b.GuestId == f.GuestId);
        if (!string.IsNullOrEmpty(f.Status)) q = q.Where(b => b.Status == f.Status);
        if (f.From.HasValue) q = q.Where(b => b.CheckIn >= f.From);
        if (f.To.HasValue) q = q.Where(b => b.CheckIn <= f.To);
        var total = await q.CountAsync();
        var items = await q.OrderBy(b => b.CheckIn).ThenBy(b => b.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        return (items, total);
    }

    public Task<Booking?> GetAsync(int id) => _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
    public Task<Booking?> GetByExternalRefAsync(string externalRef) =>
        _db.Bookings.FirstOrDefaultAsync(b => b.ExternalRef == externalRef);
    public Task<List<Booking>> ListForGuestAsync(int guestId) =>
        _db.Bookings.Where(b => b.GuestId == guestId).OrderByDescending(b => b.CheckIn).ToListAsync();

    public Task<bool> RoomHasOverlapAsync(int roomId, int excludeBookingId, DateOnly checkIn, DateOnly checkOut) =>
        _db.Bookings.AnyAsync(b => b.RoomId == roomId && b.Id != excludeBookingId
            && (b.Status == BookingStatuses.Confirmed || b.Status == BookingStatuses.CheckedIn)
            && b.CheckIn < checkOut && b.CheckOut > checkIn);

    public Task<List<Booking>> ListOverlappingAsync(int? hotelId, DateOnly from, DateOnly to)
    {
        var staying = BookingStatuses.Staying;
        var q = _db.Bookings.Where(b => staying.Contains(b.Status) && b.CheckIn <= to && b.CheckOut > from);
        if (hotelId.HasValue) q = q.Where(b => b.HotelId == hotelId);
        return q.ToListAsync();
    }

    public Task<List<Booking>> ListArrivalsAsync(DateOnly date, int? hotelId)
    {
        var q = _db.Bookings.Where(b => b.CheckIn == date && b.Status == BookingStatuses.Confirmed);
        if (hotelId.HasValue) q = q.Where(b => b.HotelId == hotelId);
        return q.OrderBy(b => b.HotelId).ThenBy(b => b.Id).ToListAsync();
    }

    public Task<List<Booking>> ListForRoomAsync(int roomId, DateOnly from, DateOnly to) =>
        _db.Bookings.Where(b => b.RoomId == roomId
            && (b.Status == BookingStatuses.Confirmed || b.Status == BookingStatuses.CheckedIn)
            && b.CheckIn < to && b.CheckOut > from).ToListAsync();

    public async Task<int> NextReferenceNumberAsync()
    {
        var last = await _db.Bookings.Where(b => b.Reference.StartsWith("LH-"))
            .OrderByDescending(b => b.Id).Select(b => b.Reference).FirstOrDefaultAsync();
        return last != null && int.TryParse(last.Substring(3), out var n) ? n + 1 : 50000;
    }

    public Task<List<BookingNote>> ListNotesAsync(int bookingId) =>
        _db.BookingNotes.Where(n => n.BookingId == bookingId).OrderBy(n => n.CreatedAt).ToListAsync();

    public async Task AddAsync(Booking booking) { _db.Bookings.Add(booking); await _db.SaveChangesAsync(); }
    public Task AddNoteAsync(BookingNote note) { _db.BookingNotes.Add(note); return Task.CompletedTask; }
    public Task SaveAsync() => _db.SaveChangesAsync();
}
