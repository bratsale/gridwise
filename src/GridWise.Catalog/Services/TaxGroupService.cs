using System.Text.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using GridWise.Catalog.Events;
using GridWise.Catalog.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridWise.Catalog.Services;

public abstract record TaxGroupResult
{
    public sealed record Created(TaxGroupResponse TaxGroup) : TaxGroupResult;
    public sealed record NotFound : TaxGroupResult;
    public sealed record Conflict(string Reason) : TaxGroupResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : TaxGroupResult;
}

public class TaxGroupService(CatalogDbContext db, TimeProvider time)
{
    private const string AlreadyExists = "tax group already exists";
    private const string VersionMismatch = "version mismatch";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TaxGroupResult> CreateAsync(
        CreateTaxGroupRequest req, string correlationId, CancellationToken ct)
    {
        if (await db.TaxGroups.AnyAsync(g => g.Code == req.Code, ct))
            return new TaxGroupResult.Conflict(AlreadyExists);

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var effectiveFrom = req.EffectiveFrom ?? today;

        var errors = CatalogRules.ValidateEffectiveFrom(effectiveFrom, today, previousEffectiveFrom: null);
        if (errors.Count > 0)
            return new TaxGroupResult.ValidationFailed(errors);

        db.TaxGroups.Add(new TaxGroup { Code = req.Code, CreatedAt = now });
        var response = AddVersion(
            req.Code, 1, req.Vat, req.EcoTax, VersionStatus.Active, effectiveFrom, correlationId, now);

        return await SaveAsync(response, "tax_groups", AlreadyExists, ct);
    }

    public async Task<TaxGroupResult> AddVersionAsync(
        string code, int expectedVersion, TaxGroupVersionRequest req, string correlationId, CancellationToken ct)
    {
        var (latest, failure) = await LoadLatestAsync(code, expectedVersion, ct);
        if (failure is not null)
            return failure;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var effectiveFrom = req.EffectiveFrom ?? today;

        var errors = CatalogRules.ValidateEffectiveFrom(effectiveFrom, today, latest!.EffectiveFrom);
        if (errors.Count > 0)
            return new TaxGroupResult.ValidationFailed(errors);

        var response = AddVersion(
            code, latest.VersionNo + 1, req.Vat, req.EcoTax, VersionStatus.Active, effectiveFrom, correlationId, now);

        return await SaveAsync(response, "tax_group_versions", VersionMismatch, ct);
    }

    public async Task<TaxGroupResult> RetireAsync(
        string code, int expectedVersion, DateOnly? effectiveFrom, string correlationId, CancellationToken ct)
    {
        var (latest, failure) = await LoadLatestAsync(code, expectedVersion, ct);
        if (failure is not null)
            return failure;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var retiredFrom = effectiveFrom ?? today;

        var errors = CatalogRules.ValidateEffectiveFrom(retiredFrom, today, latest!.EffectiveFrom);
        if (errors.Count > 0)
            return new TaxGroupResult.ValidationFailed(errors);

        var response = AddVersion(
            code, latest.VersionNo + 1, latest.Vat, latest.EcoTax, VersionStatus.Retired, retiredFrom, correlationId, now);

        return await SaveAsync(response, "tax_group_versions", VersionMismatch, ct);
    }

    public async Task<List<TaxGroupResponse>> ListAsync(bool includeRetired, CancellationToken ct)
    {
        var latest = await db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.VersionNo == db.TaxGroupVersions.Where(x => x.Code == v.Code).Max(x => x.VersionNo))
            .Where(v => includeRetired || v.Status != VersionStatus.Retired)
            .OrderBy(v => v.Code)
            .ToListAsync(ct);

        return latest.Select(ToResponse).ToList();
    }

    public async Task<TaxGroupResponse?> GetLatestAsync(string code, CancellationToken ct)
    {
        var latest = await db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.Code == code)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        return latest is null ? null : ToResponse(latest);
    }

    public async Task<List<TaxGroupResponse>?> ListVersionsAsync(string code, CancellationToken ct)
    {
        var versions = await db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.Code == code)
            .OrderBy(v => v.VersionNo)
            .ToListAsync(ct);

        return versions.Count == 0 ? null : versions.Select(ToResponse).ToList();
    }

    public async Task<TaxGroupResponse?> GetVersionAsync(string code, int versionNo, CancellationToken ct)
    {
        var version = await db.TaxGroupVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Code == code && v.VersionNo == versionNo, ct);

        return version is null ? null : ToResponse(version);
    }

    private TaxGroupResponse AddVersion(
        string code, int versionNo, decimal vat, decimal ecoTax, VersionStatus status,
        DateOnly effectiveFrom, string correlationId, DateTimeOffset now)
    {
        var response = new TaxGroupResponse(code, versionNo, vat, ecoTax, status.ToString(), effectiveFrom);

        db.TaxGroupVersions.Add(new TaxGroupVersion
        {
            Code = code,
            VersionNo = versionNo,
            Vat = vat,
            EcoTax = ecoTax,
            Status = status,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "tax-group",
            AggregateId = code,
            EventType = EventTypes.TaxGroupVersionCreated,
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(
                new TaxGroupVersionCreatedV1(code, versionNo, vat, ecoTax, status.ToString(), effectiveFrom),
                Json),
            CorrelationId = correlationId,
            TraceParent = Tracing.CurrentTraceParent(),
            OccurredAt = now
        });

        return response;
    }

    private async Task<(TaxGroupVersion? Latest, TaxGroupResult? Failure)> LoadLatestAsync(
        string code, int expectedVersion, CancellationToken ct)
    {
        var latest = await db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.Code == code)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
            return (null, new TaxGroupResult.NotFound());
        if (latest.Status == VersionStatus.Retired)
            return (null, new TaxGroupResult.Conflict("tax group is retired"));
        if (latest.VersionNo != expectedVersion)
            return (null, new TaxGroupResult.Conflict(VersionMismatch));

        return (latest, null);
    }

    private async Task<TaxGroupResult> SaveAsync(
        TaxGroupResponse response, string conflictTable, string conflictReason, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } pg && pg.TableName == conflictTable)
        {
            return new TaxGroupResult.Conflict(conflictReason);
        }

        return new TaxGroupResult.Created(response);
    }

    private static TaxGroupResponse ToResponse(TaxGroupVersion v)
        => new(v.Code, v.VersionNo, v.Vat, v.EcoTax, v.Status.ToString(), v.EffectiveFrom);
}
