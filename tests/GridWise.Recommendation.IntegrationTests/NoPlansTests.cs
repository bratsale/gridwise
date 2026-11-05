using System.Net;
using System.Net.Http.Json;
using GridWise.Recommendation.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

// Own collection, so own factory and an empty database: no plan copies exist here.
[CollectionDefinition("recommendation-empty")]
public class EmptyDatabaseCollection : ICollectionFixture<RecommendationFactory>;

[Collection("recommendation-empty")]
public class NoPlansTests(RecommendationFactory factory)
{
    [Fact]
    public async Task Without_active_plans_the_service_is_unavailable()
    {
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
                VALUES ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', {DateTimeOffset.UtcNow})
                """);
        }

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/recommendations", new { consumptionKwh = 250m, taxGroup = "household" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("no plans available yet", problem!.Title);
    }
}
