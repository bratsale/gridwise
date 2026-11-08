namespace GridWise.Batch.Events;

public static class EventTypes
{
    public const string BatchChunkRequested = "BatchChunkRequested";
}

public static class Topics
{
    public const string BatchChunks = "batch.chunks";
    public const string BatchResults = "batch.results";
}
