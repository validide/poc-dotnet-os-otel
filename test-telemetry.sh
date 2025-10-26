#!/bin/bash

echo "=== OpenTelemetry + OpenSearch Test Script ==="
echo ""

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test counter
TESTS_PASSED=0
TESTS_FAILED=0

# Function to test HTTP endpoint
test_endpoint() {
    local url=$1
    local expected_status=$2
    local description=$3
    local method=${4:-GET}
    local data=$5

    if [ "$method" = "POST" ] && [ -n "$data" ]; then
        actual_status=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$url" \
            -H "Content-Type: application/json" \
            -d "$data")
    else
        actual_status=$(curl -s -o /dev/null -w "%{http_code}" "$url")
    fi

    if [ "$actual_status" = "$expected_status" ]; then
        echo -e "  ${GREEN}✓${NC} $description (Expected: $expected_status, Got: $actual_status)"
        ((TESTS_PASSED++))
        return 0
    else
        echo -e "  ${RED}✗${NC} $description (Expected: $expected_status, Got: $actual_status)"
        ((TESTS_FAILED++))
        return 1
    fi
}

echo "1. Checking if services are running..."
docker compose ps

echo ""
echo "2. Waiting for services to initialize..."
sleep 5

echo ""
echo -e "${BLUE}3. Running Service Health Checks...${NC}"
echo ""

# Main API Health Checks
echo "Main API (http://localhost:5233):"
test_endpoint "http://localhost:5233/" "200" "GET / - Root endpoint"
test_endpoint "http://localhost:5233/api/data" "200" "GET /api/data - Normal trace"
test_endpoint "http://localhost:5233/api/error" "500" "GET /api/error - Error trace"
test_endpoint "http://localhost:5233/api/chain" "200" "GET /api/chain - Chained trace"

echo ""

# Storage API Health Checks (via Main API)
echo "Storage API endpoints (via Main API):"
test_endpoint "http://localhost:5233/api/storage/items" "200" "GET /api/storage/items - List items"
test_endpoint "http://localhost:5233/api/storage/items" "201" "POST /api/storage/items - Create item" "POST" '{"name":"HealthCheckItem","value":"Test"}'
test_endpoint "http://localhost:5233/api/storage/items/1" "200" "GET /api/storage/items/1 - Get item by ID"
test_endpoint "http://localhost:5233/api/storage/items/999" "404" "GET /api/storage/items/999 - Not found"

echo ""

# Storage API Direct Health Checks
echo "Storage API (http://localhost:5234) - Direct access:"
test_endpoint "http://localhost:5234/" "200" "GET / - Root endpoint"
test_endpoint "http://localhost:5234/api/items" "200" "GET /api/items - List items"

echo ""

# Analytics API Health Checks (via Main API)
echo "Analytics API endpoints (via Main API):"
test_endpoint "http://localhost:5233/api/analytics/summary" "200" "GET /api/analytics/summary - Get summary"

echo ""

# Analytics API Direct Health Checks
echo "Analytics API (http://localhost:5235) - Direct access:"
test_endpoint "http://localhost:5235/" "200" "GET / - Root endpoint"
test_endpoint "http://localhost:5235/api/analytics" "200" "GET /api/analytics - Get all analytics"
test_endpoint "http://localhost:5235/api/analytics/track" "200" "POST /api/analytics/track - Track call" "POST" '{"endpoint":"/test","method":"GET"}'

echo ""

# Workflow endpoint
echo "Complex Workflow:"
test_endpoint "http://localhost:5233/api/workflow" "200" "POST /api/workflow - Execute workflow" "POST" '{"name":"HealthCheck","data":"test"}'

echo ""
echo "================================================"
echo -e "${GREEN}Tests Passed: $TESTS_PASSED${NC}"
echo -e "${RED}Tests Failed: $TESTS_FAILED${NC}"
echo "================================================"

if [ $TESTS_FAILED -gt 0 ]; then
    echo ""
    echo -e "${RED}⚠ Some health checks failed. Please review the errors above.${NC}"
    echo "Check container logs:"
    echo "  docker logs main-app"
    echo "  docker logs storage-api"
    echo "  docker logs analytics-api"
    echo ""
    read -p "Do you want to continue with telemetry generation? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborting."
        exit 1
    fi
else
    echo -e "${GREEN}✓ All health checks passed! Proceeding with telemetry generation...${NC}"
fi

echo ""
echo -e "${BLUE}4. Main API - Generating normal traces (GET /api/data)...${NC}"
for i in {1..20}; do
    echo "  Request $i/20..."
    curl -s http://localhost:5233/api/data > /dev/null
done

echo ""
echo -e "${BLUE}5. Main API - Generating error traces (GET /api/error)...${NC}"
for i in {1..10}; do
    echo "  Error request $i/10..."
    curl -s http://localhost:5233/api/error > /dev/null
done

echo ""
echo -e "${BLUE}6. Main API - Generating chained traces (GET /api/chain)...${NC}"
for i in {1..10}; do
    echo "  Chained request $i/10..."
    curl -s http://localhost:5233/api/chain > /dev/null
done

