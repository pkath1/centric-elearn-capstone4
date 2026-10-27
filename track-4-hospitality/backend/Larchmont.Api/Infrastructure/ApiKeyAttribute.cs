using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Larchmont.Api.Infrastructure;

// Closed by default: returns 401 when the header is missing, when the configured key is empty, or when the key is wrong.
// Runs as an authorization filter, so it happens before model binding and body validation.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    private readonly string _configKey;

    public ApiKeyAttribute(string configKey = "Lock:ApiKey") => _configKey = configKey;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var expected = config[_configKey];
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided) || !SameKey(expected, provided))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid X-Api-Key header is required."
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }
        return Task.CompletedTask;
    }

    // Hash both sides so the comparison takes the same time whatever the lengths are.
    private static bool SameKey(string expected, string provided)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
