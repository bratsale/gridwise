using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Security;
using GridWise.Batch.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Api;

public static class JobEndpoints
{
    public const long MaxBodyBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/jobs", CreateJob)
            .WithName("CreateJob")
            .WithTags("Jobs")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<JobCreatedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();

        app.MapGet("/jobs/{id:guid}", GetJob)
            .WithName("GetJob")
            .WithTags("Jobs")
            .Produces<JobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/jobs", ListJobs)
            .WithName("ListJobs")
            .WithTags("Jobs")
            .Produces<List<JobResponse>>();

        app.MapJobActionEndpoints();

        return app;
    }

    private static async Task<IResult> CreateJob(HttpContext http, BatchDbContext db, TimeProvider time, CancellationToken ct)
    {
        var request = http.Request;

        // Rejects an oversized body before it is read (413). Content-Length is checked too, because not every server enforces the feature.
        var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
            limit.MaxRequestBodySize = MaxBodyBytes;
        if (request.ContentLength > MaxBodyBytes)
            return Rejected(StatusCodes.Status413PayloadTooLarge, "The request body is larger than 10 MB.");

        if (!request.HasFormContentType)
            return Rejected(StatusCodes.Status400BadRequest, "Send the CSV as multipart/form-data in the field \"file\".");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null)
            return Rejected(StatusCodes.Status400BadRequest, "The field \"file\" is missing.");
        if (file.Length == 0)
            return Rejected(StatusCodes.Status400BadRequest, "The file is empty.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();

        if (CsvInspector.CountRows(content, out var error) is not { } totalRows)
            return Rejected(StatusCodes.Status400BadRequest, error!);

        var job = new Job
        {
            Id = Guid.CreateVersion7(),
            CreatedBy = http.User.UserId(),
            Status = JobStatus.Pending,
            FileName = Path.GetFileName(file.FileName) is { Length: > 0 } name ? name[..Math.Min(name.Length, 255)] : "upload.csv",
            TotalRows = totalRows,
            CreatedAt = time.GetUtcNow()
        };
        db.Jobs.Add(job);
        db.JobFiles.Add(new JobFile { JobId = job.Id, Content = content });
        await db.SaveChangesAsync(ct); // one transaction: the job and its file

        return Results.Accepted($"/jobs/{job.Id}", new JobCreatedResponse(job.Id, job.Status.ToString(), job.TotalRows));
    }

    private static async Task<IResult> GetJob(Guid id, BatchDbContext db, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found.");

        var counts = await ChunkCountsAsync(db, [id], ct);
        return Results.Ok(ToResponse(job, counts));
    }

    private static async Task<IResult> ListJobs(BatchDbContext db, CancellationToken ct)
    {
        // Ids are UUID v7, so they break ties between jobs created in the same instant.
        var jobs = await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Take(50)
            .ToListAsync(ct);

        var counts = await ChunkCountsAsync(db, jobs.Select(j => j.Id).ToList(), ct);
        return Results.Ok(jobs.Select(j => ToResponse(j, counts)));
    }

    internal static async Task<Dictionary<Guid, (int Total, int Completed)>> ChunkCountsAsync(
        BatchDbContext db, List<Guid> jobIds, CancellationToken ct)
    {
        var rows = await db.Chunks.AsNoTracking()
            .Where(c => jobIds.Contains(c.JobId))
            .GroupBy(c => c.JobId)
            .Select(g => new { JobId = g.Key, Total = g.Count(), Completed = g.Count(c => c.Status == ChunkStatus.Completed) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.JobId, r => (r.Total, r.Completed));
    }

    internal static JobResponse ToResponse(Job job, Dictionary<Guid, (int Total, int Completed)> counts)
        => counts.TryGetValue(job.Id, out var c) ? JobResponse.From(job, c.Total, c.Completed) : JobResponse.From(job, 0, 0);

    private static IResult Rejected(int status, string title) => Results.Problem(statusCode: status, title: title);
}
