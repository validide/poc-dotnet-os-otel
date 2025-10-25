# OpenTelemetry + OpenSearch 3 Demo

A minimal proof of concept demonstrating OpenTelemetry integration with OpenSearch 3 using .NET.

## Architecture

```
.NET API → OpenTelemetry Collector → Data Prepper → OpenSearch → OpenSearch Dashboards
```

- **ASP.NET Core API**: Generates traces, metrics, and logs
- **OpenTelemetry Collector**: Receives and processes telemetry data
- **Data Prepper**: Transforms telemetry data for OpenSearch
- **OpenSearch**: Stores telemetry data
- **OpenSearch Dashboards**: Visualizes the data

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

- **API**: http://localhost:5233
  - `GET /` - Info
  - `GET /api/data` - Generate trace with random data
  - `GET /api/error` - Generate error trace
  - `GET /api/chain` - Generate parent-child trace spans

- **OpenSearch**: http://localhost:9200 (admin/6fGjWHZukijsP5^PJ2zGj)
- **OpenSearch Dashboards**: http://localhost:5601 (admin/6fGjWHZukijsP5^PJ2zGj)
- **OTEL Collector**: 
  - gRPC: localhost:4317
  - HTTP: localhost:4318

## What's Being Collected

### Traces
- HTTP request spans from ASP.NET Core
- Custom spans with tags and attributes
- Error spans with exception details
- Distributed tracing across services

### Logs
- Structured logs with correlation to traces
- Log levels (Information, Warning, Error)
- Exception logs

### Metrics
- HTTP request metrics
- Request duration
- Active requests

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

- Add custom instrumentation in your code
- Configure sampling strategies
- Set up alerts based on trace data
- Create custom dashboards in OpenSearch
- Add more exporters (Jaeger, Zipkin, etc.)
- Implement distributed tracing across multiple services

## Notes

- This is a **development setup** with security plugin enabled but using default credentials
- For production, use proper authentication, TLS, and resource limits
- Data Prepper transforms OTLP data to OpenSearch-compatible format
- All telemetry is correlated via `traceId` and `spanId`