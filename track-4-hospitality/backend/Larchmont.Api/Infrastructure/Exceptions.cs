using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Larchmont.Api.Infrastructure;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

public class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException ve:
                problem = new ValidationProblemDetails(
                    ve.Errors.GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
                { Status = 400, Title = "Validation failed" };
                break;
            case NotFoundException nf:
                problem = new ProblemDetails { Status = 404, Title = "Not found", Detail = nf.Message };
                break;
            case ConflictException c:
                problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = c.Message };
                break;
            case BusinessRuleException br:
                problem = new ProblemDetails { Status = 422, Title = "Rule violated", Detail = br.Message };
                break;
            case ForbiddenException fb:
                problem = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = fb.Message };
                break;
            default:
                _logger.LogError(exception, "Unhandled error on {Path}", context.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Unexpected error" };
                break;
        }
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), ct);
        return true;
    }
}
