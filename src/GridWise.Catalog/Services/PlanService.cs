using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridWise.Catalog.Services;

public abstract record CreatePlanResult
{
    public sealed record Created(PlanResponse Plan) : CreatePlanResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : CreatePlanResult;
    public sealed record KeyReusedWithDifferentBody : CreatePlanResult;
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
        var response = new PlanResponse(
            planId, 1, req.Name, req.Discount, nameof(VersionStatus.Active), effectiveFrom, tiers);
        var responseJson = JsonSerializer.Serialize(response, Json);

        db.Plans.Add(new Plan
        {
            Id = planId,
            CreatedAt = now,
            Versions =
            [
                new PlanVersion
                {
                    PlanId = planId,
                    VersionNo = 1,
                    Name = req.Name,
                    Discount = req.Discount,
                    Status = VersionStatus.Active,
                    EffectiveFrom = effectiveFrom,
                    CreatedAt = now,
                    Tiers = tiers.Select((t, i) => new PlanTier
                    {
                        PlanId = planId,
                        VersionNo = 1,
                        TierNo = i + 1,
                        Threshold = t.Threshold,
                        PricePerKwh = t.PricePerKwh
                    }).ToList()
                }
            ]
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "plan",
            AggregateId = planId.ToString(),
            EventType = "PlanVersionCreated",
            EventVersion = 1,
            Payload = responseJson,
            CorrelationId = correlationId,
            OccurredAt = now
        });

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

    private Task<IdempotencyKey?> FindKeyAsync(string idempotencyKey, CancellationToken ct)
        => db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Endpoint == Endpoint && k.Key == idempotencyKey, ct);

    private static CreatePlanResult Replay(IdempotencyKey stored, string requestHash)
        => stored.RequestHash == requestHash
            ? new CreatePlanResult.Created(JsonSerializer.Deserialize<PlanResponse>(stored.ResponseBody, Json)!)
            : new CreatePlanResult.KeyReusedWithDifferentBody();
}
