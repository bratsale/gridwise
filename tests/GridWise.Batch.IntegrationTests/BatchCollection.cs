namespace GridWise.Batch.IntegrationTests;

[CollectionDefinition("batch")]
public class BatchCollection : ICollectionFixture<BatchFactory>;

// A second collection means a second database, so the dispatcher tests do not see the jobs of the upload tests.
[CollectionDefinition("batch-dispatch")]
public class DispatchCollection : ICollectionFixture<BatchFactory>;
