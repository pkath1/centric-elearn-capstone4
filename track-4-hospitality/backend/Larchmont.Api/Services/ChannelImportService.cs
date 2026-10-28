using System.Globalization;
using System.Text;
using Larchmont.Api.Domain;
using Larchmont.Api.Dtos;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;

namespace Larchmont.Api.Services;

public interface IChannelImportService
{
    Task<ImportResultDto> ImportAsync(string csv);
}

public class ChannelImportService : IChannelImportService
{
    private static readonly string[] RequiredColumns =
    {
        "external_ref", "channel", "hotel_code", "room_type_code", "guest_first", "guest_last",
        "guest_email", "check_in", "check_out", "adults", "status"
    };

    private readonly IBookingRepository _bookings;
    private readonly IGuestRepository _guests;
    private readonly IHotelRepository _hotels;
    private readonly IRoomTypeRepository _types;
    private readonly IRateRepository _rates;

    public ChannelImportService(IBookingRepository bookings, IGuestRepository guests, IHotelRepository hotels,
        IRoomTypeRepository types, IRateRepository rates)
    {
        _bookings = bookings;
        _guests = guests;
        _hotels = hotels;
        _types = types;
        _rates = rates;
    }

    public async Task<ImportResultDto> ImportAsync(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new BusinessRuleException("The file is empty.");
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var header = ParseHeader(lines[0]);
        EnsureRequiredColumns(header);

        var run = new ImportRun();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            run.Total++;
            await ProcessRowAsync(run, new ImportRow(i + 1, header, SplitCells(lines[i], escapedQuotes: true)));
        }

