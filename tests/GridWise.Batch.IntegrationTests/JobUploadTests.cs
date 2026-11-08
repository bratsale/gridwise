using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch")]
public class JobUploadTests(BatchFactory factory)
{
    private const string Header = "customer_id,consumption_kwh,tax_group";

    private static string Csv(int rows, string header = Header, string newline = "\n")
        => header + newline + string.Concat(Enumerable.Range(1, rows).Select(i => $"c{i},250.5,HOUSEHOLD{newline}"));

    private static MultipartFormDataContent Form(byte[]? content, string field = "file", string fileName = "customers.csv")
    {
        var form = new MultipartFormDataContent();
        if (content is not null)
            form.Add(new ByteArrayContent(content), field, fileName);
        else
            form.Add(new StringContent("x"), "other");
        return form;
    }

    private static MultipartFormDataContent Form(string csv) => Form(Encoding.UTF8.GetBytes(csv));

    private Task<HttpResponseMessage> Upload(HttpContent content) => factory.CreateClient().PostAsync("/jobs", content);

    private static async Task<string?> ProblemTitle(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    [Fact]
    public async Task Valid_upload_returns_202_with_location_and_total_rows()
    {
        var response = await Upload(Form(Csv(3)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(3, body.GetProperty("totalRows").GetInt32());
        Assert.Equal($"/jobs/{id}", response.Headers.Location?.OriginalString);

        var job = await factory.CreateClient().GetFromJsonAsync<JsonElement>(response.Headers.Location);
        Assert.Equal(id, job.GetProperty("id").GetGuid());
        Assert.Equal("customers.csv", job.GetProperty("fileName").GetString());
        Assert.Equal(3, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("startedAt").ValueKind);
    }

    [Fact]
    public async Task Missing_file_is_rejected()
    {
        var response = await Upload(Form((byte[]?)null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("missing", await ProblemTitle(response));
    }

    [Fact]
    public async Task Request_that_is_not_multipart_is_rejected()
    {
        var response = await Upload(new StringContent(Csv(1), Encoding.UTF8, "text/csv"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        var response = await Upload(Form([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty", await ProblemTitle(response));
    }

    [Fact]
    public async Task Header_only_is_rejected()
    {
        var response = await Upload(Form(Csv(0)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no data rows", await ProblemTitle(response));
    }

    [Theory]
    [InlineData("customer_id,consumption_kwh")]
    [InlineData("customer_id,tax_group,consumption_kwh")]
    [InlineData("id,consumption_kwh,tax_group")]
    [InlineData("customer_id,consumption_kwh,tax_group,extra")]
    public async Task Wrong_header_is_rejected(string header)
    {
        var response = await Upload(Form(Csv(2, header)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("header", await ProblemTitle(response));
    }

    [Fact]
    public async Task Bom_crlf_and_loose_header_are_accepted()
    {
        var csv = Csv(2, " Customer_ID , CONSUMPTION_KWH,Tax_Group ", "\r\n");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        var response = await Upload(Form(bytes));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("totalRows").GetInt32());
    }

    [Fact]
    public async Task Exactly_100000_rows_are_accepted()
    {
        var response = await Upload(Form(Csv(100_000)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100_000, body.GetProperty("totalRows").GetInt32());
    }

    [Fact]
    public async Task More_than_100000_rows_are_rejected()
    {
        var response = await Upload(Form(Csv(100_001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("100000", await ProblemTitle(response));
    }

    [Fact]
    public async Task Body_over_10_MB_is_rejected_with_413()
    {
        var response = await Upload(Form(new byte[10 * 1024 * 1024 + 1]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_job_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_newest_first_and_at_most_50()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 52; i++)
        {
            factory.Time.Advance(TimeSpan.FromSeconds(1));
            var created = await (await Upload(Form(Csv(1)))).Content.ReadFromJsonAsync<JsonElement>();
            ids.Add(created.GetProperty("id").GetGuid());
        }

        var list = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/jobs");

        var listed = list.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(50, listed.Count);
        Assert.Equal(Enumerable.Reverse(ids).Take(50), listed);
    }
}