echo ""
echo -e "${BLUE}7. Storage API (via Main API) - Creating items (POST /api/storage/items)...${NC}"
for i in {1..15}; do
    echo "  Creating storage item $i/15..."
    curl -s -X POST http://localhost:5233/api/storage/items \
        -H "Content-Type: application/json" \
        -d "{\"name\":\"TestItem$i\",\"value\":\"Value for item $i\"}" > /dev/null
done

echo ""
echo -e "${BLUE}8. Storage API (via Main API) - Listing all items (GET /api/storage/items)...${NC}"
for i in {1..10}; do
    echo "  Fetching all items - request $i/10..."
    curl -s http://localhost:5233/api/storage/items > /dev/null
done

echo ""
echo -e "${BLUE}9. Storage API (via Main API) - Getting items by ID (GET /api/storage/items/{id})...${NC}"
for i in {1..15}; do
    item_id=$((i % 15 + 1))
    echo "  Fetching item ID $item_id - request $i/15..."
    curl -s http://localhost:5233/api/storage/items/$item_id > /dev/null
done

echo ""
echo -e "${BLUE}10. Storage API (Direct) - Listing items (GET /api/items)...${NC}"
for i in {1..10}; do
    echo "  Direct fetch request $i/10..."
    curl -s http://localhost:5234/api/items > /dev/null
done

echo ""
echo -e "${BLUE}11. Storage API (Direct) - Creating items (POST /api/items)...${NC}"
for i in {1..10}; do
    echo "  Direct create request $i/10..."
    curl -s -X POST http://localhost:5234/api/items \
        -H "Content-Type: application/json" \
        -d "{\"name\":\"DirectItem$i\",\"value\":\"Direct value $i\"}" > /dev/null
done

echo ""
echo -e "${BLUE}12. Storage API (Direct) - Getting items by ID (GET /api/items/{id})...${NC}"
for i in {1..10}; do
    item_id=$((i % 10 + 1))
    echo "  Direct get by ID request $i/10..."
    curl -s http://localhost:5234/api/items/$item_id > /dev/null
done

echo ""
echo -e "${BLUE}13. Storage API (Direct) - Updating items (PUT /api/items/{id})...${NC}"
for i in {1..10}; do
    item_id=$((i % 10 + 1))
    echo "  Updating item ID $item_id - request $i/10..."
    curl -s -X PUT http://localhost:5234/api/items/$item_id \
        -H "Content-Type: application/json" \
        -d "{\"name\":\"UpdatedItem$i\",\"value\":\"Updated value $i\"}" > /dev/null
done

echo ""
echo -e "${BLUE}14. Storage API (Direct) - Deleting items (DELETE /api/items/{id})...${NC}"
for i in {1..5}; do
    item_id=$((20 + i))
    echo "  Deleting item ID $item_id - request $i/5..."
    curl -s -X DELETE http://localhost:5234/api/items/$item_id > /dev/null
done

echo ""
echo -e "${BLUE}15. Analytics API (via Main API) - Getting summary (GET /api/analytics/summary)...${NC}"
for i in {1..10}; do
    echo "  Fetching analytics summary - request $i/10..."
    curl -s http://localhost:5233/api/analytics/summary > /dev/null
done

echo ""
echo -e "${BLUE}16. Analytics API (via Main API) - Getting endpoint stats (GET /api/analytics/{endpoint})...${NC}"
for i in {1..10}; do
    echo "  Fetching endpoint analytics - request $i/10..."
    curl -s http://localhost:5233/api/analytics/api:storage:items > /dev/null
done

echo ""
echo -e "${BLUE}17. Analytics API (Direct) - Getting all analytics (GET /api/analytics)...${NC}"
for i in {1..10}; do
    echo "  Direct analytics fetch - request $i/10..."
    curl -s http://localhost:5235/api/analytics > /dev/null
done

echo ""
echo -e "${BLUE}18. Analytics API (Direct) - Tracking calls (POST /api/analytics/track)...${NC}"
for i in {1..15}; do
    endpoint="/test/endpoint$((i % 5 + 1))"
    method=$([ $((i % 2)) -eq 0 ] && echo "GET" || echo "POST")
    echo "  Tracking $method $endpoint - request $i/15..."
    curl -s -X POST http://localhost:5235/api/analytics/track \
        -H "Content-Type: application/json" \
        -d "{\"endpoint\":\"$endpoint\",\"method\":\"$method\"}" > /dev/null
done

echo ""
echo -e "${BLUE}19. Analytics API (Direct) - Getting specific endpoint analytics (GET /api/analytics/{endpoint})...${NC}"
for i in {1..10}; do
    endpoint="api:storage:items"
    echo "  Getting specific endpoint analytics - request $i/10..."
    curl -s http://localhost:5235/api/analytics/$endpoint > /dev/null
done

echo ""
echo -e "${BLUE}20. Analytics API (Direct) - Clearing analytics (DELETE /api/analytics/{endpoint})...${NC}"
for i in {1..5}; do
    endpoint="test:endpoint$i"
    echo "  Clearing analytics for $endpoint - request $i/5..."
    curl -s -X DELETE http://localhost:5235/api/analytics/$endpoint > /dev/null
