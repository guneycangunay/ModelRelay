using System.Text.Json;
using ModelRelay.Api.Security;
using ModelRelay.Api.Telemetry;
using ModelRelay.Core.Abstractions;
using ModelRelay.Core.Billing;
using ModelRelay.Core.Contracts;
using ModelRelay.Core.Models;
using ModelRelay.Core.Routing;
using ModelRelay.Core.Security;
using ModelRelay.Core.Tokens;
using ModelRelay.Infrastructure.Postgres;
using ModelRelay.Infrastructure.Providers;
using ModelRelay.Infrastructure.Redis;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var postgres = Environment.GetEnvironmentVariable("MODELRELAY_POSTGRES")
    ?? builder.Configuration["ModelRelay:Postgres"]
    ?? throw new InvalidOperationException("PostgreSQL connection string is missing.");
var redis = Environment.GetEnvironmentVariable("MODELRELAY_REDIS")
    ?? builder.Configuration["ModelRelay:Redis"]
    ?? throw new InvalidOperationException("Redis connection string is missing.");
var demoApiKey = Environment.GetEnvironmentVariable("MODELRELAY_DEMO_API_KEY")
    ?? builder.Configuration["ModelRelay:DemoApiKey"]
    ?? "relay_demo_key_change_me";

static int ConfigInt(IConfiguration configuration, string key, int fallback)
    => int.TryParse(configuration[key], out var value) ? value : fallback;

var routerPolicy = new RouterPolicy(
    TimeoutMilliseconds: ConfigInt(builder.Configuration, "Router:TimeoutMilliseconds", 1800),
    MaxRetries: ConfigInt(builder.Configuration, "Router:MaxRetries", 1),
    CircuitFailureThreshold: ConfigInt(builder.Configuration, "Router:CircuitFailureThreshold", 3),
    CircuitOpenSeconds: ConfigInt(builder.Configuration, "Router:CircuitOpenSeconds", 20));

builder.Services.AddSingleton(NpgsqlDataSource.Create(postgres));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
builder.Services.AddSingleton<PostgresStore>();
builder.Services.AddSingleton<ITenantStore>(sp => sp.GetRequiredService<PostgresStore>());
builder.Services.AddSingleton<IUsageStore>(sp => sp.GetRequiredService<PostgresStore>());
builder.Services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
builder.Services.AddSingleton<DatabaseInitializer>(sp => new DatabaseInitializer(sp.GetRequiredService<NpgsqlDataSource>(), demoApiKey));

