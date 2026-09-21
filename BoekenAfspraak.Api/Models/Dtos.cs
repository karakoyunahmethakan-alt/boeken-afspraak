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
    string TimeSlot,          // "18:24"
    List<string>? Isbns       // null/empty => flat per-10-books pricing
);

public record BookPriceDto(string Isbn, decimal? BolSecondHandPrice, decimal EstimatedOffer);

public record AppointmentResultDto(
    Guid ManageToken,
    string Date,
    string TimeSlot,
    decimal EstimatedPriceEuro,
    bool PricedByIsbn,
    List<BookPriceDto> BookPrices
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
    bool PricedByIsbn,
    string? OriginalDate,
    string? OriginalTimeSlot,
    int RescheduleCount,
    DateTime CreatedAtUtc
);
