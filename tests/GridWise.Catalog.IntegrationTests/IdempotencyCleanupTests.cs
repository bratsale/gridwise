using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using GridWise.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class IdempotencyCleanupTests(CatalogFactory factory)
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

        var deleted = await IdempotencyKeyCleanup.DeleteExpiredAsync(db, now, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await db.IdempotencyKeys.CountAsync(k => k.Key == old.Key));
        Assert.Equal(1, await db.IdempotencyKeys.CountAsync(k => k.Key == recent.Key));
    }
}
