namespace BoekenAfspraak.Api.Models;

public enum AppointmentStatus
{
    Confirmed = 0,
    Cancelled = 1
}

// One booked pickup appointment. The (Date, TimeSlot) pair is protected by a
// unique index that only applies to Confirmed rows (see AppDbContext) — that
// filtered index is the actual mechanism that stops two people grabbing the
// same slot and that frees a slot again the instant a row is cancelled.
public class Appointment
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }

    public int BookCount { get; set; }
    public string? BookType { get; set; }

    // Comma-separated list of uploaded photo file names (stored under
    // /data/uploads on the persistent volume). Null/empty if none uploaded.
    public string? PhotoFileNames { get; set; }

    // Pricing snapshot at time of booking — informational only, the real
    // price is agreed in person. Kept on the row so history/CSV export
    // reflects what the customer was shown, even if tiers change later.
    public bool PricedByIsbn { get; set; }
    public decimal EstimatedPriceEuro { get; set; }

    public DateOnly Date { get; set; }
    public string TimeSlot { get; set; } = ""; // e.g. "18:24"

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;

    // Secret, unguessable token that identifies this appointment in the
    // customer-facing manage/cancel/reschedule link. Never expose the
    // numeric Id in a public URL.
    public Guid ManageToken { get; set; } = Guid.NewGuid();

    // Light audit trail for reschedules (no separate history table needed
    // for a small personal project — CSV export still shows the original
    // slot via these two columns).
    public DateOnly? OriginalDate { get; set; }
    public string? OriginalTimeSlot { get; set; }
    public int RescheduleCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; set; }

    public ICollection<AppointmentBook> Books { get; set; } = new List<AppointmentBook>();
}

// One ISBN entered by the customer, with the Bol.com-derived estimate for
// that single book. Only populated when the customer chose the ISBN path.
public class AppointmentBook
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public string Isbn { get; set; } = "";
    public decimal? BolSecondHandPrice { get; set; } // null if lookup failed/not found
    public decimal EstimatedOffer { get; set; }
}
