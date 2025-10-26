using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;

#pragma warning disable S6664
#pragma warning disable S1075
#pragma warning disable S3903

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
var serviceName = "MinimalOtelDemo";
var serviceVersion = "1.0.0";
var otlpEndpoint = builder.Configuration["OTLP_ENDPOINT"] ?? "http://otel-collector:4317";

// API URLs
var storageApiUrl = builder.Configuration["StorageApiUrl"] ?? "http://storage-api:5234";
var analyticsApiUrl = builder.Configuration["AnalyticsApiUrl"] ?? "http://analytics-api:5235";

// Add OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
            options.Protocol = OtlpExportProtocol.Grpc;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
            options.Protocol = OtlpExportProtocol.Grpc;
        }));

// Add OpenTelemetry Logging
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion));
    options.AddOtlpExporter(exporterOptions =>
    {
        exporterOptions.Endpoint = new Uri(otlpEndpoint);
        exporterOptions.Protocol = OtlpExportProtocol.Grpc;
    });
});

builder.Services.AddHttpClient();

var app = builder.Build();

var activitySource = new ActivitySource(serviceName);

// Helper method to track analytics
async Task TrackAnalytics(IHttpClientFactory httpClientFactory, string endpoint, string method, ILogger logger)
{
    try
    {
        using var activity = activitySource.StartActivity("TrackAnalytics");
        var client = httpClientFactory.CreateClient();
        var trackRequest = new { Endpoint = endpoint, Method = method };
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(trackRequest),
            System.Text.Encoding.UTF8,
            "application/json");

        await client.PostAsync($"{analyticsApiUrl}/api/analytics/track", content);
        logger.LogDebug("Tracked analytics for {Method} {Endpoint}", method, endpoint);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to track analytics");
    }
}

app.MapGet("/", () => "OpenTelemetry + OpenSearch Demo - Try /api/data, /api/error, /api/storage/*, or /api/analytics/*");

app.MapGet("/api/data", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetData");
    activity?.SetTag("operation", "fetch-data");

    logger.LogInformation("Processing data request");

    // Track analytics in background
    _ = TrackAnalytics(httpClientFactory, "/api/data", "GET", logger);

    // Simulate some work
    await Task.Delay(Random.Shared.Next(50, 200));

    var data = new
    {
        Message = "Hello from OpenTelemetry!",
        Timestamp = DateTime.UtcNow,
        RandomValue = Random.Shared.Next(1, 100)
    };

    activity?.SetTag("random_value", data.RandomValue);
    logger.LogInformation("Data request completed with value {Value}", data.RandomValue);

    return Results.Ok(data);
});

app.MapGet("/api/error", (ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("ErrorEndpoint");

    logger.LogWarning("Error endpoint called - simulating error");

    try
    {
        throw new InvalidOperationException("Simulated error for testing");
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddException(ex);
        logger.LogError(ex, "An error occurred");
        return Results.Problem("Simulated error occurred");
    }
});

app.MapGet("/api/chain", async (HttpRequest request, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("ChainedRequest");

    logger.LogInformation("Starting chained request");

    var client = httpClientFactory.CreateClient();

    // Make internal call to /api/data to create trace chain
    var response = await client.GetAsync(request.GetEncodedUrl().Replace("/api/chain", "/api/data"));
    var content = await response.Content.ReadAsStringAsync();

    logger.LogInformation("Chained request completed");

    return Results.Ok(new { ChainedResult = content, Timestamp = DateTime.UtcNow });
});

const string Pattern = "/api/storage/items";

// Storage API endpoints - demonstrating distributed tracing across services
app.MapGet(Pattern, async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetStorageItems");

    logger.LogInformation("Fetching items from storage API");

    // Track analytics
    _ = TrackAnalytics(httpClientFactory, Pattern, "GET", logger);

    var client = httpClientFactory.CreateClient();
    var response = await client.GetAsync($"{storageApiUrl}/api/items");

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("Failed to fetch items from storage API: {StatusCode}", response.StatusCode);
        return Results.Problem("Failed to fetch items from storage API");
    }

    var content = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Successfully fetched items from storage API");

    return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<object>(content));
});

app.MapGet("/api/storage/items/{id:int}", async (int id, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetStorageItem");
    activity?.SetTag("item.id", id);

    logger.LogInformation("Fetching item {Id} from storage API", id);

    // Track analytics
    _ = TrackAnalytics(httpClientFactory, $"/api/storage/items/{id}", "GET", logger);

    var client = httpClientFactory.CreateClient();
    var response = await client.GetAsync($"{storageApiUrl}/api/items/{id}");

    if (!response.IsSuccessStatusCode)
    {
        logger.LogWarning("Item {Id} not found in storage API", id);
        return Results.NotFound(new { error = $"Item {id} not found" });
    }

    var content = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Successfully fetched item {Id} from storage API", id);

    return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<object>(content));
});

