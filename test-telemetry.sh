#!/bin/bash

echo "=== OpenTelemetry + OpenSearch Test Script ==="
echo ""

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "1. Checking if services are running..."
docker compose ps

echo ""
echo "2. Waiting for services to be ready..."
sleep 5

echo ""
echo "3. Generating telemetry data..."
for i in {1..5}; do
    echo "  Request $i/5..."
    curl -s http://localhost:5233/api/data > /dev/null
    sleep 1
done

echo ""
echo "4. Generating an error trace..."
curl -s http://localhost:5233/api/error > /dev/null

echo ""
echo "5. Generating a chained trace..."
curl -s http://localhost:5233/api/chain > /dev/null

echo ""
echo "6. Waiting for data to be processed..."
sleep 10

echo ""
echo "7. Checking OpenSearch indices..."
echo "Expected indices: otel-v1-apm-span-*, otel-v1-apm-service-map, otel-logs-*"
curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj https://localhost:9200/_cat/indices?v | grep -E "otel|apm"

echo ""
echo "8. Counting traces in OpenSearch..."
TRACE_COUNT=$(curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-span-*/_count" 2>/dev/null | grep -o '"count":[0-9]*' | grep -o '[0-9]*')
SERVICE_MAP_COUNT=$(curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-service-map/_count" 2>/dev/null | grep -o '"count":[0-9]*' | grep -o '[0-9]*')

echo "  Spans: $TRACE_COUNT"
echo "  Service map entries: $SERVICE_MAP_COUNT"

if [ "$TRACE_COUNT" -gt 0 ]; then
    echo -e "${GREEN}✓ Success! Traces are being stored in OpenSearch${NC}"
    echo ""
    echo "Access the Observability plugin:"
    echo "  1. Go to http://localhost:5601"
    echo "  2. Login with admin/6fGjWHZukijsP5^PJ2zGj"
    echo "  3. Navigate to: Observability → Trace Analytics"
    echo "  4. You should see the Dashboard, Services, and Traces tabs"
else
    echo -e "${RED}✗ No traces found. Checking logs...${NC}"
    echo ""
    echo "=== Collector Logs (last 20 lines) ==="
    docker logs otel-collector --tail 20
    echo ""
    echo "=== Data Prepper Logs (last 20 lines) ==="
    docker logs data-prepper --tail 20
fi

echo ""
echo "9. Sample trace data from Observability indices:"
curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-span-*/_search?size=1&pretty" | head -50

echo ""
echo "=== Test Complete ==="
echo "Access OpenSearch Dashboards at: http://localhost:5601"
echo "Username: admin"
echo "Password: 6fGjWHZukijsP5^PJ2zGj"