using FluentValidation;
using FluentValidation.Results;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;

namespace Larchmont.Api.Services;

// Lock checkout webhook (BR-02, BR-18). The API key is checked by the controller before this runs.
public class LockIntegrationService : ILockIntegrationService
{
    public const string LockReason = "LockCheckout";
    private const int MaxRoomNumberLength = 10;

    private readonly IHotelRepository _hotels;
    private readonly IRoomRepository _rooms;
    private readonly IRoomTypeRepository _types;
    private readonly IClock _clock;

    public LockIntegrationService(IHotelRepository hotels, IRoomRepository rooms, IRoomTypeRepository types, IClock clock)
    {
        _hotels = hotels;
        _rooms = rooms;
        _types = types;
        _clock = clock;
    }

    public async Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request)
    {
        if (request is null) throw Invalid("request", "A request body is required.");
        if (request.HotelId <= 0) throw Invalid("hotelId", "hotelId must be greater than 0.");
        var roomNumber = (request.RoomNumber ?? "").Trim();
        if (roomNumber.Length == 0 || roomNumber.Length > MaxRoomNumberLength)
            throw Invalid("roomNumber", $"roomNumber is required and must be {MaxRoomNumberLength} characters or fewer.");

        var now = _clock.UtcNow;
        var lockTime = request.CheckedOutAt ?? now;
        if (lockTime > now) throw new BusinessRuleException("checkedOutAt cannot be in the future.");

        if (await _hotels.GetAsync(request.HotelId) == null) throw new NotFoundException($"Hotel {request.HotelId} not found.");
        var room = await _rooms.GetByNumberAsync(request.HotelId, roomNumber)
            ?? throw new NotFoundException($"Room {roomNumber} not found at hotel {request.HotelId}.");
        var type = await _types.GetAsync(room.RoomTypeId) ?? throw new NotFoundException($"Room type {room.RoomTypeId} not found.");

        // Q3 default: the stamp is the earlier of the lock time and an open turnaround's stamp. It is set once.
        var open = await _rooms.GetOpenTurnaroundAsync(room.Id);
        var created = open == null;
        if (open == null)
        {
            await _rooms.AddTurnaroundAsync(new RoomTurnaround
            {
                RoomId = room.Id,
                CheckoutAt = lockTime,
                TargetMinutes = type.SlaMinutes,
                IsOpen = true
            });
        }
        else if (lockTime < open.CheckoutAt)
        {
            open.CheckoutAt = lockTime;
        }

        // BR-02: Dirty from any status except OutOfOrder, which stays OutOfOrder.
        if (room.Status != RoomStatuses.Dirty && room.Status != RoomStatuses.OutOfOrder)
        {
            await _rooms.AddStatusEventAsync(new RoomStatusEvent
            {
                RoomId = room.Id,
                FromStatus = room.Status,
                ToStatus = RoomStatuses.Dirty,
                ChangedAt = now,
                Reason = LockReason
            });
            room.Status = RoomStatuses.Dirty;
            room.RowVersion++;
        }
        await _rooms.SaveAsync();
        return new LockCheckoutResultDto(room.Id, room.Status, created);
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new[] { new ValidationFailure(field, message) });
}
