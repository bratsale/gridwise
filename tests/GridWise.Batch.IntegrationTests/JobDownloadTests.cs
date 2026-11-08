using System.Net;
using System.Net.Http.Json;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class JobDownloadTests(BatchFactory factory)
{
    private readonly ResultTestSupport _t = new(factory);

    private Task<HttpResponseMessage> Get(Guid jobId, string file) => factory.CreateClient().GetAsync($"/jobs/{jobId}/{file}");

    [Fact]
    public async Task Results_are_ordered_by_line_with_the_header_and_a_download_name()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        // The second chunk arrives first, so the order has to come from the query, not from the insert order.
        await _t.Handle(ResultTestSupport.Completed(jobId, 2, 2, 0, firstLine: 502));
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 2, 1, firstLine: 2));
        await _t.Handle(ResultTestSupport.Completed(jobId, 3, 1, 0, firstLine: 1002));

        var response = await Get(jobId, "results.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"results-{jobId}.csv", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(
            "customer_id,plan_name,plan_version,total\r\n" +
            "c2,Standard,1,24.10\r\nc3,Standard,1,24.10\r\n" +
            "c502,Standard,1,24.10\r\nc503,Standard,1,24.10\r\n" +
            "c1002,Standard,1,24.10\r\n",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fields_with_commas_quotes_and_line_breaks_are_escaped()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 2, 0, planName: "Gold, \"Plus\"\nEdition"));

        var csv = await (await Get(jobId, "results.csv")).Content.ReadAsStringAsync();

        Assert.Contains("c2,\"Gold, \"\"Plus\"\"\nEdition\",1,24.10\r\n", csv);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "\"'\rx\"")] // the carriage return also makes the cell quoted
    [InlineData("=1,2", "\"'=1,2\"")] // the prefix and the quoting work together
    [InlineData("Plain = fine", "Plain = fine")] // only the first character counts
    public async Task Text_cells_that_look_like_formulas_get_a_leading_quote_but_numbers_do_not(string planName, string expectedCell)
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 1, 0, planName: planName));

        var csv = await (await Get(jobId, "results.csv")).Content.ReadAsStringAsync();

        // plan_version and total are written by the service and stay as they are.
        Assert.Contains($"c2,{expectedCell},1,24.10\r\n", csv);
    }

    [Fact]
    public async Task Customer_id_and_error_text_are_protected_too_and_line_numbers_are_not()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.Handle($$$"""
            {"id":"{{{Guid.NewGuid()}}}","type":"BatchChunkCompleted","version":1,"timestamp":"2026-11-03T10:00:00Z","correlationId":null,
             "data":{"jobId":"{{{jobId}}}","chunkNo":1,
               "results":[{"line":2,"customerId":"=HYPERLINK(\"http://x\")","planId":"5eed0000-0000-0000-0000-000000000001","planVersionNo":1,"planName":"Standard","total":-5.00,"taxGroupVersionNo":1}],
               "errors":[{"line":3,"error":"@bad"}]}}
            """);

        var results = await (await Get(jobId, "results.csv")).Content.ReadAsStringAsync();
        var errors = await (await Get(jobId, "errors.csv")).Content.ReadAsStringAsync();

        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\",Standard,1,-5.00\r\n", results); // the negative total is not prefixed
        Assert.Contains("\r\n3,'@bad\r\n", errors);
    }

    [Fact]
    public async Task Errors_are_ordered_by_line_and_escaped()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        await _t.Handle(ResultTestSupport.Completed(jobId, 2, 498, 2, firstLine: 502));
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 499, 1, firstLine: 2));
        await _t.Handle(ResultTestSupport.Completed(jobId, 3, 200, 0, firstLine: 1002));

        var response = await Get(jobId, "errors.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"errors-{jobId}.csv", response.Content.Headers.ContentDisposition?.FileName);
        // The error text has no comma here; the escaping of quoted text is checked through the results test above.
        Assert.Equal(
            "line,error\r\n" +
            "501,consumption_kwh 'abc' is not a number.\r\n" +
            "1000,consumption_kwh 'abc' is not a number.\r\n" +
            "1001,consumption_kwh 'abc' is not a number.\r\n",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cancelled_job_has_partial_results()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 500, 0, firstLine: 2));
        await factory.CreateClient().PostAsync($"/jobs/{jobId}/cancel", null);

        var results = await Get(jobId, "results.csv");
        var errors = await Get(jobId, "errors.csv");

        Assert.Equal(HttpStatusCode.OK, results.StatusCode);
        var lines = (await results.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(501, lines.Length); // header + the 500 rows of the one chunk that finished
        Assert.Equal(HttpStatusCode.OK, errors.StatusCode);
        Assert.Equal("line,error\r\n", await errors.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Downloads_are_a_conflict_while_the_job_is_pending_or_running()
    {
        var running = await _t.UploadAndDispatch(1200);
        var form = new MultipartFormDataContent { { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(ResultTestSupport.Csv(3))), "file", "c.csv" } };
        var pending = (await (await factory.CreateClient().PostAsync("/jobs", form)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();

        foreach (var jobId in new[] { running, pending })
            foreach (var file in new[] { "results.csv", "errors.csv" })
                Assert.Equal(HttpStatusCode.Conflict, (await Get(jobId, file)).StatusCode);
    }

    [Theory]
    [InlineData("results.csv")]
    [InlineData("errors.csv")]
    public async Task Unknown_job_is_404(string file)
        => Assert.Equal(HttpStatusCode.NotFound, (await Get(Guid.NewGuid(), file)).StatusCode);
}
