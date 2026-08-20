using System.Security.Cryptography;
using System.Text;
using ModelRelay.Core.Abstractions;
using ModelRelay.Core.Models;

namespace ModelRelay.Api.Security;

public static class ApiKeyAuth
{
    public const string TenantItemKey = "modelrelay.tenant";

    public static async Task<IResult?> AuthenticateAsync(
        HttpContext httpContext,
        ITenantStore tenantStore,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var values))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Missing API key",
                detail: "Provide X-Api-Key.");
        }

        var apiKey = values.ToString();
        if (apiKey.Length is < 16 or > 256)
        {
            return Results.Problem(statusCode: 401, title: "Invalid API key");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();
        var tenant = await tenantStore.FindByApiKeyHashAsync(hash, cancellationToken);

        if (tenant is null)
        {
            return Results.Problem(statusCode: 401, title: "Invalid API key");
        }

        httpContext.Items[TenantItemKey] = tenant;
        return null;
    }

    public static Tenant GetTenant(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(TenantItemKey, out var value) && value is Tenant tenant
            ? tenant
            : throw new InvalidOperationException("Tenant was not authenticated.");
}
