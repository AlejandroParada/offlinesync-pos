using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OfflineSync.BranchClient.Outbox;

internal sealed class SyncApi(HttpClient http)
{
    public async Task<SyncCallResult> SendAsync(string payload, CancellationToken cancellationToken)
    {
        var sale = JsonSerializer.Deserialize<SalePayload>(payload, JsonDefaults.Options)
            ?? throw new InvalidOperationException("El outbox tiene una venta ilegible.");

        using var response = await http.PostAsJsonAsync(
            "/sync/sales",
            new BatchPayload([sale]),
            JsonDefaults.Options,
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return SyncCallResult.Fail(Trim(body));
        }

        if (!response.IsSuccessStatusCode)
        {
            return SyncCallResult.Retry($"HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        var parsed = JsonSerializer.Deserialize<SyncResponse>(body, JsonDefaults.Options);
        var item = parsed?.Results.FirstOrDefault();
        if (item is null)
        {
            return SyncCallResult.Retry("La API no devolvió un resultado.");
        }

        return item.Status switch
        {
            "applied" or "duplicate" => SyncCallResult.Done(Describe(item)),
            "conflict" => SyncCallResult.Retry(item.Detail ?? "conflicto de stock"),
            "rejected" => SyncCallResult.Retry(item.Detail ?? "venta rechazada"),
            _ => SyncCallResult.Retry($"Estado desconocido: {item.Status}")
        };
    }

    private static string Describe(SyncItemResponse item)
    {
        if (item.Discrepancies is not { Count: > 0 })
        {
            return item.Status;
        }

        var notes = string.Join(
            ", ",
            item.Discrepancies.Select(discrepancy => $"{discrepancy.Reason} stock={discrepancy.QuantityOnHand}"));
        return $"{item.Status}; {notes}";
    }

    private static string Trim(string value) =>
        value.Length <= 300 ? value : value[..300];
}
