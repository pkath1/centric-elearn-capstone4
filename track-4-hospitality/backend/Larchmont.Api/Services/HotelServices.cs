using FluentValidation;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Mappers;
using Larchmont.Api.Repositories;

namespace Larchmont.Api.Services;

public interface IHotelService
{
    Task<List<HotelDto>> ListAsync();
    Task<HotelDto> GetAsync(int id);
    Task<HotelDto> CreateAsync(HotelRequest request);
    Task<HotelDto> UpdateAsync(int id, HotelRequest request);
}

public class HotelService : IHotelService
{
    private readonly IHotelRepository _hotels;
    private readonly IValidator<HotelRequest> _validator;

    public HotelService(IHotelRepository hotels, IValidator<HotelRequest> validator)
    {
        _hotels = hotels;
        _validator = validator;
    }

    public async Task<List<HotelDto>> ListAsync() => (await _hotels.ListAsync()).Select(h => h.ToDto()).ToList();

    public async Task<HotelDto> GetAsync(int id) =>
        (await _hotels.GetAsync(id) ?? throw new NotFoundException($"Hotel {id} not found.")).ToDto();

    public async Task<HotelDto> CreateAsync(HotelRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        if (await _hotels.GetByCodeAsync(request.Code.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"Hotel code {request.Code} is already used.");
        var hotel = new Hotel();
        hotel.Apply(request);
        await _hotels.AddAsync(hotel);
        return hotel.ToDto();
    }

    public async Task<HotelDto> UpdateAsync(int id, HotelRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var hotel = await _hotels.GetAsync(id) ?? throw new NotFoundException($"Hotel {id} not found.");
        hotel.Apply(request);
        await _hotels.SaveAsync();
        return hotel.ToDto();
    }
}

public interface IRoomTypeService
{
    Task<List<RoomTypeDto>> ListAsync();
    Task<RoomTypeDto> GetAsync(int id);
    Task<RoomTypeDto> CreateAsync(RoomTypeRequest request);
    Task<RoomTypeDto> UpdateAsync(int id, RoomTypeRequest request);
}

public class RoomTypeService : IRoomTypeService
{
    private readonly IRoomTypeRepository _types;
    private readonly IValidator<RoomTypeRequest> _validator;

    public RoomTypeService(IRoomTypeRepository types, IValidator<RoomTypeRequest> validator)
    {
        _types = types;
        _validator = validator;
    }

    public async Task<List<RoomTypeDto>> ListAsync() => (await _types.ListAsync()).Select(t => t.ToDto()).ToList();

    public async Task<RoomTypeDto> GetAsync(int id) =>
        (await _types.GetAsync(id) ?? throw new NotFoundException($"Room type {id} not found.")).ToDto();

    public async Task<RoomTypeDto> CreateAsync(RoomTypeRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        if (await _types.GetByCodeAsync(request.Code.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"Room type code {request.Code} is already used.");
        var type = new RoomType();
        type.Apply(request);
        await _types.AddAsync(type);
        return type.ToDto();
    }

    public async Task<RoomTypeDto> UpdateAsync(int id, RoomTypeRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var type = await _types.GetAsync(id) ?? throw new NotFoundException($"Room type {id} not found.");
        type.Apply(request);
        await _types.SaveAsync();
        return type.ToDto();
    }
}

public interface IRoomService
{
    Task<List<RoomDto>> ListAsync(int? hotelId, string? status, int? floor);
    Task<RoomDto> GetAsync(int id);
    Task<RoomDto> CreateAsync(RoomRequest request);
    Task<RoomDto> UpdateAsync(int id, RoomRequest request);
    Task<RoomDto> SetStatusAsync(int id, RoomStatusRequest request);
    Task<RoomDto> SetOutOfOrderAsync(int id, OutOfOrderRequest request);
    Task<RoomAvailabilityDto> GetAvailabilityAsync(int id, DateOnly from, DateOnly to);
}

public class RoomService : IRoomService
{
    private readonly IRoomRepository _rooms;
    private readonly IRoomTypeRepository _types;
    private readonly IHotelRepository _hotels;
    private readonly IBookingRepository _bookings;
    private readonly IValidator<RoomRequest> _validator;

