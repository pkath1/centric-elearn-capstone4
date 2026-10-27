using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Larchmont.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Larchmont.Tests;

// Clock whose instant the test controls. Never reads the real date or time zone.
public class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = HousekeepingFixture.At(12);
}

// ApiFactory with a fixed clock and a test-only lock key. Each test creates its own
// factory, so each test gets a fresh InMemory database.
public class HousekeepingFactory : ApiFactory
{
    // Test-only value. Not a credential.
    public const string LockKey = "test-lock-key-not-a-secret";

    public FixedClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Lock:ApiKey", LockKey);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    // Seeds hotels 1 and 2, room types, rate plan, guest, users, plus the given entities.
    public void SeedHousekeeping(params object[] entities)
    {
        Seed(db =>
        {
            HousekeepingFixture.Core(db);
            foreach (var e in entities) db.Add(e);
        });
    }

    public T Query<T>(Func<LarchmontDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<LarchmontDbContext>());
    }
}

public static class HousekeepingFixture
{
    // Users (ids are fixed so tests read as the spec does).
    public const int Supervisor = 2;
    public const int HousekeeperA = 3;
    public const int Maintenance = 4;
    public const int HousekeeperB = 5;
    public const int HousekeeperC = 6;
    public const int FrontDesk = 8;
    public const int HousekeeperOtherHotel = 9;

    public static DateTime At(int hour, int minute = 0) => new(2026, 10, 27, hour, minute, 0, DateTimeKind.Utc);

    public static void Core(LarchmontDbContext db)
    {
        db.Hotels.Add(new Hotel { Id = 1, Code = "EDI", Name = "Larchmont Edinburgh", City = "Edinburgh", Region = "Scotland", AddressLine = "14 Castle Terrace", Phone = "+44 131 555 0101" });
        db.Hotels.Add(new Hotel { Id = 2, Code = "GLA", Name = "Larchmont Glasgow", City = "Glasgow", Region = "Scotland", AddressLine = "1 Argyle Street", Phone = "+44 141 555 0102" });
        db.RoomTypes.Add(new RoomType { Id = 1, Code = "STD", Name = "Standard Double", MaxOccupancy = 2, BaseRate = 95, SlaMinutes = 45 });
        db.RoomTypes.Add(new RoomType { Id = 2, Code = "STE", Name = "Suite", MaxOccupancy = 3, BaseRate = 240, SlaMinutes = 75 });
        db.RoomTypes.Add(new RoomType { Id = 3, Code = "FAM", Name = "Family Room", MaxOccupancy = 4, BaseRate = 160, SlaMinutes = 75 });
        db.RatePlans.Add(new RatePlan { Id = 1, Code = "BAR", Name = "Best Available" });
        db.Guests.Add(new Guest { Id = 1, FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.test", Phone = "+44 7700 900001", CreatedAt = At(9) });
        db.Users.Add(User(Supervisor, "supervisor@example.test", "Supervisor", 1));
        db.Users.Add(User(HousekeeperA, "housekeeper.a@example.test", "Housekeeper", 1));
        db.Users.Add(User(Maintenance, "maintenance@example.test", "Maintenance", 1));
        db.Users.Add(User(HousekeeperB, "housekeeper.b@example.test", "Housekeeper", 1));
        db.Users.Add(User(HousekeeperC, "housekeeper.c@example.test", "Housekeeper", 1));
        db.Users.Add(User(FrontDesk, "frontdesk@example.test", "FrontDesk", 1));
        db.Users.Add(User(HousekeeperOtherHotel, "housekeeper.gla@example.test", "Housekeeper", 2));
    }

    private static User User(int id, string email, string role, int hotelId) => new()
    {
        Id = id, Email = email, DisplayName = role + " " + id, Role = role, HotelId = hotelId,
        PasswordHash = "not-used-in-tests", Active = true
    };

    public static Room NewRoom(int id, string number, int floor, string status, int roomTypeId = 1,
        bool simpleTurn = false, int rowVersion = 1, int hotelId = 1) => new()
    {
        Id = id, HotelId = hotelId, RoomTypeId = roomTypeId, RoomNumber = number, Floor = floor,
        Status = status, SimpleTurn = simpleTurn, RowVersion = rowVersion
    };

    public static Task<HttpResponseMessage> PutStatus(HttpClient client, int roomId, object body) =>
        client.PutAsJsonAsync($"/api/rooms/{roomId}/status", body);

    public static Task<HttpResponseMessage> LockCheckout(HttpClient client, string? apiKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/lock/checkout")
        {
            Content = JsonContent.Create(body)
        };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    public static DateTime Utc(JsonElement value) =>
        DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    // Keys of the ProblemDetails "errors" object (FluentValidation field names).
    public static List<string> ErrorFields(JsonElement problem) =>
        problem.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
            ? errors.EnumerateObject().Select(p => p.Name).ToList()
            : new List<string>();

    public static bool NamesField(JsonElement problem, string field) =>
        ErrorFields(problem).Any(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));
}
