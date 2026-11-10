using System.Text.Json;

namespace OfflineSync.BranchClient;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed record SalePayload(
    Guid IdempotencyKey,
    string BranchId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<SaleLinePayload> Lines);

internal sealed record SaleLinePayload(Guid ProductId, int Quantity, decimal UnitPrice);

internal sealed record BatchPayload(IReadOnlyList<SalePayload> Sales);

internal sealed record SyncResponse(IReadOnlyList<SyncItemResponse> Results);

internal sealed record SyncItemResponse(
    Guid IdempotencyKey,
    string Status,
    Guid? SaleId,
    string? Detail,
    IReadOnlyList<DiscrepancyResponse>? Discrepancies);

internal sealed record DiscrepancyResponse(Guid ProductId, int QuantityOnHand, string Reason);

internal enum SyncCallOutcome
{
    Done,
    Retry,
    Fail
}

internal sealed record SyncCallResult(SyncCallOutcome Outcome, string? Detail)
{
    public static SyncCallResult Done(string? detail = null) => new(SyncCallOutcome.Done, detail);

    public static SyncCallResult Retry(string detail) => new(SyncCallOutcome.Retry, detail);

    public static SyncCallResult Fail(string detail) => new(SyncCallOutcome.Fail, detail);
}
