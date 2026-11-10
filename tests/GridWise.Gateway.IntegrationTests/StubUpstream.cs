using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Gateway.IntegrationTests;

/// <summary>What a stub service saw, echoed back as the response body.</summary>
public record Echo(string Upstream, string Method, string Path, string Query, int BodyLength, string Body, string? Authorization);

/// <summary>
/// A stand-in for one service behind the gateway: a real Kestrel server on a free port that answers every request with an
/// <see cref="Echo"/>. GET /jobs/{id}/events answers as a server-sent events stream: one event at once, the second only after
/// <see cref="ReleaseStream"/>, so a test can see whether the first event got through while the upstream is still writing.
/// </summary>
public sealed class StubUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StubUpstream(WebApplication app) => _app = app;

    public string Address { get; private set; } = "";

    /// <summary>Every request this stub received, in order (the streaming one included).</summary>
    public ConcurrentQueue<Echo> Received { get; } = new();

    public void ReleaseStream() => _release.TrySetResult();

    public static async Task<StubUpstream> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
        var app = builder.Build();
        var stub = new StubUpstream(app);

        app.MapGet("/jobs/{id}/events", async (HttpContext http) =>
        {
            stub.Received.Enqueue(new Echo(name, http.Request.Method, http.Request.Path, http.Request.QueryString.Value ?? "", 0, "",
                http.Request.Headers.Authorization.ToString()));
            http.Response.ContentType = "text/event-stream";
            await http.Response.WriteAsync("data: first\n\n");
            await http.Response.Body.FlushAsync();
            await stub._release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await http.Response.WriteAsync("data: second\n\n");
        });

        app.Map("{**path}", async (HttpContext http) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync();
            var echo = new Echo(name, http.Request.Method, http.Request.Path, http.Request.QueryString.Value ?? "",
                body.Length, body.Length <= 10_000 ? body : "", http.Request.Headers.Authorization.ToString());
            stub.Received.Enqueue(echo);
            await http.Response.WriteAsJsonAsync(echo);
        });

        await app.StartAsync();
        stub.Address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/";
        return stub;
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseStream();
        await _app.DisposeAsync();
    }
}
