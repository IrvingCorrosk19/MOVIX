#!/bin/bash
BASE="http://127.0.0.1:55392"
TENANT_ID="00000000-0000-0000-0000-000000000001"
TS=$(date +%s)
LOG="/c/Proyectos/RiderFlow/tests/qa_evidence_raw.log"
> "$LOG"

log() { echo "$1" | tee -a "$LOG"; }

http_call() {
  local label="$1"
  local method="$2"
  local url="$3"
  shift 3
  local tmpfile
  tmpfile=$(mktemp -p /c/Proyectos/RiderFlow/tests/)
  local http_code
  http_code=$(curl -s -o "$tmpfile" -w "%{http_code}" -X "$method" "$url" "$@" 2>&1)
  local body
  body=$(cat "$tmpfile")
  rm -f "$tmpfile"
  log "[$label] HTTP $http_code"
  log "  Body: $(echo "$body" | head -c 500)"
  echo "${http_code}|${body}"
}

log "================================================================"
log "QA REAL EXECUTION - $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
log "Base URL: $BASE"
log "================================================================"
log ""
log "## INFRA CHECKS"

# Health
log "### Health"
result=$(http_call "GET /health" GET "$BASE/health")
HEALTH_HTTP=$(echo "$result" | cut -d'|' -f1)
[ "$HEALTH_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL"

# Ready
log "### Ready"
result=$(http_call "GET /ready" GET "$BASE/ready")
READY_HTTP=$(echo "$result" | cut -d'|' -f1)
[ "$READY_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL"

log ""
log "## LAYER 1: AUTH + INFRASTRUCTURE BASE"

# 1. Login Admin
log "### 1.1 Login Admin"
result=$(http_call "Login Admin" POST "$BASE/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  --data-raw '{"email":"admin@movix.io","password":"Admin@1234!"}')
ADMIN_HTTP=$(echo "$result" | cut -d'|' -f1)
ADMIN_BODY=$(echo "$result" | cut -d'|' -f2-)
ADMIN_TOKEN=$(echo "$ADMIN_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('accessToken',''))" 2>/dev/null)
if [ "$ADMIN_HTTP" = "200" ] && [ -n "$ADMIN_TOKEN" ]; then
  log "  RESULT: PASS"
else
  log "  RESULT: FAIL - cannot proceed without admin token"
  exit 1
fi

# 2. Login Driver
log "### 1.2 Login Driver"
result=$(http_call "Login Driver" POST "$BASE/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  --data-raw '{"email":"driver@movix.io","password":"Driver@1234!"}')
DRIVER_HTTP=$(echo "$result" | cut -d'|' -f1)
DRIVER_BODY=$(echo "$result" | cut -d'|' -f2-)
DRIVER_TOKEN=$(echo "$DRIVER_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('accessToken',''))" 2>/dev/null)
[ "$DRIVER_HTTP" = "200" ] && log "  RESULT: PASS" || { log "  RESULT: FAIL"; exit 1; }

# 3. Driver Status Online
log "### 1.3 Driver Status Online"
result=$(http_call "Driver Status Online" POST "$BASE/api/v1/drivers/status" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  --data-raw '{"status":1}')
DSTATUS_HTTP=$(echo "$result" | cut -d'|' -f1)
[ "$DSTATUS_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL (HTTP $DSTATUS_HTTP)"

# 4. Driver Location
log "### 1.4 Driver Location Update"
result=$(http_call "Driver Location" POST "$BASE/api/v1/drivers/location" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  --data-raw '{"latitude":19.4326,"longitude":-99.1332}')
DLOC_HTTP=$(echo "$result" | cut -d'|' -f1)
[ "$DLOC_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL (HTTP $DLOC_HTTP)"

# 5. Register Passenger (unique email)
log "### 1.5 Register Passenger"
PASS_EMAIL="passenger-qa-${TS}@movix.io"
result=$(http_call "Register Passenger" POST "$BASE/api/v1/auth/register" \
  -H "Content-Type: application/json" \
  --data-raw "{\"email\":\"$PASS_EMAIL\",\"password\":\"PassQA@1234\",\"tenantId\":\"$TENANT_ID\"}")
PREG_HTTP=$(echo "$result" | cut -d'|' -f1)
PREG_BODY=$(echo "$result" | cut -d'|' -f2-)
if [ "$PREG_HTTP" = "202" ] || [ "$PREG_HTTP" = "200" ]; then
  log "  RESULT: PASS (HTTP $PREG_HTTP)"
else
  log "  RESULT: FAIL (HTTP $PREG_HTTP) - $(echo $PREG_BODY | head -c 200)"
fi

# 6. Login Passenger
log "### 1.6 Login Passenger"
result=$(http_call "Login Passenger" POST "$BASE/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  --data-raw "{\"email\":\"$PASS_EMAIL\",\"password\":\"PassQA@1234\"}")
PLOGIN_HTTP=$(echo "$result" | cut -d'|' -f1)
PLOGIN_BODY=$(echo "$result" | cut -d'|' -f2-)
PASS_TOKEN=$(echo "$PLOGIN_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('accessToken',''))" 2>/dev/null)
if [ -z "$PASS_TOKEN" ]; then
  log "  WARN: Passenger login failed ($PLOGIN_HTTP), using admin token as fallback"
  PASS_TOKEN="$ADMIN_TOKEN"
else
  log "  RESULT: PASS"
fi

# 7. Create Tariff
log "### 1.7 Create Tariff"
TARIFF_PRIORITY=$((TS % 9000 + 100))
result=$(http_call "Create Tariff" POST "$BASE/api/v1/admin/tariffs" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  --data-raw "{\"name\":\"QA Tariff $TS\",\"currency\":\"USD\",\"baseFare\":2.50,\"pricePerKm\":1.20,\"pricePerMinute\":0.25,\"minimumFare\":5.00,\"priority\":$TARIFF_PRIORITY,\"effectiveFromUtc\":\"2025-01-01T00:00:00Z\",\"effectiveUntilUtc\":null}")
TARIFF_HTTP=$(echo "$result" | cut -d'|' -f1)
TARIFF_BODY=$(echo "$result" | cut -d'|' -f2-)
TARIFF_ID=$(echo "$TARIFF_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null)
[ "$TARIFF_HTTP" = "200" ] && log "  RESULT: PASS (id=$TARIFF_ID)" || log "  RESULT: FAIL (HTTP $TARIFF_HTTP)"

# 8. Activate Tariff
log "### 1.8 Activate Tariff"
ACTARIFF_HTTP="SKIP"
if [ -n "$TARIFF_ID" ]; then
  result=$(http_call "Activate Tariff" POST "$BASE/api/v1/admin/tariffs/$TARIFF_ID/activate" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  ACTARIFF_HTTP=$(echo "$result" | cut -d'|' -f1)
  ACTARIFF_BODY=$(echo "$result" | cut -d'|' -f2-)
  if [ "$ACTARIFF_HTTP" = "200" ]; then
    log "  RESULT: PASS"
  else
    log "  RESULT: FAIL (HTTP $ACTARIFF_HTTP)"
    log "  Body: $(echo "$ACTARIFF_BODY" | head -c 400)"
  fi
fi

log ""
log "## LAYER 2: FULL RIDE LIFECYCLE"

# 9. Fare Quote
log "### 2.0 GET /fare/quote"
result=$(http_call "Fare Quote" GET "$BASE/api/v1/fare/quote?pickupLat=19.4326&pickupLon=-99.1332&dropoffLat=19.4350&dropoffLon=-99.1400&tenantId=$TENANT_ID" \
  -H "Authorization: Bearer $PASS_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID")
FARE_HTTP=$(echo "$result" | cut -d'|' -f1)
FARE_BODY=$(echo "$result" | cut -d'|' -f2-)
log "  HTTP: $FARE_HTTP"
[ "$FARE_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL (HTTP $FARE_HTTP)"

# 10. Create Trip
log "### 2.1 POST /trips"
IDEM_KEY="qa-trip-$TS"
result=$(http_call "Create Trip" POST "$BASE/api/v1/trips" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $PASS_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: $IDEM_KEY" \
  --data-raw '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen QA","dropoffAddress":"Destino QA","estimatedAmount":12.50,"currency":"USD"}')
TRIP_HTTP=$(echo "$result" | cut -d'|' -f1)
TRIP_BODY=$(echo "$result" | cut -d'|' -f2-)
TRIP_ID=$(echo "$TRIP_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null)
TRIP_STATUS=$(echo "$TRIP_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
[ "$TRIP_HTTP" = "200" ] && log "  RESULT: PASS (id=$TRIP_ID, status=$TRIP_STATUS)" || log "  RESULT: FAIL (HTTP $TRIP_HTTP)"

# 11. Assign Driver
log "### 2.2 POST /trips/{id}/assign-driver"
ASSIGN_HTTP="SKIP"
if [ -n "$TRIP_ID" ]; then
  result=$(http_call "Assign Driver" POST "$BASE/api/v1/trips/$TRIP_ID/assign-driver" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  ASSIGN_HTTP=$(echo "$result" | cut -d'|' -f1)
  ASSIGN_BODY=$(echo "$result" | cut -d'|' -f2-)
  TRIP_STATUS=$(echo "$ASSIGN_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  if [ "$ASSIGN_HTTP" = "200" ]; then
    log "  RESULT: PASS (status=$TRIP_STATUS)"
  else
    log "  RESULT: FAIL (HTTP $ASSIGN_HTTP)"
    log "  Body: $(echo "$ASSIGN_BODY" | head -c 500)"
  fi
fi

# 12. Driver Accept
log "### 2.3 POST /trips/{id}/accept"
ACCEPT_HTTP="SKIP"
if [ -n "$TRIP_ID" ] && [ "$ASSIGN_HTTP" = "200" ]; then
  result=$(http_call "Driver Accept" POST "$BASE/api/v1/trips/$TRIP_ID/accept" \
    -H "Authorization: Bearer $DRIVER_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  ACCEPT_HTTP=$(echo "$result" | cut -d'|' -f1)
  ACCEPT_BODY=$(echo "$result" | cut -d'|' -f2-)
  TRIP_STATUS=$(echo "$ACCEPT_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  [ "$ACCEPT_HTTP" = "200" ] && log "  RESULT: PASS (status=$TRIP_STATUS)" || { log "  RESULT: FAIL (HTTP $ACCEPT_HTTP)"; log "  Body: $(echo $ACCEPT_BODY | head -c 300)"; }
fi

# 13. Driver Arrive
log "### 2.4 POST /trips/{id}/arrive"
ARRIVE_HTTP="SKIP"
if [ -n "$TRIP_ID" ] && [ "$ACCEPT_HTTP" = "200" ]; then
  result=$(http_call "Driver Arrive" POST "$BASE/api/v1/trips/$TRIP_ID/arrive" \
    -H "Authorization: Bearer $DRIVER_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  ARRIVE_HTTP=$(echo "$result" | cut -d'|' -f1)
  ARRIVE_BODY=$(echo "$result" | cut -d'|' -f2-)
  TRIP_STATUS=$(echo "$ARRIVE_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  [ "$ARRIVE_HTTP" = "200" ] && log "  RESULT: PASS (status=$TRIP_STATUS)" || { log "  RESULT: FAIL (HTTP $ARRIVE_HTTP)"; log "  Body: $(echo $ARRIVE_BODY | head -c 300)"; }
fi

# 14. Driver Start
log "### 2.5 POST /trips/{id}/start"
START_HTTP="SKIP"
if [ -n "$TRIP_ID" ] && [ "$ARRIVE_HTTP" = "200" ]; then
  result=$(http_call "Driver Start" POST "$BASE/api/v1/trips/$TRIP_ID/start" \
    -H "Authorization: Bearer $DRIVER_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  START_HTTP=$(echo "$result" | cut -d'|' -f1)
  START_BODY=$(echo "$result" | cut -d'|' -f2-)
  TRIP_STATUS=$(echo "$START_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  [ "$START_HTTP" = "200" ] && log "  RESULT: PASS (status=$TRIP_STATUS)" || { log "  RESULT: FAIL (HTTP $START_HTTP)"; log "  Body: $(echo $START_BODY | head -c 300)"; }
fi

# 15. Driver Complete
log "### 2.6 POST /trips/{id}/complete"
COMPLETE_HTTP="SKIP"
if [ -n "$TRIP_ID" ] && [ "$START_HTTP" = "200" ]; then
  result=$(http_call "Driver Complete" POST "$BASE/api/v1/trips/$TRIP_ID/complete" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $DRIVER_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID" \
    --data-raw '{"distanceKm":5.2,"durationMinutes":18}')
  COMPLETE_HTTP=$(echo "$result" | cut -d'|' -f1)
  COMPLETE_BODY=$(echo "$result" | cut -d'|' -f2-)
  TRIP_STATUS=$(echo "$COMPLETE_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  [ "$COMPLETE_HTTP" = "200" ] && log "  RESULT: PASS (status=$TRIP_STATUS)" || { log "  RESULT: FAIL (HTTP $COMPLETE_HTTP)"; log "  Body: $(echo $COMPLETE_BODY | head -c 300)"; }
fi

# 16. GET Trip Final
log "### 2.7 GET /trips/{id}"
GETTRIP_HTTP="SKIP"
FINAL_STATUS="unknown"
if [ -n "$TRIP_ID" ]; then
  result=$(http_call "GET Trip Final" GET "$BASE/api/v1/trips/$TRIP_ID" \
    -H "Authorization: Bearer $PASS_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID")
  GETTRIP_HTTP=$(echo "$result" | cut -d'|' -f1)
  GETTRIP_BODY=$(echo "$result" | cut -d'|' -f2-)
  FINAL_STATUS=$(echo "$GETTRIP_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  log "  Final status: $FINAL_STATUS"
  [ "$GETTRIP_HTTP" = "200" ] && log "  RESULT: PASS" || log "  RESULT: FAIL"
fi

# 17. Create Payment
log "### 2.8 POST /payments"
PAY_HTTP="SKIP"
PAY_STATUS="N/A"
PAY_ID="N/A"
if [ -n "$TRIP_ID" ] && [ "$COMPLETE_HTTP" = "200" ]; then
  result=$(http_call "Create Payment" POST "$BASE/api/v1/payments" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $PASS_TOKEN" \
    -H "Idempotency-Key: qa-pay-$TS" \
    --data-raw "{\"tripId\":\"$TRIP_ID\",\"amount\":15.00,\"currency\":\"USD\"}")
  PAY_HTTP=$(echo "$result" | cut -d'|' -f1)
  PAY_BODY=$(echo "$result" | cut -d'|' -f2-)
  PAY_STATUS=$(echo "$PAY_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status',''))" 2>/dev/null)
  PAY_ID=$(echo "$PAY_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null)
  [ "$PAY_HTTP" = "200" ] && log "  RESULT: PASS (id=$PAY_ID, status=$PAY_STATUS)" || { log "  RESULT: FAIL (HTTP $PAY_HTTP)"; log "  Body: $(echo $PAY_BODY | head -c 300)"; }
fi

log ""
log "## LAYER 3: IDEMPOTENCY"

# 18. Duplicate Trip
log "### 3.1 Duplicate Trip (same Idempotency-Key)"
IDEM_TRIP_HTTP="SKIP"
IDEM_ID="N/A"
if [ -n "$TRIP_ID" ]; then
  result=$(http_call "Duplicate Trip" POST "$BASE/api/v1/trips" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $PASS_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID" \
    -H "Idempotency-Key: $IDEM_KEY" \
    --data-raw '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen QA","dropoffAddress":"Destino QA","estimatedAmount":12.50,"currency":"USD"}')
  IDEM_TRIP_HTTP=$(echo "$result" | cut -d'|' -f1)
  IDEM_BODY=$(echo "$result" | cut -d'|' -f2-)
  IDEM_ID=$(echo "$IDEM_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null)
  if [ "$IDEM_TRIP_HTTP" = "200" ] && [ "$IDEM_ID" = "$TRIP_ID" ]; then
    log "  RESULT: PASS (same tripId=$IDEM_ID returned)"
  elif [ "$IDEM_TRIP_HTTP" = "200" ] && [ "$IDEM_ID" != "$TRIP_ID" ]; then
    log "  RESULT: FAIL - DUPLICATE CREATED (original=$TRIP_ID new=$IDEM_ID)"
  else
    log "  RESULT: FAIL (HTTP $IDEM_TRIP_HTTP)"
  fi
fi

# 19. Duplicate Payment
log "### 3.2 Duplicate Payment (same Idempotency-Key)"
IDEM_PAY_HTTP="SKIP"
if [ -n "$TRIP_ID" ] && [ "$PAY_HTTP" = "200" ]; then
  result=$(http_call "Duplicate Payment" POST "$BASE/api/v1/payments" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $PASS_TOKEN" \
    -H "Idempotency-Key: qa-pay-$TS" \
    --data-raw "{\"tripId\":\"$TRIP_ID\",\"amount\":15.00,\"currency\":\"USD\"}")
  IDEM_PAY_HTTP=$(echo "$result" | cut -d'|' -f1)
  IDEM_PAY_BODY=$(echo "$result" | cut -d'|' -f2-)
  log "  HTTP: $IDEM_PAY_HTTP"
  log "  Body: $(echo $IDEM_PAY_BODY | head -c 200)"
  [ "$IDEM_PAY_HTTP" = "200" ] && log "  RESULT: PASS (idempotent)" || log "  RESULT: FAIL"
fi

log ""
log "## LAYER 4: MULTI-TENANT ISOLATION"
log "### 4.1 Cross-Tenant Access Attempt"
XTENANCY_HTTP="SKIP"
if [ -n "$TRIP_ID" ]; then
  WRONG_TENANT="00000000-0000-0000-0000-000000000099"
  result=$(http_call "Cross-Tenant GET" GET "$BASE/api/v1/trips/$TRIP_ID" \
    -H "Authorization: Bearer $PASS_TOKEN" \
    -H "X-Tenant-Id: $WRONG_TENANT")
  XTENANCY_HTTP=$(echo "$result" | cut -d'|' -f1)
  XTENANCY_BODY=$(echo "$result" | cut -d'|' -f2-)
  if [ "$XTENANCY_HTTP" = "403" ] || [ "$XTENANCY_HTTP" = "404" ]; then
    log "  RESULT: PASS (blocked with $XTENANCY_HTTP)"
  else
    log "  RESULT: FAIL (expected 403/404, got $XTENANCY_HTTP)"
    log "  Body: $(echo $XTENANCY_BODY | head -c 300)"
  fi
fi

log ""
log "## LAYER 5: CONCURRENCY TEST"
log "### 5.1 Concurrent Assign-Driver (2 parallel on new trip)"

TS2=$((TS + 1))
result=$(http_call "Create Trip for Concurrency" POST "$BASE/api/v1/trips" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $PASS_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: qa-conc-$TS2" \
  --data-raw '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Concurrent A","dropoffAddress":"Concurrent B","estimatedAmount":10.00,"currency":"USD"}')
CONC_HTTP=$(echo "$result" | cut -d'|' -f1)
CONC_BODY=$(echo "$result" | cut -d'|' -f2-)
CONC_TRIP_ID=$(echo "$CONC_BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null)
log "  Concurrent trip: HTTP $CONC_HTTP, id=$CONC_TRIP_ID"

# Re-ensure driver is online
result=$(http_call "Driver Online Before Concurrency" POST "$BASE/api/v1/drivers/status" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  --data-raw '{"status":1}')
DONLINE2_HTTP=$(echo "$result" | cut -d'|' -f1)
log "  Driver reset Online: HTTP $DONLINE2_HTTP"

CONC1_HTTP="SKIP"
CONC2_HTTP="SKIP"
CONC1_BODY=""
CONC2_BODY=""

if [ -n "$CONC_TRIP_ID" ]; then
  TF1="/c/Proyectos/RiderFlow/tests/conc_body1.txt"
  TF2="/c/Proyectos/RiderFlow/tests/conc_body2.txt"
  THF1="/c/Proyectos/RiderFlow/tests/conc_http1.txt"
  THF2="/c/Proyectos/RiderFlow/tests/conc_http2.txt"

  curl -s -o "$TF1" -w "%{http_code}" -X POST "$BASE/api/v1/trips/$CONC_TRIP_ID/assign-driver" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID" > "$THF1" &
  PID1=$!
  curl -s -o "$TF2" -w "%{http_code}" -X POST "$BASE/api/v1/trips/$CONC_TRIP_ID/assign-driver" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "X-Tenant-Id: $TENANT_ID" > "$THF2" &
  PID2=$!
  wait $PID1 2>/dev/null || true
  wait $PID2 2>/dev/null || true

  CONC1_HTTP=$(cat "$THF1" 2>/dev/null || echo "ERR")
  CONC1_BODY=$(cat "$TF1" 2>/dev/null || echo "")
  CONC2_HTTP=$(cat "$THF2" 2>/dev/null || echo "ERR")
  CONC2_BODY=$(cat "$TF2" 2>/dev/null || echo "")

  log "  Request 1: HTTP $CONC1_HTTP | Body: $(echo $CONC1_BODY | head -c 200)"
  log "  Request 2: HTTP $CONC2_HTTP | Body: $(echo $CONC2_BODY | head -c 200)"

  if [ "$CONC1_HTTP" = "200" ] && [ "$CONC2_HTTP" = "200" ]; then
    log "  RESULT: CRITICAL FAIL - Both 200 (double-assign vulnerability)"
  elif { [ "$CONC1_HTTP" = "200" ] && [ "$CONC2_HTTP" != "200" ]; } || \
       { [ "$CONC2_HTTP" = "200" ] && [ "$CONC1_HTTP" != "200" ]; }; then
    log "  RESULT: PASS - Exactly one succeeded, one rejected"
  elif [ "$CONC1_HTTP" = "409" ] && [ "$CONC2_HTTP" = "409" ]; then
    log "  RESULT: FAIL - Both 409, neither assigned. Stale RowVersion issue."
  else
    log "  RESULT: INCONCLUSIVE - R1=$CONC1_HTTP R2=$CONC2_HTTP"
  fi

  rm -f "$TF1" "$TF2" "$THF1" "$THF2"
fi

log ""
log "================================================================"
log "FINAL SUMMARY"
log "================================================================"
log "Health:           HTTP $HEALTH_HTTP"
log "Ready:            HTTP $READY_HTTP"
log "Admin Login:      HTTP $ADMIN_HTTP"
log "Driver Login:     HTTP $DRIVER_HTTP"
log "Driver Online:    HTTP $DSTATUS_HTTP"
log "Driver Location:  HTTP $DLOC_HTTP"
log "Register Pass:    HTTP $PREG_HTTP"
log "Login Pass:       HTTP $PLOGIN_HTTP"
log "Create Tariff:    HTTP $TARIFF_HTTP (id=$TARIFF_ID)"
log "Activate Tariff:  HTTP $ACTARIFF_HTTP"
log "Fare Quote:       HTTP $FARE_HTTP"
log "Create Trip:      HTTP $TRIP_HTTP (id=$TRIP_ID, status=$TRIP_STATUS)"
log "Assign Driver:    HTTP $ASSIGN_HTTP"
log "Driver Accept:    HTTP $ACCEPT_HTTP"
log "Driver Arrive:    HTTP $ARRIVE_HTTP"
log "Driver Start:     HTTP $START_HTTP"
log "Driver Complete:  HTTP $COMPLETE_HTTP"
log "GET Trip Final:   HTTP $GETTRIP_HTTP (status=$FINAL_STATUS)"
log "Create Payment:   HTTP $PAY_HTTP (status=$PAY_STATUS)"
log "Idempotency Trip: HTTP $IDEM_TRIP_HTTP (id=$IDEM_ID)"
log "Idempotency Pay:  HTTP $IDEM_PAY_HTTP"
log "Multi-tenant Iso: HTTP $XTENANCY_HTTP"
log "Concurrency R1:   HTTP $CONC1_HTTP"
log "Concurrency R2:   HTTP $CONC2_HTTP"
log "================================================================"
