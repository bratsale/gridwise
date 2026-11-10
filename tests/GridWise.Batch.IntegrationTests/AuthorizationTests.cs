using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GridWise.Batch.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch")]
public class AuthorizationTests(BatchFactory factory)
{
    private static MultipartFormDataContent Csv()
        => new() { { new ByteArrayContent(Encoding.UTF8.GetBytes("customer_id,consumption_kwh,tax_group\nc1,250,household\n")), "file", "c.csv" } };

    private async Task<Guid> Upload(HttpClient client)
    {
        var response = await client.PostAsync("/jobs", Csv());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await factory.CreateClient().Anonymous().GetAsync("/jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Every_endpoint_is_403_for_a_customer()
    {
        var jobId = await Upload(factory.CreateClient()); // as admin
        var customer = factory.CreateClient().As(TestAuth.Customer, "customer-1");

        var responses = new[]
        {
            await customer.PostAsync("/jobs", Csv()),
            await customer.GetAsync("/jobs"),
            await customer.GetAsync($"/jobs/{jobId}"),
            await customer.PostAsync($"/jobs/{jobId}/cancel", null),
            await customer.GetAsync($"/jobs/{jobId}/events"),
            await customer.GetAsync($"/jobs/{jobId}/results.csv"),
            await customer.GetAsync($"/jobs/{jobId}/errors.csv")
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal("application/problem+json", responses[0].Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_job_stores_the_admin_who_uploaded_it()
    {
        var jobId = await Upload(factory.CreateClient().As(TestAuth.Admin, "admin-uploader"));

        using var scope = factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<BatchDbContext>().Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal("admin-uploader", job.CreatedBy);
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
