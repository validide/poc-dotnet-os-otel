# OpenTelemetry + OpenSearch 3 Demo

A proof of concept demonstrating OpenTelemetry integration with OpenSearch 3 using .NET, featuring distributed tracing across multiple microservices with PostgreSQL and Valkey (Redis).

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Main API (5233)                         │
│                  Orchestrates requests across                   │
│                   Storage API & Analytics API                   │
└────────┬──────────────────────────────────┬───────────────────┘
         │                                   │
         ▼                                   ▼
┌─────────────────────┐           ┌─────────────────────┐
│  Storage API (5234) │           │ Analytics API (5235)│
│  - EF Core          │           │  - StackExchange    │
│  - PostgreSQL DB    │           │  - Valkey (Redis)   │
└──────────┬──────────┘           └──────────┬──────────┘
           │                                  │
           ▼                                  ▼
    ┌──────────┐                      ┌──────────┐
    │PostgreSQL│                      │  Valkey  │
    └──────────┘                      └──────────┘

                        ↓ All Services Send Telemetry ↓

┌─────────────────────────────────────────────────────────────────┐
│           OpenTelemetry Collector (4317, 4318)                  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Data Prepper (21890-21892)                     │
│              Transforms OTLP → OpenSearch format                │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      OpenSearch (9200)                          │
│          Stores traces, logs, metrics with indices              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                 OpenSearch Dashboards (5601)                    │
│              Visualizes telemetry with Trace Analytics          │
└─────────────────────────────────────────────────────────────────┘
```

### Services

- **Main API (MinimalOtelDemo)**: Orchestrates requests, coordinates between Storage and Analytics APIs
- **Storage API**: Manages persistent data using EF Core with PostgreSQL
- **Analytics API**: Tracks API call statistics using Valkey (Redis-compatible)
- **PostgreSQL**: Relational database for storage API
- **Valkey**: In-memory data store for analytics (Redis-compatible)
- **OpenTelemetry Collector**: Receives and processes telemetry data (OTLP protocol)
- **Data Prepper**: Transforms telemetry data for OpenSearch compatibility
- **OpenSearch**: Stores telemetry data with optimized indices
- **OpenSearch Dashboards**: Visualizes distributed traces, service maps, and logs

## Prerequisites

- Docker and Docker Compose
- .NET SDK (for local development)

## Quick Start

1. **Clone or create the project structure** with all the files provided.

2. **Start the stack**:
   ```bash
   docker compose up --build
   ```

3. **Wait for services to be healthy** (about 30-60 seconds):
   - OpenSearch will be ready when you see "Node started"
   - All services will start automatically

4. **Generate some telemetry data**:
   ```bash
   # Normal request with trace
   curl http://localhost:5233/api/data

   # Simulate an error
   curl http://localhost:5233/api/error

   # Create a chained request (parent-child spans)
   curl http://localhost:5233/api/chain

   # Create items in Storage API (PostgreSQL + EF Core)
   curl -X POST http://localhost:5233/api/storage/items \
     -H "Content-Type: application/json" \
     -d '{"name":"TestItem","value":"TestValue"}'

   # Retrieve items from Storage API
   curl http://localhost:5233/api/storage/items

   # Get analytics summary
   curl http://localhost:5233/api/analytics/summary

   # Run complex workflow (Storage + Analytics + distributed tracing)
   curl -X POST http://localhost:5233/api/workflow \
     -H "Content-Type: application/json" \
     -d '{"name":"MyWorkflow","data":"Some workflow data"}'
   ```

   Or use the automated test script:
   ```bash
   bash test-telemetry.sh
   ```

5. **Access OpenSearch Dashboards**:
   - URL: http://localhost:5601
   - Username: `admin`
   - Password: `6fGjWHZukijsP5^PJ2zGj`

## Viewing Telemetry Data

### Observability Plugin (Recommended for Traces)

1. Go to **Observability** from the main menu
2. Click on **Trace Analytics**
3. You should see:
   - **Dashboard**: Overview of trace groups, latency trends, and error rates
   - **Services**: Service map showing relationships between services
   - **Traces**: Individual traces with detailed spans

The Observability plugin provides a much better experience for trace analysis with:
- Service dependency maps
- Latency percentiles (p50, p90, p99)
- Error rate tracking
- Trace group analysis

**Note**: The Observability plugin uses specific index patterns:
- Spans: `otel-v1-apm-span-*` 
- Service map: `otel-v1-apm-service-map`

These are automatically created by Data Prepper when using `index_type: trace-analytics-raw` and `index_type: trace-analytics-service-map`.

### Option 2: Index Patterns (Discover)

1. Go to **Management** → **Stack Management** → **Index Patterns**
2. Create index pattern: `otel-v1-apm-span-*`
3. Go to **Discover** and select the pattern
4. You should see all traces with fields like `spanId`, `traceId`, `serviceName`, etc.

### Option 3: Dev Tools (Direct Query)

1. Go to **Management** → **Dev Tools**
2. Query traces:
   ```json
   GET otel-traces-*/_search
   {
     "size": 10,
     "sort": [{"startTime": "desc"}]
   }
   ```

3. Query logs:
   ```json
   GET otel-logs-*/_search
   {
     "size": 10,
     "sort": [{"observedTimestamp": "desc"}]
   }
   ```

4. Query service map:
   ```json
   GET otel-service-map/_search
   ```

### Option 4: Pre-configured Observability Dashboards

The Observability plugin may come with pre-built dashboards. Look for:
1. **Observability** → **Trace Analytics** → **Dashboard**
2. Pre-aggregated metrics like trace counts, latency histograms, and error rates

## Key Endpoints

### Main API (http://localhost:5233)
**Basic Endpoints:**
- `GET /` - Info
- `GET /api/data` - Generate trace with random data
- `GET /api/error` - Generate error trace
- `GET /api/chain` - Generate parent-child trace spans

**Storage API Endpoints (PostgreSQL + EF Core):**
- `GET /api/storage/items` - List all items from storage
- `GET /api/storage/items/{id}` - Get specific item by ID
- `POST /api/storage/items` - Create new item
  ```json
  {"name": "ItemName", "value": "ItemValue"}
  ```

**Analytics Endpoints (Valkey/Redis):**
- `GET /api/analytics/summary` - Get all tracked endpoints statistics
- `GET /api/analytics/{endpoint}` - Get statistics for specific endpoint

**Complex Workflows:**
- `POST /api/workflow` - Execute complex workflow (Storage + Analytics + distributed tracing)
  ```json
  {"name": "WorkflowName", "data": "Optional workflow data"}
  ```

### Storage API (http://localhost:5234)
Direct access to storage API:
- `GET /api/items` - List all items
- `GET /api/items/{id}` - Get item by ID
- `POST /api/items` - Create item
- `PUT /api/items/{id}` - Update item
- `DELETE /api/items/{id}` - Delete item

### Analytics API (http://localhost:5235)
Direct access to analytics API:
- `GET /api/analytics` - Get all tracked endpoints
- `GET /api/analytics/{endpoint}` - Get endpoint statistics
- `POST /api/analytics/track` - Track an API call
- `DELETE /api/analytics/{endpoint}` - Clear endpoint analytics

### Infrastructure Services
- **PostgreSQL**: localhost:5432 (postgres/postgres)
- **Valkey (Redis)**: localhost:6379
- **OpenSearch**: http://localhost:9200 (admin/6fGjWHZukijsP5^PJ2zGj)
- **OpenSearch Dashboards**: http://localhost:5601 (admin/6fGjWHZukijsP5^PJ2zGj)
- **OTEL Collector**:
  - gRPC: localhost:4317
  - HTTP: localhost:4318
  - Metrics: localhost:8888
- **Data Prepper**:
  - OTLP Traces gRPC: localhost:21890
  - OTLP Traces HTTP: localhost:21891
  - OTLP Logs gRPC: localhost:21892

## What's Being Collected

### Traces
- **HTTP request spans** from ASP.NET Core (all three APIs)
- **Database operations** from EF Core (Storage API → PostgreSQL)
  - SQL queries with parameters
  - Transaction spans
  - Connection pool operations
- **Redis operations** from StackExchange.Redis (Analytics API → Valkey)
  - GET, SET, INCR commands
  - Sorted set operations
  - Key deletion operations
- **Custom spans** with tags and attributes
- **Error spans** with exception details and stack traces
- **Distributed tracing** across all microservices with context propagation
- **Parent-child span relationships** showing service dependencies

### Logs
- Structured logs with correlation to traces
- Log levels (Information, Warning, Error)
- Exception logs

### Metrics
- HTTP request metrics
- Request duration
- Active requests

## Distributed Tracing Showcase

This POC demonstrates several distributed tracing patterns:

### 1. **Service-to-Service Communication**
The Main API orchestrates requests across Storage and Analytics APIs, creating a complete trace that spans all three services:
```
Main API → Storage API → PostgreSQL
         → Analytics API → Valkey