        return new ImportResultDto(run.Total, run.Imported, run.Skipped, run.Cancelled, run.Errors);
    }

    private async Task ProcessRowAsync(ImportRun run, ImportRow row)
    {
        if (row.CellCount < RequiredColumns.Length) { run.Reject(row, "Row has too few columns."); return; }
        if (string.IsNullOrEmpty(row.ExternalRef)) { run.Reject(row, "external_ref is required."); return; }

        var existing = await _bookings.GetByExternalRefAsync(row.ExternalRef);
        if (row.Status == "CANCELLED")
        {
            await CancelAsync(run, row, existing);
            return;
        }

        var stayError = ReadStay(row, out var checkIn, out var checkOut);
        if (stayError != null) { run.Reject(row, stayError); return; }
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (!int.TryParse(row.Get("adults"), out var adults) || adults < 1)
        {
            run.Reject(row, "adults must be a whole number of at least 1.");
            return;
        }

        var hotel = await _hotels.GetByCodeAsync(row.Get("hotel_code").ToUpperInvariant());
        if (hotel == null) { run.Reject(row, $"Unknown hotel code '{row.Get("hotel_code")}'."); return; }
        var type = await _types.GetByCodeAsync(row.Get("room_type_code").ToUpperInvariant());
        if (type == null) { run.Reject(row, $"Unknown room type '{row.Get("room_type_code")}'."); return; }
        if (adults > type.MaxOccupancy) { run.Reject(row, $"{type.Name} sleeps at most {type.MaxOccupancy}."); return; }

        var planCode = string.IsNullOrEmpty(row.Get("rate_plan_code")) ? "BAR" : row.Get("rate_plan_code");
        var plan = await _rates.GetPlanByCodeAsync(planCode.ToUpperInvariant());
        if (plan == null) { run.Reject(row, $"Unknown rate plan '{planCode}'."); return; }

        var channel = MapChannel(row.Get("channel"));

        var (priceError, totalAmount) = await ResolveTotalAsync(row, hotel, type, plan, checkIn, nights);
        if (priceError != null) { run.Reject(row, priceError); return; }

        if (existing != null)
        {
            if (row.Status == "MODIFIED" && existing.Status == BookingStatuses.Confirmed)
            {
                await ApplyModificationAsync(run, existing, channel, checkIn, checkOut, adults, totalAmount);
            }
            else
            {
                run.Skip();
            }
            return;
        }

        var email = row.Get("guest_email");
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            run.Reject(row, "guest_email is missing or not an email address.");
            return;
        }
        var guest = await _guests.GetByEmailAsync(email);
        if (guest == null)
        {
            if (string.IsNullOrEmpty(row.Get("guest_first")) || string.IsNullOrEmpty(row.Get("guest_last")))
            {
                run.Reject(row, "Guest name is required for a new guest.");
                return;
            }
            guest = NewGuest(row, hotel, email);
            await _guests.AddAsync(guest);
        }

        var now = DateTime.UtcNow;
        var booking = new Booking
        {
            Reference = $"LH-{await _bookings.NextReferenceNumberAsync()}",
            ExternalRef = row.ExternalRef,
            Channel = channel,
            HotelId = hotel.Id,
            GuestId = guest.Id,
            RoomTypeId = type.Id,
            RatePlanId = plan.Id,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Adults = adults,
            Status = BookingStatuses.Confirmed,
            TotalAmount = totalAmount,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _bookings.AddAsync(booking);
        await _bookings.AddNoteAsync(new BookingNote
        {
            BookingId = booking.Id,
            Note = $"Imported from {channel} ({row.ExternalRef}).",
            CreatedAt = now
        });
        await _bookings.SaveAsync();
        run.RecordImported();
    }

    private async Task CancelAsync(ImportRun run, ImportRow row, Booking? existing)
    {
        if (existing == null) { run.Reject(row, "Cancellation for a booking we do not hold."); return; }
        if (existing.Status == BookingStatuses.Cancelled) { run.Skip(); return; }
        if (existing.Status != BookingStatuses.Confirmed)
        {
            run.Reject(row, $"Booking is {existing.Status} and cannot be cancelled.");
            return;
        }
        existing.Status = BookingStatuses.Cancelled;
        existing.RoomId = null;
        existing.UpdatedAt = DateTime.UtcNow;
        await _bookings.AddNoteAsync(new BookingNote
        {
            BookingId = existing.Id,
            Note = $"Cancelled by {existing.Channel} channel manager import.",
            CreatedAt = DateTime.UtcNow
        });
        await _bookings.SaveAsync();
        run.RecordCancelled();
    }

    private async Task ApplyModificationAsync(ImportRun run, Booking existing, string channel, DateOnly checkIn,
        DateOnly checkOut, int adults, decimal totalAmount)
    {
        existing.CheckIn = checkIn;
        existing.CheckOut = checkOut;
        existing.Adults = adults;
        existing.TotalAmount = totalAmount;
        existing.UpdatedAt = DateTime.UtcNow;
        await _bookings.AddNoteAsync(new BookingNote
        {
            BookingId = existing.Id,
            Note = $"Dates changed by {channel} channel manager import.",
            CreatedAt = DateTime.UtcNow
        });
        await _bookings.SaveAsync();
        run.RecordImported();
    }

    // Returns (error, total). Supplied total_amount wins; otherwise nightly rate times nights.
    private async Task<(string? Error, decimal Total)> ResolveTotalAsync(ImportRow row, Hotel hotel, RoomType type,
        RatePlan plan, DateOnly checkIn, int nights)
    {
        var amountText = row.Get("total_amount");
        if (!string.IsNullOrEmpty(amountText))
        {
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var supplied) || supplied <= 0)
                return ("total_amount is not a positive number.", 0);
            return (null, supplied);
        }

        var rate = await _rates.FindRateAsync(hotel.Id, type.Id, plan.Id, checkIn);
        if (rate == null) return ("No rate loaded and no total_amount supplied.", 0);
        return (null, rate.NightlyRate * nights);
    }

    private static string? ReadStay(ImportRow row, out DateOnly checkIn, out DateOnly checkOut)
    {
        checkIn = default;
        checkOut = default;
        if (!DateOnly.TryParseExact(row.Get("check_in"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out checkIn))
            return "check_in is not a valid yyyy-MM-dd date.";
        if (!DateOnly.TryParseExact(row.Get("check_out"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out checkOut))
            return "check_out is not a valid yyyy-MM-dd date.";
        if (checkOut <= checkIn) return "check_out must be after check_in.";
        if (checkOut.DayNumber - checkIn.DayNumber > 30) return "Stay is longer than 30 nights.";
        return null;
    }

    private static string MapChannel(string channel)
    {
        if (channel.Equals("BKG", StringComparison.OrdinalIgnoreCase)) return "Booking.com";
        if (channel.Equals("EXP", StringComparison.OrdinalIgnoreCase)) return "Expedia";
        if (string.IsNullOrEmpty(channel)) return "Direct";
        return channel;
    }

    private static Guest NewGuest(ImportRow row, Hotel hotel, string email)
    {
        var phone = row.Get("guest_phone");
        return new Guest
        {
            FirstName = row.Get("guest_first"),
            LastName = row.Get("guest_last"),
            Email = email,
            Phone = string.IsNullOrEmpty(phone) ? "n/a" : phone,
            AddressLine = "Not supplied by channel",
            City = hotel.City,
            Postcode = "n/a",
            Country = "GB",
            Region = hotel.Region,
            LoyaltyTier = "None",
            CreatedAt = DateTime.UtcNow
        };
    }

    // Throws for the first required column (in RequiredColumns order) that the header does not contain.
    private static void EnsureRequiredColumns(List<string> header)
    {
        foreach (var column in RequiredColumns)
        {
            if (!header.Contains(column)) throw new BusinessRuleException($"Missing column '{column}'.");
        }
    }

    // Header cells: quotes toggle quoting and a doubled quote is NOT an escape here (kept as-is from the original code).
    private static List<string> ParseHeader(string headerLine) =>
        SplitCells(headerLine, escapedQuotes: false).Select(h => h.ToLowerInvariant()).ToList();

    // Data cells: a doubled quote inside a quoted cell is one literal quote. Every cell is trimmed.
    private static List<string> SplitCells(string line, bool escapedQuotes)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var c = 0; c < line.Length; c++)
        {
            var ch = line[c];
            if (ch == '"')
            {
                if (escapedQuotes && inQuotes && c + 1 < line.Length && line[c + 1] == '"') { sb.Append('"'); c++; continue; }
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ',' && !inQuotes) { cells.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    // Running totals and error list for one import call.
    private sealed class ImportRun
    {
        public int Total { get; set; }
        public int Imported { get; private set; }
        public int Skipped { get; private set; }
        public int Cancelled { get; private set; }
        public List<ImportRowError> Errors { get; } = new();

        public void Reject(ImportRow row, string message)
        {
            Errors.Add(new ImportRowError(row.Line, row.ExternalRef, message));
            Skipped++;
        }

        public void Skip() => Skipped++;

        public void RecordImported() => Imported++;

        public void RecordCancelled()
        {
            Cancelled++;
            Imported++;
        }
    }

    private sealed class ImportRow
    {
        private readonly List<string> _header;
        private readonly List<string> _cells;

        public ImportRow(int line, List<string> header, List<string> cells)
        {
            Line = line;
            _header = header;
            _cells = cells;
        }

        public int Line { get; }
        public int CellCount => _cells.Count;
        public string ExternalRef => Get("external_ref");
        public string Status => Get("status").ToUpperInvariant();

        public string Get(string name)
        {
            var idx = _header.IndexOf(name);
            return idx >= 0 && idx < _cells.Count ? _cells[idx] : "";
        }
    }
}