builder.Services.AddSingleton<ILlmProvider>(_ => new FakeLlmProvider("fake-primary", "primary"));
builder.Services.AddSingleton<ILlmProvider>(_ => new FakeLlmProvider("fake-secondary", "secondary"));
builder.Services.AddSingleton<ProviderCircuitRegistry>();
builder.Services.AddSingleton(routerPolicy);
builder.Services.AddSingleton<ModelRouter>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("modelrelay-api"))
    .WithTracing(tracing => tracing
        .AddSource(RelayTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(RelayTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/info", () => Results.Ok(new
{
    service = "ModelRelay",
    version = "1.0.0",
    models = new[] { "relay-fast", "relay-balanced" }
}));

app.MapHealthChecks("/health/live");

app.MapGet("/health/ready", async (
    NpgsqlDataSource dataSource,
    IConnectionMultiplexer redisConnection,
    CancellationToken cancellationToken) =>
{
    try
    {
        await using var command = dataSource.CreateCommand("SELECT 1;");
        await command.ExecuteScalarAsync(cancellationToken);
        await redisConnection.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.Problem(statusCode: 503, title: "ModelRelay is not ready");
    }
});

app.MapPost("/v1/chat/completions", async (
    HttpContext http,
    ChatCompletionRequest request,
    ITenantStore tenantStore,
    IRateLimitStore rateLimit,
    IUsageStore usageStore,
    ModelRouter router,
    CancellationToken cancellationToken) =>
{
    var authFailure = await ApiKeyAuth.AuthenticateAsync(http, tenantStore, cancellationToken);
    if (authFailure is not null)
    {
        return authFailure;
    }

    var tenant = ApiKeyAuth.GetTenant(http);

    if (request.Messages is null || request.Messages.Count == 0)
    {
        return Results.Problem(statusCode: 400, title: "messages must contain at least one item");
    }

    if (!ModelPricing.TryGet(request.Model, out _))
    {
        return Results.Problem(
            statusCode: 400,
            title: "Unsupported model",
            detail: "Use relay-fast or relay-balanced.");
    }

    if (!await rateLimit.TryConsumeAsync(tenant.Id, tenant.RequestsPerMinute, cancellationToken))
    {
        http.Response.Headers["Retry-After"] = "60";
        return Results.Problem(statusCode: 429, title: "Rate limit exceeded");
    }

    var maxTokens = Math.Clamp(request.MaxTokens ?? 256, 1, 2048);
    var temperature = Math.Clamp(request.Temperature ?? 0.2, 0, 2);
    var (redactedMessages, redactionCount) = PromptRedactor.Redact(request.Messages);
    var estimatedPromptTokens = TokenEstimator.Estimate(redactedMessages);

    var reservation = await usageStore.TryReserveBudgetAsync(
        tenant,
        request.Model,
        estimatedPromptTokens,
        maxTokens,
        cancellationToken);

    if (reservation is null)
    {
        return Results.Problem(statusCode: 402, title: "Monthly AI budget exceeded");
    }

    var requestId = $"chatcmpl_{Guid.NewGuid():N}";
    using var activity = RelayTelemetry.ActivitySource.StartActivity("chat.completion");
    activity?.SetTag("modelrelay.model", request.Model);
    activity?.SetTag("modelrelay.tenant_id", tenant.Id.ToString("N"));

    try
    {
        var providerRequest = new ProviderRequest(
            requestId,
            request.Model,
            redactedMessages.Select(x => (x.Role, x.Content)).ToArray(),
            maxTokens,
            temperature);

        var (providerResult, fallbackCount) = await router.CompleteAsync(providerRequest, cancellationToken);
        var cost = ModelPricing.CalculateActualCost(
            request.Model,
            providerResult.PromptTokens,
            providerResult.CompletionTokens);

        var usage = new UsageRecord(
            requestId,
            tenant.Id,
            request.Model,
            providerResult.Provider,
            providerResult.PromptTokens,
            providerResult.CompletionTokens,
            cost,
            providerResult.LatencyMs,
            fallbackCount,
            redactionCount,
            "ok");

        await usageStore.FinalizeUsageAsync(reservation, usage, cancellationToken);

        RelayTelemetry.Requests.Add(1,
            new KeyValuePair<string, object?>("model", request.Model),
            new KeyValuePair<string, object?>("provider", providerResult.Provider));
        RelayTelemetry.Fallbacks.Add(fallbackCount);
        RelayTelemetry.Redactions.Add(redactionCount);
        RelayTelemetry.ProviderLatency.Record(providerResult.LatencyMs,
            new KeyValuePair<string, object?>("provider", providerResult.Provider));

        http.Response.Headers["X-ModelRelay-Provider"] = providerResult.Provider;
        http.Response.Headers["X-ModelRelay-Fallbacks"] = fallbackCount.ToString();
        http.Response.Headers["X-ModelRelay-Redactions"] = redactionCount.ToString();

        var response = new ChatCompletionResponse(
            requestId,
            "chat.completion",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            request.Model,
            new[]
            {
                new ChatChoice(
                    0,
                    new ChatMessage("assistant", providerResult.Content),
                    "stop")
            },
            new ChatUsage(
                providerResult.PromptTokens,
                providerResult.CompletionTokens,
                providerResult.PromptTokens + providerResult.CompletionTokens));

        if (!request.Stream)
        {
            return Results.Json(response);
        }

        http.Response.StatusCode = 200;
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers["Cache-Control"] = "no-cache";
        http.Response.Headers["Connection"] = "keep-alive";

        var words = providerResult.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var chunk = new
            {
                id = requestId,
                @object = "chat.completion.chunk",
                created = response.Created,
                model = request.Model,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = (i == 0 ? "" : " ") + words[i] },
                        finish_reason = (string?)null
                    }
                }
            };

            await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
            await http.Response.Body.FlushAsync(cancellationToken);
        }

        var finalChunk = new
        {
            id = requestId,
            @object = "chat.completion.chunk",
            created = response.Created,
            model = request.Model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { },
                    finish_reason = "stop"
                }
            }
        };

        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalChunk)}\n\n", cancellationToken);
        await http.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        return Results.Empty;
    }
    catch (AllProvidersUnavailableException ex)
    {
        await usageStore.ReleaseReservationAsync(reservation, CancellationToken.None);
        return Results.Problem(
            statusCode: 503,
            title: "All model providers are unavailable",
            detail: ex.Message);
    }
    catch
    {
        await usageStore.ReleaseReservationAsync(reservation, CancellationToken.None);
        throw;
    }
});

app.MapGet("/admin/metrics/summary", async (
    HttpContext http,
    ITenantStore tenantStore,
    IUsageStore usageStore,
    CancellationToken cancellationToken) =>
{
    var authFailure = await ApiKeyAuth.AuthenticateAsync(http, tenantStore, cancellationToken);
    if (authFailure is not null)
    {
        return authFailure;
    }

    var tenant = ApiKeyAuth.GetTenant(http);
    var summary = await usageStore.GetDashboardSummaryAsync(tenant.Id, cancellationToken);
    return Results.Ok(summary);
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
