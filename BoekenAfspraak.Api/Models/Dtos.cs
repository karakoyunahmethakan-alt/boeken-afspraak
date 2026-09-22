namespace BoekenAfspraak.Api.Models;

// --- Booking flow ---

public record CreateAppointmentRequest(
    string Name,
    string Address,
    string Email,
    string? Phone,
    int BookCount,
    string? BookType,
    string Date,              // "yyyy-MM-dd"
    string TimeSlot           // "18:24"
);

public record AppointmentResultDto(
    Guid ManageToken,
    string Date,
    string TimeSlot,
    decimal EstimatedPriceEuro
);

public record RescheduleRequest(string NewDate, string NewTimeSlot);

// --- Admin ---

public record AdminLoginRequest(string Email, string Password);
public record AdminLoginResponse(string Token, DateTime ExpiresAtUtc);

public record AdminAppointmentDto(
    int Id,
    string Name,
    string Address,
    string Email,
    string? Phone,
    int BookCount,
    string? BookType,
    string Date,
    string TimeSlot,
    string Status,
    decimal EstimatedPriceEuro,
    string? OriginalDate,
    string? OriginalTimeSlot,
    int RescheduleCount,
    DateTime CreatedAtUtc
);
