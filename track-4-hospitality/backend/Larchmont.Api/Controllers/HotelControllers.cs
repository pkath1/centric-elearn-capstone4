using Larchmont.Api.Dtos;
using Larchmont.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Larchmont.Api.Controllers;

[ApiController]
[Route("api/hotels")]
public class HotelsController : ControllerBase
{
    private readonly IHotelService _hotels;
    private readonly IRoomService _rooms;

    public HotelsController(IHotelService hotels, IRoomService rooms)
    {
        _hotels = hotels;
        _rooms = rooms;
    }

    [HttpGet]
    public async Task<ActionResult<List<HotelDto>>> List() => await _hotels.ListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HotelDto>> Get(int id) => await _hotels.GetAsync(id);

    [HttpPost]
    public async Task<ActionResult<HotelDto>> Create(HotelRequest request)
    {
        var created = await _hotels.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HotelDto>> Update(int id, HotelRequest request) => await _hotels.UpdateAsync(id, request);

    [HttpGet("{id:int}/rooms")]
    public async Task<ActionResult<List<RoomDto>>> Rooms(int id, [FromQuery] string? status, [FromQuery] int? floor)
    {
        await _hotels.GetAsync(id);
        return await _rooms.ListAsync(id, status, floor);
    }
}

[ApiController]
[Route("api/room-types")]
public class RoomTypesController : ControllerBase
{
    private readonly IRoomTypeService _types;
    public RoomTypesController(IRoomTypeService types) => _types = types;

    [HttpGet]
    public async Task<ActionResult<List<RoomTypeDto>>> List() => await _types.ListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoomTypeDto>> Get(int id) => await _types.GetAsync(id);

    [HttpPost]
    public async Task<ActionResult<RoomTypeDto>> Create(RoomTypeRequest request)
    {
        var created = await _types.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoomTypeDto>> Update(int id, RoomTypeRequest request) => await _types.UpdateAsync(id, request);
}

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _rooms;
    public RoomsController(IRoomService rooms) => _rooms = rooms;

    [HttpGet]
    public async Task<ActionResult<List<RoomDto>>> List([FromQuery] int? hotelId, [FromQuery] string? status, [FromQuery] int? floor) =>
        await _rooms.ListAsync(hotelId, status, floor);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoomDto>> Get(int id) => await _rooms.GetAsync(id);

    [HttpPost]
    public async Task<ActionResult<RoomDto>> Create(RoomRequest request)
    {
        var created = await _rooms.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoomDto>> Update(int id, RoomRequest request) => await _rooms.UpdateAsync(id, request);

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<RoomDto>> SetStatus(int id, RoomStatusRequest request) => await _rooms.SetStatusAsync(id, request);

    [HttpPost("{id:int}/out-of-order")]
    public async Task<ActionResult<RoomDto>> SetOutOfOrder(int id, OutOfOrderRequest request) => await _rooms.SetOutOfOrderAsync(id, request);

    [HttpGet("{id:int}/availability")]
    public async Task<ActionResult<RoomAvailabilityDto>> Availability(int id, [FromQuery] DateOnly from, [FromQuery] DateOnly to) =>
        await _rooms.GetAvailabilityAsync(id, from, to);
}
