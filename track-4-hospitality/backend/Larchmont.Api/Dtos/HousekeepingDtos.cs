namespace Larchmont.Api.Dtos;

// Board (GET /api/hotels/{id}/board)
public record BoardRoomDto(int RoomId, string RoomNumber, string RoomType, string Status, DateTime? CheckoutAt,
    int? TargetMinutes, DateTime? ReadyBy, int? HousekeeperUserId);
public record BoardFloorDto(int Floor, bool Busy, IReadOnlyList<BoardRoomDto> Rooms);
public record HotelBoardDto(int HotelId, int? BusiestFloor, IReadOnlyList<BoardFloorDto> Floors);

// SLA breaches (GET /api/hotels/{id}/sla-breaches)
public record SlaBreachDto(int RoomId, string RoomNumber, string RoomType, DateTime CheckoutAt, DateTime? ReadyAt,
    int TargetMinutes, int ElapsedMinutes, int OverrunMinutes);
public record SlaBreachListDto(IReadOnlyList<SlaBreachDto> Items);

// Ready-by (GET /api/rooms/{id}/ready-by)
public record ReadyByDto(int RoomId, string RoomNumber, string Status, DateTime? ReadyBy, bool Overdue,
    bool OutOfOrder, DateTime? ReturnsAt);

// Floor assignments (GET and PUT /api/hotels/{id}/floor-assignments)
public record FloorAssignmentRequest(int Floor, int HousekeeperUserId);
public record SetFloorAssignmentsRequest(int ChangedByUserId, IReadOnlyList<FloorAssignmentRequest> Assignments);
public record FloorAssignmentDto(int Floor, int HousekeeperUserId);

// Spot-checks (GET /api/hotels/{id}/spot-checks, POST /api/rooms/{id}/spot-check)
public record SpotCheckDto(int RoomId, string RoomNumber, DateTime ReadyAt, int? ChangedByUserId);
public record SpotCheckListDto(IReadOnlyList<SpotCheckDto> Items);
public record SpotCheckRequest(int ChangedByUserId);

// Out of order (POST /api/rooms/{id}/out-of-order)
public record OutOfOrderRequest(int ChangedByUserId, string Reason, DateTime? ExpectedReturn, int RowVersion);

// Lock checkout webhook (POST /api/integrations/lock/checkout)
public record LockCheckoutRequest(int HotelId, string RoomNumber, DateTime? CheckedOutAt);
public record LockCheckoutResultDto(int RoomId, string Status, bool TurnaroundCreated);
