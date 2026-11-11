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

    // Every rule an upload can break is answered with 400 and a title that names the reason (the web app translates these).
    [Theory]
    [InlineData("no file field", null, "missing")]
    [InlineData("an empty file", null, "empty")]
    [InlineData("only the header", null, "no data rows")]
    [InlineData("a header with a column missing", "customer_id,consumption_kwh", "header")]
    [InlineData("a header in another order", "customer_id,tax_group,consumption_kwh", "header")]
    [InlineData("a header with another column name", "id,consumption_kwh,tax_group", "header")]
    [InlineData("a header with an extra column", "customer_id,consumption_kwh,tax_group,extra", "header")]
    [InlineData("more than 100,000 rows", null, "100000")]
    public async Task An_upload_that_breaks_a_rule_is_rejected_with_the_reason(string upload, string? header, string reason)
    {
        var content = upload switch
        {
            "no file field" => Form((byte[]?)null),
            "an empty file" => Form([]),
            "only the header" => Form(Csv(0)),
            "more than 100,000 rows" => Form(Csv(100_001)),
            _ => Form(Csv(2, header!)),
        };

        var response = await Upload(content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await ProblemTitle(response));
    }

    [Fact]
    public async Task Request_that_is_not_multipart_is_rejected()
    {
        var response = await Upload(new StringContent(Csv(1), Encoding.UTF8, "text/csv"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
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
    public async Task Line_of_200_characters_is_accepted_and_201_is_rejected_with_its_line_number()
    {
        var ok = Header + "\n" + new string('a', 200) + "\n";
        Assert.Equal(HttpStatusCode.Accepted, (await Upload(Form(ok))).StatusCode);

        // Line 1 header, 2 row, 3 blank, 4 too long.
        var tooLong = Header + "\nc1,1,a\n\n" + new string('a', 201) + "\nc2,2,a\n";
        var response = await Upload(Form(tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Line 4 is longer than 200 characters.", await ProblemTitle(response));
    }

    [Fact]
    public async Task Body_over_10_MB_is_rejected_with_413()
    {
        var response = await Upload(Form(new byte[10 * 1024 * 1024 + 1]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // Every endpoint of a job answers 404 for an id that does not exist (not 500, not an empty stream or file).
    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/events")]
    [InlineData("GET", "/results.csv")]
    [InlineData("GET", "/errors.csv")]
    [InlineData("POST", "/cancel")]
    public async Task An_unknown_job_is_404_on_every_job_endpoint(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/jobs/{Guid.NewGuid()}{path}"));

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
