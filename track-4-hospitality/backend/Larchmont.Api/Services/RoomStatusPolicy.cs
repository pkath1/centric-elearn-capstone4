namespace Larchmont.Api.Services;

// Contract for BR-02 to BR-11 (status moves and roles). Logic comes in a later phase.
public interface IRoomStatusPolicy
{
    // Statuses the room can move to from fromStatus, for this role (BR-10 message).
    IReadOnlyList<string> AllowedTargets(string fromStatus, string role, bool simpleTurn);

    // Throws BusinessRuleException (422) for an illegal move, ForbiddenException (403) for a role
    // that may not make the move, and BusinessRuleException when a reason is required and missing.
    void EnsureMoveAllowed(string fromStatus, string toStatus, string role, bool simpleTurn, string? reason);

    // Roles that may make the move fromStatus to toStatus (BR-11 message).
    IReadOnlyList<string> RolesFor(string fromStatus, string toStatus);
}

public class RoomStatusPolicy : IRoomStatusPolicy
{
    public IReadOnlyList<string> AllowedTargets(string fromStatus, string role, bool simpleTurn) =>
        throw new NotImplementedException();

    public void EnsureMoveAllowed(string fromStatus, string toStatus, string role, bool simpleTurn, string? reason) =>
        throw new NotImplementedException();

    public IReadOnlyList<string> RolesFor(string fromStatus, string toStatus) =>
        throw new NotImplementedException();
}