done

echo ""
echo -e "${BLUE}21. Complex Workflows (POST /api/workflow)...${NC}"
for i in {1..15}; do
    echo "  Running workflow $i/15..."
    curl -s -X POST http://localhost:5233/api/workflow \
        -H "Content-Type: application/json" \
        -d "{\"name\":\"Workflow$i\",\"data\":\"Processing batch $i with complex operations\"}" > /dev/null
done

echo ""
echo -e "${BLUE}22. Mixed workload simulation...${NC}"
echo "  Simulating real-world traffic with mixed requests..."
for i in {1..30}; do
    case $((i % 6)) in
        0)
            curl -s http://localhost:5233/api/data > /dev/null
            ;;
        1)
            curl -s http://localhost:5233/api/storage/items > /dev/null
            ;;
        2)
            curl -s http://localhost:5234/api/items/$((i % 15 + 1)) > /dev/null
            ;;
        3)
            curl -s http://localhost:5235/api/analytics > /dev/null
            ;;
        4)
            curl -s http://localhost:5233/api/chain > /dev/null
            ;;
        5)
            curl -s -X POST http://localhost:5233/api/workflow \
                -H "Content-Type: application/json" \
                -d "{\"name\":\"MixedWorkflow$i\",\"data\":\"data\"}" > /dev/null
            ;;
    esac
    [ $((i % 10)) -eq 0 ] && echo "    Completed $i/30 mixed requests..."
done

echo ""
echo -e "${GREEN}✓ Telemetry generation complete!${NC}"
echo "  Total API calls made: ~250+"
echo "  - Main API: ~80 calls"
echo "  - Storage API: ~90 calls"
echo "  - Analytics API: ~60 calls"
echo "  - Mixed workload: 30 calls"

echo ""
echo -e "${BLUE}23. Waiting for data to be processed...${NC}"
echo "  Allowing time for OTEL Collector and Data Prepper to process all traces..."
sleep 15

echo ""
echo -e "${BLUE}24. Checking OpenSearch indices...${NC}"
echo "Expected indices: otel-v1-apm-span-*, otel-v1-apm-service-map, otel-logs-*"
curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj https://localhost:9200/_cat/indices?v | grep -E "otel|apm"

echo ""
echo -e "${BLUE}25. Counting traces in OpenSearch...${NC}"
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
echo -e "${BLUE}26. Sample trace data from Observability indices:${NC}"
curl -k -s -u admin:6fGjWHZukijsP5^PJ2zGj "https://localhost:9200/otel-v1-apm-span-*/_search?size=1&pretty" | head -50

echo ""
echo "================================================"
echo "=== Test Complete ==="
echo "================================================"
echo ""
echo -e "${GREEN}Summary:${NC}"
echo ""
echo "Health Checks:"
echo "  ✓ Tests Passed: $TESTS_PASSED"
if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "  ${RED}✗ Tests Failed: $TESTS_FAILED${NC}"
else
    echo "  ✗ Tests Failed: $TESTS_FAILED"
fi
echo ""
echo "Telemetry Generated:"
echo "  • Total Traces: $TRACE_COUNT spans"
echo "  • Service Map Entries: $SERVICE_MAP_COUNT"
echo "  • API Calls Made: ~250+"
echo ""
echo "Endpoints Tested:"
echo "  Main API (localhost:5233):"
echo "    - GET /api/data (20 calls)"
echo "    - GET /api/error (10 calls)"
echo "    - GET /api/chain (10 calls)"
echo "    - GET /api/storage/items (10 calls)"
echo "    - POST /api/storage/items (15 calls)"
echo "    - GET /api/storage/items/{id} (15 calls)"
echo "    - GET /api/analytics/summary (10 calls)"
echo "    - GET /api/analytics/{endpoint} (10 calls)"
echo "    - POST /api/workflow (15 calls)"
echo ""
echo "  Storage API (localhost:5234):"
echo "    - GET /api/items (10 calls)"
echo "    - POST /api/items (10 calls)"
echo "    - GET /api/items/{id} (10 calls)"
echo "    - PUT /api/items/{id} (10 calls)"
echo "    - DELETE /api/items/{id} (5 calls)"
echo ""
echo "  Analytics API (localhost:5235):"
echo "    - GET /api/analytics (10 calls)"
echo "    - POST /api/analytics/track (15 calls)"
echo "    - GET /api/analytics/{endpoint} (10 calls)"
echo "    - DELETE /api/analytics/{endpoint} (5 calls)"
echo ""
echo "  Mixed Workload: 30 randomized calls"
echo ""
echo "================================================"
echo ""
echo "Access OpenSearch Dashboards: http://localhost:5601"
echo "  Username: admin"
echo "  Password: 6fGjWHZukijsP5^PJ2zGj"
echo ""
echo "View Trace Analytics:"
echo "  1. Navigate to: Observability → Trace Analytics"
echo "  2. Explore the Dashboard, Services, and Traces tabs"
echo "  3. Check the Service Map for distributed tracing visualization"
echo ""