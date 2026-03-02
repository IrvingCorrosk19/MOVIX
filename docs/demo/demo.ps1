# MOVIX E2E Demo Script (PowerShell)
# Requisitos: API en marcha (Development), Payments:Mode=Simulation.
# Variables de entorno: ADMIN_EMAIL, ADMIN_PASSWORD; opcional: DRIVER_EMAIL, DRIVER_PASSWORD (para seed/script).

$ErrorActionPreference = "Stop"
$BASE_URL = if ($env:BASE_URL) { $env:BASE_URL } else { "http://localhost:8080" }

function Invoke-DemoApi {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Headers = @{},
        [object]$Body = $null
    )
    $params = @{
        Uri         = "$BASE_URL$Path"
        Method      = $Method
        ContentType = "application/json"
        Headers     = $Headers
    }
    if ($Body) {
        $params.Body = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
    }
    $response = Invoke-RestMethod @params
    return $response
}

function Assert-Status {
    param([int]$Expected, [int]$Actual, [string]$Step)
    if ($Actual -ne $Expected) {
        throw "Demo step '$Step' failed: expected HTTP $Expected, got $Actual"
    }
}

# --- 0. Health ---
Write-Host "Checking API health..."
try {
    $r = Invoke-WebRequest -Uri "$BASE_URL/health" -Method Get -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Health returned $($r.StatusCode)" }
} catch {
    Write-Error "API not reachable at $BASE_URL. Start the API first (e.g. docker-compose up -d)."
    exit 1
}

# --- 1. Login Admin (requires seed: ADMIN_EMAIL, ADMIN_PASSWORD) ---
$adminEmail = $env:ADMIN_EMAIL ?? "admin@movix.local"
$adminPassword = $env:ADMIN_PASSWORD ?? "TuPasswordSeguro"
Write-Host "Login Admin..."
$loginBody = @{ email = $adminEmail; password = $adminPassword } | ConvertTo-Json
$loginResp = Invoke-DemoApi -Method Post -Path "/api/v1/auth/login" -Body $loginBody
if (-not $loginResp.accessToken) {
    Write-Error "Admin login failed. Set ADMIN_EMAIL and ADMIN_PASSWORD (seed user)."
    exit 1
}
$ADMIN_TOKEN = $loginResp.accessToken
Write-Host "ADMIN_TOKEN obtained."

# --- 2. Create Tenant (requires Authorization) ---
$tenantBody = @{ name = "Demo City" } | ConvertTo-Json
$tenantResp = Invoke-DemoApi -Method Post -Path "/api/v1/admin/tenants" -Headers @{ Authorization = "Bearer $ADMIN_TOKEN" } -Body $tenantBody
$TENANT_ID = $tenantResp.id
Write-Host "TENANT_ID = $TENANT_ID"

# --- 3. Create + Activate Tariff (requires X-Tenant-Id) ---
$tariffBody = '{"name":"Standard","currency":"USD","baseFare":2.50,"pricePerKm":1.20,"pricePerMinute":0.25,"minimumFare":5.00}'
$tariffResp = Invoke-DemoApi -Method Post -Path "/api/v1/admin/tariffs" -Headers @{
    Authorization = "Bearer $ADMIN_TOKEN"
    "X-Tenant-Id" = $TENANT_ID
} -Body $tariffBody
$TARIFF_ID = $tariffResp.id
Write-Host "TARIFF_ID = $TARIFF_ID"

Invoke-DemoApi -Method Post -Path "/api/v1/admin/tariffs/$TARIFF_ID/activate" -Headers @{
    Authorization = "Bearer $ADMIN_TOKEN"
    "X-Tenant-Id" = $TENANT_ID
} | Out-Null
Write-Host "Tariff activated."

# --- 4. Quote Fare (requires X-Tenant-Id) ---
$quoteResp = Invoke-DemoApi -Method Get -Path "/api/v1/fare/quote?distanceKm=5&durationMin=10" -Headers @{
    Authorization = "Bearer $ADMIN_TOKEN"
    "X-Tenant-Id" = $TENANT_ID
}
Write-Host "Quote = $($quoteResp.fareAmount) $($quoteResp.currency)"

# --- 5. Register Passenger ---
$regBody = @{ email = "passenger@demo.local"; password = "Pass1234" } | ConvertTo-Json
try {
    $regR = Invoke-WebRequest -Uri "$BASE_URL/api/v1/auth/register" -Method Post -Body $regBody -ContentType "application/json" -UseBasicParsing
    if ($regR.StatusCode -ne 202) { Write-Warning "Register returned $($regR.StatusCode)" }
} catch { }
# Login Passenger
$passLogin = Invoke-DemoApi -Method Post -Path "/api/v1/auth/login" -Body (@{ email = "passenger@demo.local"; password = "Pass1234" } | ConvertTo-Json)
$PASSENGER_TOKEN = $passLogin.accessToken
Write-Host "PASSENGER_TOKEN obtained."

# --- 6. Driver: use seed driver if DRIVER_EMAIL set ---
$DRIVER_TOKEN = $null
if ($env:DRIVER_EMAIL -and $env:DRIVER_PASSWORD) {
    $driverLogin = Invoke-DemoApi -Method Post -Path "/api/v1/auth/login" -Body (@{ email = $env:DRIVER_EMAIL; password = $env:DRIVER_PASSWORD } | ConvertTo-Json)
    $DRIVER_TOKEN = $driverLogin.accessToken
    # Onboarding (idempotent if already exists)
    $onbBody = @{ licenseNumber = "DL-001"; vehiclePlate = "ABC-123"; vehicleModel = "Sedan"; vehicleColor = "White" } | ConvertTo-Json
    try {
        Invoke-DemoApi -Method Post -Path "/api/v1/drivers/onboarding" -Headers @{ Authorization = "Bearer $DRIVER_TOKEN" } -Body $onbBody | Out-Null
    } catch { }
    # Status Online
    Invoke-DemoApi -Method Post -Path "/api/v1/drivers/status" -Headers @{ Authorization = "Bearer $DRIVER_TOKEN" } -Body '{"status":1}' | Out-Null
    Write-Host "Driver online."
}