    public RoomService(IRoomRepository rooms, IRoomTypeRepository types, IHotelRepository hotels,
        IBookingRepository bookings, IValidator<RoomRequest> validator)
    {
        _rooms = rooms;
        _types = types;
        _hotels = hotels;
        _bookings = bookings;
        _validator = validator;
    }

    public async Task<List<RoomDto>> ListAsync(int? hotelId, string? status, int? floor)
    {
        var rooms = await _rooms.ListAsync(hotelId, status, floor);
        var names = (await _types.ListAsync()).ToDictionary(t => t.Id, t => t.Name);
        return rooms.Select(r => r.ToDto(names.GetValueOrDefault(r.RoomTypeId, ""))).ToList();
    }

    public async Task<RoomDto> GetAsync(int id)
    {
        var room = await _rooms.GetAsync(id) ?? throw new NotFoundException($"Room {id} not found.");
        return await ToDtoAsync(room);
    }

    public async Task<RoomDto> CreateAsync(RoomRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        await EnsureReferencesAsync(request);
        var room = new Room
        {
            HotelId = request.HotelId,
            RoomTypeId = request.RoomTypeId,
            RoomNumber = request.RoomNumber.Trim(),
            Floor = request.Floor,
            Notes = request.Notes,
            Status = RoomStatuses.Ready
        };
        await _rooms.AddAsync(room);
        return await ToDtoAsync(room);
    }

    public async Task<RoomDto> UpdateAsync(int id, RoomRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var room = await _rooms.GetAsync(id) ?? throw new NotFoundException($"Room {id} not found.");
        await EnsureReferencesAsync(request);
        room.HotelId = request.HotelId;
        room.RoomTypeId = request.RoomTypeId;
        room.RoomNumber = request.RoomNumber.Trim();
        room.Floor = request.Floor;
        room.Notes = request.Notes;
        await _rooms.SaveAsync();
        return await ToDtoAsync(room);
    }

    public async Task<RoomDto> SetStatusAsync(int id, RoomStatusRequest request)
    {
        if (!RoomStatuses.All.Contains(request.Status))
            throw new BusinessRuleException($"Status must be one of: {string.Join(", ", RoomStatuses.All)}.");
        var room = await _rooms.GetAsync(id) ?? throw new NotFoundException($"Room {id} not found.");
        var previous = room.Status;
        room.Status = request.Status;
        await _rooms.AddStatusEventAsync(new RoomStatusEvent
        {
            RoomId = room.Id,
            FromStatus = previous,
            ToStatus = request.Status,
            ChangedByUserId = request.ChangedByUserId,
            ChangedAt = DateTime.UtcNow
        });
        await _rooms.SaveAsync();
        return await ToDtoAsync(room);
    }

    public Task<RoomDto> SetOutOfOrderAsync(int id, OutOfOrderRequest request) => throw new NotImplementedException();

    public async Task<RoomAvailabilityDto> GetAvailabilityAsync(int id, DateOnly from, DateOnly to)
    {
        if (to <= from) throw new BusinessRuleException("'to' must be after 'from'.");
        var room = await _rooms.GetAsync(id) ?? throw new NotFoundException($"Room {id} not found.");
        var clashes = await _bookings.ListForRoomAsync(room.Id, from, to);
        return new RoomAvailabilityDto(room.Id, from, to, clashes.Count == 0, clashes.Select(c => c.Reference).ToList());
    }

    private async Task EnsureReferencesAsync(RoomRequest request)
    {
        if (await _hotels.GetAsync(request.HotelId) == null) throw new NotFoundException($"Hotel {request.HotelId} not found.");
        if (await _types.GetAsync(request.RoomTypeId) == null) throw new NotFoundException($"Room type {request.RoomTypeId} not found.");
    }

    private async Task<RoomDto> ToDtoAsync(Room room)
    {
        var type = await _types.GetAsync(room.RoomTypeId);
        return room.ToDto(type?.Name ?? "");
    }
}
