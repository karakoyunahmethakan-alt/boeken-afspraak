using Microsoft.EntityFrameworkCore;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentBook> AppointmentBooks => Set<AppointmentBook>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The actual lock: two requests can race to insert the same
        // (Date, TimeSlot), but only one INSERT can win while Status = 0
        // (Confirmed). SQLite rejects the second with a unique-constraint
        // error, which the endpoint turns into a clean "slot just taken"
        // response. Because the index is filtered to Confirmed rows,
        // cancelling a row (Status = 1) frees the slot immediately without
        // deleting any history.
        modelBuilder.Entity<Appointment>()
            .HasIndex(a => new { a.Date, a.TimeSlot })
            .IsUnique()
            .HasFilter("\"Status\" = 0");

        modelBuilder.Entity<AppointmentBook>()
            .HasOne(b => b.Appointment)
            .WithMany(a => a.Books)
            .HasForeignKey(b => b.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AdminUser>()
            .HasIndex(u => u.Email)
            .IsUnique();
    }
}
