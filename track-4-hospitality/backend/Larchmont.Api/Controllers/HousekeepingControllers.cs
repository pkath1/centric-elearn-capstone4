using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Larchmont.Api.Controllers;

[ApiController]
[Route("api/hotels")]
public class HousekeepingBoardController : ControllerBase
{
    private readonly IHousekeepingBoardService _board;
    private readonly IFloorAssignmentService _floors;

    public HousekeepingBoardController(IHousekeepingBoardService board, IFloorAssignmentService floors)
    {
        _board = board;
        _floors = floors;
    }

    [HttpGet("{id:int}/board")]
    public async Task<ActionResult<HotelBoardDto>> Board(int id) => await _board.GetBoardAsync(id);

    [HttpGet("{id:int}/sla-breaches")]
    public async Task<ActionResult<SlaBreachListDto>> SlaBreaches(int id) => await _board.GetSlaBreachesAsync(id);

    [HttpGet("{id:int}/spot-checks")]
    public async Task<ActionResult<SpotCheckListDto>> SpotChecks(int id) => await _board.GetSpotChecksAsync(id);

    [HttpGet("{id:int}/floor-assignments")]
    public async Task<ActionResult<List<FloorAssignmentDto>>> ListFloorAssignments(int id) => await _floors.ListAsync(id);

    [HttpPut("{id:int}/floor-assignments")]
    public async Task<ActionResult<List<FloorAssignmentDto>>> SetFloorAssignments(int id, SetFloorAssignmentsRequest request) =>
        await _floors.SetAsync(id, request);
}

[ApiController]
[Route("api/rooms")]
public class HousekeepingRoomsController : ControllerBase
{
    private readonly IHousekeepingBoardService _board;

    public HousekeepingRoomsController(IHousekeepingBoardService board) => _board = board;

    [HttpGet("{id:int}/ready-by")]
    public async Task<ActionResult<ReadyByDto>> ReadyBy(int id) => await _board.GetReadyByAsync(id);

    [HttpPost("{id:int}/spot-check")]
    public async Task<ActionResult<SpotCheckDto>> MarkSpotChecked(int id, SpotCheckRequest request) =>
        await _board.MarkSpotCheckedAsync(id, request);
}

[ApiController]
[Route("api/integrations/lock")]
public class LockIntegrationController : ControllerBase
{
    private readonly ILockIntegrationService _lock;

    public LockIntegrationController(ILockIntegrationService lockService) => _lock = lockService;

    [HttpPost("checkout")]
    [ApiKey("Lock:ApiKey")]
    public async Task<ActionResult<LockCheckoutResultDto>> Checkout(LockCheckoutRequest request) =>
        await _lock.CheckoutAsync(request);
}
