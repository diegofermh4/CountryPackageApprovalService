using CountryPackageApprovalService.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Country_PackageAPI.Middleware;

/// <summary>
/// The single place that translates a thrown <see cref="DomainException"/> into an HTTP response, so
/// controllers stay free of try/catch and status-code bookkeeping. Every response is a standard
/// <see cref="ProblemDetails"/> body (RFC 9457) carrying the request's trace id for correlation with logs -
/// see docs/ARCHITECTURE.md §7 "Observability". Unrecognized exceptions are logged with full detail but
/// returned to the caller as a generic 500 with no internal detail, since a stack trace or exception message
/// can leak information about operational data or infrastructure the caller should never see.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            var (status, title) = MapStatus(ex);
            _logger.LogWarning(ex, "Request {Method} {Path} rejected: {Status} {Message}",
                context.Request.Method, context.Request.Path, status, ex.Message);
            await WriteProblemAsync(context, status, title, ex.Message);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred while processing the request. It has been logged for investigation.");
        }
    }

    private static (int Status, string Title) MapStatus(DomainException ex) => ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
        UnauthorizedStepActionException => (StatusCodes.Status403Forbidden, "Forbidden"),
        StepLockedException => (StatusCodes.Status409Conflict, "Conflict"),
        InvalidStepStateException => (StatusCodes.Status409Conflict, "Conflict"),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        BusinessRuleValidationException => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
        _ => (StatusCodes.Status400BadRequest, "Bad Request")
    };

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        return context.Response.WriteAsJsonAsync(problem);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseDomainExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
