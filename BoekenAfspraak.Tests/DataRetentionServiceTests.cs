using BoekenAfspraak.Api.Data;
using BoekenAfspraak.Api.Models;
using BoekenAfspraak.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BoekenAfspraak.Tests;

// Exercises DataRetentionService.RunOnceAsync directly (internal, exposed to
// this assembly via InternalsVisibleTo) against the same DI-wired
// AppDbContext/DataDirectory as the running app, instead of waiting for its
// real 24-hour background loop.
public class DataRetentionServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DataRetentionServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static Appointment MakeAppointment(
        int dateOffsetDays, string timeSlot, string email,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        DateTime? cancelledAtUtc = null, string? photoFileNames = null) => new()
    {
        Name = "Retentie Test",
        Address = "Teststraat 1",
        Email = email,
        BookCount = 12,
        Date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(dateOffsetDays)),
        TimeSlot = timeSlot,
        EstimatedPriceEuro = 3.50m,
        Status = status,
        CancelledAtUtc = cancelledAtUtc,
        PhotoFileNames = photoFileNames
    };

    private DataRetentionService GetService() =>
        _factory.Services.GetServices<IHostedService>().OfType<DataRetentionService>().Single();

    [Fact]
    public async Task RunOnceAsync_DeletesAppointmentsPastRetentionByDate_KeepsRecentOnes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retentionDays = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value.DataRetentionDays;

        var expired = MakeAppointment(-(retentionDays + 5), "18:00", "verlopen@example.com");
        var recent = MakeAppointment(3, "18:12", "recent@example.com");
        db.AddRange(expired, recent);
        await db.SaveChangesAsync();

        await GetService().RunOnceAsync(CancellationToken.None);

        Assert.False(await db.Appointments.AnyAsync(a => a.Email == "verlopen@example.com"));
        Assert.True(await db.Appointments.AnyAsync(a => a.Email == "recent@example.com"));
    }

    [Fact]
    public async Task RunOnceAsync_DeletesOldCancelledAppointments_EvenIfDateIsInFuture()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retentionDays = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value.DataRetentionDays;

        var oldCancelled = MakeAppointment(10, "18:24", "oudgeannuleerd@example.com",
            status: AppointmentStatus.Cancelled, cancelledAtUtc: DateTime.UtcNow.AddDays(-(retentionDays + 5)));
        var recentlyCancelled = MakeAppointment(11, "18:36", "recentgeannuleerd@example.com",
            status: AppointmentStatus.Cancelled, cancelledAtUtc: DateTime.UtcNow.AddDays(-1));
        db.AddRange(oldCancelled, recentlyCancelled);
        await db.SaveChangesAsync();

        await GetService().RunOnceAsync(CancellationToken.None);

        Assert.False(await db.Appointments.AnyAsync(a => a.Email == "oudgeannuleerd@example.com"));
        Assert.True(await db.Appointments.AnyAsync(a => a.Email == "recentgeannuleerd@example.com"));
    }

    [Fact]
    public async Task RunOnceAsync_DeletesUploadedPhotosFromDisk_ForExpiredAppointment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retentionDays = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value.DataRetentionDays;

        var appointment = MakeAppointment(-(retentionDays + 5), "18:48", "fotos@example.com",
            photoFileNames: "foto1.jpg,foto2.jpg");
        db.Add(appointment);
        await db.SaveChangesAsync();

        var folder = Path.Combine(_factory.DataDir, "uploads", appointment.ManageToken.ToString());
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "foto1.jpg"), new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(Path.Combine(folder, "foto2.jpg"), new byte[] { 4, 5, 6 });

        await GetService().RunOnceAsync(CancellationToken.None);

        Assert.False(Directory.Exists(folder));
        Assert.False(await db.Appointments.AnyAsync(a => a.Email == "fotos@example.com"));
    }
}
