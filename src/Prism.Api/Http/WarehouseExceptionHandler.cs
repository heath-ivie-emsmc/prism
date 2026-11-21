using Microsoft.AspNetCore.Diagnostics;
using Prism.Application.Warehouse;

namespace Prism.Api.Http;

/// <summary>
/// Turns the warehouse's typed failures into ProblemDetails, in one place. The body says only what happened: never the SQL,
/// the host, the connection string or the Provider.
/// </summary>
public sealed class WarehouseExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        (int status, string title)? mapped = exception switch
        {
            WarehouseUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The data warehouse is not available. Try again shortly."),
            WarehouseResultTooLargeException => (StatusCodes.Status500InternalServerError, "The query returned more data than allowed."),
            WarehouseQueryRejectedException => (StatusCodes.Status500InternalServerError, "The query was refused."),
            _ => null,
        };
        return mapped is { } result ? WriteAsync(httpContext, result.status, result.title) : ValueTask.FromResult(false);
    }

    private async ValueTask<bool> WriteAsync(HttpContext httpContext, int status, string title)
    {
        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = null,
            ProblemDetails = { Status = status, Title = title },
        });
    }
}
