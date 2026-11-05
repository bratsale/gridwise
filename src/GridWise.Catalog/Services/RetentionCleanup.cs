using GridWise.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Catalog.Services;

public class RetentionCleanup(
    IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<RetentionCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan OutboxRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

                var now = time.GetUtcNow();

                var deletedKeys = await DeleteExpiredKeysAsync(db, now, stoppingToken);
                if (deletedKeys > 0)
                    logger.LogInformation("Deleted {Count} expired idempotency keys.", deletedKeys);

                var deletedOutbox = await DeletePublishedOutboxAsync(db, now, stoppingToken);
                if (deletedOutbox > 0)
                    logger.LogInformation("Deleted {Count} published outbox messages.", deletedOutbox);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Retention cleanup failed.");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    public static Task<int> DeleteExpiredKeysAsync(CatalogDbContext db, DateTimeOffset now, CancellationToken ct)
        => db.IdempotencyKeys.Where(k => k.CreatedAt < now - Retention).ExecuteDeleteAsync(ct);

    public static Task<int> DeletePublishedOutboxAsync(CatalogDbContext db, DateTimeOffset now, CancellationToken ct)
        => db.OutboxMessages
            .Where(m => m.PublishedAt != null && m.PublishedAt < now - OutboxRetention)
            .ExecuteDeleteAsync(ct);
}
