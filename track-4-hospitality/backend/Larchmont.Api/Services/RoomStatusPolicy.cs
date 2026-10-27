using FluentValidation;
using FluentValidation.Results;
using Larchmont.Api.Domain;
using Larchmont.Api.Infrastructure;

namespace Larchmont.Api.Services;

public static class HousekeepingRoles
{
    public const string Admin = "Admin";
    public const string RoomsDirector = "RoomsDirector";
    public const string Manager = "Manager";
    public const string FrontDesk = "FrontDesk";
    public const string Housekeeper = "Housekeeper";
    public const string Supervisor = "Supervisor";
    public const string Maintenance = "Maintenance";
}

public interface IRoomStatusPolicy
{
    // Statuses the room can move to from fromStatus for this role (BR-10). Empty when the role can move nowhere.
    IReadOnlyList<string> AllowedTargets(string fromStatus, string role, bool simpleTurn);

    // Throws BusinessRuleException (422) for an illegal move, ForbiddenException (403) for a role that may not
    // make the move, and ValidationException (400, field "reason") for a missing or over-long reason.
    void EnsureMoveAllowed(string fromStatus, string toStatus, string role, bool simpleTurn, string? reason);

    // Roles that may make the move fromStatus to toStatus (BR-11). Empty when the move is never legal.
    IReadOnlyList<string> RolesFor(string fromStatus, string toStatus);

    // Q1 default: Standard Double (STD), Standard Twin (TWN) and Superior Double (SUP) are simple turns.
    bool IsSimpleTurnType(string roomTypeCode);
}

public class RoomStatusPolicy : IRoomStatusPolicy
{
    public const string SimpleTurnShortcutReason = "SimpleTurnShortcut";
    private const int MaxReasonLength = 255;
    private static readonly string[] SimpleTurnTypeCodes = { "STD", "TWN", "SUP" };

    // From null means "any status except OutOfOrder" (BR-08).
    private sealed record Move(string? From, string To, string[] Roles, bool NeedsSimpleTurn, bool NeedsReason);

    private static readonly Move[] Moves =
    {
        new(RoomStatuses.Dirty, RoomStatuses.Cleaning, new[] { HousekeepingRoles.Housekeeper, HousekeepingRoles.Supervisor }, false, false),
        new(RoomStatuses.Cleaning, RoomStatuses.Inspected, new[] { HousekeepingRoles.Supervisor }, false, false),
        new(RoomStatuses.Cleaning, RoomStatuses.Ready, new[] { HousekeepingRoles.Housekeeper }, true, false),
        new(RoomStatuses.Inspected, RoomStatuses.Ready, new[] { HousekeepingRoles.Supervisor }, false, false),
        new(RoomStatuses.Inspected, RoomStatuses.Cleaning, new[] { HousekeepingRoles.Supervisor }, false, false),
        new(null, RoomStatuses.OutOfOrder, new[] { HousekeepingRoles.Maintenance, HousekeepingRoles.Supervisor }, false, true),
        new(RoomStatuses.OutOfOrder, RoomStatuses.Dirty, new[] { HousekeepingRoles.Maintenance, HousekeepingRoles.Supervisor }, false, false),
    };

    public IReadOnlyList<string> AllowedTargets(string fromStatus, string role, bool simpleTurn) =>
        StateTargets(fromStatus, simpleTurn)
            .Where(t => RoleMayMove(role, Find(fromStatus, t)!))
            .ToList();

    public void EnsureMoveAllowed(string fromStatus, string toStatus, string role, bool simpleTurn, string? reason)
    {
        var move = fromStatus == toStatus ? null : Find(fromStatus, toStatus);

        // BR-10: the move is not in the table, or it needs a simple turn and this room is not one.
        if (move is null || (move.NeedsSimpleTurn && !simpleTurn))
        {
            // The message names what the caller may do from here; when that is nothing, what the state allows.
            // OutOfOrder is left out: BR-08 lets it be set from any status, and AC-07 and AC-08 list only the cleaning path.
            var allowed = AllowedTargets(fromStatus, role, simpleTurn).Where(t => t != RoomStatuses.OutOfOrder).ToList();
            if (allowed.Count == 0) allowed = StateTargets(fromStatus, simpleTurn).Where(t => t != RoomStatuses.OutOfOrder).ToList();
            var message = Describe(fromStatus, allowed);
            if (fromStatus == RoomStatuses.Cleaning && toStatus == RoomStatuses.Ready && role == HousekeepingRoles.Housekeeper)
                message = "Only a Supervisor can mark this room Ready after inspection. " + message;
            throw new BusinessRuleException(message);
        }

        // BR-11: the move is legal for the room but this role may not make it.
        if (!RoleMayMove(role, move))
            throw new ForbiddenException(
                $"Only {string.Join(" or ", move.Roles.Select(r => "a " + r))} can move a room from {fromStatus} to {toStatus}.");

        // BR-08: a reason is required for OutOfOrder; any reason is at most 255 characters.
        if (move.NeedsReason && string.IsNullOrWhiteSpace(reason))
            throw Invalid("reason", "A reason is required to set a room OutOfOrder.");
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
            throw Invalid("reason", $"Reason must be {MaxReasonLength} characters or fewer.");
    }

    public IReadOnlyList<string> RolesFor(string fromStatus, string toStatus) =>
        fromStatus == toStatus ? Array.Empty<string>() : Find(fromStatus, toStatus)?.Roles ?? Array.Empty<string>();

    public bool IsSimpleTurnType(string roomTypeCode) =>
        SimpleTurnTypeCodes.Contains(roomTypeCode, StringComparer.OrdinalIgnoreCase);

    private static Move? Find(string from, string to) =>
        Moves.FirstOrDefault(m => m.To == to && (m.From == from || (m.From is null && from != RoomStatuses.OutOfOrder)));

    // Statuses legal from fromStatus for any role, given the simple-turn flag.
    private static List<string> StateTargets(string fromStatus, bool simpleTurn) =>
        RoomStatuses.All
            .Where(t => t != fromStatus && Find(fromStatus, t) is { } m && (!m.NeedsSimpleTurn || simpleTurn))
            .ToList();

    // Admin may make every move that any role may make.
    private static bool RoleMayMove(string role, Move move) =>
        role == HousekeepingRoles.Admin || move.Roles.Contains(role);

    private static string Describe(string fromStatus, IReadOnlyList<string> targets) =>
        targets.Count == 0
            ? $"{fromStatus} rooms cannot move to any other status."
            : $"{fromStatus} rooms can move to: {string.Join(", ", targets)}.";

    private static ValidationException Invalid(string field, string message) =>
        new(new[] { new ValidationFailure(field, message) });
}
