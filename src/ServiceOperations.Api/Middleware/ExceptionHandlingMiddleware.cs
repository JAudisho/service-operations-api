using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (RequestValidationException exception)
        {
            await WriteAsync(context, new HttpValidationProblemDetails(
                exception.Errors.ToDictionary(x => x.Key, x => x.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed"
            });
        }
        catch (ResourceNotFoundException exception)
        {
            await WriteAsync(context, Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message));
        }
        catch (ResourceConflictException exception)
        {
            await WriteAsync(context, Problem(StatusCodes.Status409Conflict, "Request conflict", exception.Message));
        }
        catch (BadHttpRequestException exception)
        {
            await WriteAsync(context, Problem(StatusCodes.Status400BadRequest, "Invalid request", exception.Message));
        }
        catch (JsonException exception)
        {
            await WriteAsync(context, Problem(StatusCodes.Status400BadRequest, "Invalid JSON", exception.Message));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "A SQL Server operation failed");
            await WriteAsync(context, Problem(StatusCodes.Status503ServiceUnavailable,
                "Database unavailable", "The database operation could not be completed."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An unhandled request error occurred");
            await WriteAsync(context, Problem(StatusCodes.Status500InternalServerError,
                "Unexpected error", "An unexpected error occurred."));
        }
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status, Title = title, Detail = detail
    };

    private static async Task WriteAsync(HttpContext context, ProblemDetails details)
    {
        context.Response.StatusCode = details.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(details);
    }
}
