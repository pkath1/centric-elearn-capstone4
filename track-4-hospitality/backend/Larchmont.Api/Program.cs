using DbUp;
using FluentValidation;
using Larchmont.Api.Data;
using Larchmont.Api.Infrastructure;
using Larchmont.Api.Repositories;
using Larchmont.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Larchmont")
    ?? throw new InvalidOperationException("ConnectionStrings:Larchmont is not set.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<LarchmontDbContext>(o =>
    o.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<IHotelRepository, HotelRepository>();
builder.Services.AddScoped<IRoomTypeRepository, RoomTypeRepository>();
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<IGuestRepository, GuestRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IRateRepository, RateRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoomTurnaroundRepository, RoomTurnaroundRepository>();
builder.Services.AddScoped<IFloorAssignmentRepository, FloorAssignmentRepository>();
builder.Services.AddScoped<IRoomSpotCheckRepository, RoomSpotCheckRepository>();

builder.Services.AddScoped<IClock, SystemClock>();
builder.Services.AddScoped<IRoomStatusPolicy, RoomStatusPolicy>();
builder.Services.AddScoped<IHousekeepingBoardService, HousekeepingBoardService>();
builder.Services.AddScoped<IFloorAssignmentService, FloorAssignmentService>();
builder.Services.AddScoped<ILockIntegrationService, LockIntegrationService>();

builder.Services.AddScoped<IHotelService, HotelService>();
builder.Services.AddScoped<IRoomTypeService, RoomTypeService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IGuestService, GuestService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IRateService, RateService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IChannelImportService, ChannelImportService>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    var upgrader = DeployChanges.To
        .MySqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly, name => name.EndsWith(".sql"))
        .WithVariablesDisabled()
        .LogToConsole()
        .Build();
    var result = upgrader.PerformUpgrade();
    if (!result.Successful) throw result.Error;
}

app.UseExceptionHandler();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

public partial class Program { }
