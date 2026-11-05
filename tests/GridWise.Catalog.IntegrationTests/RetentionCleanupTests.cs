using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using GridWise.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class RetentionCleanupTests(CatalogFactory factory)
{
    private static IdempotencyKey NewKey(DateTimeOffset createdAt) => new()
    {
        Key = Guid.NewGuid().ToString("N"),
        Endpoint = "POST /plans",
        RequestHash = "hash",
        ResponseStatus = 201,
        ResponseBody = "{}",
        CreatedAt = createdAt
    };

    [Fact]
    public async Task Only_keys_older_than_24_hours_are_deleted()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var old = NewKey(now.AddHours(-25));
        var recent = NewKey(now.AddHours(-1));
        db.IdempotencyKeys.AddRange(old, recent);
        await db.SaveChangesAsync();

        var deleted = await RetentionCleanup.DeleteExpiredKeysAsync(db, now, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await db.IdempotencyKeys.CountAsync(k => k.Key == old.Key));
        Assert.Equal(1, await db.IdempotencyKeys.CountAsync(k => k.Key == recent.Key));
    }

    private static OutboxMessage NewOutbox(DateTimeOffset occurredAt, DateTimeOffset? publishedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = "plan",
        AggregateId = Guid.NewGuid().ToString(),
        EventType = "PlanVersionCreated",
        EventVersion = 1,
        Payload = "{}",
        OccurredAt = occurredAt,
        PublishedAt = publishedAt
    };

    [Fact]
    public async Task Only_outbox_rows_published_more_than_7_days_ago_are_deleted()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
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
