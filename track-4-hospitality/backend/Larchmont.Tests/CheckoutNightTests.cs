using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Larchmont.Api.Repositories;
using Larchmont.Api.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Larchmont.Tests;

// LH-56: a guest sleeps the night of check-in, not the night of check-out.
// A booking that checks out on day D must not count day D as a sold night.
public class CheckoutNightTests
{
    private static Booking Stay(int id, int hotelId, string checkIn, string checkOut) => new()
    {
        Id = id,
        Reference = $"LH-{50000 + id}",
        HotelId = hotelId,
        GuestId = 1,
        RoomTypeId = 1,
        RatePlanId = 1,
        CheckIn = DateOnly.Parse(checkIn),
        CheckOut = DateOnly.Parse(checkOut),
        Adults = 1,
        Status = BookingStatuses.CheckedOut,
        TotalAmount = 100m,
    };

    [Fact]
    public async Task Occupancy_BookingCheckingOutOnDay_DoesNotCountThatDay()
    {
        // Given one booking 1 to 2 October at hotel 1 (one night, the night of 1 October).
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(b => b.ListOverlappingAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<Booking> { Stay(1, 1, "2026-10-01", "2026-10-02") });
        var rooms = new Mock<IRoomRepository>();
        rooms.Setup(r => r.CountAvailableForHotelAsync(1)).ReturnsAsync(20);
        rooms.Setup(r => r.CountOutOfOrderForHotelAsync(1)).ReturnsAsync(0);
        var hotels = new Mock<IHotelRepository>();
        hotels.Setup(h => h.GetAsync(1)).ReturnsAsync(new Hotel { Id = 1, Name = "Larchmont Edinburgh" });
        var sut = new ReportService(bookings.Object, rooms.Object, hotels.Object,
            new Mock<IGuestRepository>().Object, new Mock<IRoomTypeRepository>().Object);

        // When the occupancy report is read for 1 and 2 October.
        var report = await sut.GetOccupancyAsync(1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));

        // Then 1 October has one room sold and 2 October (the checkout day) has none.
        Assert.Equal(1, report.Days[0].RoomsSold);
        Assert.Equal(0, report.Days[1].RoomsSold);
        Assert.Equal(1, report.RoomNightsSold);
        Assert.Equal(2.5m, report.OccupancyPct); // 1 of 40 room nights
    }

    [Fact]
    public async Task ListOverlapping_BookingCheckingOutOnFromDay_IsExcluded()
    {
        var options = new DbContextOptionsBuilder<LarchmontDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new LarchmontDbContext(options))
        {
            seed.Bookings.Add(Stay(1, 1, "2026-09-29", "2026-10-02")); // checks out on the 'from' day
            seed.Bookings.Add(Stay(2, 1, "2026-10-02", "2026-10-03")); // first night is the 'from' day
            seed.Bookings.Add(Stay(3, 1, "2026-10-07", "2026-10-08")); // checks in on the 'to' day
            seed.Bookings.Add(Stay(4, 1, "2026-10-08", "2026-10-09")); // checks in after 'to'
            await seed.SaveChangesAsync();
        }

        await using var db = new LarchmontDbContext(options);
        var repo = new BookingRepository(db);

        var result = await repo.ListOverlappingAsync(1, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 7));

        Assert.DoesNotContain(result, b => b.Id == 1);
        Assert.Contains(result, b => b.Id == 2);
        Assert.Contains(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 4);
    }
}
