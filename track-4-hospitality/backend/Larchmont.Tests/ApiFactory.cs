using Larchmont.Api.Data;
using Larchmont.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Larchmont.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "larchmont-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LarchmontDbContext>>();
            // The InMemory store has no transactions. Code that opens one (floor assignments, BR-17) runs without
            // one here; the all-or-nothing rollback is only real on MySQL (see docs/perf-note.md).
            services.AddDbContext<LarchmontDbContext>(o => o.UseInMemoryDatabase(_dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    public void Seed(Action<LarchmontDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LarchmontDbContext>();
        seed(db);
        db.SaveChanges();
    }

    private bool _seeded;

    public void SeedBasics()
    {
        if (_seeded) return;
        _seeded = true;
        Seed(db =>
        {
            db.Hotels.Add(new Hotel { Id = 1, Code = "EDI", Name = "Larchmont Edinburgh", City = "Edinburgh", Region = "Scotland", AddressLine = "14 Castle Terrace", Phone = "+44 131 555 0101" });
            db.RoomTypes.Add(new RoomType { Id = 1, Code = "STD", Name = "Standard Double", MaxOccupancy = 2, BaseRate = 95 });
        });
    }
}
