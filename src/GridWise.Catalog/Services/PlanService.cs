using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using GridWise.Catalog.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridWise.Catalog.Services;

public abstract record CreatePlanResult
{
    public sealed record Created(PlanResponse Plan) : CreatePlanResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : CreatePlanResult;
    public sealed record KeyReusedWithDifferentBody : CreatePlanResult;
}

public abstract record ChangePlanResult
{
    public sealed record Created(PlanResponse Plan) : ChangePlanResult;
    public sealed record NotFound : ChangePlanResult;
    public sealed record Conflict(string Reason) : ChangePlanResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : ChangePlanResult;
}

public class PlanService(CatalogDbContext db, TimeProvider time)
{
    private const string Endpoint = "POST /plans";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CreatePlanResult> CreateAsync(
        CreatePlanRequest req, string idempotencyKey, string correlationId, CancellationToken ct)
    {
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req))));

        var existing = await FindKeyAsync(idempotencyKey, ct);
        if (existing is not null)
            return Replay(existing, requestHash);

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var effectiveFrom = req.EffectiveFrom ?? today;
        var tiers = req.Tiers ?? [];

        var draft = new PlanDraft(
            req.Name, req.Discount, effectiveFrom,
            tiers.Select(t => new TierDraft(t.Threshold, t.PricePerKwh)).ToList());

        var errors = CatalogRules.ValidatePlan(draft, today, previousEffectiveFrom: null);
        if (errors.Count > 0)
            return new CreatePlanResult.ValidationFailed(errors);

        var planId = Guid.CreateVersion7();

        db.Plans.Add(new Plan { Id = planId, CreatedAt = now });
        var response = AddVersion(
            planId, 1, req.Name, req.Discount, VersionStatus.Active, effectiveFrom, tiers, correlationId, now);
        var responseJson = JsonSerializer.Serialize(response, Json);

        db.IdempotencyKeys.Add(new IdempotencyKey
        {
            Key = idempotencyKey,
            Endpoint = Endpoint,
            RequestHash = requestHash,
            ResponseStatus = StatusCodes.Status201Created,
            ResponseBody = responseJson,
            CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: "idempotency_keys"
        })
        {
            db.ChangeTracker.Clear();
            return Replay(await db.IdempotencyKeys.AsNoTracking().FirstAsync(
                k => k.Endpoint == Endpoint && k.Key == idempotencyKey, ct), requestHash);
        }

        return new CreatePlanResult.Created(response);
    }

    public async Task<ChangePlanResult> AddVersionAsync(
        Guid planId, int expectedVersion, CreatePlanRequest req, string correlationId, CancellationToken ct)
    {
        var (latest, failure) = await LoadLatestAsync(planId, expectedVersion, ct);
        if (failure is not null)
            return failure;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var effectiveFrom = req.EffectiveFrom ?? today;
        var tiers = req.Tiers ?? [];

        var draft = new PlanDraft(
            req.Name, req.Discount, effectiveFrom,
            tiers.Select(t => new TierDraft(t.Threshold, t.PricePerKwh)).ToList());

        var errors = CatalogRules.ValidatePlan(draft, today, latest!.EffectiveFrom);
        if (errors.Count > 0)
            return new ChangePlanResult.ValidationFailed(errors);

        var response = AddVersion(
            planId, latest.VersionNo + 1, req.Name, req.Discount, VersionStatus.Active, effectiveFrom, tiers, correlationId, now);

        return await SaveAsync(response, ct);
    }

    public async Task<ChangePlanResult> RetireAsync(
        Guid planId, int expectedVersion, DateOnly? effectiveFrom, string correlationId, CancellationToken ct)
    {
        var (latest, failure) = await LoadLatestAsync(planId, expectedVersion, ct);
        if (failure is not null)
            return failure;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var retiredFrom = effectiveFrom ?? today;

        var errors = CatalogRules.ValidateEffectiveFrom(retiredFrom, today, latest!.EffectiveFrom);
        if (errors.Count > 0)
            return new ChangePlanResult.ValidationFailed(errors);

        var response = AddVersion(
            planId, latest.VersionNo + 1, latest.Name, latest.Discount, VersionStatus.Retired, retiredFrom, [], correlationId, now);

        return await SaveAsync(response, ct);
    }

    private PlanResponse AddVersion(
        Guid planId, int versionNo, string name, decimal discount, VersionStatus status,
        DateOnly effectiveFrom, List<TierRequest> tiers, string correlationId, DateTimeOffset now)
    {
        var response = new PlanResponse(planId, versionNo, name, discount, status.ToString(), effectiveFrom, tiers);

        db.PlanVersions.Add(new PlanVersion
        {
            PlanId = planId,
            VersionNo = versionNo,
            Name = name,
            Discount = discount,
            Status = status,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now,
            Tiers = tiers.Select((t, i) => new PlanTier
            {
                PlanId = planId,
                VersionNo = versionNo,
                TierNo = i + 1,
                Threshold = t.Threshold,
                PricePerKwh = t.PricePerKwh
            }).ToList()
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "plan",
            AggregateId = planId.ToString(),
            EventType = EventTypes.PlanVersionCreated,
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(
                new PlanVersionCreatedV1(
                    planId, versionNo, name, discount, status.ToString(), effectiveFrom,
                    tiers.Select(t => new PlanTierV1(t.Threshold, t.PricePerKwh)).ToList()),
                Json),
            CorrelationId = correlationId,
            OccurredAt = now
        });

        return response;
    }

    private async Task<(PlanVersion? Latest, ChangePlanResult? Failure)> LoadLatestAsync(
        Guid planId, int expectedVersion, CancellationToken ct)
    {
        var latest = await db.PlanVersions.AsNoTracking()
            .Where(v => v.PlanId == planId)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
            return (null, new ChangePlanResult.NotFound());
        if (latest.Status == VersionStatus.Retired)
            return (null, new ChangePlanResult.Conflict("plan is retired"));
        if (latest.VersionNo != expectedVersion)
            return (null, new ChangePlanResult.Conflict("version mismatch"));

        return (latest, null);
    }

    private async Task<ChangePlanResult> SaveAsync(PlanResponse response, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: "plan_versions"
        })
        {
            return new ChangePlanResult.Conflict("version mismatch");
        }

        return new ChangePlanResult.Created(response);
    }

    public async Task<List<PlanResponse>> ListAsync(bool includeRetired, CancellationToken ct)
    {
        var latest = await db.PlanVersions.AsNoTracking()
            .Include(v => v.Tiers)
            .Where(v => v.VersionNo == db.PlanVersions.Where(x => x.PlanId == v.PlanId).Max(x => x.VersionNo))
            .Where(v => includeRetired || v.Status != VersionStatus.Retired)
            .OrderBy(v => v.PlanId)
            .ToListAsync(ct);

        return latest.Select(ToResponse).ToList();
    }

    public async Task<PlanResponse?> GetLatestAsync(Guid id, CancellationToken ct)
    {
        var latest = await db.PlanVersions.AsNoTracking()
            .Include(v => v.Tiers)
            .Where(v => v.PlanId == id)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        return latest is null ? null : ToResponse(latest);
    }

    public async Task<List<PlanResponse>?> ListVersionsAsync(Guid id, CancellationToken ct)
    {
        var versions = await db.PlanVersions.AsNoTracking()
            .Include(v => v.Tiers)
            .Where(v => v.PlanId == id)
            .OrderBy(v => v.VersionNo)
            .ToListAsync(ct);

        return versions.Count == 0 ? null : versions.Select(ToResponse).ToList();
    }

    public async Task<PlanResponse?> GetVersionAsync(Guid id, int versionNo, CancellationToken ct)
    {
        var version = await db.PlanVersions.AsNoTracking()
            .Include(v => v.Tiers)
            .FirstOrDefaultAsync(v => v.PlanId == id && v.VersionNo == versionNo, ct);

        return version is null ? null : ToResponse(version);
    }

    private static PlanResponse ToResponse(PlanVersion v)
        => new(
            v.PlanId, v.VersionNo, v.Name, v.Discount, v.Status.ToString(), v.EffectiveFrom,
            v.Tiers.OrderBy(t => t.TierNo).Select(t => new TierRequest(t.Threshold, t.PricePerKwh)).ToList());

    private Task<IdempotencyKey?> FindKeyAsync(string idempotencyKey, CancellationToken ct)
        => db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Endpoint == Endpoint && k.Key == idempotencyKey, ct);

    private static CreatePlanResult Replay(IdempotencyKey stored, string requestHash)
        => stored.RequestHash == requestHash
            ? new CreatePlanResult.Created(JsonSerializer.Deserialize<PlanResponse>(stored.ResponseBody, Json)!)
            : new CreatePlanResult.KeyReusedWithDifferentBody();
}
