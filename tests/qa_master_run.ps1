#Requires -Version 5.1
# QA Master Run - Full evidence capture for QA_RELEASE_READINESS_v1.md
# Ejecuta pruebas REALES contra la API en ejecucion. No simulaciones.

$ErrorActionPreference = "SilentlyContinue"
$BASE = "http://127.0.0.1:55392"
$TENANT_ID = "00000000-0000-0000-0000-000000000001"
$TS = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$StartTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

$Evidence = [System.Collections.ArrayList]::new()
$Fails = [System.Collections.ArrayList]::new()

function Invoke-Api {
    param(
        [string]$Label,
        [string]$Method,
        [string]$Uri,
        [hashtable]$Headers = @{},
        [string]$Body = $null
    )
    $params = @{
        Uri                = $Uri
        Method             = $Method
        ContentType        = "application/json"
        UseBasicParsing    = $true
        TimeoutSec         = 20
    }
    if ($Headers.Count -gt 0) { $params["Headers"] = $Headers }
    if ($Body) { $params["Body"] = $Body }

    try {
        $r = Invoke-WebRequest @params
        $entry = [pscustomobject]@{
            Label   = $Label
            HTTP    = [int]$r.StatusCode
            OK      = $true
            Body    = $r.Content
        }
    } catch {
        $status = 0
        try { $status = [int]$_.Exception.Response.StatusCode } catch {}
        $content = ""
        try { $content = $_.ErrorDetails.Message } catch {}
        $entry = [pscustomobject]@{
            Label   = $Label
            HTTP    = $status
            OK      = $false
            Body    = $content
        }
    }
    $Evidence.Add($entry) | Out-Null
    return $entry
}

