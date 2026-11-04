using GridWise.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Catalog.Services;

public class IdempotencyKeyCleanup(
    IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<IdempotencyKeyCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

                var deleted = await DeleteExpiredAsync(db, time.GetUtcNow(), stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} expired idempotency keys.", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Idempotency key cleanup failed.");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    public static Task<int> DeleteExpiredAsync(CatalogDbContext db, DateTimeOffset now, CancellationToken ct)
        => db.IdempotencyKeys.Where(k => k.CreatedAt < now - Retention).ExecuteDeleteAsync(ct);
}
