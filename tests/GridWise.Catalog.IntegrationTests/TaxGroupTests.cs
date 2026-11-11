using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class TaxGroupTests(CatalogFactory factory)
{
    private static string NewCode() => "test-" + Guid.NewGuid().ToString("N")[..8];

    private static object Household(string code, decimal vat = 0.17m) => new { code, vat, ecoTax = 0.005m };

    private static object NextVersion() => new { vat = 0.20m, ecoTax = 0.010m };

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        return factory.CreateClient().SendAsync(request);
    }

    private Task<HttpResponseMessage> Create(string code, decimal vat = 0.17m)
        => Send(HttpMethod.Post, "/tax-groups", Household(code, vat));

    private async Task<string> CreateGroup()
    {
        var code = NewCode();
        Assert.Equal(HttpStatusCode.Created, (await Create(code)).StatusCode);
        return code;
    }

    private async Task<T> InDb<T>(Func<CatalogDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    }

    private Task<int> OutboxCount(string code) => InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId == code));

    [Fact]
    public async Task Create_returns_created_group()
    {
        var code = NewCode();

        var response = await Create(code);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
        Assert.Equal($"/tax-groups/{code}", response.Headers.Location?.ToString());
        Assert.Equal(1, await InDb(db => db.TaxGroups.CountAsync(g => g.Code == code)));
        Assert.Equal(1, await InDb(db => db.TaxGroupVersions.CountAsync(v => v.Code == code && v.VersionNo == 1)));
        Assert.Equal(1, await OutboxCount(code));
    }

    [Fact]
    public async Task Create_same_code_twice_conflicts()
    {
        var code = await CreateGroup();

        var second = await Create(code);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Parallel_creates_with_same_code_one_wins()
    {
        var code = NewCode();

        var responses = await Task.WhenAll(Create(code), Create(code));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await OutboxCount(code));
    }

    // A code must be lower case (letters, digits, dashes) and VAT a fraction from 0 to 1; the error names the field.
    // The key is compared ignoring case, like the web client does (data annotations report "Code" and "Vat").
    [Theory]
    [InlineData("an upper-case code", "Household", "0.17", "code")]
    [InlineData("VAT above 100 %", null, "1.5", "vat")]
    public async Task Invalid_tax_group_input_is_rejected_on_its_field(string input, string? code, string vat, string errorOn)
    {
        var response = await Create(code ?? NewCode(), decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(problem!.Errors.Keys.Any(k => k.Equals(errorOn, StringComparison.OrdinalIgnoreCase)),
            $"{input}: expected an error on '{errorOn}', got: {string.Join(", ", problem.Errors.Keys)}");
    }

    [Fact]
    public async Task New_version_is_created()
    {
        var code = await CreateGroup();

        var response = await Send(HttpMethod.Post, $"/tax-groups/{code}/versions", NextVersion(), "\"1\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
        Assert.Equal($"/tax-groups/{code}/versions/2", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task New_version_with_stale_if_match_conflicts()
    {
        var code = await CreateGroup();
        await Send(HttpMethod.Post, $"/tax-groups/{code}/versions", NextVersion(), "\"1\"");

        var response = await Send(HttpMethod.Post, $"/tax-groups/{code}/versions", NextVersion(), "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task New_version_without_if_match_is_rejected()
    {
        var code = await CreateGroup();

        var response = await Send(HttpMethod.Post, $"/tax-groups/{code}/versions", NextVersion());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task New_version_for_unknown_code_is_not_found()
    {
        var response = await Send(HttpMethod.Post, $"/tax-groups/{NewCode()}/versions", NextVersion(), "\"1\"");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retire_adds_a_retired_version_and_blocks_new_versions()
    {
        var code = await CreateGroup();

        var response = await Send(HttpMethod.Delete, $"/tax-groups/{code}", ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var versions = await InDb(db => db.TaxGroupVersions.Where(v => v.Code == code).OrderBy(v => v.VersionNo).ToListAsync());
        Assert.Equal(VersionStatus.Retired, versions[1].Status);
        Assert.Equal(versions[0].Vat, versions[1].Vat);
        Assert.Equal(versions[0].EcoTax, versions[1].EcoTax);

        var newVersion = await Send(HttpMethod.Post, $"/tax-groups/{code}/versions", NextVersion(), "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, newVersion.StatusCode);
    }

    [Fact]
    public async Task List_hides_retired_groups_unless_requested()
    {
        var active = await CreateGroup();
        var retired = await CreateGroup();
        await Send(HttpMethod.Delete, $"/tax-groups/{retired}", ifMatch: "\"1\"");

        var defaultList = await (await Send(HttpMethod.Get, "/tax-groups")).Content.ReadFromJsonAsync<List<TaxGroupResponse>>();
        var fullList = await (await Send(HttpMethod.Get, "/tax-groups?includeRetired=true")).Content.ReadFromJsonAsync<List<TaxGroupResponse>>();

        Assert.Contains(defaultList!, g => g.Code == active);
        Assert.DoesNotContain(defaultList!, g => g.Code == retired);
        var listed = Assert.Single(fullList!, g => g.Code == retired);
        Assert.Equal("Retired", listed.Status);
    }
}
