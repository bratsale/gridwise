using System.Net;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch")]
public class HealthTests(BatchFactory factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_return_200(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