```

### 2. **Database Instrumentation**
- **EF Core** traces show:
  - SQL query execution time
  - Query text and parameters
  - Connection management
  - Transaction boundaries
- **Redis** traces show:
  - Command type (GET, SET, ZADD, etc.)
  - Key names
  - Command execution time

### 3. **Automatic Context Propagation**
OpenTelemetry automatically propagates trace context across:
- HTTP calls between services
- Database operations
- Redis operations
- Custom spans

### 4. **Service Map Visualization**
OpenSearch Observability creates a service map showing:
- Main API dependencies on Storage and Analytics APIs
- Storage API dependency on PostgreSQL
- Analytics API dependency on Valkey
- Request rates and error rates between services

### 5. **Complex Workflow Example**
The `/api/workflow` endpoint demonstrates a full distributed trace:
1. Main API receives request
2. Calls Storage API to create item (PostgreSQL insert)
3. Tracks analytics (Valkey increment)
4. Simulates processing
5. Retrieves analytics summary (Valkey sorted set query)
6. Returns aggregated result

All operations are correlated in a single trace with parent-child span relationships.

## OpenTelemetry Instrumentation Libraries Used

- **OpenTelemetry.Instrumentation.AspNetCore** - HTTP server instrumentation
- **OpenTelemetry.Instrumentation.Http** - HTTP client instrumentation
- **OpenTelemetry.Instrumentation.EntityFrameworkCore** - EF Core database instrumentation
- **OpenTelemetry.Instrumentation.StackExchangeRedis** - Redis client instrumentation
- **OpenTelemetry.Exporter.OpenTelemetryProtocol** - OTLP exporter

## Troubleshooting

**No traces visible in OpenSearch:**

1. **Check if services are running:**
   ```bash
   docker compose ps
   ```

2. **Verify the .NET app is sending telemetry:**
   ```bash
   docker logs dotnet-app
   ```

3. **Check OpenTelemetry Collector logs:**
   ```bash
   docker logs otel-collector
   ```
   You should see traces being received and exported.

4. **Check Data Prepper logs:**
   ```bash
   docker logs data-prepper
   ```
   Look for any errors about connecting to OpenSearch.

5. **Verify indices were created:**
   ```bash
   curl -k -u admin:6fGjWHZukijsP5^PJ2zGj https://localhost:9200/_cat/indices?v
   ```
   You should see indices like:
   - `otel-v1-apm-span-000001` (or similar with date)
   - `otel-v1-apm-service-map`
   - `otel-logs-2024.10.26`

6. **Check if data is in OpenSearch:**
   ```bash
   # Check spans
   curl -k -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-span-*/_count"
   
   # Check service map
   curl -k -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-service-map/_count"
   ```

7. **Generate more traffic:**
   ```bash
   for i in {1..10}; do curl http://localhost:5233/api/data; sleep 1; done
   for i in {1..10}; do curl http://localhost:5233/api/error; sleep 1; done
   for i in {1..10}; do curl http://localhost:5233/api/chain; sleep 1; done
   ```

8. **Check collector debug output:**
   The collector is configured with detailed debug logging. Check the logs for any OTLP receiver errors.

**Services not starting:**
```bash
docker compose down -v
docker compose up --build
```

**No data in OpenSearch:**
1. Check collector logs: `docker logs otel-collector`
2. Check data-prepper logs: `docker logs data-prepper`
3. Verify indices exist: `curl -k -u admin:6fGjWHZukijsP5^PJ2zGj https://localhost:9200/_cat/indices`

