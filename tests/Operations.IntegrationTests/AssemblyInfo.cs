using Xunit;

// Each integration test class boots its own SQL Server container, so keep classes from running in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
