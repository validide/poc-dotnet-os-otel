using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using StorageApi;

var builder = WebApplication.CreateBuilder(args);

// Get OTLP endpoint from environment or use default
var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=postgres;Database=storagedb;Username=postgres;Password=postgres";

// Configure DbContext with PostgreSQL
builder.Services.AddDbContext<StorageDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure OpenTelemetry
var serviceName = "StorageApi";
var serviceVersion = "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            // Enable detailed query information
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                // Add command text and parameters to the span
                var parameters = string.Join(", ",
                    command.Parameters.Cast<System.Data.Common.DbParameter>()
                        .Select(p => $"{p.ParameterName}={p.Value}"));
                if (!string.IsNullOrEmpty(parameters))
                {
                    activity.SetTag("db.query_parameters", parameters);
                }
            };
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

// Ensure database is created and migrations are applied
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
    try
    {
        app.Logger.LogInformation("Ensuring database is created...");
        dbContext.Database.EnsureCreated();
        app.Logger.LogInformation("Database is ready");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "An error occurred while creating the database");
    }
}

// API Endpoints
app.MapGet("/", () =>
{
    return Results.Ok(new { message = "Storage API - Use /api/items for CRUD operations" });
});

// GET all items
app.MapGet("/api/items", async (StorageDbContext db, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetAllItems");
    logger.LogInformation("Retrieving all items from database");

    var items = await db.Items.ToListAsync();

    activity?.SetTag("items.count", items.Count);
    logger.LogInformation("Retrieved {Count} items", items.Count);

    return Results.Ok(items);
});

// GET item by ID
app.MapGet("/api/items/{id}", async (int id, StorageDbContext db, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GetItemById");
    activity?.SetTag("item.id", id);
    logger.LogInformation("Retrieving item with ID: {Id}", id);

    var item = await db.Items.FindAsync(id);

    if (item == null)
    {
        logger.LogWarning("Item with ID {Id} not found", id);
        return Results.NotFound(new { error = $"Item with ID {id} not found" });
    }

    logger.LogInformation("Found item: {Name}", item.Name);
    return Results.Ok(item);
});

// POST new item
app.MapPost("/api/items", async (StorageItem item, StorageDbContext db, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("CreateItem");
    activity?.SetTag("item.name", item.Name);
    logger.LogInformation("Creating new item: {Name}", item.Name);

    item.CreatedAt = DateTime.UtcNow;
    db.Items.Add(item);
    await db.SaveChangesAsync();

    activity?.SetTag("item.id", item.Id);
    logger.LogInformation("Created item with ID: {Id}", item.Id);

    return Results.Created($"/api/items/{item.Id}", item);
});

// PUT update item
app.MapPut("/api/items/{id}", async (int id, StorageItem updatedItem, StorageDbContext db, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("UpdateItem");
    activity?.SetTag("item.id", id);
    logger.LogInformation("Updating item with ID: {Id}", id);

    var item = await db.Items.FindAsync(id);
    if (item == null)
    {
        logger.LogWarning("Item with ID {Id} not found for update", id);
        return Results.NotFound(new { error = $"Item with ID {id} not found" });
    }

    item.Name = updatedItem.Name;
    item.Value = updatedItem.Value;
    item.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    logger.LogInformation("Updated item: {Name}", item.Name);
    return Results.Ok(item);
});

// DELETE item
app.MapDelete("/api/items/{id}", async (int id, StorageDbContext db, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("DeleteItem");
    activity?.SetTag("item.id", id);
    logger.LogInformation("Deleting item with ID: {Id}", id);

    var item = await db.Items.FindAsync(id);
    if (item == null)
    {
        logger.LogWarning("Item with ID {Id} not found for deletion", id);
        return Results.NotFound(new { error = $"Item with ID {id} not found" });
    }

    db.Items.Remove(item);
    await db.SaveChangesAsync();

    logger.LogInformation("Deleted item: {Name}", item.Name);
    return Results.NoContent();
});

app.Run();
