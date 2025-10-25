using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
var serviceName = "MinimalOtelDemo";
var serviceVersion = "1.0.0";
var otlpEndpoint = builder.Configuration["OTLP_ENDPOINT"] ?? "http://otel-collector:4317";

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

app.MapGet("/", () => "OpenTelemetry + OpenSearch Demo - Try /api/data or /api/error");

app.MapGet("/api/data", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetData");
    activity?.SetTag("operation", "fetch-data");

    logger.LogInformation("Processing data request");

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

app.Run();