**Connection refused errors:**
- Wait 30-60 seconds for all services to initialize
- OpenSearch takes the longest to start

## Cleanup

```bash
docker compose down -v
```

The `-v` flag removes volumes, ensuring a clean state for next run.

## Next Steps

### Expand the Architecture
- Add message queue (RabbitMQ, Kafka) with OpenTelemetry instrumentation
- Add MongoDB with OpenTelemetry.Instrumentation.MongoDB
- Implement gRPC services with OpenTelemetry.Instrumentation.GrpcNetClient
- Add background workers with OpenTelemetry instrumentation

### Enhance Observability
- Configure **sampling strategies** (head-based, tail-based sampling in collector)
- Set up **alerts** based on trace data (error rates, latency thresholds)
- Create **custom dashboards** in OpenSearch
- Add **span events** for significant operations
- Implement **baggage** for cross-cutting concerns (tenant ID, user ID)

### Production Readiness
- Configure **authentication and authorization** (proper credentials, OAuth)
- Enable **TLS/SSL** for all services
- Set up **resource limits** (memory, CPU) for containers
- Implement **rate limiting** on APIs
- Add **health checks** for all services
- Configure **log aggregation** patterns

### Advanced Tracing
- Add custom **metrics** (counters, histograms, gauges)
- Implement **profiling** integration
- Add **correlation** between traces and logs
- Use **exemplars** to link metrics to traces
- Implement **trace context injection** for asynchronous operations

## Notes

- This is a **development setup** with security plugin enabled but using default credentials
- For production, use proper authentication, TLS, and resource limits
- Data Prepper transforms OTLP data to OpenSearch-compatible format
- All telemetry is correlated via `traceId` and `spanId`