using FluentValidation;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;

namespace Larchmont.Api.Validators;

public class HotelRequestValidator : AbstractValidator<HotelRequest>
{
    public HotelRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(8);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(20);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
    }
}

public class RoomTypeRequestValidator : AbstractValidator<RoomTypeRequest>
{
    public RoomTypeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(8);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.MaxOccupancy).InclusiveBetween(1, 10);
        RuleFor(x => x.BaseRate).GreaterThan(0);
    }
}

public class RoomRequestValidator : AbstractValidator<RoomRequest>
{
    public RoomRequestValidator()
    {
        RuleFor(x => x.HotelId).GreaterThan(0);
        RuleFor(x => x.RoomTypeId).GreaterThan(0);
        RuleFor(x => x.RoomNumber).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Floor).InclusiveBetween(0, 60);
        RuleFor(x => x.Notes).MaximumLength(255);
    }
}

public class GuestRequestValidator : AbstractValidator<GuestRequest>
{
    public GuestRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Postcode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).Length(2);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(20);
    }
}

public class BookingRequestValidator : AbstractValidator<BookingRequest>
{
    public BookingRequestValidator()
    {
        RuleFor(x => x.HotelId).GreaterThan(0);
        RuleFor(x => x.GuestId).GreaterThan(0);
        RuleFor(x => x.RoomTypeId).GreaterThan(0);
        RuleFor(x => x.RatePlanId).GreaterThan(0);
        RuleFor(x => x.Adults).InclusiveBetween(1, 10);
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Check-out must be after check-in.");
        RuleFor(x => x).Must(x => x.CheckOut.DayNumber - x.CheckIn.DayNumber <= 30).WithMessage("Maximum stay is 30 nights.");
    }
}

public class BookingUpdateRequestValidator : AbstractValidator<BookingUpdateRequest>
{
    public BookingUpdateRequestValidator()
    {
        RuleFor(x => x.Adults).InclusiveBetween(1, 10);
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Check-out must be after check-in.");
    }
}

public class BookingNoteRequestValidator : AbstractValidator<BookingNoteRequest>
{
    public BookingNoteRequestValidator()
    {
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

public class RoomRateRequestValidator : AbstractValidator<RoomRateRequest>
{
    public RoomRateRequestValidator()
    {
        RuleFor(x => x.NightlyRate).GreaterThan(0);
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom);
    }
}

public class UserRequestValidator : AbstractValidator<UserRequest>
{
    public UserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Role).Must(r => new[] { "Admin", "RoomsDirector", "Manager", "FrontDesk", "Housekeeper", "Supervisor", "Maintenance" }.Contains(r)).WithMessage("Unknown role.");
        RuleFor(x => x.Password).MinimumLength(10);
    }
}

// Status membership is not checked here: an unknown status is a 422 from the service (AC-40).
public class RoomStatusRequestValidator : AbstractValidator<RoomStatusRequest>
{
    public RoomStatusRequestValidator()
    {
        RuleFor(x => x.ChangedByUserId).NotNull().GreaterThan(0);
        RuleFor(x => x.RowVersion).NotNull().GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(255);
        When(x => x.Status == RoomStatuses.OutOfOrder, () =>
        {
            RuleFor(x => x.Reason).Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Reason is required when status is OutOfOrder.");
        });
    }
}

public class OutOfOrderRequestValidator : AbstractValidator<OutOfOrderRequest>
{
    public OutOfOrderRequestValidator()
    {
        RuleFor(x => x.ChangedByUserId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.Reason).Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Reason is required.").MaximumLength(255);
    }
}

public class FloorAssignmentRequestValidator : AbstractValidator<FloorAssignmentRequest>
{
    public FloorAssignmentRequestValidator()
    {
        RuleFor(x => x.Floor).InclusiveBetween(0, 60);
        RuleFor(x => x.HousekeeperUserId).GreaterThan(0);
    }
}

public class SetFloorAssignmentsRequestValidator : AbstractValidator<SetFloorAssignmentsRequest>
{
    public SetFloorAssignmentsRequestValidator()
    {
        RuleFor(x => x.ChangedByUserId).GreaterThan(0);
        RuleFor(x => x.Assignments).NotNull();
        RuleForEach(x => x.Assignments).SetValidator(new FloorAssignmentRequestValidator());
        RuleFor(x => x.Assignments)
            .Must(list => list == null || list.Select(a => a.Floor).Distinct().Count() == list.Count)
            .WithMessage("Each floor may appear only once.");
    }
}

public class SpotCheckRequestValidator : AbstractValidator<SpotCheckRequest>
{
    public SpotCheckRequestValidator()
    {
        RuleFor(x => x.ChangedByUserId).GreaterThan(0);
    }
}

public class LockCheckoutRequestValidator : AbstractValidator<LockCheckoutRequest>
{
    public LockCheckoutRequestValidator()
    {
        RuleFor(x => x.HotelId).GreaterThan(0);
        RuleFor(x => x.RoomNumber).NotEmpty().MaximumLength(10);
    }
}
