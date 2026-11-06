using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class RetentionCleanupTests(RecommendationFactory factory)
{
    private static OutboxMessage NewOutbox(DateTimeOffset occurredAt, DateTimeOffset? publishedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = "recommendation",
        AggregateId = Guid.NewGuid().ToString(),
        EventType = "RecommendationCalculated",
        EventVersion = 1,
        Payload = "{}",
        OccurredAt = occurredAt,
        PublishedAt = publishedAt
    };

    [Fact]
    public async Task Only_outbox_rows_published_more_than_7_days_ago_are_deleted()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var oldPublished = NewOutbox(now.AddDays(-9), now.AddDays(-8));
        var recentPublished = NewOutbox(now.AddDays(-2), now.AddDays(-1));
        var oldUnpublished = NewOutbox(now.AddDays(-30), null);
        db.OutboxMessages.AddRange(oldPublished, recentPublished, oldUnpublished);
        await db.SaveChangesAsync();

        var deleted = await RetentionCleanup.DeletePublishedOutboxAsync(db, now, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await db.OutboxMessages.CountAsync(m => m.Id == oldPublished.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Id == recentPublished.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Id == oldUnpublished.Id));
    }
}
