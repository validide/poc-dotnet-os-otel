using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using StackExchange.Redis;
#pragma warning disable S3903

var builder = WebApplication.CreateBuilder(args);

// Get OTLP endpoint from environment or use default
var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "valkey:6379";

// Configure Redis connection
var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;

var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

// Register Redis for DI
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Configure OpenTelemetry
var serviceName = "AnalyticsApi";
var serviceVersion = "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRedisInstrumentation(redis, options =>
        {
            options.SetVerboseDatabaseStatements = true;
            options.EnrichActivityWithTimingEvents = true;
        })
        .AddSource(serviceName)
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));

// Add logging with OpenTelemetry
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion));
    options.AddOtlpExporter(exporterOptions => exporterOptions.Endpoint = new Uri(otlpEndpoint));
});

var app = builder.Build();

// Create ActivitySource for custom spans
var activitySource = new ActivitySource(serviceName);

// Test Redis connection
try
{
    var db = redis.GetDatabase();
    await db.PingAsync();
    app.Logger.LogInformation("Successfully connected to Redis/Valkey");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to connect to Redis/Valkey");
}

// API Endpoints
app.MapGet("/", () =>
{
    return Results.Ok(new { message = "Analytics API - Tracks API call statistics using Redis/Valkey" });
});

// Track an API call
app.MapPost("/api/analytics/track", async (TrackRequest request, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("TrackApiCall");
    activity?.SetTag("endpoint", request.Endpoint);
    activity?.SetTag("method", request.Method);

    logger.LogInformation("Tracking API call: {Method} {Endpoint}", request.Method, request.Endpoint);

    var db = redis.GetDatabase();
    var key = $"api:calls:{request.Endpoint}:{request.Method}";
    var timestampKey = $"api:calls:timestamps:{request.Endpoint}:{request.Method}";

    // Increment call counter
    var count = await db.StringIncrementAsync(key);

    // Store timestamp of the call
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    await db.SortedSetAddAsync(timestampKey, timestamp, timestamp);

    // Keep only last 1000 timestamps per endpoint
    await db.SortedSetRemoveRangeByRankAsync(timestampKey, 0, -1001);

    activity?.SetTag("call.count", count);
    logger.LogInformation("Call count for {Method} {Endpoint}: {Count}", request.Method, request.Endpoint, count);

    return Results.Ok(new { endpoint = request.Endpoint, method = request.Method, totalCalls = count });
});

// Get analytics for a specific endpoint
app.MapGet("/api/analytics/{endpoint}", async (string endpoint, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetEndpointAnalytics");
    activity?.SetTag("endpoint", endpoint);

    logger.LogInformation("Retrieving analytics for endpoint: {Endpoint}", endpoint);

    var db = redis.GetDatabase();
    var stats = new Dictionary<string, object>();

    // Get stats for all methods
    foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
    {
        var key = $"api:calls:{endpoint}:{method}";
        var timestampKey = $"api:calls:timestamps:{endpoint}:{method}";

        var count = await db.StringGetAsync(key);
        var timestamps = await db.SortedSetRangeByScoreAsync(timestampKey, order: Order.Descending, take: 100);

        var callCount = count.HasValue ? (long)count : 0;

        // Calculate calls in last minute, hour, and day
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oneMinuteAgo = now - (60 * 1000);
        var oneHourAgo = now - (60 * 60 * 1000);
        var oneDayAgo = now - (24 * 60 * 60 * 1000);

        var callsLastMinute = await db.SortedSetLengthAsync(timestampKey, oneMinuteAgo, now);
        var callsLastHour = await db.SortedSetLengthAsync(timestampKey, oneHourAgo, now);
        var callsLastDay = await db.SortedSetLengthAsync(timestampKey, oneDayAgo, now);

        if (callCount > 0)
        {
            stats[method] = new
            {
                totalCalls = callCount,
                lastMinute = callsLastMinute,
                lastHour = callsLastHour,
                lastDay = callsLastDay,
                recentTimestamps = timestamps.Take(10).Select(t => DateTimeOffset.FromUnixTimeMilliseconds((long)t).ToString("o"))
            };
        }
    }

    logger.LogInformation("Retrieved analytics for {Endpoint}: {StatCount} methods tracked", endpoint, stats.Count);

    return Results.Ok(new { endpoint, statistics = stats });
});

// Get all tracked endpoints
app.MapGet("/api/analytics", async (IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetAllAnalytics");

    logger.LogInformation("Retrieving all analytics");

    var db = redis.GetDatabase();
    var server = redis.GetServer(redis.GetEndPoints()[0]);

    var keys = server.Keys(pattern: "api:calls:*", pageSize: 1000)
        .Where(k => !k.ToString().Contains("timestamps"))
        .ToList();

    var allStats = new Dictionary<string, Dictionary<string, object>>();

    foreach (var key in keys)
    {
        var parts = key.ToString().Split(':');
        if (parts.Length >= 4)
        {
            var endpoint = parts[2];
            var method = parts[3];
            var count = await db.StringGetAsync(key);

            if (!allStats.ContainsKey(endpoint))
            {
                allStats[endpoint] = new Dictionary<string, object>();
            }

            allStats[endpoint][method] = new { totalCalls = (long)count };
        }
    }

    activity?.SetTag("endpoints.count", allStats.Count);
    logger.LogInformation("Retrieved analytics for {Count} endpoints", allStats.Count);

    return Results.Ok(allStats);
});

// Clear analytics for a specific endpoint
app.MapDelete("/api/analytics/{endpoint}", async (string endpoint, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("ClearEndpointAnalytics");
    activity?.SetTag("endpoint", endpoint);

    logger.LogInformation("Clearing analytics for endpoint: {Endpoint}", endpoint);

    var db = redis.GetDatabase();
    var keysDeleted = 0;

    foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
    {
        var key = $"api:calls:{endpoint}:{method}";
        var timestampKey = $"api:calls:timestamps:{endpoint}:{method}";

        if (await db.KeyDeleteAsync(key)) keysDeleted++;
        if (await db.KeyDeleteAsync(timestampKey)) keysDeleted++;
    }

    activity?.SetTag("keys.deleted", keysDeleted);
    logger.LogInformation("Cleared {Count} keys for endpoint {Endpoint}", keysDeleted, endpoint);

    return Results.Ok(new { endpoint, keysDeleted });
});

app.Run();

public record TrackRequest(string Endpoint, string Method);
