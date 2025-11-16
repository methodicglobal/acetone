using System.Text.Json;

namespace Acetone.V2.Proxy.Middleware;

/// <summary>
/// Middleware that handles exceptions and returns appropriate error responses
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Get or create correlation ID
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        _logger.LogError(exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}",
            correlationId, context.Request.Path);

        // Set status code based on exception type
        var statusCode = exception switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            InvalidOperationException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        var errorResponse = new
        {
            error = new
            {
                message = GetErrorMessage(exception, statusCode),
                correlationId = correlationId,
                timestamp = DateTime.UtcNow,
                path = context.Request.Path.ToString()
            }
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await context.Response.WriteAsync(json);
    }

    private static string GetErrorMessage(Exception exception, int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status404NotFound => "The requested resource was not found",
            StatusCodes.Status504GatewayTimeout => "The request timed out",
            StatusCodes.Status503ServiceUnavailable => "Service is temporarily unavailable",
            StatusCodes.Status401Unauthorized => "Unauthorized access",
            _ => "An unexpected error occurred"
        };
    }
}
