using System.Net;

namespace GridWise.Gateway.IntegrationTests;

[Collection("gateway")]
public class HealthTests(GatewayFixture gateway)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_healthy(string path)
    {
        var response = await gateway.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_path_outside_the_api_is_not_found()
    {
        var response = await gateway.CreateClient().GetAsync("/plans");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
