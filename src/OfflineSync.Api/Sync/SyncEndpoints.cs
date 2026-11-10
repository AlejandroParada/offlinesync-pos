using OfflineSync.Application.Sync;

namespace OfflineSync.Api.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sync/sales", async (
                SyncSalesRequest? request,
                SyncSalesHandler handler,
                ILogger<SyncSalesHandler> logger,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(request.ToCommand(), cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Lote de {Count} ventas ({Branches}). Aplicadas {Applied}, duplicadas {Duplicates}, rechazadas {Rejected}, en conflicto {Conflicts}",
                    request?.Sales?.Count ?? 0,
                    string.Join(',', (request?.Sales ?? []).Select(sale => sale.BranchId).Distinct()),
                    result.Results.Count(item => item.Status == SyncItemStatus.Applied),
                    result.Results.Count(item => item.Status == SyncItemStatus.Duplicate),
                    result.Results.Count(item => item.Status == SyncItemStatus.Rejected),
                    result.Results.Count(item => item.Status == SyncItemStatus.Conflict));
                return Results.Ok(result.ToResponse());
            })
            .WithName("SyncSales")
            .WithTags("Sync")
            .WithSummary("Sincroniza un lote de ventas emitidas en una sucursal.")
            .WithDescription("Cada venta trae una clave de idempotencia generada en la sucursal. Reenviar la misma clave no vuelve a descontar stock.")
            .Produces<SyncSalesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }
}
