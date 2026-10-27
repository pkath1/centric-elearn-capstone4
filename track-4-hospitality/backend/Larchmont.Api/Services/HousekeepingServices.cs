using Larchmont.Api.Dtos;

namespace Larchmont.Api.Services;

public interface IHousekeepingBoardService
{
    Task<HotelBoardDto> GetBoardAsync(int hotelId);
    Task<SlaBreachListDto> GetSlaBreachesAsync(int hotelId);
    Task<ReadyByDto> GetReadyByAsync(int roomId);
    Task<SpotCheckListDto> GetSpotChecksAsync(int hotelId);
    Task<SpotCheckDto> MarkSpotCheckedAsync(int roomId, SpotCheckRequest request);
}

public class HousekeepingBoardService : IHousekeepingBoardService
{
    public Task<HotelBoardDto> GetBoardAsync(int hotelId) => throw new NotImplementedException();
    public Task<SlaBreachListDto> GetSlaBreachesAsync(int hotelId) => throw new NotImplementedException();
    public Task<ReadyByDto> GetReadyByAsync(int roomId) => throw new NotImplementedException();
    public Task<SpotCheckListDto> GetSpotChecksAsync(int hotelId) => throw new NotImplementedException();
    public Task<SpotCheckDto> MarkSpotCheckedAsync(int roomId, SpotCheckRequest request) => throw new NotImplementedException();
}

public interface IFloorAssignmentService
{
    Task<List<FloorAssignmentDto>> ListAsync(int hotelId);
    Task<List<FloorAssignmentDto>> SetAsync(int hotelId, SetFloorAssignmentsRequest request);
}

public class FloorAssignmentService : IFloorAssignmentService
{
    public Task<List<FloorAssignmentDto>> ListAsync(int hotelId) => throw new NotImplementedException();
    public Task<List<FloorAssignmentDto>> SetAsync(int hotelId, SetFloorAssignmentsRequest request) => throw new NotImplementedException();
}

public interface ILockIntegrationService
{
    Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request);
}

public class LockIntegrationService : ILockIntegrationService
{
    public Task<LockCheckoutResultDto> CheckoutAsync(LockCheckoutRequest request) => throw new NotImplementedException();
}