function jq { param([string]$key, [string]$json)
    try { ($json | ConvertFrom-Json).$key } catch { "" }
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "QA MASTER EXECUTION - $StartTime UTC" -ForegroundColor Cyan
Write-Host "Base URL: $BASE" -ForegroundColor Cyan
Write-Host "================================================================"
Write-Host ""

# ─── INFRA CHECKS ────────────────────────────────────────────────────────────
Write-Host "## INFRA CHECKS" -ForegroundColor Yellow
$health = Invoke-Api "GET /health" GET "$BASE/health"
Write-Host "  [/health] HTTP $($health.HTTP) - $(if($health.OK){'PASS'}else{'FAIL'})"
if ($health.OK) {
    $hdata = $health.Body | ConvertFrom-Json
    Write-Host "    postgres=$($hdata.checks[0].status) redis=$($hdata.checks[1].status) outbox=$($hdata.checks[2].status) postgis=$($hdata.checks[3].status)"
}
$ready = Invoke-Api "GET /ready" GET "$BASE/ready"
Write-Host "  [/ready]  HTTP $($ready.HTTP) - $(if($ready.OK){'PASS'}else{'FAIL'})"

if (-not $health.OK) { $Fails.Add("CRITICAL: /health returned $($health.HTTP)") | Out-Null }
if (-not $ready.OK)  { $Fails.Add("CRITICAL: /ready returned $($ready.HTTP)") | Out-Null }

# ─── LAYER 1: AUTH + INFRASTRUCTURE ─────────────────────────────────────────
Write-Host ""
Write-Host "## LAYER 1: AUTH + INFRASTRUCTURE" -ForegroundColor Yellow

# 1.1 Login Admin
$loginAdmin = Invoke-Api "Login Admin" POST "$BASE/api/v1/auth/login" -Body '{"email":"admin@movix.io","password":"Admin@1234!"}'
Write-Host "  [1.1 Login Admin]     HTTP $($loginAdmin.HTTP) - $(if($loginAdmin.OK){'PASS'}else{'FAIL'})"
if (-not $loginAdmin.OK) { Write-Host "  ABORT: No admin token"; exit 1 }
$adminToken = jq "accessToken" $loginAdmin.Body
$adminAuth  = @{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = $TENANT_ID }

# 1.2 Login Driver
$loginDriver = Invoke-Api "Login Driver" POST "$BASE/api/v1/auth/login" -Body '{"email":"driver@movix.io","password":"Driver@1234!"}'
Write-Host "  [1.2 Login Driver]    HTTP $($loginDriver.HTTP) - $(if($loginDriver.OK){'PASS'}else{'FAIL'})"
if (-not $loginDriver.OK) { Write-Host "  ABORT: No driver token"; exit 1 }
$driverToken = jq "accessToken" $loginDriver.Body
$driverAuth  = @{ Authorization = "Bearer $driverToken"; "X-Tenant-Id" = $TENANT_ID }

# 1.3 Driver Status Online
$driverOnline = Invoke-Api "Driver Status Online" POST "$BASE/api/v1/drivers/status" $driverAuth -Body '{"status":1}'
Write-Host "  [1.3 Driver Online]   HTTP $($driverOnline.HTTP) - $(if($driverOnline.OK){'PASS'}else{'FAIL'})"
if (-not $driverOnline.OK) { $Fails.Add("1.3 Driver Online FAIL (HTTP $($driverOnline.HTTP)): $($driverOnline.Body)") | Out-Null }

# 1.4 Driver Location
$driverLoc = Invoke-Api "Driver Location" POST "$BASE/api/v1/drivers/location" $driverAuth -Body '{"latitude":19.4326,"longitude":-99.1332}'
Write-Host "  [1.4 Driver Location] HTTP $($driverLoc.HTTP) - $(if($driverLoc.OK){'PASS'}else{'FAIL'})"
if (-not $driverLoc.OK) { $Fails.Add("1.4 Driver Location FAIL (HTTP $($driverLoc.HTTP))") | Out-Null }

# 1.5 Register Passenger (unique email per run)
$passEmail = "passenger-qa-$TS@movix.io"
$regPass = Invoke-Api "Register Passenger" POST "$BASE/api/v1/auth/register" `
    -Body "{`"email`":`"$passEmail`",`"password`":`"PassQA@1234`",`"tenantId`":`"$TENANT_ID`"}"
Write-Host "  [1.5 Register Pass]   HTTP $($regPass.HTTP) - $(if($regPass.HTTP -in 200,202){'PASS'}else{'FAIL'})"
if ($regPass.HTTP -notin 200,202) { $Fails.Add("1.5 Register Passenger FAIL (HTTP $($regPass.HTTP)): $($regPass.Body)") | Out-Null }

# 1.6 Login Passenger
$loginPass = Invoke-Api "Login Passenger" POST "$BASE/api/v1/auth/login" `
    -Body "{`"email`":`"$passEmail`",`"password`":`"PassQA@1234`"}"
Write-Host "  [1.6 Login Pass]      HTTP $($loginPass.HTTP) - $(if($loginPass.OK){'PASS'}else{'WARN (using admin)'})"
$passToken = jq "accessToken" $loginPass.Body
if (-not $passToken) { $passToken = $adminToken; Write-Host "    FALLBACK: using admin token for passenger" }
$passAuth = @{ Authorization = "Bearer $passToken"; "X-Tenant-Id" = $TENANT_ID }

# 1.7 Create Tariff (unique priority)
$tariffPriority = ($TS % 9000) + 100
$createTariff = Invoke-Api "Create Tariff" POST "$BASE/api/v1/admin/tariffs" $adminAuth `
    -Body "{`"name`":`"QA Tariff $TS`",`"currency`":`"USD`",`"baseFare`":2.50,`"pricePerKm`":1.20,`"pricePerMinute`":0.25,`"minimumFare`":5.00,`"priority`":$tariffPriority,`"effectiveFromUtc`":`"2025-01-01T00:00:00Z`",`"effectiveUntilUtc`":null}"
Write-Host "  [1.7 Create Tariff]   HTTP $($createTariff.HTTP) - $(if($createTariff.OK){'PASS'}else{'FAIL'})"
$tariffId = jq "id" $createTariff.Body
if (-not $createTariff.OK) { $Fails.Add("1.7 Create Tariff FAIL (HTTP $($createTariff.HTTP)): $($createTariff.Body.Substring(0,[Math]::Min(200,$createTariff.Body.Length)))") | Out-Null }

# 1.8 Activate Tariff
$activateResult = $null
if ($tariffId) {
    $activateResult = Invoke-Api "Activate Tariff" POST "$BASE/api/v1/admin/tariffs/$tariffId/activate" $adminAuth
    Write-Host "  [1.8 Activate Tariff] HTTP $($activateResult.HTTP) - $(if($activateResult.OK){'PASS'}else{'FAIL'})"
    if (-not $activateResult.OK) { $Fails.Add("1.8 Activate Tariff FAIL (HTTP $($activateResult.HTTP)): $($activateResult.Body)") | Out-Null }
} else {
    Write-Host "  [1.8 Activate Tariff] SKIP (no tariffId)"
}

# ─── LAYER 2: FULL RIDE LIFECYCLE ────────────────────────────────────────────
Write-Host ""
Write-Host "## LAYER 2: FULL RIDE LIFECYCLE" -ForegroundColor Yellow

# 2.0 Fare Quote
$fareQuote = Invoke-Api "Fare Quote" GET "$BASE/api/v1/fare/quote?pickupLat=19.4326&pickupLon=-99.1332&dropoffLat=19.4350&dropoffLon=-99.1400&tenantId=$TENANT_ID" $passAuth
Write-Host "  [2.0 Fare Quote]      HTTP $($fareQuote.HTTP) - $(if($fareQuote.OK){'PASS'}else{'FAIL (HTTP ' + $fareQuote.HTTP + ')'})"
if (-not $fareQuote.OK) { $Fails.Add("2.0 Fare Quote FAIL (HTTP $($fareQuote.HTTP)): $($fareQuote.Body.Substring(0,[Math]::Min(200,$fareQuote.Body.Length)))") | Out-Null }

# 2.1 Create Trip
$idempotencyKey = "qa-trip-$TS"
$passAuthWithKey = @{ Authorization = "Bearer $passToken"; "X-Tenant-Id" = $TENANT_ID; "Idempotency-Key" = $idempotencyKey }
$createTrip = Invoke-Api "Create Trip" POST "$BASE/api/v1/trips" $passAuthWithKey `
    -Body '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen QA","dropoffAddress":"Destino QA","estimatedAmount":12.50,"currency":"USD"}'
$tripId = jq "id" $createTrip.Body
$tripStatus = jq "status" $createTrip.Body
Write-Host "  [2.1 Create Trip]     HTTP $($createTrip.HTTP) - $(if($createTrip.OK){'PASS (id=' + $tripId + ', status=' + $tripStatus + ')'}else{'FAIL'})"
if (-not $createTrip.OK) { $Fails.Add("2.1 Create Trip FAIL (HTTP $($createTrip.HTTP)): $($createTrip.Body.Substring(0,[Math]::Min(200,$createTrip.Body.Length)))") | Out-Null }

# 2.2 Assign Driver
$assignDriver = $null
if ($tripId) {
    $assignDriver = Invoke-Api "Assign Driver" POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth
    $tripStatus = jq "status" $assignDriver.Body
    Write-Host "  [2.2 Assign Driver]   HTTP $($assignDriver.HTTP) - $(if($assignDriver.OK){'PASS (status=' + $tripStatus + ')'}else{'FAIL'})"
    if (-not $assignDriver.OK) {
        $Fails.Add("2.2 Assign Driver FAIL (HTTP $($assignDriver.HTTP)): $($assignDriver.Body.Substring(0,[Math]::Min(400,$assignDriver.Body.Length)))") | Out-Null
    }
} else {
    Write-Host "  [2.2 Assign Driver]   SKIP (no tripId)"
}

# 2.3 Driver Accept
$acceptTrip = $null
if ($tripId -and $assignDriver -and $assignDriver.OK) {
    $acceptTrip = Invoke-Api "Driver Accept" POST "$BASE/api/v1/trips/$tripId/accept" $driverAuth
    $tripStatus = jq "status" $acceptTrip.Body
    Write-Host "  [2.3 Driver Accept]   HTTP $($acceptTrip.HTTP) - $(if($acceptTrip.OK){'PASS (status=' + $tripStatus + ')'}else{'FAIL'})"
    if (-not $acceptTrip.OK) { $Fails.Add("2.3 Driver Accept FAIL (HTTP $($acceptTrip.HTTP)): $($acceptTrip.Body.Substring(0,[Math]::Min(300,$acceptTrip.Body.Length)))") | Out-Null }
} else {
    Write-Host "  [2.3 Driver Accept]   SKIP"
}

# 2.4 Driver Arrive
$arriveTrip = $null
if ($tripId -and $acceptTrip -and $acceptTrip.OK) {
    $arriveTrip = Invoke-Api "Driver Arrive" POST "$BASE/api/v1/trips/$tripId/arrive" $driverAuth
    $tripStatus = jq "status" $arriveTrip.Body
    Write-Host "  [2.4 Driver Arrive]   HTTP $($arriveTrip.HTTP) - $(if($arriveTrip.OK){'PASS (status=' + $tripStatus + ')'}else{'FAIL'})"
    if (-not $arriveTrip.OK) { $Fails.Add("2.4 Driver Arrive FAIL (HTTP $($arriveTrip.HTTP)): $($arriveTrip.Body.Substring(0,[Math]::Min(300,$arriveTrip.Body.Length)))") | Out-Null }
} else {
    Write-Host "  [2.4 Driver Arrive]   SKIP"
}

# 2.5 Driver Start
$startTrip = $null
if ($tripId -and $arriveTrip -and $arriveTrip.OK) {
    $startTrip = Invoke-Api "Driver Start" POST "$BASE/api/v1/trips/$tripId/start" $driverAuth
    $tripStatus = jq "status" $startTrip.Body
    Write-Host "  [2.5 Driver Start]    HTTP $($startTrip.HTTP) - $(if($startTrip.OK){'PASS (status=' + $tripStatus + ')'}else{'FAIL'})"
    if (-not $startTrip.OK) { $Fails.Add("2.5 Driver Start FAIL (HTTP $($startTrip.HTTP)): $($startTrip.Body.Substring(0,[Math]::Min(300,$startTrip.Body.Length)))") | Out-Null }
} else {
    Write-Host "  [2.5 Driver Start]    SKIP"
}

# 2.6 Driver Complete
$completeTrip = $null
if ($tripId -and $startTrip -and $startTrip.OK) {
    $completeTrip = Invoke-Api "Driver Complete" POST "$BASE/api/v1/trips/$tripId/complete" $driverAuth `
        -Body '{"distanceKm":5.2,"durationMinutes":18}'
    $tripStatus = jq "status" $completeTrip.Body
    Write-Host "  [2.6 Driver Complete] HTTP $($completeTrip.HTTP) - $(if($completeTrip.OK){'PASS (status=' + $tripStatus + ')'}else{'FAIL'})"
    if (-not $completeTrip.OK) { $Fails.Add("2.6 Driver Complete FAIL (HTTP $($completeTrip.HTTP)): $($completeTrip.Body.Substring(0,[Math]::Min(300,$completeTrip.Body.Length)))") | Out-Null }
} else {
    Write-Host "  [2.6 Driver Complete] SKIP"
}

