using OfflineSync.Domain;

namespace OfflineSync.Api;

public static class ErrorHandling
{
    public static void UseOfflineSyncErrors(this WebApplication app)
    {
        app.UseExceptionHandler(handler =>
        {
            handler.Run(async context =>
            {
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OfflineSync.Errors");

                if (error is DomainException domain)
                {
                    logger.LogInformation(domain, "Solicitud rechazada.");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new ApiError(
                        "La solicitud no se puede procesar.",
                        domain.Message)).ConfigureAwait(false);
                    return;
                }

                logger.LogError(error, "Error no controlado.");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new ApiError("Error interno.", null)).ConfigureAwait(false);
            });
        });
    }

    private sealed record ApiError(string Title, string? Detail);
}
