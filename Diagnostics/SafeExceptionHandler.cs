using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;

namespace RentRight.Diagnostics;

public sealed class SafeExceptionHandler(ILogger<SafeExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        DiagnosticEvents.RecordFailure(logger, context);
        var originalPath = context.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? context.Request.Path.Value;
        if (!new PathString(originalPath).StartsWithSegments('/' + ApiConventions.RoutePrefix))
            return false; // Keep the existing MVC /Home/Error view and request ID.

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails { Status = 500, Title = "An error occurred while processing your request.", Instance = originalPath };
        ApiProblemDetailsDefaults.Apply(problem, context, ApiErrorCodes.InternalError);
        if (!await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