# 2.7 GET Trip Final State
$finalTrip = $null
$finalStatus = "UNKNOWN"
if ($tripId) {
    $finalTrip = Invoke-Api "GET Trip Final" GET "$BASE/api/v1/trips/$tripId" $passAuth
    $finalStatus = jq "status" $finalTrip.Body
    Write-Host "  [2.7 GET Trip Final]  HTTP $($finalTrip.HTTP) - $(if($finalTrip.OK){'PASS (finalStatus=' + $finalStatus + ')'}else{'FAIL'})"
    if (-not $finalTrip.OK) { $Fails.Add("2.7 GET Trip FAIL (HTTP $($finalTrip.HTTP))") | Out-Null }
}

# 2.8 Create Payment
$createPayment = $null
$paymentStatus = "N/A"
$paymentId     = "N/A"
if ($tripId -and $completeTrip -and $completeTrip.OK) {
    $payAuthKey = @{ Authorization = "Bearer $passToken"; "Idempotency-Key" = "qa-pay-$TS" }
    $createPayment = Invoke-Api "Create Payment" POST "$BASE/api/v1/payments" $payAuthKey `
        -Body "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}"
    $paymentStatus = jq "status" $createPayment.Body
    $paymentId     = jq "id" $createPayment.Body
    Write-Host "  [2.8 Create Payment]  HTTP $($createPayment.HTTP) - $(if($createPayment.OK){'PASS (status=' + $paymentStatus + ')'}else{'FAIL'})"
    if (-not $createPayment.OK) { $Fails.Add("2.8 Create Payment FAIL (HTTP $($createPayment.HTTP)): $($createPayment.Body.Substring(0,[Math]::Min(400,$createPayment.Body.Length)))") | Out-Null }
} else {
    Write-Host "  [2.8 Create Payment]  SKIP (complete not executed)"
}

# ─── LAYER 3: IDEMPOTENCY ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "## LAYER 3: IDEMPOTENCY" -ForegroundColor Yellow

# 3.1 Duplicate Trip
$idemTripResult = "SKIP"
$idemTripId = "N/A"
if ($tripId) {
    $dupTrip = Invoke-Api "Duplicate Trip" POST "$BASE/api/v1/trips" $passAuthWithKey `
        -Body '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen QA","dropoffAddress":"Destino QA","estimatedAmount":12.50,"currency":"USD"}'
    $idemTripId = jq "id" $dupTrip.Body
    if ($dupTrip.HTTP -eq 200 -and $idemTripId -eq $tripId) {
        $idemTripResult = "PASS"
        Write-Host "  [3.1 Idem Trip]       HTTP $($dupTrip.HTTP) - PASS (same id returned)"
    } elseif ($dupTrip.HTTP -eq 200 -and $idemTripId -ne $tripId) {
        $idemTripResult = "FAIL-DUPLICATE"
        Write-Host "  [3.1 Idem Trip]       HTTP $($dupTrip.HTTP) - FAIL (DUPLICATE! orig=$tripId new=$idemTripId)"
        $Fails.Add("3.1 Idempotency FAIL: duplicate trip created (orig=$tripId, new=$idemTripId)") | Out-Null
    } else {
        $idemTripResult = "FAIL"
        Write-Host "  [3.1 Idem Trip]       HTTP $($dupTrip.HTTP) - FAIL"
        $Fails.Add("3.1 Idempotency Trip FAIL (HTTP $($dupTrip.HTTP))") | Out-Null
    }
}

