namespace GridWise.Batch.Events;

public static class EventTypes
{
    public const string BatchChunkRequested = "BatchChunkRequested";
    public const string BatchChunkCompleted = "BatchChunkCompleted";
}

public static class Topics
{
    public const string BatchChunks = "batch.chunks";
    public const string BatchResults = "batch.results";
    public const string BatchDlq = "batch.dlq";
}
