using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;

namespace Larchmont.Api.Mappers;

public static class HotelMapper
{
    public static HotelDto ToDto(this Hotel h) => new(h.Id, h.Code, h.Name, h.City, h.Region, h.AddressLine, h.Phone, h.Active);

    public static void Apply(this Hotel h, HotelRequest r)
    {
        h.Code = r.Code.Trim().ToUpperInvariant();
        h.Name = r.Name.Trim();
        h.City = r.City.Trim();
        h.Region = r.Region.Trim();
        h.AddressLine = r.AddressLine.Trim();
        h.Phone = r.Phone.Trim();
        h.Active = r.Active;
    }
}

public static class RoomTypeMapper
{
    public static RoomTypeDto ToDto(this RoomType t) => new(t.Id, t.Code, t.Name, t.MaxOccupancy, t.BaseRate);

    public static void Apply(this RoomType t, RoomTypeRequest r)
    {
        t.Code = r.Code.Trim().ToUpperInvariant();
        t.Name = r.Name.Trim();
        t.MaxOccupancy = r.MaxOccupancy;
        t.BaseRate = r.BaseRate;
    }
}

public static class RoomMapper
{
    public static RoomDto ToDto(this Room r, string roomTypeName) =>
        new(r.Id, r.HotelId, r.RoomTypeId, roomTypeName, r.RoomNumber, r.Floor, r.Status, r.Notes,
            r.RowVersion, r.SimpleTurn, r.OooReason, r.OooSince, r.OooExpectedReturn);
}

public static class GuestMapper
{
    public static GuestDto ToDto(this Guest g) =>
        new(g.Id, g.FirstName, g.LastName, g.Email, g.Phone, g.AddressLine, g.City, g.Postcode, g.Country, g.Region, g.LoyaltyTier, g.CreatedAt);

    public static void Apply(this Guest g, GuestRequest r)
    {
        g.FirstName = r.FirstName.Trim();
        g.LastName = r.LastName.Trim();
        g.Email = r.Email.Trim();
        g.Phone = r.Phone.Trim();
        g.AddressLine = r.AddressLine.Trim();
        g.City = r.City.Trim();
        g.Postcode = r.Postcode.Trim();
        g.Country = r.Country.Trim().ToUpperInvariant();
        g.Region = r.Region.Trim();
        g.LoyaltyTier = r.LoyaltyTier;
    }
}

public static class BookingMapper
{
    public static BookingDto ToDto(this Booking b, string hotelName, string guestName, string roomTypeName, string? roomNumber) =>
        new(b.Id, b.Reference, b.ExternalRef, b.Channel, b.HotelId, hotelName, b.GuestId, guestName, b.RoomTypeId, roomTypeName,
            b.RoomId, roomNumber, b.RatePlanId, b.CheckIn, b.CheckOut, b.Nights, b.Adults, b.Status, b.TotalAmount);

    public static BookingNoteDto ToDto(this BookingNote n) => new(n.Id, n.BookingId, n.AuthorUserId, n.Note, n.CreatedAt);
}

public static class RateMapper
{
    public static RatePlanDto ToDto(this RatePlan p) => new(p.Id, p.Code, p.Name, p.DiscountPct, p.RequiresPrepayment);
    public static RoomRateDto ToDto(this RoomRate r) => new(r.Id, r.HotelId, r.RoomTypeId, r.RatePlanId, r.ValidFrom, r.ValidTo, r.NightlyRate);
}

public static class UserMapper
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Email, u.DisplayName, u.Role, u.HotelId, u.PasswordHash, u.Active, u.Phone, u.LastLoginAt);
}
