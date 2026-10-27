using Larchmont.Api.Dtos;
using Larchmont.Api.Domain;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;

namespace Larchmont.Api.Services;

public interface IReportService
{
    Task<OccupancyReportDto> GetOccupancyAsync(int? hotelId, DateOnly from, DateOnly to);
    Task<List<ArrivalDto>> GetArrivalsAsync(DateOnly date, int? hotelId);
    Task<List<RevenueRowDto>> GetRevenueAsync(DateOnly from, DateOnly to);
}

public class ReportService : IReportService
{
    private readonly IBookingRepository _bookings;
    private readonly IRoomRepository _rooms;
    private readonly IHotelRepository _hotels;
    private readonly IGuestRepository _guests;
    private readonly IRoomTypeRepository _types;

    public ReportService(IBookingRepository bookings, IRoomRepository rooms, IHotelRepository hotels,
        IGuestRepository guests, IRoomTypeRepository types)
    {
        _bookings = bookings;
        _rooms = rooms;
        _hotels = hotels;
        _guests = guests;
        _types = types;
    }

    public async Task<OccupancyReportDto> GetOccupancyAsync(int? hotelId, DateOnly from, DateOnly to)
    {
        if (to < from) throw new BusinessRuleException("'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber > 92) throw new BusinessRuleException("Report range is limited to 93 days.");
        if (hotelId.HasValue && await _hotels.GetAsync(hotelId.Value) == null)
            throw new NotFoundException($"Hotel {hotelId} not found.");

        var bookings = await _bookings.ListOverlappingAsync(hotelId, from, to);
        var days = new List<OccupancyDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var sold = bookings.Count(b => b.CheckIn <= day && day <= b.CheckOut);
            days.Add(new OccupancyDayDto(day, sold));
        }

        // BR-20: out-of-order rooms are not available, so they leave the capacity; they are reported separately.
        var rooms = await _rooms.CountAvailableForHotelAsync(hotelId);
        var outOfOrder = await _rooms.CountOutOfOrderForHotelAsync(hotelId);
        var nightsSold = days.Sum(d => d.RoomsSold);
        var capacity = rooms * days.Count;
        var pct = capacity == 0 ? 0m : Math.Round(100m * nightsSold / capacity, 1);
        return new OccupancyReportDto(hotelId, from, to, rooms, nightsSold, pct, days, outOfOrder);
    }

    public async Task<List<ArrivalDto>> GetArrivalsAsync(DateOnly date, int? hotelId)
    {
        var result = new List<ArrivalDto>();
        foreach (var b in await _bookings.ListArrivalsAsync(date, hotelId))
        {
            var hotel = await _hotels.GetAsync(b.HotelId);
            var guest = await _guests.GetAsync(b.GuestId);
            var type = await _types.GetAsync(b.RoomTypeId);
            var room = b.RoomId.HasValue ? await _rooms.GetAsync(b.RoomId.Value) : null;
            result.Add(new ArrivalDto(b.Id, b.Reference, hotel?.Name ?? "", guest == null ? "" : $"{guest.FirstName} {guest.LastName}",
                type?.Name ?? "", room?.RoomNumber, b.Nights, b.Status));
        }
        return result;
    }

    public async Task<List<RevenueRowDto>> GetRevenueAsync(DateOnly from, DateOnly to)
    {
        var bookings = await _bookings.ListOverlappingAsync(null, from, to);
        var hotels = (await _hotels.ListAsync()).ToDictionary(h => h.Id, h => h.Name);
        return bookings.Where(b => b.CheckIn >= from && b.CheckIn <= to)
            .GroupBy(b => b.HotelId)
            .Select(g => new RevenueRowDto(g.Key, hotels.GetValueOrDefault(g.Key, ""), g.Count(), g.Sum(b => b.TotalAmount)))
            .OrderBy(r => r.HotelName).ToList();
    }
}
