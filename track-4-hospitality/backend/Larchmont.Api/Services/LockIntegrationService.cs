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
    public const string StampMovedReason = "LockCheckoutStampMovedEarlier";

    private readonly IHotelRepository _hotels;
    private readonly IRoomRepository _rooms;
    private readonly IRoomTypeRepository _types;
    private readonly IValidator<LockCheckoutRequest> _validator;
    private readonly IClock _clock;

    public LockIntegrationService(IHotelRepository hotels, IRoomRepository rooms, IRoomTypeRepository types,
        IValidator<LockCheckoutRequest> validator, IClock clock)
    {
        _hotels = hotels;
        _rooms = rooms;
        _types = types;
        _validator = validator;
        _clock = clock;
    }

    public async Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request)
    {
        // A null body cannot go through ValidateAndThrowAsync; the controller already returns 400 for an empty body.
        if (request is null) throw Invalid("request", "A request body is required.");
        await _validator.ValidateAndThrowAsync(request);
        var roomNumber = request.RoomNumber.Trim();

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
            // Option A (user decision): the lock event may move an open stamp earlier, and the move is recorded.
            await _rooms.AddStatusEventAsync(new RoomStatusEvent
            {
                RoomId = room.Id,
                FromStatus = room.Status,
                ToStatus = room.Status,
                ChangedAt = now,
                Reason = StampMovedReason
            });
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
