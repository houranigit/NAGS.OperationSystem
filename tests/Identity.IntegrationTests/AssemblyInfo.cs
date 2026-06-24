using Xunit;

// Each test class boots its own SQL Server Testcontainer. Running collections sequentially keeps
// only one container alive at a time, avoiding memory pressure that crashes concurrent containers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
