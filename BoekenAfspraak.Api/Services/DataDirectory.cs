namespace BoekenAfspraak.Api.Services;

// Wraps the DATA_DIR path (see Program.cs) so it can be injected via DI
// instead of recomputed from environment/config in every consumer.
public record DataDirectory(string Path);