# --- 7. Create Trip (requires X-Tenant-Id and Idempotency-Key) ---
$idemKey = "demo-trip-" + [int][double]::Parse((Get-Date -UFormat %s))
$tripBody = @{
    pickupLatitude   = 9.0
    pickupLongitude  = -79.5
    dropoffLatitude = 9.05
    dropoffLongitude = -79.55
    pickupAddress   = "Origin"
    dropoffAddress  = "Dest"
    estimatedAmount = 10.50
    currency        = "USD"
} | ConvertTo-Json
$tripResp = Invoke-DemoApi -Method Post -Path "/api/v1/trips" -Headers @{
    Authorization   = "Bearer $PASSENGER_TOKEN"
    "X-Tenant-Id"  = $TENANT_ID
    "Idempotency-Key" = $idemKey
} -Body $tripBody
$TRIP_ID = $tripResp.id
Write-Host "TRIP_ID = $TRIP_ID"

# --- 8. Assign Driver (needs at least one driver Online) ---
try {
    $assignResp = Invoke-DemoApi -Method Post -Path "/api/v1/trips/$TRIP_ID/assign-driver" -Headers @{
        Authorization  = "Bearer $ADMIN_TOKEN"
        "X-Tenant-Id" = $TENANT_ID
    }
    Write-Host "Trip assigned to driver. Status = $($assignResp.status)"
} catch {
    Write-Warning "Assign driver failed (no drivers available?). Continue without driver transitions."
}

# --- 9. Transition: Arrive, Start, Complete (only if we have DRIVER_TOKEN) ---
if ($DRIVER_TOKEN) {
    $h = @{ Authorization = "Bearer $DRIVER_TOKEN"; "X-Tenant-Id" = $TENANT_ID }
    Invoke-DemoApi -Method Post -Path "/api/v1/trips/$TRIP_ID/arrive" -Headers $h | Out-Null
    Invoke-DemoApi -Method Post -Path "/api/v1/trips/$TRIP_ID/start" -Headers $h | Out-Null
    $completeBody = @{ distanceKm = 5.2; durationMinutes = 12; tenantId = $null } | ConvertTo-Json
    Invoke-DemoApi -Method Post -Path "/api/v1/trips/$TRIP_ID/complete" -Headers @{ Authorization = "Bearer $DRIVER_TOKEN" } -Body $completeBody | Out-Null
    Write-Host "Trip completed (fare snapshot applied)."
}

# --- 10. Create Payment (Idempotency-Key required) ---
$payIdem = "demo-pay-" + [int][double]::Parse((Get-Date -UFormat %s))
$payBody = @{ tripId = $TRIP_ID; amount = 10.50; currency = "USD" } | ConvertTo-Json
$payResp = Invoke-DemoApi -Method Post -Path "/api/v1/payments" -Headers @{
    Authorization    = "Bearer $PASSENGER_TOKEN"
    "Idempotency-Key" = $payIdem
} -Body $payBody
$PAYMENT_ID = $payResp.id
$EXTERNAL_ID = $payResp.externalPaymentId
$CLIENT_SECRET = $payResp.clientSecret
Write-Host "PAYMENT_ID = $PAYMENT_ID"
Write-Host "ExternalPaymentId = $EXTERNAL_ID"
Write-Host "ClientSecret = $CLIENT_SECRET"

# --- 11. Simulate Webhook (Development only) ---
if ($env:ASPNETCORE_ENVIRONMENT -eq "Development" -or -not $env:ASPNETCORE_ENVIRONMENT) {
    $simBody = @{ paymentId = $PAYMENT_ID; eventType = "payment_intent.succeeded" } | ConvertTo-Json
    try {
        Invoke-DemoApi -Method Post -Path "/api/v1/payments/simulate-webhook" -Body $simBody | Out-Null
        Write-Host "Simulate webhook sent; payment should be Completed."
    } catch {
        Write-Warning "Simulate webhook failed (e.g. not Development). Skip."
    }
}

# --- 12. Admin Ops: payments filtered by status ---
$opsPay = Invoke-DemoApi -Method Get -Path "/api/v1/admin/ops/payments?status=Completed" -Headers @{ Authorization = "Bearer $ADMIN_TOKEN" }
Write-Host "Ops payments (Completed) count: $($opsPay.Count)"

# --- 13. Admin Ops: outbox (optional deadletter) ---
$opsOutbox = Invoke-DemoApi -Method Get -Path "/api/v1/admin/ops/outbox" -Headers @{ Authorization = "Bearer $ADMIN_TOKEN" }
Write-Host "Outbox messages (recent) count: $($opsOutbox.Count)"

Write-Host ""
Write-Host "--- Demo summary ---"
Write-Host "TENANT_ID       = $TENANT_ID"
Write-Host "TARIFF_ID       = $TARIFF_ID"
Write-Host "TRIP_ID         = $TRIP_ID"
Write-Host "PAYMENT_ID      = $PAYMENT_ID"
Write-Host "ExternalPayId   = $EXTERNAL_ID"
Write-Host "ClientSecret    = $CLIENT_SECRET"
Write-Host "Done."
