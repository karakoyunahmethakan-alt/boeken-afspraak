// The whole suite shares one CustomWebApplicationFactory (one real app host,
// one temp SQLite db) and relies on process-wide environment variables
// (DATA_DIR, ADMIN_EMAIL, ADMIN_PASSWORD — see CustomWebApplicationFactory)
// being set before that single host builds. Running test collections in
// parallel would let a second factory's env vars race with this one's, so
// parallelization is disabled for this assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
