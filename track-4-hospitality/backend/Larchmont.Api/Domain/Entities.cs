namespace Larchmont.Api.Domain;

public class Hotel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class RoomType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxOccupancy { get; set; }
    public decimal BaseRate { get; set; }
    public int SlaMinutes { get; set; } = 45;
}

public class RatePlan
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal DiscountPct { get; set; }
    public bool RequiresPrepayment { get; set; }
}

public class RoomRate
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public int RoomTypeId { get; set; }
    public int RatePlanId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public decimal NightlyRate { get; set; }
}

public class Room
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public int RoomTypeId { get; set; }
    public string RoomNumber { get; set; } = "";
    public int Floor { get; set; }
    public string Status { get; set; } = RoomStatuses.Ready;
    public string? Notes { get; set; }
    public string? OooReason { get; set; }
    public DateTime? OooSince { get; set; }
    public DateTime? OooExpectedReturn { get; set; }
    public bool SimpleTurn { get; set; }
    public int RowVersion { get; set; } = 1;
}

public static class RoomStatuses
{
    public const string Dirty = "Dirty";
    public const string Cleaning = "Cleaning";
    public const string Inspected = "Inspected";
    public const string Ready = "Ready";
    public const string OutOfOrder = "OutOfOrder";
    public static readonly string[] All = { Dirty, Cleaning, Inspected, Ready, OutOfOrder };
}

public class RoomStatusEvent
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public int? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
}

public class RoomTurnaround
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int? BookingId { get; set; }
    public DateTime CheckoutAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public int TargetMinutes { get; set; }
    public bool IsOpen { get; set; } = true;
}

public class FloorAssignment
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public int Floor { get; set; }
    public int HousekeeperUserId { get; set; }
    public bool Active { get; set; } = true;
}

public class RoomSpotCheck
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public long StatusHistoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CheckedAt { get; set; }
    public int? CheckedByUserId { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public int? HotelId { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool Active { get; set; } = true;
    public string? Phone { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class Guest
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string Country { get; set; } = "GB";
    public string Region { get; set; } = "";
    public string LoyaltyTier { get; set; } = "None";
    public DateTime CreatedAt { get; set; }
}

public static class BookingStatuses
{
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string CheckedOut = "CheckedOut";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public static readonly string[] All = { Confirmed, CheckedIn, CheckedOut, Cancelled, NoShow };
    public static readonly string[] Staying = { Confirmed, CheckedIn, CheckedOut };
}

public class Booking
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string? ExternalRef { get; set; }
    public string Channel { get; set; } = "Direct";
    public int HotelId { get; set; }
    public int GuestId { get; set; }
    public int RoomTypeId { get; set; }
    public int? RoomId { get; set; }
    public int RatePlanId { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Adults { get; set; }
    public string Status { get; set; } = BookingStatuses.Confirmed;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
}

public class BookingNote
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int? AuthorUserId { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
