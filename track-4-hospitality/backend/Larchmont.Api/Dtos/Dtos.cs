namespace Larchmont.Api.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public record HotelDto(int Id, string Code, string Name, string City, string Region, string AddressLine, string Phone, bool Active);
public record HotelRequest(string Code, string Name, string City, string Region, string AddressLine, string Phone, bool Active = true);

public record RoomTypeDto(int Id, string Code, string Name, int MaxOccupancy, decimal BaseRate);
public record RoomTypeRequest(string Code, string Name, int MaxOccupancy, decimal BaseRate);

public record RoomDto(int Id, int HotelId, int RoomTypeId, string RoomTypeName, string RoomNumber, int Floor, string Status, string? Notes);
public record RoomRequest(int HotelId, int RoomTypeId, string RoomNumber, int Floor, string? Notes);
public record RoomStatusRequest(string Status, int? ChangedByUserId, int? RowVersion = null, string? Reason = null);
public record RoomAvailabilityDto(int RoomId, DateOnly From, DateOnly To, bool Available, IReadOnlyList<string> ConflictingBookings);

public record GuestDto(int Id, string FirstName, string LastName, string Email, string Phone, string AddressLine, string City, string Postcode, string Country, string Region, string LoyaltyTier, DateTime CreatedAt);
public record GuestRequest(string FirstName, string LastName, string Email, string Phone, string AddressLine, string City, string Postcode, string Country, string Region, string LoyaltyTier = "None");

public record BookingDto(int Id, string Reference, string? ExternalRef, string Channel, int HotelId, string HotelName, int GuestId, string GuestName, int RoomTypeId, string RoomTypeName, int? RoomId, string? RoomNumber, int RatePlanId, DateOnly CheckIn, DateOnly CheckOut, int Nights, int Adults, string Status, decimal TotalAmount);
public record BookingRequest(int HotelId, int GuestId, int RoomTypeId, int RatePlanId, DateOnly CheckIn, DateOnly CheckOut, int Adults, string? Channel);
public record BookingUpdateRequest(DateOnly CheckIn, DateOnly CheckOut, int Adults);
public record AssignRoomRequest(int RoomId);
public record CancelResultDto(int BookingId, string Status, decimal FeeAmount);
public record BookingNoteDto(int Id, int BookingId, int? AuthorUserId, string Note, DateTime CreatedAt);
public record BookingNoteRequest(int? AuthorUserId, string Note);

public record RatePlanDto(int Id, string Code, string Name, decimal DiscountPct, bool RequiresPrepayment);
public record RoomRateDto(int Id, int HotelId, int RoomTypeId, int RatePlanId, DateOnly ValidFrom, DateOnly ValidTo, decimal NightlyRate);
public record RoomRateRequest(int HotelId, int RoomTypeId, int RatePlanId, DateOnly ValidFrom, DateOnly ValidTo, decimal NightlyRate);
public record QuoteDto(int HotelId, int RoomTypeId, int RatePlanId, int Nights, decimal NightlyRate, decimal Total);

public record UserDto(int Id, string Email, string DisplayName, string Role, int? HotelId, string PasswordHash, bool Active, string? Phone, DateTime? LastLoginAt);
public record UserRequest(string Email, string DisplayName, string Role, int? HotelId, string Password, string? Phone);
public record UserUpdateRequest(string DisplayName, string Role, int? HotelId, bool Active, string? Phone);

public record OccupancyDayDto(DateOnly Date, int RoomsSold);
public record OccupancyReportDto(int? HotelId, DateOnly From, DateOnly To, int RoomsAvailable, int RoomNightsSold, decimal OccupancyPct, IReadOnlyList<OccupancyDayDto> Days);
public record ArrivalDto(int BookingId, string Reference, string HotelName, string GuestName, string RoomTypeName, string? RoomNumber, int Nights, string Status);
public record RevenueRowDto(int HotelId, string HotelName, int Bookings, decimal Revenue);

public record ImportRowError(int Line, string ExternalRef, string Message);
public record ImportResultDto(int TotalRows, int Imported, int Skipped, int Cancelled, IReadOnlyList<ImportRowError> Errors);