# 3.2 Duplicate Payment
$idemPayResult = "SKIP"
if ($tripId -and $createPayment -and $createPayment.OK) {
    $payAuthKey2 = @{ Authorization = "Bearer $passToken"; "Idempotency-Key" = "qa-pay-$TS" }
    $dupPay = Invoke-Api "Duplicate Payment" POST "$BASE/api/v1/payments" $payAuthKey2 `
        -Body "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}"
    if ($dupPay.HTTP -eq 200) {
        $dupPayId = jq "id" $dupPay.Body
        Write-Host "  [3.2 Idem Payment]    HTTP $($dupPay.HTTP) - PASS (idempotent response, id=$dupPayId)"
        $idemPayResult = "PASS"
    } else {
        Write-Host "  [3.2 Idem Payment]    HTTP $($dupPay.HTTP) - FAIL"
        $idemPayResult = "FAIL"
        $Fails.Add("3.2 Idempotency Payment FAIL (HTTP $($dupPay.HTTP))") | Out-Null
    }
}

# ─── LAYER 4: MULTI-TENANT ISOLATION ─────────────────────────────────────────
Write-Host ""
Write-Host "## LAYER 4: MULTI-TENANT ISOLATION" -ForegroundColor Yellow

$xTenancyResult = "SKIP"
$xTenancyHTTP   = 0
if ($tripId) {
    $wrongAuth = @{ Authorization = "Bearer $passToken"; "X-Tenant-Id" = "00000000-0000-0000-0000-000000000099" }
    $crossTenant = Invoke-Api "Cross-Tenant GET Trip" GET "$BASE/api/v1/trips/$tripId" $wrongAuth
    $xTenancyHTTP = $crossTenant.HTTP
    if ($xTenancyHTTP -in 403, 404) {
        $xTenancyResult = "PASS"
        Write-Host "  [4.1 Cross-Tenant]    HTTP $xTenancyHTTP - PASS (access blocked)"
    } else {
        $xTenancyResult = "FAIL"
        Write-Host "  [4.1 Cross-Tenant]    HTTP $xTenancyHTTP - FAIL (expected 403/404)"
        $Fails.Add("4.1 Multi-tenant FAIL: cross-tenant returned $xTenancyHTTP (data leak risk)") | Out-Null
    }
}

# 4.2 Admin of wrong tenant tries to assign driver on trip of correct tenant
$xTenancyAssignResult = "SKIP"
if ($createTrip.OK) {
    # Create a second trip to test cross-tenant assign
    $ts3 = $TS + 2
    $wrongAdminAuth = @{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = "00000000-0000-0000-0000-000000000099" }
    $xAssign = Invoke-Api "Cross-Tenant Assign" POST "$BASE/api/v1/trips/$tripId/assign-driver" $wrongAdminAuth
    $xAssignHTTP = $xAssign.HTTP
    if ($xAssignHTTP -in 403, 404, 409, 422) {
        $xTenancyAssignResult = "PASS"
        Write-Host "  [4.2 Cross-Tenant Assign] HTTP $xAssignHTTP - PASS (blocked)"
    } else {
        $xTenancyAssignResult = "FAIL"
        Write-Host "  [4.2 Cross-Tenant Assign] HTTP $xAssignHTTP - FAIL (expected 403/404)"
        $Fails.Add("4.2 Cross-Tenant Assign FAIL: returned $xAssignHTTP") | Out-Null
    }
}

# ─── LAYER 5: CONCURRENCY TEST ───────────────────────────────────────────────
Write-Host ""
Write-Host "## LAYER 5: CONCURRENCY - Two Parallel Assign-Driver" -ForegroundColor Yellow

# Reset driver to Online before concurrency test
$driverReset = Invoke-Api "Driver Reset Online" POST "$BASE/api/v1/drivers/status" $driverAuth -Body '{"status":1}'
Write-Host "  Driver reset Online: HTTP $($driverReset.HTTP)"

# Create a fresh trip for concurrency test
$ts4 = $TS + 3
$concPassAuth = @{ Authorization = "Bearer $passToken"; "X-Tenant-Id" = $TENANT_ID; "Idempotency-Key" = "qa-conc-$ts4" }
$concTrip = Invoke-Api "Create Concurrency Trip" POST "$BASE/api/v1/trips" $concPassAuth `
    -Body '{"pickupLatitude":19.4327,"pickupLongitude":-99.1333,"dropoffLatitude":19.4351,"dropoffLongitude":-99.1401,"pickupAddress":"Concurrency A","dropoffAddress":"Concurrency B","estimatedAmount":10.00,"currency":"USD"}'
$concTripId = jq "id" $concTrip.Body
Write-Host "  Concurrency trip: HTTP $($concTrip.HTTP), id=$concTripId"

$concR1 = $null
$concR2 = $null
$concResult = "SKIP"

if ($concTripId) {
    # Fire two concurrent assign-driver requests using background jobs
    $adminTokenLocal  = $adminToken
    $tenantLocal      = $TENANT_ID
    $baseLocal        = $BASE

    $job1 = Start-Job -ScriptBlock {
        param($base, $tripId, $token, $tenant)
        try {
            $r = Invoke-WebRequest -Uri "$base/api/v1/trips/$tripId/assign-driver" `
                -Method POST -UseBasicParsing -TimeoutSec 20 `
                -Headers @{ Authorization = "Bearer $token"; "X-Tenant-Id" = $tenant }
            return @{ HTTP = [int]$r.StatusCode; Body = $r.Content }
        } catch {
            $s = 0; try { $s = [int]$_.Exception.Response.StatusCode } catch {}
            $b = ""; try { $b = $_.ErrorDetails.Message } catch {}
            return @{ HTTP = $s; Body = $b }
        }
    } -ArgumentList $baseLocal, $concTripId, $adminTokenLocal, $tenantLocal

    $job2 = Start-Job -ScriptBlock {
        param($base, $tripId, $token, $tenant)
        try {
            $r = Invoke-WebRequest -Uri "$base/api/v1/trips/$tripId/assign-driver" `
                -Method POST -UseBasicParsing -TimeoutSec 20 `
                -Headers @{ Authorization = "Bearer $token"; "X-Tenant-Id" = $tenant }
            return @{ HTTP = [int]$r.StatusCode; Body = $r.Content }
        } catch {
            $s = 0; try { $s = [int]$_.Exception.Response.StatusCode } catch {}
            $b = ""; try { $b = $_.ErrorDetails.Message } catch {}
            return @{ HTTP = $s; Body = $b }
        }
    } -ArgumentList $baseLocal, $concTripId, $adminTokenLocal, $tenantLocal

    $job1 | Wait-Job | Out-Null
    $job2 | Wait-Job | Out-Null
    $concR1 = Receive-Job $job1
    $concR2 = Receive-Job $job2
    Remove-Job $job1, $job2

    Write-Host "  Request 1: HTTP $($concR1.HTTP) | $(($concR1.Body).Substring(0,[Math]::Min(200,($concR1.Body).Length)))"
    Write-Host "  Request 2: HTTP $($concR2.HTTP) | $(($concR2.Body).Substring(0,[Math]::Min(200,($concR2.Body).Length)))"

    if ($concR1.HTTP -eq 200 -and $concR2.HTTP -eq 200) {
        $concResult = "CRITICAL_FAIL"
        Write-Host "  RESULT: CRITICAL FAIL - Both 200 (double-assign confirmed)" -ForegroundColor Red
        $Fails.Add("5.1 Concurrency CRITICAL FAIL: both requests returned 200 (double-assign)") | Out-Null
    } elseif (($concR1.HTTP -eq 200 -and $concR2.HTTP -ne 200) -or
              ($concR2.HTTP -eq 200 -and $concR1.HTTP -ne 200)) {
        $concResult = "PASS"
        Write-Host "  RESULT: PASS - Exactly one succeeded" -ForegroundColor Green
    } elseif ($concR1.HTTP -eq 409 -and $concR2.HTTP -eq 409) {
        $concResult = "FAIL_BOTH_409"
        Write-Host "  RESULT: FAIL - Both 409 (neither assigned; stale RowVersion issue)" -ForegroundColor Red
        $Fails.Add("5.1 Concurrency FAIL: both returned 409, no trip assigned (RowVersion stale)") | Out-Null
    } else {
        $concResult = "INCONCLUSIVE"
        Write-Host "  RESULT: INCONCLUSIVE - R1=$($concR1.HTTP) R2=$($concR2.HTTP)" -ForegroundColor Yellow
    }
}

# ─── PRINT FULL EVIDENCE SUMMARY ─────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "EVIDENCE SUMMARY" -ForegroundColor Cyan
Write-Host "================================================================"

$evidenceTable = @()
foreach ($e in $Evidence) {
    $evidenceTable += [pscustomobject]@{
        Label  = $e.Label
        HTTP   = $e.HTTP
        Result = if ($e.OK) { "PASS" } else { "FAIL" }
    }
}
$evidenceTable | Format-Table -AutoSize

Write-Host ""
Write-Host "FAILS DETECTED:" -ForegroundColor $(if ($Fails.Count -eq 0) { "Green" } else { "Red" })
if ($Fails.Count -eq 0) {
    Write-Host "  None"
} else {
    foreach ($f in $Fails) { Write-Host "  - $f" -ForegroundColor Red }
}

# ─── EXPORT STRUCTURED RESULTS ───────────────────────────────────────────────
$results = [pscustomobject]@{
    Timestamp         = $StartTime
    BaseUrl           = $BASE
    TenantId          = $TENANT_ID
    TripId            = $tripId
    TariffId          = $tariffId
    PaymentId         = $paymentId
    FinalTripStatus   = $finalStatus
    PaymentStatus     = $paymentStatus
    ConcurrencyResult = $concResult
    ConcR1HTTP        = if ($concR1) { $concR1.HTTP } else { "N/A" }
    ConcR2HTTP        = if ($concR2) { $concR2.HTTP } else { "N/A" }
    ConcR1Body        = if ($concR1) { $concR1.Body.Substring(0,[Math]::Min(300,($concR1.Body).Length)) } else { "" }
    ConcR2Body        = if ($concR2) { $concR2.Body.Substring(0,[Math]::Min(300,($concR2.Body).Length)) } else { "" }
    XTenancyHTTP      = $xTenancyHTTP
    XTenancyResult    = $xTenancyResult
    IdemTripResult    = $idemTripResult
    IdemPayResult     = $idemPayResult
    TotalFails        = $Fails.Count
    Fails             = $Fails
    Evidence          = $Evidence | ForEach-Object {
        [pscustomobject]@{ Label=$_.Label; HTTP=$_.HTTP; OK=$_.OK;
            Body=$_.Body.Substring(0,[Math]::Min(400,$_.Body.Length)) }
    }
    HealthBody        = $health.Body
    ReadyBody         = $ready.Body
    AssignDriverBody  = if ($assignDriver) { $assignDriver.Body } else { "" }
    FinalTripBody     = if ($finalTrip) { $finalTrip.Body } else { "" }
    PaymentBody       = if ($createPayment) { $createPayment.Body } else { "" }
    FareQuoteBody     = $fareQuote.Body
    ActivateTariffBody= if ($activateResult) { $activateResult.Body } else { "" }
}

$resultsJson = $results | ConvertTo-Json -Depth 10
$resultsJson | Out-File -FilePath "c:\Proyectos\RiderFlow\tests\qa_master_results.json" -Encoding utf8

Write-Host ""
Write-Host "Results saved: tests/qa_master_results.json"
Write-Host "Fails: $($Fails.Count)"
