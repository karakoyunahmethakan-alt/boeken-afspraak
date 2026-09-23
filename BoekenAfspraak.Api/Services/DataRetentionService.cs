using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BoekenAfspraak.Api.Data;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Api.Services;

// AVG/GDPR: verwijdert automatisch afspraken die klaar zijn met hun
// bewaartermijn (verlopen datum of oude annulering), inclusief de
// bijbehorende geuploade foto's op schijf. Draait direct bij opstarten
// en daarna elke 24 uur.
public class DataRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AppOptions> _options;
    private readonly DataDirectory _dataDirectory;
    private readonly ILogger<DataRetentionService> _logger;

    public DataRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<AppOptions> options,
        DataDirectory dataDirectory,
        ILogger<DataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _dataDirectory = dataDirectory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention run mislukt.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Shutdown requested during the delay — loop condition exits next.
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var retentionDays = _options.Value.DataRetentionDays;
        var utcNow = DateTime.UtcNow;
        var cancelledCutoff = utcNow.AddDays(-retentionDays);
        var dateCutoff = DateOnly.FromDateTime(utcNow).AddDays(-retentionDays);

        var expired = await db.Appointments
            .Where(a =>
                (a.Status == AppointmentStatus.Cancelled && a.CancelledAtUtc != null && a.CancelledAtUtc < cancelledCutoff)
                || a.Date < dateCutoff)
            .ToListAsync(stoppingToken);

        foreach (var appointment in expired)
            DeletePhotos(appointment);

        if (expired.Count > 0)
        {
            db.Appointments.RemoveRange(expired);
            await db.SaveChangesAsync(stoppingToken);
        }

        _logger.LogInformation("Data retention: {Count} afspraken verwijderd.", expired.Count);
    }

    // Photos for an appointment live under uploads/{ManageToken}/ (see
    // Program.cs's photo upload endpoint) and nothing else is ever written
    // there, so removing the whole folder removes exactly the files listed
    // in PhotoFileNames.
    private void DeletePhotos(Appointment appointment)
    {
        var folder = Path.Combine(_dataDirectory.Path, "uploads", appointment.ManageToken.ToString());
        if (!Directory.Exists(folder)) return;

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kon uploadmap voor afspraak {Id} niet volledig verwijderen.", appointment.Id);
        }
    }
}
