namespace OfflineSync.BranchClient.Outbox;

internal sealed class OutboxSyncService(
    SqliteOutbox outbox,
    SyncApi api,
    IConfiguration configuration,
    ILogger<OutboxSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var branchId = configuration["Branch:Id"] ?? "pocitos";
        await outbox.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await outbox.EnqueueDemoAsync(branchId, stoppingToken).ConfigureAwait(false);
        logger.LogInformation("Sucursal {BranchId} lista. Las ventas quedan en el outbox hasta que la API responda.", branchId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var message = await outbox.LeaseNextAsync(stoppingToken).ConfigureAwait(false);
                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var result = await api.SendAsync(message.Payload, stoppingToken).ConfigureAwait(false);
                    switch (result.Outcome)
                    {
                        case SyncCallOutcome.Done:
                            await outbox.MarkSentAsync(message.Id, stoppingToken).ConfigureAwait(false);
                            logger.LogInformation(
                                "Venta {Key} sincronizada ({Detail}).",
                                message.IdempotencyKey,
                                result.Detail);
                            break;
                        case SyncCallOutcome.Fail:
                            await outbox.MarkFailedAsync(message.Id, result.Detail, stoppingToken).ConfigureAwait(false);
                            logger.LogError(
                                "Venta {Key} descartada: {Detail}",
                                message.IdempotencyKey,
                                result.Detail);
                            break;
                        default:
                            await outbox.MarkRetryAsync(message.Id, message.Attempts, result.Detail, stoppingToken)
                                .ConfigureAwait(false);
                            logger.LogWarning(
                                "Venta {Key} queda pendiente: {Detail}",
                                message.IdempotencyKey,
                                result.Detail);
                            break;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested
                    && exception is HttpRequestException or TaskCanceledException)
                {
                    await outbox.MarkRetryAsync(message.Id, message.Attempts, exception.Message, stoppingToken)
                        .ConfigureAwait(false);
                    logger.LogWarning(exception, "Sin red para la venta {Key}. Se reintenta.", message.IdempotencyKey);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Cliente de sucursal detenido.");
        }
    }
}
