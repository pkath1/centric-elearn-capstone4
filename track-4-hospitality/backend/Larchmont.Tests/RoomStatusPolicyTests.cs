using FluentValidation;
using Larchmont.Api.Domain;
using Larchmont.Api.Services;

namespace Larchmont.Tests;

// Direct checks of the status-move table (spec: BR-08, BR-10, BR-11). The API validator also rejects a blank
// OutOfOrder reason, so these tests are the only ones that exercise the policy's own reason check.
public class RoomStatusPolicyTests
{
    private static readonly RoomStatusPolicy Policy = new();

    [Fact]
    public void OutOfOrder_BlankReason_IsRejectedByPolicyNamingReason()
    {
        // Given a Ready room and Maintenance. When the policy is asked for OutOfOrder with a blank reason.
        var ex = Assert.Throws<ValidationException>(() =>
            Policy.EnsureMoveAllowed(RoomStatuses.Ready, RoomStatuses.OutOfOrder, HousekeepingRoles.Maintenance, false, "   "));

        // Then the failure names the field "reason" (BR-08).
        Assert.Contains(ex.Errors, e => e.PropertyName == "reason");
    }

    [Fact]
    public void OutOfOrder_NullReason_IsRejectedByPolicy()
    {
        Assert.Throws<ValidationException>(() =>
            Policy.EnsureMoveAllowed(RoomStatuses.Ready, RoomStatuses.OutOfOrder, HousekeepingRoles.Supervisor, false, null));
    }

    [Fact]
    public void OutOfOrder_ReasonOf255CharactersAfterTrim_IsAccepted()
    {
        // BR-08: at most 255 characters, counted after trimming.
        Policy.EnsureMoveAllowed(RoomStatuses.Ready, RoomStatuses.OutOfOrder, HousekeepingRoles.Maintenance, false,
            "  " + new string('x', 255) + "  ");
    }

    [Fact]
    public void OutOfOrder_ReasonOf256CharactersAfterTrim_IsRejected()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            Policy.EnsureMoveAllowed(RoomStatuses.Ready, RoomStatuses.OutOfOrder, HousekeepingRoles.Maintenance, false,
                new string('x', 256)));
        Assert.Contains(ex.Errors, e => e.PropertyName == "reason");
    }

    [Theory]
    [InlineData("STD", true)]
    [InlineData("TWN", true)]
    [InlineData("SUP", true)]
    [InlineData("STE", false)]
    [InlineData("FAM", false)]
    public void SimpleTurnTypes_AreStandardDoubleTwinAndSuperiorDoubleOnly(string code, bool expected)
    {
        // Q1 default in the spec: Suite and Family Room are not simple turns.
        Assert.Equal(expected, Policy.IsSimpleTurnType(code));
    }
}
