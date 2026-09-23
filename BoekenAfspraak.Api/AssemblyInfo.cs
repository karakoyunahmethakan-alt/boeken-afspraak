using System.Runtime.CompilerServices;

// Lets BoekenAfspraak.Tests call DataRetentionService.RunOnceAsync directly
// (internal) instead of waiting for its real 24h background loop.
[assembly: InternalsVisibleTo("BoekenAfspraak.Tests")]