app.MapPost(Pattern, async (StorageItemRequest item, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("CreateStorageItem");
    activity?.SetTag("item.name", item.Name);

    logger.LogInformation("Creating item in storage API: {Name}", item.Name);

    // Track analytics
    _ = TrackAnalytics(httpClientFactory, Pattern, "POST", logger);

    var client = httpClientFactory.CreateClient();
    var content = new StringContent(
        System.Text.Json.JsonSerializer.Serialize(item),
        System.Text.Encoding.UTF8,
        "application/json");

    var response = await client.PostAsync($"{storageApiUrl}/api/items", content);

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("Failed to create item in storage API: {StatusCode}", response.StatusCode);
        return Results.Problem("Failed to create item in storage API");
    }

    var responseContent = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Successfully created item in storage API: {Name}", item.Name);

    return Results.Created($"/api/storage/items", System.Text.Json.JsonSerializer.Deserialize<object>(responseContent));
});

// Analytics endpoints - get statistics
app.MapGet("/api/analytics/summary", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetAnalyticsSummary");

    logger.LogInformation("Fetching analytics summary");

    var client = httpClientFactory.CreateClient();
    var response = await client.GetAsync($"{analyticsApiUrl}/api/analytics");

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("Failed to fetch analytics summary: {StatusCode}", response.StatusCode);
        return Results.Problem("Failed to fetch analytics summary");
    }

    var content = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Successfully fetched analytics summary");

    return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<object>(content));
});

app.MapGet("/api/analytics/{endpoint}", async (string endpoint, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetEndpointAnalytics");
    activity?.SetTag("endpoint", endpoint);

    logger.LogInformation("Fetching analytics for endpoint: {Endpoint}", endpoint);

    var client = httpClientFactory.CreateClient();
    var response = await client.GetAsync($"{analyticsApiUrl}/api/analytics/{endpoint}");

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("Failed to fetch analytics for endpoint {Endpoint}: {StatusCode}", endpoint, response.StatusCode);
        return Results.Problem($"Failed to fetch analytics for endpoint {endpoint}");
    }

    var content = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Successfully fetched analytics for endpoint: {Endpoint}", endpoint);

    return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<object>(content));
});

// Complex workflow demonstrating full distributed tracing
app.MapPost("/api/workflow", async (WorkflowRequest request, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("ComplexWorkflow");
    activity?.SetTag("workflow.name", request.Name);

    logger.LogInformation("Starting complex workflow: {Name}", request.Name);

    // Track analytics
    _ = TrackAnalytics(httpClientFactory, "/api/workflow", "POST", logger);

    var client = httpClientFactory.CreateClient();

    // Step 1: Create item in storage
    var storageItem = new StorageItemRequest(
        Name: $"Workflow-{request.Name}",
        Value: request.Data ?? "Default workflow data"
    );

    var storageContent = new StringContent(
        System.Text.Json.JsonSerializer.Serialize(storageItem),
        System.Text.Encoding.UTF8,
        "application/json");

    var storageResponse = await client.PostAsync($"{storageApiUrl}/api/items", storageContent);

    if (!storageResponse.IsSuccessStatusCode)
    {
        logger.LogError("Workflow failed: Could not create storage item");
        return Results.Problem("Workflow failed: Could not create storage item");
    }

    var createdItem = await storageResponse.Content.ReadAsStringAsync();
    logger.LogInformation("Workflow step 1 complete: Created storage item");

    // Step 2: Simulate processing
    await Task.Delay(Random.Shared.Next(100, 300));
    logger.LogInformation("Workflow step 2 complete: Processing done");

    // Step 3: Get analytics summary
    var analyticsResponse = await client.GetAsync($"{analyticsApiUrl}/api/analytics");
    var analytics = await analyticsResponse.Content.ReadAsStringAsync();
    logger.LogInformation("Workflow step 3 complete: Retrieved analytics");

    var result = new
    {
        WorkflowName = request.Name,
        Status = "Completed",
        CreatedItem = System.Text.Json.JsonSerializer.Deserialize<object>(createdItem),
        AnalyticsSummary = System.Text.Json.JsonSerializer.Deserialize<object>(analytics),
        CompletedAt = DateTime.UtcNow
    };

    logger.LogInformation("Complex workflow completed: {Name}", request.Name);

    return Results.Ok(result);
});

app.Run();

public record StorageItemRequest(string Name, string Value);
public record WorkflowRequest(string Name, string? Data);
