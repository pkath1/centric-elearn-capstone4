using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;
using Larchmont.Api.Services;
using Moq;

namespace Larchmont.Tests;

// Characterisation tests for ChannelImportService. They pin what the code does TODAY, including odd and buggy
// behaviour. They are the safety net for the Phase 8 refactor: do not change an assertion here unless the
// behaviour change is deliberate. Bugs found while writing these are listed in docs/refactor-note.md.
public class ChannelImportCharacterisationTests
{
    private const string Header = "external_ref,channel,hotel_code,room_type_code,guest_first,guest_last,guest_email,guest_phone,check_in,check_out,adults,rate_plan_code,total_amount,status";

    private sealed class Harness
    {
        public readonly Mock<IBookingRepository> Bookings = new();
        public readonly Mock<IGuestRepository> Guests = new();
        public readonly Mock<IHotelRepository> Hotels = new();
        public readonly Mock<IRoomTypeRepository> Types = new();
        public readonly Mock<IRateRepository> Rates = new();

        public readonly Dictionary<string, Hotel> HotelsByCode = new();
        public readonly Dictionary<string, RoomType> TypesByCode = new();
        public readonly Dictionary<string, RatePlan> PlansByCode = new();
        public readonly List<RoomRate> RoomRates = new();
        public readonly Dictionary<string, Booking> BookingsByRef = new();
        public readonly Dictionary<string, Guest> GuestsByEmail = new();

        public readonly List<Booking> Added = new();
        public readonly List<BookingNote> Notes = new();
        public readonly List<Guest> NewGuests = new();
        public int Saves;
        public int ReferenceCalls;
        // When Saves reaches this number, SaveAsync throws. 0 means never.
        public int FailSaveAt;

        private int _nextId = 100;
        private int _nextRef = 50000;

        public Harness()
        {
            HotelsByCode["EDI"] = new Hotel { Id = 1, Code = "EDI", Name = "Larchmont Edinburgh", City = "Edinburgh", Region = "Scotland" };
            HotelsByCode["MAN"] = new Hotel { Id = 2, Code = "MAN", Name = "Larchmont Manchester", City = "Manchester", Region = "North West" };
            HotelsByCode["LON"] = new Hotel { Id = 3, Code = "LON", Name = "Larchmont London", City = "London", Region = "London" };
            HotelsByCode["BTH"] = new Hotel { Id = 4, Code = "BTH", Name = "Larchmont Bath", City = "Bath", Region = "South West" };
            HotelsByCode["BRI"] = new Hotel { Id = 5, Code = "BRI", Name = "Larchmont Bristol", City = "Bristol", Region = "South West" };
            HotelsByCode["LDS"] = new Hotel { Id = 6, Code = "LDS", Name = "Larchmont Leeds", City = "Leeds", Region = "Yorkshire" };

            TypesByCode["STD"] = new RoomType { Id = 1, Code = "STD", Name = "Standard Double", MaxOccupancy = 2 };
            TypesByCode["SUP"] = new RoomType { Id = 2, Code = "SUP", Name = "Superior King", MaxOccupancy = 2 };
            TypesByCode["FAM"] = new RoomType { Id = 3, Code = "FAM", Name = "Family Room", MaxOccupancy = 4 };
            TypesByCode["TWN"] = new RoomType { Id = 4, Code = "TWN", Name = "Twin Room", MaxOccupancy = 2 };
            TypesByCode["JNR"] = new RoomType { Id = 5, Code = "JNR", Name = "Junior Suite", MaxOccupancy = 3 };
            TypesByCode["STE"] = new RoomType { Id = 6, Code = "STE", Name = "Executive Suite", MaxOccupancy = 4 };

            PlansByCode["BAR"] = new RatePlan { Id = 1, Code = "BAR", Name = "Best available" };
            PlansByCode["ADV"] = new RatePlan { Id = 2, Code = "ADV", Name = "Advance purchase" };

            // Only EDI/STD/BAR and BRI/STD/BAR have a rate. Every other hotel/type/plan combination has none.
            RoomRates.Add(new RoomRate { HotelId = 1, RoomTypeId = 1, RatePlanId = 1, NightlyRate = 120m,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31) });
            RoomRates.Add(new RoomRate { HotelId = 5, RoomTypeId = 1, RatePlanId = 1, NightlyRate = 95m,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31) });

