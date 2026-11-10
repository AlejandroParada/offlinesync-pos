using System.Text.Json;

namespace OfflineSync.BranchClient.Outbox;

internal sealed record OutboxMessage(long Id, Guid IdempotencyKey, string Payload, int Attempts);

internal sealed class SqliteOutbox(string path)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                idempotency_key TEXT NOT NULL UNIQUE,
                payload TEXT NOT NULL,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                next_attempt_at INTEGER NOT NULL,
                created_at INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueDemoAsync(string branchId, CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var sale in DemoSales.Create(branchId, occurredAt))
        {
            var payload = JsonSerializer.Serialize(sale, JsonDefaults.Options);
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO outbox (idempotency_key, payload, status, attempts, next_attempt_at, created_at)
                VALUES (@key, @payload, 'pending', 0, @now, @now)
                ON CONFLICT(idempotency_key) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@key", sale.IdempotencyKey.ToString("D"));
            command.Parameters.AddWithValue("@payload", payload);
            command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<OutboxMessage?> LeaseNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, idempotency_key, payload, attempts
            FROM outbox
            WHERE status = 'pending' AND next_attempt_at <= @now
            ORDER BY id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new OutboxMessage(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3));
    }

    public Task MarkSentAsync(long id, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE outbox
            SET status = 'sent', last_error = NULL
            WHERE id = @id;
            """,
            cancellationToken,
            ("@id", id));

    public Task MarkFailedAsync(long id, string? error, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE outbox
            SET status = 'failed', last_error = @error
            WHERE id = @id;
            """,
            cancellationToken,
            ("@id", id),
            ("@error", (object?)error ?? "error"));

    public Task MarkRetryAsync(long id, int attempts, string? error, CancellationToken cancellationToken)
    {
        var nextAttempts = attempts + 1;
        if (nextAttempts >= 8)
        {
            return MarkFailedAsync(id, error, cancellationToken);
        }

        var delaySeconds = Math.Min(15, Math.Pow(2, nextAttempts - 1));
        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToUnixTimeMilliseconds();
        return ExecuteAsync(
            """
            UPDATE outbox
            SET attempts = @attempts, last_error = @error, next_attempt_at = @next
            WHERE id = @id;
            """,
            cancellationToken,
            ("@id", id),
            ("@attempts", nextAttempts),
            ("@error", (object?)error ?? "error"),
            ("@next", nextAttempt));
    }

    private Microsoft.Data.Sqlite.SqliteConnection Open() => new($"Data Source={path}");

    private async Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static class DemoSales
{
    // Mismos productos que OfflineSync.Infrastructure.Catalog.DemoCatalog.
    public static readonly Guid Ibuprofen = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Amoxicillin = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AlcoholGel = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid IbuprofenSale = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid AlcoholSale = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid AmoxicillinSale = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");

    public static IReadOnlyList<SalePayload> Create(string branchId, DateTimeOffset occurredAt) =>
    [
        new(IbuprofenSale, branchId, occurredAt, [new SaleLinePayload(Ibuprofen, 1, 320m)]),
        new(AlcoholSale, branchId, occurredAt, [new SaleLinePayload(AlcoholGel, 3, 190m)]),
        new(AmoxicillinSale, branchId, occurredAt, [new SaleLinePayload(Amoxicillin, 1, 410m)])
    ];
}
