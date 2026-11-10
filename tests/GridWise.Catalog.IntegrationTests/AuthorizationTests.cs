using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class AuthorizationTests(CatalogFactory factory)
{
    private static object NewPlan(string name) => new
    {
        name, discount = 0.05m, tiers = new[] { new { threshold = (decimal?)null, pricePerKwh = 0.06m } }
    };

    private static HttpRequestMessage CreatePlanRequest(string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans") { Content = JsonContent.Create(NewPlan(name)) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await factory.CreateClient().Anonymous().GetAsync("/plans");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData("/plans")]
    [InlineData("/tax-groups")]
    [InlineData("/tax-groups/household/versions")]
    public async Task A_customer_can_read(string path)
    {
        var response = await factory.CreateClient().As(TestAuth.Customer, "customer-1").GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_cannot_change_plans_or_tax_groups_403()
    {
        var customer = factory.CreateClient().As(TestAuth.Customer, "customer-1");

        var createPlan = await customer.SendAsync(CreatePlanRequest("Not allowed"));
        var newVersion = await customer.PostAsJsonAsync($"/plans/{Guid.NewGuid()}/versions", NewPlan("x"));
        var retirePlan = await customer.DeleteAsync($"/plans/{Guid.NewGuid()}");
        var createTaxGroup = await customer.PostAsJsonAsync("/tax-groups", new { code = "nope", vat = 0.2m, ecoTax = 0.01m });
        var retireTaxGroup = await customer.DeleteAsync("/tax-groups/household");

        Assert.All([createPlan, newVersion, retirePlan, createTaxGroup, retireTaxGroup],
            response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        var problem = await createPlan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
        Assert.Equal("application/problem+json", createPlan.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_plan_version_and_its_event_record_the_admin_who_made_it()
    {
        var admin = factory.CreateClient().As(TestAuth.Admin, "admin-audit-1");

        var response = await admin.SendAsync(CreatePlanRequest("Audited"));
        var planId = (await response.Content.ReadFromJsonAsync<PlanResponse>())!.Id;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var version = await db.PlanVersions.AsNoTracking().SingleAsync(v => v.PlanId == planId);
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == planId.ToString());
        Assert.Equal("admin-audit-1", version.ChangedBy);
        Assert.Equal("admin-audit-1", JsonDocument.Parse(outbox.Payload).RootElement.GetProperty("changedBy").GetString());
    }

    [Fact]
    public async Task The_tax_group_version_and_its_event_record_the_admin_who_made_it()
    {
        var code = $"audit-{Guid.NewGuid():N}"[..16];
        var admin = factory.CreateClient().As(TestAuth.Admin, "admin-audit-2");

        var response = await admin.PostAsJsonAsync("/tax-groups", new { code, vat = 0.2m, ecoTax = 0.01m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var version = await db.TaxGroupVersions.AsNoTracking().SingleAsync(v => v.Code == code);
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == code);
        Assert.Equal("admin-audit-2", version.ChangedBy);
        Assert.Equal("admin-audit-2", JsonDocument.Parse(outbox.Payload).RootElement.GetProperty("changedBy").GetString());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/openapi/v1.json")]
    public async Task Health_and_openapi_need_no_token(string path)
    {
        var response = await factory.CreateClient().Anonymous().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