            Hotels.Setup(h => h.GetByCodeAsync(It.IsAny<string>()))
                .Returns((string code) => Task.FromResult(HotelsByCode.GetValueOrDefault(code)));
            Types.Setup(t => t.GetByCodeAsync(It.IsAny<string>()))
                .Returns((string code) => Task.FromResult(TypesByCode.GetValueOrDefault(code)));
            Rates.Setup(r => r.GetPlanByCodeAsync(It.IsAny<string>()))
                .Returns((string code) => Task.FromResult(PlansByCode.GetValueOrDefault(code)));
            Rates.Setup(r => r.FindRateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly>()))
                .Returns((int hotelId, int typeId, int planId, DateOnly date) => Task.FromResult(
                    RoomRates.FirstOrDefault(r => r.HotelId == hotelId && r.RoomTypeId == typeId && r.RatePlanId == planId
                        && r.ValidFrom <= date && r.ValidTo >= date)));

            Bookings.Setup(b => b.GetByExternalRefAsync(It.IsAny<string>()))
                .Returns((string extRef) => Task.FromResult(BookingsByRef.GetValueOrDefault(extRef)));
            Bookings.Setup(b => b.NextReferenceNumberAsync())
                .Returns(() => { ReferenceCalls++; return Task.FromResult(++_nextRef); });
            Bookings.Setup(b => b.AddAsync(It.IsAny<Booking>()))
                .Returns((Booking booking) =>
                {
                    booking.Id = ++_nextId;
                    Added.Add(booking);
                    if (booking.ExternalRef != null) BookingsByRef[booking.ExternalRef] = booking;
                    return Task.CompletedTask;
                });
            Bookings.Setup(b => b.AddNoteAsync(It.IsAny<BookingNote>()))
                .Returns((BookingNote note) => { Notes.Add(note); return Task.CompletedTask; });
            Bookings.Setup(b => b.SaveAsync())
                .Returns(() =>
                {
                    Saves++;
                    if (Saves == FailSaveAt) throw new InvalidOperationException("db down");
                    return Task.CompletedTask;
                });

            Guests.Setup(g => g.GetByEmailAsync(It.IsAny<string>()))
                .Returns((string email) => Task.FromResult(GuestsByEmail.GetValueOrDefault(email)));
            Guests.Setup(g => g.AddAsync(It.IsAny<Guest>()))
                .Returns((Guest guest) =>
                {
                    guest.Id = ++_nextId;
                    NewGuests.Add(guest);
                    GuestsByEmail[guest.Email] = guest;
                    return Task.CompletedTask;
                });
        }

        public Booking SeedBooking(string extRef, string status = BookingStatuses.Confirmed, string channel = "Booking.com")
        {
            var booking = new Booking
            {
                Id = ++_nextId, Reference = "LH-SEED", ExternalRef = extRef, Channel = channel, HotelId = 1, GuestId = 7,
                RoomTypeId = 1, RoomId = 5, RatePlanId = 1, CheckIn = new DateOnly(2026, 10, 10), CheckOut = new DateOnly(2026, 10, 12),
                Adults = 2, Status = status, TotalAmount = 240m,
                CreatedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)
            };
            BookingsByRef[extRef] = booking;
            return booking;
        }

        public Task<ImportResultDto> Import(string csv) =>
            new ChannelImportService(Bookings.Object, Guests.Object, Hotels.Object, Types.Object, Rates.Object).ImportAsync(csv);
    }

    private static string Row(string extRef = "BKG-1", string channel = "BKG", string hotel = "EDI", string type = "STD",
        string first = "Ada", string last = "Lovelace", string email = "ada@example.com", string phone = "07700900001",
        string checkIn = "2026-10-10", string checkOut = "2026-10-12", string adults = "2", string plan = "BAR",
        string total = "", string status = "CONFIRMED") =>
        string.Join(',', extRef, channel, hotel, type, first, last, email, phone, checkIn, checkOut, adults, plan, total, status);

    private static string Csv(params string[] rows) => string.Join('\n', new[] { Header }.Concat(rows));

    private static ImportRowError Err(int line, string extRef, string message) => new(line, extRef, message);

    private static string ReadSampleCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "docs", "sample-channel-export.csv")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sample-channel-export.csv"));
    }

    // ---------- Input and header ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData(null)]
    public async Task EmptyInput_ThrowsBusinessRule(string? csv)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new Harness().Import(csv!));
        Assert.Equal("The file is empty.", ex.Message);
    }

    [Fact]
    public async Task HeaderOnly_ReturnsZeroRowsAndNoErrors()
    {
        var result = await new Harness().Import(Header);
        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Cancelled);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task MissingColumns_ReportsFirstMissingInRequiredOrder()
    {
        var csv = Header.Replace(",guest_email", "").Replace(",status", "") + "\n";
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new Harness().Import(csv));
        Assert.Equal("Missing column 'guest_email'.", ex.Message);
    }

    [Fact]
    public async Task MissingChannelAndStatus_ReportsChannelFirst()
    {
        var csv = Header.Replace("channel,", "").Replace(",status", "") + "\n";
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new Harness().Import(csv));
        Assert.Equal("Missing column 'channel'.", ex.Message);
    }

    [Fact]
    public async Task HeaderAlias_IsNotAccepted()
    {
        var csv = Header.Replace("guest_email", "email") + "\n" + Row();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new Harness().Import(csv));
        Assert.Equal("Missing column 'guest_email'.", ex.Message);
    }

    [Fact]
    public async Task HeaderNames_AreTrimmedAndCaseInsensitive()
    {
        var upperHeader = " " + Header.ToUpperInvariant().Replace(",", " , ");
        var result = await new Harness().Import(upperHeader + "\n" + Row());
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task HeaderNames_MayBeQuoted()
    {
        var quotedHeader = string.Join(',', Header.Split(',').Select(h => $"\"{h}\""));
        var result = await new Harness().Import(quotedHeader + "\n" + Row());
        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public async Task ColumnOrder_IsTakenFromHeader()
    {
        var reversedHeader = string.Join(',', Header.Split(',').Reverse());
        var reversedRow = string.Join(',', Row().Split(',').Reverse());
        var h = new Harness();
        var result = await h.Import(reversedHeader + "\n" + reversedRow);
        Assert.Equal(1, result.Imported);
        Assert.Equal("Booking.com", h.Added[0].Channel);
        Assert.Equal("Ada", h.NewGuests[0].FirstName);
    }

    [Fact]
    public async Task ExtraColumns_AreIgnored()
    {
        var result = await new Harness().Import(Header + ",notes\n" + Row() + ",hello");
        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public async Task Bom_BeforeFirstHeader_BreaksExternalRefColumn()
    {
        // The controller's StreamReader strips a UTF-8 BOM from uploads, so this only matters for direct callers.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new Harness().Import("﻿" + Header + "\n" + Row()));
        Assert.Equal("Missing column 'external_ref'.", ex.Message);
    }

    [Fact]
    public async Task CrLfLineEndings_AreAccepted()
    {
        var result = await new Harness().Import(Header + "\r\n" + Row(extRef: "BKG-A") + "\r\n" + Row(extRef: "BKG-B") + "\r\n");
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.Imported);
    }

    [Fact]
    public async Task BlankLines_AreNotCounted_ButPhysicalLineNumbersAre()
    {
        var csv = Header + "\n\n   \n" + Row(extRef: "BKG-A", checkOut: "2026-10-09");
        var result = await new Harness().Import(csv);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new[] { Err(4, "BKG-A", "check_out must be after check_in.") }, result.Errors);
    }

    [Fact]
    public async Task TrailingNewline_DoesNotCountAsRow()
    {
        var result = await new Harness().Import(Csv(Row()) + "\n");
        Assert.Equal(1, result.TotalRows);
    }

    // ---------- Row parsing ----------

    [Fact]
    public async Task QuotedComma_StaysInsideOneCell()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "EXP-1", channel: "EXP", first: "\"Ahmed, Dr\"", last: "Rahman")));
        Assert.Equal(1, result.Imported);
        Assert.Equal("Ahmed, Dr", h.NewGuests[0].FirstName);
        Assert.Equal("Rahman", h.NewGuests[0].LastName);
    }

    [Fact]
    public async Task DoubledQuote_BecomesOneQuote()
    {
        var h = new Harness();
        await h.Import(Csv(Row(first: "\"Ada \"\"Countess\"\"\"")));
        Assert.Equal("Ada \"Countess\"", h.NewGuests[0].FirstName);
    }

    [Fact]
    public async Task CellWhitespace_IsTrimmed_EvenInsideQuotes()
    {
        var h = new Harness();
        await h.Import(Csv(Row(first: "\"  Ada  \"")));
        Assert.Equal("Ada", h.NewGuests[0].FirstName);
    }

    [Fact]
    public async Task ShortRow_ReportsTooFewColumns_WithoutLookingUpBooking()
    {
        var h = new Harness();
        var result = await h.Import(Csv("BKG-SHORT,BKG,EDI,STD,Ada"));
        Assert.Equal(new[] { Err(2, "BKG-SHORT", "Row has too few columns.") }, result.Errors);
        Assert.Equal(1, result.Skipped);
        h.Bookings.Verify(b => b.GetByExternalRefAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ShortRow_WithBlankRef_ReportsTooFewBeforeRefRequired()
    {
        var result = await new Harness().Import(Csv(","));
        Assert.Equal(new[] { Err(2, "", "Row has too few columns.") }, result.Errors);
    }

    [Fact]
    public async Task TenColumnRow_IsStillTooFew()
    {
        var result = await new Harness().Import(Csv("BKG-10,BKG,EDI,STD,Ada,Lovelace,ada@example.com,07700900001,2026-10-10,2026-10-12"));
        Assert.Equal(new[] { Err(2, "BKG-10", "Row has too few columns.") }, result.Errors);
    }

    [Fact]
    public async Task ElevenColumnRow_PassesLengthCheck_AndMissingStatusIsTreatedAsNewBooking()
    {
        // The length check uses the 11 required names, not the header width. Trailing cells then read as "".
        var h = new Harness();
        var result = await h.Import(Csv("BKG-11,BKG,EDI,STD,Ada,Lovelace,ada@example.com,07700900001,2026-10-10,2026-10-12,2"));
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
        var booking = Assert.Single(h.Added);
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Equal(1, booking.RatePlanId);
        Assert.Equal(240m, booking.TotalAmount);
    }

    [Fact]
    public async Task ExternalRefBlank_IsRequired_AndNotLookedUp()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "")));
        Assert.Equal(new[] { Err(2, "", "external_ref is required.") }, result.Errors);
        h.Bookings.Verify(b => b.GetByExternalRefAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExternalRefWhitespaceOnly_IsRequired()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "   ")));
        Assert.Equal(new[] { Err(2, "", "external_ref is required.") }, result.Errors);
    }

    // ---------- Cancellations ----------

    [Fact]
    public async Task Cancel_UnknownBooking_IsReportedAndSkipped()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-X", status: "CANCELLED")));
        Assert.Equal(new[] { Err(2, "BKG-X", "Cancellation for a booking we do not hold.") }, result.Errors);
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Cancelled);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Cancel_UnknownBooking_IsCheckedBeforeDatesAndHotel()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-X", status: "CANCELLED", checkIn: "nope", hotel: "ZZZ")));
        Assert.Equal(new[] { Err(2, "BKG-X", "Cancellation for a booking we do not hold.") }, result.Errors);
    }

    [Fact]
    public async Task Cancel_Confirmed_CancelsReleasesRoomAndNotesExistingChannel()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-C", BookingStatuses.Confirmed, channel: "Expedia");
        var before = DateTime.UtcNow;

        var result = await h.Import(Csv(Row(extRef: "BKG-C", channel: "BKG", status: "cancelled")));

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Cancelled);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.Equal(BookingStatuses.Cancelled, booking.Status);
        Assert.Null(booking.RoomId);
        Assert.InRange(booking.UpdatedAt, before, DateTime.UtcNow);
        var note = Assert.Single(h.Notes);
        Assert.Equal(booking.Id, note.BookingId);
        Assert.Equal("Cancelled by Expedia channel manager import.", note.Note);
        Assert.Equal(1, h.Saves);
        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IsSilentlySkipped()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-C", BookingStatuses.Cancelled);
        var result = await h.Import(Csv(Row(extRef: "BKG-C", status: "CANCELLED")));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Cancelled);
        Assert.Equal(0, result.Imported);
        Assert.Empty(h.Notes);
        Assert.Equal(0, h.Saves);
        Assert.Equal(BookingStatuses.Cancelled, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatuses.CheckedIn)]
    [InlineData(BookingStatuses.CheckedOut)]
    [InlineData(BookingStatuses.NoShow)]
    public async Task Cancel_NonConfirmedBooking_IsRejectedAndUnchanged(string status)
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-C", status);
        var result = await h.Import(Csv(Row(extRef: "BKG-C", status: "CANCELLED")));
        Assert.Equal(new[] { Err(2, "BKG-C", $"Booking is {status} and cannot be cancelled.") }, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Cancelled);
        Assert.Equal(status, booking.Status);
        Assert.Equal(5, booking.RoomId);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Cancel_KnownBooking_IgnoresBadDates()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-C");
        var result = await h.Import(Csv(Row(extRef: "BKG-C", status: "CANCELLED", checkIn: "nope")));
        Assert.Equal(1, result.Cancelled);
        Assert.Equal(1, result.Imported);
        Assert.Equal(BookingStatuses.Cancelled, booking.Status);
    }

    // ---------- Dates, nights and adults ----------

    [Theory]
    [InlineData("10/10/2026")]
    [InlineData("2026-1-5")]
    [InlineData("2026-02-30")]
    [InlineData("")]
    [InlineData("2026-10-10T00:00")]
    public async Task InvalidCheckIn_IsRejectedBeforeAnyLookup(string checkIn)
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-D", checkIn: checkIn)));
        Assert.Equal(new[] { Err(2, "BKG-D", "check_in is not a valid yyyy-MM-dd date.") }, result.Errors);
        h.Hotels.Verify(x => x.GetByCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("10/12/2026")]
    [InlineData("2026-10-1")]
    [InlineData("2026-13-01")]
    [InlineData("")]
    public async Task InvalidCheckOut_IsRejected(string checkOut)
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-D", checkOut: checkOut)));
        Assert.Equal(new[] { Err(2, "BKG-D", "check_out is not a valid yyyy-MM-dd date.") }, result.Errors);
    }

    [Fact]
    public async Task BothDatesInvalid_ReportsCheckInFirst()
    {
        var result = await new Harness().Import(Csv(Row(checkIn: "bad", checkOut: "bad")));
        Assert.Equal("check_in is not a valid yyyy-MM-dd date.", Assert.Single(result.Errors).Message);
    }

    [Theory]
    [InlineData("2026-10-10")]
    [InlineData("2026-10-09")]
    public async Task CheckOutNotAfterCheckIn_IsRejected(string checkOut)
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-D", checkOut: checkOut)));
        Assert.Equal(new[] { Err(2, "BKG-D", "check_out must be after check_in.") }, result.Errors);
    }

    [Fact]
    public async Task Stay_Of30Nights_IsAccepted()
    {
        var result = await new Harness().Import(Csv(Row(checkIn: "2026-10-01", checkOut: "2026-10-31", total: "100")));
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Stay_Of31Nights_IsRejected()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-L", checkIn: "2026-10-01", checkOut: "2026-11-01", total: "100")));
        Assert.Equal(new[] { Err(2, "BKG-L", "Stay is longer than 30 nights.") }, result.Errors);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task InvalidAdults_IsRejected(string adults)
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-A", adults: adults)));
        Assert.Equal(new[] { Err(2, "BKG-A", "adults must be a whole number of at least 1.") }, result.Errors);
    }

    [Fact]
    public async Task AdultsWithPlusSign_IsAccepted()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(adults: "+2")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, Assert.Single(h.Added).Adults);
    }

    [Fact]
    public async Task DateError_IsReportedBeforeAdultsError()
    {
        var result = await new Harness().Import(Csv(Row(checkOut: "bad", adults: "0")));
        Assert.Equal("check_out is not a valid yyyy-MM-dd date.", Assert.Single(result.Errors).Message);
    }

    // ---------- Hotel, room type and occupancy ----------

    [Fact]
    public async Task UnknownHotel_EchoesInputCase_AndLooksUpUpperCase()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-H", hotel: "xyz")));
        Assert.Equal(new[] { Err(2, "BKG-H", "Unknown hotel code 'xyz'.") }, result.Errors);
        h.Hotels.Verify(x => x.GetByCodeAsync("XYZ"), Times.Once);
    }

    [Fact]
    public async Task HotelCode_IsCaseInsensitive()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(hotel: "edi")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, Assert.Single(h.Added).HotelId);
        h.Hotels.Verify(x => x.GetByCodeAsync("EDI"), Times.Once);
    }

    [Fact]
    public async Task UnknownRoomType_EchoesInputCase()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-T", type: "qq")));
        Assert.Equal(new[] { Err(2, "BKG-T", "Unknown room type 'qq'.") }, result.Errors);
        h.Types.Verify(x => x.GetByCodeAsync("QQ"), Times.Once);
    }

    [Fact]
    public async Task UnknownHotel_IsCheckedBeforeRoomType()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-H", hotel: "ZZZ", type: "QQ")));
        Assert.Equal("Unknown hotel code 'ZZZ'.", Assert.Single(result.Errors).Message);
        h.Types.Verify(x => x.GetByCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OccupancyAboveMax_IsRejected_WithRoomTypeName()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-O", adults: "3")));
        Assert.Equal(new[] { Err(2, "BKG-O", "Standard Double sleeps at most 2.") }, result.Errors);
    }

    [Fact]
    public async Task OccupancyCheck_IsBeforeRatePlanCheck()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-O", adults: "3", plan: "NOPE")));
        Assert.Equal("Standard Double sleeps at most 2.", Assert.Single(result.Errors).Message);
    }

    // ---------- Rate plans ----------

    [Fact]
    public async Task BlankRatePlan_DefaultsToBar()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(plan: "", total: "200")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, Assert.Single(h.Added).RatePlanId);
        h.Rates.Verify(x => x.GetPlanByCodeAsync("BAR"), Times.Once);
    }

    [Fact]
    public async Task RatePlanLookup_IsUpperCased_ButErrorEchoesInput()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-P", plan: "corp")));
        Assert.Equal(new[] { Err(2, "BKG-P", "Unknown rate plan 'corp'.") }, result.Errors);
        h.Rates.Verify(x => x.GetPlanByCodeAsync("CORP"), Times.Once);
    }

    [Fact]
    public async Task LowerCaseKnownRatePlan_IsAccepted()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(plan: "adv", total: "300")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, Assert.Single(h.Added).RatePlanId);
    }

    [Fact]
    public async Task BlankRatePlan_WhenBarMissing_ReportsBar()
    {
        var h = new Harness();
        h.PlansByCode.Remove("BAR");
        var result = await h.Import(Csv(Row(extRef: "BKG-P", plan: "")));
        Assert.Equal(new[] { Err(2, "BKG-P", "Unknown rate plan 'BAR'.") }, result.Errors);
    }

    // ---------- Channel mapping ----------

    [Theory]
    [InlineData("BKG", "Booking.com")]
    [InlineData("bkg", "Booking.com")]
    [InlineData("EXP", "Expedia")]
    [InlineData("exp", "Expedia")]
    [InlineData("", "Direct")]
    [InlineData("WEB", "WEB")]
    [InlineData("Booking.com", "Booking.com")]
    public async Task Channel_IsMappedOrPassedThrough(string channel, string expected)
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-CH", channel: channel)));
        Assert.Equal(1, result.Imported);
        Assert.Equal(expected, Assert.Single(h.Added).Channel);
        Assert.Equal($"Imported from {expected} (BKG-CH).", Assert.Single(h.Notes).Note);
    }

    // ---------- Amounts ----------

    [Fact]
    public async Task SuppliedTotal_IsUsedAsIs_AndRateIsNotLookedUp()
    {
        var h = new Harness();
        await h.Import(Csv(Row(total: "318.75")));
        Assert.Equal(318.75m, Assert.Single(h.Added).TotalAmount);
        h.Rates.Verify(x => x.FindRateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task SuppliedTotal_AcceptsThousandsSeparator()
    {
        var h = new Harness();
        await h.Import(Csv(Row(total: "\"1,000.00\"")));
        Assert.Equal(1000m, Assert.Single(h.Added).TotalAmount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("12.5.5")]
    [InlineData("$100")]
    public async Task InvalidSuppliedTotal_IsRejected(string total)
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-M", total: total)));
        Assert.Equal(new[] { Err(2, "BKG-M", "total_amount is not a positive number.") }, result.Errors);
    }

    [Fact]
    public async Task BlankTotal_UsesNightlyRateTimesNights()
    {
        var h = new Harness();
        await h.Import(Csv(Row(checkIn: "2026-10-10", checkOut: "2026-10-13")));
        Assert.Equal(360m, Assert.Single(h.Added).TotalAmount);
        h.Rates.Verify(x => x.FindRateAsync(1, 1, 1, new DateOnly(2026, 10, 10)), Times.Once);
    }

    [Fact]
    public async Task BlankTotal_NoRateLoaded_IsRejected()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-R", hotel: "MAN")));
        Assert.Equal(new[] { Err(2, "BKG-R", "No rate loaded and no total_amount supplied.") }, result.Errors);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task InvalidTotal_IsCheckedBeforeRateLookup()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-M", hotel: "MAN", total: "0")));
        Assert.Equal("total_amount is not a positive number.", Assert.Single(result.Errors).Message);
        h.Rates.Verify(x => x.FindRateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task RatePlanError_IsCheckedBeforeMissingRate()
    {
        var result = await new Harness().Import(Csv(Row(extRef: "BKG-R", hotel: "MAN", plan: "NOPE")));
        Assert.Equal("Unknown rate plan 'NOPE'.", Assert.Single(result.Errors).Message);
    }

    // ---------- Existing bookings and duplicates ----------

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("PENDING")]
    [InlineData("")]
    public async Task DuplicateRef_IsSilentlySkipped_ForAnyNonModifiedStatus(string status)
    {
        var h = new Harness();
        h.SeedBooking("BKG-D");
        var result = await h.Import(Csv(Row(extRef: "BKG-D", status: status)));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Imported);
        Assert.Empty(h.Added);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task DuplicateRef_ValidationErrorsAreStillReported()
    {
        var h = new Harness();
        h.SeedBooking("BKG-D");
        var result = await h.Import(Csv(Row(extRef: "BKG-D", checkIn: "bad")));
        Assert.Equal(new[] { Err(2, "BKG-D", "check_in is not a valid yyyy-MM-dd date.") }, result.Errors);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task DuplicateRef_BadEmail_IsSilentlySkipped_BecauseEmailIsCheckedLater()
    {
        var h = new Harness();
        h.SeedBooking("BKG-D");
        var result = await h.Import(Csv(Row(extRef: "BKG-D", email: "nope")));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task DuplicateWithinSameFile_SecondRowIsSkipped()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-S"), Row(extRef: "BKG-S")));
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.Single(h.Added);
    }

    // ---------- MODIFIED ----------

    [Fact]
    public async Task Modified_ConfirmedBooking_UpdatesDatesAdultsAndTotal_KeepsRoomGuestAndChannel()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M", channel: "Booking.com");
        var before = DateTime.UtcNow;

        var result = await h.Import(Csv(Row(extRef: "BKG-M", channel: "EXP", checkIn: "2026-10-20", checkOut: "2026-10-23",
            adults: "1", status: "MODIFIED")));

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.Equal(new DateOnly(2026, 10, 20), booking.CheckIn);
        Assert.Equal(new DateOnly(2026, 10, 23), booking.CheckOut);
        Assert.Equal(1, booking.Adults);
        Assert.Equal(360m, booking.TotalAmount);
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Equal(5, booking.RoomId);
        Assert.Equal(7, booking.GuestId);
        Assert.Equal("Booking.com", booking.Channel);
        Assert.InRange(booking.UpdatedAt, before, DateTime.UtcNow);
        var note = Assert.Single(h.Notes);
        Assert.Equal("Dates changed by Expedia channel manager import.", note.Note);
        Assert.Equal(booking.Id, note.BookingId);
        Assert.Equal(1, h.Saves);
        Assert.Empty(h.Added);
        Assert.Empty(h.NewGuests);
    }

    [Fact]
    public async Task Modified_DoesNotCheckEmailOrGuestName()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M");
        var result = await h.Import(Csv(Row(extRef: "BKG-M", first: "", last: "", email: "not-an-email", total: "200", status: "MODIFIED")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(200m, booking.TotalAmount);
    }

    [Fact]
    public async Task Modified_CheckedInBooking_IsSilentlySkipped()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M", BookingStatuses.CheckedIn);
        var result = await h.Import(Csv(Row(extRef: "BKG-M", checkIn: "2026-10-11", checkOut: "2026-10-13", total: "200", status: "MODIFIED")));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new DateOnly(2026, 10, 10), booking.CheckIn);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Modified_CancelledBooking_IsSilentlySkipped()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M", BookingStatuses.Cancelled);
        var result = await h.Import(Csv(Row(extRef: "BKG-M", checkIn: "2026-10-11", checkOut: "2026-10-13", total: "200", status: "MODIFIED")));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new DateOnly(2026, 10, 10), booking.CheckIn);
    }

    [Fact]
    public async Task Modified_UnknownRef_CreatesNewBooking()
    {
        // Quirk: MODIFIED for a booking we do not hold is not an error. It falls through to the new-booking path.
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-9", total: "200.00", status: "MODIFIED")));
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
        var booking = Assert.Single(h.Added);
        Assert.Equal("BKG-9", booking.ExternalRef);
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Equal("Imported from Booking.com (BKG-9).", Assert.Single(h.Notes).Note);
    }

    [Fact]
    public async Task Modified_ExceedingOccupancy_IsRejectedAndNotApplied()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M");
        var result = await h.Import(Csv(Row(extRef: "BKG-M", adults: "3", total: "200", status: "MODIFIED")));
        Assert.Equal(new[] { Err(2, "BKG-M", "Standard Double sleeps at most 2.") }, result.Errors);
        Assert.Equal(2, booking.Adults);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Modified_WithNoRate_IsRejectedEvenWhenBookingExists()
    {
        // Quirk: the amount is priced before the existing-booking check, so a MODIFIED row with no rate fails.
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M");
        var result = await h.Import(Csv(Row(extRef: "BKG-M", hotel: "MAN", status: "MODIFIED")));
        Assert.Equal(new[] { Err(2, "BKG-M", "No rate loaded and no total_amount supplied.") }, result.Errors);
        Assert.Equal(new DateOnly(2026, 10, 10), booking.CheckIn);
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Modified_LowerCaseStatus_IsTreatedAsModified()
    {
        var h = new Harness();
        var booking = h.SeedBooking("BKG-M");
        var result = await h.Import(Csv(Row(extRef: "BKG-M", checkIn: "2026-10-11", checkOut: "2026-10-12", total: "300", status: "modified")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(new DateOnly(2026, 10, 11), booking.CheckIn);
    }

    // ---------- New bookings and guests ----------

    [Fact]
    public async Task NewBooking_HappyPath_SetsFieldsAndNote()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(total: "200.00")));

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Cancelled);
        Assert.Empty(result.Errors);

        var booking = Assert.Single(h.Added);
        var guest = Assert.Single(h.NewGuests);
        Assert.Equal("LH-50001", booking.Reference);
        Assert.Equal("BKG-1", booking.ExternalRef);
        Assert.Equal("Booking.com", booking.Channel);
        Assert.Equal(1, booking.HotelId);
        Assert.Equal(guest.Id, booking.GuestId);
        Assert.Equal(1, booking.RoomTypeId);
        Assert.Equal(1, booking.RatePlanId);
        Assert.Equal(new DateOnly(2026, 10, 10), booking.CheckIn);
        Assert.Equal(new DateOnly(2026, 10, 12), booking.CheckOut);
        Assert.Equal(2, booking.Adults);
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Equal(200.00m, booking.TotalAmount);
        Assert.Null(booking.RoomId);
        Assert.Equal(booking.CreatedAt, booking.UpdatedAt);
        var note = Assert.Single(h.Notes);
        Assert.Equal(booking.Id, note.BookingId);
        Assert.Equal("Imported from Booking.com (BKG-1).", note.Note);
        Assert.Equal(1, h.Saves);
    }

    [Fact]
    public async Task NewGuest_DefaultsFromHotelAndChannelDefaults_WhenPhoneBlank()
    {
        var h = new Harness();
        await h.Import(Csv(Row(phone: "", total: "200")));
        var guest = Assert.Single(h.NewGuests);
        Assert.Equal("Ada", guest.FirstName);
        Assert.Equal("Lovelace", guest.LastName);
        Assert.Equal("ada@example.com", guest.Email);
        Assert.Equal("n/a", guest.Phone);
        Assert.Equal("Not supplied by channel", guest.AddressLine);
        Assert.Equal("Edinburgh", guest.City);
        Assert.Equal("n/a", guest.Postcode);
        Assert.Equal("GB", guest.Country);
        Assert.Equal("Scotland", guest.Region);
        Assert.Equal("None", guest.LoyaltyTier);
        Assert.NotEqual(default, guest.CreatedAt);
    }

    [Fact]
    public async Task NewGuest_KeepsSuppliedPhone()
    {
        var h = new Harness();
        await h.Import(Csv(Row(phone: "07700 900001", total: "200")));
        Assert.Equal("07700 900001", Assert.Single(h.NewGuests).Phone);
    }

    [Fact]
    public async Task ExistingGuest_IsReused_NameNotRequiredNorUpdated()
    {
        var h = new Harness();
        h.GuestsByEmail["ada@example.com"] = new Guest { Id = 42, FirstName = "Ada", LastName = "L", Email = "ada@example.com" };
        var result = await h.Import(Csv(Row(first: "", last: "", total: "200")));
        Assert.Equal(1, result.Imported);
        Assert.Equal(42, Assert.Single(h.Added).GuestId);
        Assert.Empty(h.NewGuests);
        Assert.Equal("Ada", h.GuestsByEmail["ada@example.com"].FirstName);
    }

    [Theory]
    [InlineData("", "Lovelace")]
    [InlineData("Ada", "")]
    [InlineData("\"  \"", "Lovelace")]
    public async Task NewGuest_MissingName_IsRejected_AndNothingCreated(string first, string last)
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-N", first: first, last: last, total: "200")));
        Assert.Equal(new[] { Err(2, "BKG-N", "Guest name is required for a new guest.") }, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(h.NewGuests);
        Assert.Empty(h.Added);
        Assert.Equal(0, h.ReferenceCalls);
        Assert.Equal(0, h.Saves);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("ada.example.com")]
    public async Task GuestEmail_MissingOrWithoutAt_IsRejected(string email)
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(extRef: "BKG-E", email: email, total: "200")));
        Assert.Equal(new[] { Err(2, "BKG-E", "guest_email is missing or not an email address.") }, result.Errors);
        h.Guests.Verify(x => x.GetByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GuestEmail_OnlyAtSign_IsAccepted()
    {
        var h = new Harness();
        var result = await h.Import(Csv(Row(email: "@", total: "200")));
        Assert.Equal(1, result.Imported);
        Assert.Equal("@", Assert.Single(h.NewGuests).Email);
    }

    [Fact]
    public async Task GuestEmail_IsMatchedCaseSensitively_CreatesDuplicateGuest()
    {
        // Quirk: the lookup passes the email through unchanged. Real MySQL collation may hide this.
        var h = new Harness();
        h.GuestsByEmail["ada@example.com"] = new Guest { Id = 42, FirstName = "Ada", LastName = "L", Email = "ada@example.com" };
        var result = await h.Import(Csv(Row(email: "ADA@example.com", total: "200")));
        Assert.Equal(1, result.Imported);
        var guest = Assert.Single(h.NewGuests);
        Assert.Equal("ADA@example.com", guest.Email);
        Assert.NotEqual(42, Assert.Single(h.Added).GuestId);
    }

    [Fact]
    public async Task GuestEmailError_IsCheckedBeforeNameError()
    {
        var result = await new Harness().Import(Csv(Row(first: "", email: "nope", total: "200")));
        Assert.Equal("guest_email is missing or not an email address.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task ReferenceNumber_IsNotConsumedByFailedRows()
    {
        var h = new Harness();
        var result = await h.Import(Csv(
            Row(extRef: "BKG-BAD", hotel: "ZZZ"),
            Row(extRef: "BKG-A", total: "200"),
            Row(extRef: "BKG-B", total: "200")));
        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(2, h.ReferenceCalls);
        Assert.Equal("LH-50001", h.Added[0].Reference);
        Assert.Equal("LH-50002", h.Added[1].Reference);
    }

    // ---------- Batches, sample file and partial failure ----------

    [Fact]
    public async Task MixedBatch_CountsAndErrorOrder()
    {
        var h = new Harness();
        h.SeedBooking("BKG-SEEN");
        var result = await h.Import(Csv(
            Row(extRef: "BKG-1"),
            Row(extRef: "BKG-2", hotel: "ZZZ"),
            Row(extRef: "BKG-3", status: "CANCELLED"),
            Row(extRef: "BKG-SEEN"),
            Row(extRef: "BKG-5")));

        Assert.Equal(5, result.TotalRows);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Cancelled);
        Assert.Equal(3, result.Skipped);
        Assert.Equal(new[]
        {
            Err(3, "BKG-2", "Unknown hotel code 'ZZZ'."),
            Err(4, "BKG-3", "Cancellation for a booking we do not hold."),
        }, result.Errors);
    }

    [Fact]
    public async Task SampleCsv_OnEmptyDatabase_ExactResult()
    {
        var h = new Harness();
        var result = await h.Import(ReadSampleCsv());

        Assert.Equal(13, result.TotalRows);
        Assert.Equal(4, result.Imported);
        Assert.Equal(0, result.Cancelled);
        Assert.Equal(9, result.Skipped);
        Assert.Equal(new[]
        {
            Err(4, "EXP-801000103", "Unknown rate plan 'PKG'."),
            Err(7, "BKG-730427760", "Cancellation for a booking we do not hold."),
            Err(8, "BKG-711587811", "No rate loaded and no total_amount supplied."),
            Err(9, "BKG-801000108", "Unknown hotel code 'XXX'."),
            Err(10, "BKG-801000109", "check_out must be after check_in."),
            Err(11, "BKG-801000110", "Standard Double sleeps at most 2."),
            Err(12, "EXP-801000111", "Unknown rate plan 'CORP'."),
            Err(13, "BKG-801000112", "No rate loaded and no total_amount supplied."),
            Err(14, "BKG-801000113", "No rate loaded and no total_amount supplied."),
        }, result.Errors);

        Assert.Equal(new[]
        {
            ("LH-50001", "BKG-801000101", "Booking.com", 240m),
            ("LH-50002", "BKG-801000102", "Booking.com", 318.75m),
            ("LH-50003", "EXP-801000104", "Expedia", 229.90m),
            ("LH-50004", "BKG-801000105", "Booking.com", 95m),
        }, h.Added.Select(b => (b.Reference, b.ExternalRef!, b.Channel, b.TotalAmount)).ToArray());
        Assert.Equal(new[] { "priya.raman@example.com", "tom.whitaker@example.org", "chidi.okafor@example.com", "jacob.thomas44@example.net" },
            h.NewGuests.Select(g => g.Email).ToArray());
        Assert.Equal("n/a", h.NewGuests[2].Phone);
        Assert.Equal(4, h.Saves);
    }

    [Fact]
    public async Task SampleCsv_WithExistingBookings_CancelsOneAndSkipsMarked()
    {
        var h = new Harness();
        var cancelled = h.SeedBooking("BKG-730427760");
        h.SeedBooking("BKG-711587811");

        var result = await h.Import(ReadSampleCsv());

        Assert.Equal(13, result.TotalRows);
        Assert.Equal(5, result.Imported);
        Assert.Equal(1, result.Cancelled);
        Assert.Equal(8, result.Skipped);
        Assert.Equal(BookingStatuses.Cancelled, cancelled.Status);
        Assert.Equal("Cancelled by Booking.com channel manager import.", h.Notes.Single(n => n.BookingId == cancelled.Id).Note);
        Assert.Equal(new[] { 4, 8, 9, 10, 11, 12, 13, 14 }, result.Errors.Select(e => e.Line).ToArray());
        Assert.Equal(4, h.Added.Count(b => b.Status == BookingStatuses.Confirmed));
    }

    [Fact]
    public async Task PartialFailure_EarlierRowsStayCommitted_LaterRowsNeverRun()
    {
        // No transaction: row 1 is saved, row 2 is added and then fails on SaveAsync, row 3 is never reached.
        var h = new Harness { FailSaveAt = 2 };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Import(Csv(
            Row(extRef: "BKG-A", total: "200"),
            Row(extRef: "BKG-B", total: "200"),
            Row(extRef: "BKG-C", total: "200"))));
        Assert.Equal("db down", ex.Message);
        Assert.Equal(2, h.Added.Count);
        Assert.Equal("BKG-A", h.Added[0].ExternalRef);
        Assert.Equal("BKG-B", h.Added[1].ExternalRef);
        Assert.Equal(2, h.ReferenceCalls);
        Assert.Equal(2, h.Saves);
    }
}
