# QA Full Validation - FASE 1 a 4. Solo ejecutar, observar, reportar. No modifica codigo ni DB.
$ErrorActionPreference = "Continue"
$baseUrl = "http://127.0.0.1:55392"
$allResults = [System.Collections.ArrayList]::new()
$fails = [System.Collections.ArrayList]::new()

function Invoke-Step {
    param($Phase, $StepName, $Method, $Uri, $Headers = @{}, $Body = $null)
    try {
        $params = @{ Uri = $Uri; Method = $Method; ContentType = "application/json"; UseBasicParsing = $true; TimeoutSec = 20 }
        if ($Headers.Count -gt 0) { $params["Headers"] = $Headers }
        if ($Body) { $params["Body"] = $Body }
        $r = Invoke-WebRequest @params
        $allResults.Add(@{ Phase = $Phase; Step = $StepName; Status = $r.StatusCode; Ok = $true; Content = $r.Content }) | Out-Null
        return @{ Status = $r.StatusCode; Ok = $true; Content = $r.Content }
    } catch {
        $status = [int]$_.Exception.Response.StatusCode
        $content = try { $_.ErrorDetails.Message } catch { "" }
        $allResults.Add(@{ Phase = $Phase; Step = $StepName; Status = $status; Ok = $false; Content = $content }) | Out-Null
        return @{ Status = $status; Ok = $false; Content = $content }
    }
}

# ========== FASE 1 — SANITY CHECK ==========
$health = Invoke-Step "F1" "GET /health" GET "$baseUrl/health"
if (-not $health.Ok -or $health.Status -ne 200) {
    $fails.Add("FASE 1: GET /health failed - HTTP $($health.Status)") | Out-Null
}

$ready = Invoke-Step "F1" "GET /ready" GET "$baseUrl/ready"
if ($ready.Ok -and $ready.Content) {
    $readyJson = $ready.Content | ConvertFrom-Json
    $allResults.Add(@{ Phase = "F1"; Step = "ready.status"; Status = 0; Ok = $true; Content = $readyJson.status }) | Out-Null
}

# No DB access: we cannot directly check "tarifas activas inconsistentes" or "driver Online sin CurrentTripId". We proceed to FASE 2.

# ========== FASE 2 — RIDE LIFECYCLE ==========
$loginAdmin = Invoke-Step "F2" "Login Admin" POST "$baseUrl/api/v1/auth/login" -Body '{"email":"admin@movix.io","password":"Admin@1234!"}'
if (-not $loginAdmin.Ok) { $fails.Add("F2: Login Admin HTTP $($loginAdmin.Status)") | Out-Null }
$accessToken = ($loginAdmin.Content | ConvertFrom-Json).accessToken
$adminAuth = @{ "Authorization" = "Bearer $accessToken" }
$tenantId = "00000000-0000-0000-0000-000000000001"

$loginDriver = Invoke-Step "F2" "Login Driver" POST "$baseUrl/api/v1/auth/login" -Body '{"email":"driver@movix.io","password":"Driver@1234!"}'
if (-not $loginDriver.Ok) { $fails.Add("F2: Login Driver HTTP $($loginDriver.Status)") | Out-Null }
$driverToken = ($loginDriver.Content | ConvertFrom-Json).accessToken
$driverAuth = @{ "Authorization" = "Bearer $driverToken"; "X-Tenant-Id" = $tenantId }

$passengerEmail = "passenger-qa@movix.io"
$passengerPassword = "PassengerQA@1234!"
Invoke-Step "F2" "Register Passenger" POST "$baseUrl/api/v1/auth/register" -Body "{`"email`":`"$passengerEmail`",`"password`":`"$passengerPassword`",`"tenantId`":`"$tenantId`"}" | Out-Null
$loginPassenger = Invoke-Step "F2" "Login Passenger" POST "$baseUrl/api/v1/auth/login" -Body "{`"email`":`"$passengerEmail`",`"password`":`"$passengerPassword`"}"
$passengerToken = ($loginPassenger.Content | ConvertFrom-Json).accessToken
$passengerAuth = @{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId }
$adminTenant = @{ "Authorization" = "Bearer $accessToken"; "X-Tenant-Id" = $tenantId }

$onboarding = Invoke-Step "F2" "Driver Onboarding" POST "$baseUrl/api/v1/drivers/onboarding" $driverAuth -Body '{"licenseNumber":"LIC","vehiclePlate":"QA-1","vehicleModel":"M","vehicleColor":"C"}'
if ($onboarding.Status -eq 500) { $fails.Add("F2: Driver Onboarding 500") | Out-Null }

$st = Invoke-Step "F2" "Driver Status Online" POST "$baseUrl/api/v1/drivers/status" $driverAuth -Body '{"status":1}'
if (-not $st.Ok) { $fails.Add("F2: Driver Status HTTP $($st.Status)") | Out-Null }
Invoke-Step "F2" "Driver Location" POST "$baseUrl/api/v1/drivers/location" $driverAuth -Body '{"latitude":19.43,"longitude":-99.13}' | Out-Null

$ct = Invoke-Step "F2" "Create Tariff" POST "$baseUrl/api/v1/admin/tariffs" $adminTenant -Body '{"name":"QA Tariff","currency":"USD","baseFare":2,"pricePerKm":1,"pricePerMinute":0.2,"minimumFare":5,"priority":90,"effectiveFromUtc":"2025-01-01T00:00:00Z","effectiveUntilUtc":null}'
$tariffId = if ($ct.Ok) { ($ct.Content | ConvertFrom-Json).id } else { $null }
if ($tariffId) {
    $act = Invoke-Step "F2" "Activate Tariff" POST "$baseUrl/api/v1/admin/tariffs/$tariffId/activate" $adminTenant
    if (-not $act.Ok -and $act.Status -ne 400) { $allResults.Add(@{ Phase = "F2"; Step = "Activate Tariff (note)"; Status = $act.Status; Ok = $false; Content = $act.Content }) | Out-Null }
}

$key = "qa-trip-$(New-Guid)"
$createTrip = Invoke-Step "F2" "Create Trip" POST "$baseUrl/api/v1/trips" (@{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId; "Idempotency-Key" = $key }) -Body '{"pickupLatitude":19.43,"pickupLongitude":-99.13,"dropoffLatitude":19.44,"dropoffLongitude":-99.14,"pickupAddress":"A","dropoffAddress":"B","estimatedAmount":10,"currency":"USD"}'
$tripId = if ($createTrip.Ok) { ($createTrip.Content | ConvertFrom-Json).id } else { $null }
$createTripStatus = if ($createTrip.Ok) { ($createTrip.Content | ConvertFrom-Json).status } else { $null }
if ($createTripStatus -ne "Requested" -and $createTrip.Ok) { $fails.Add("F2: Create Trip status not Requested: $createTripStatus") | Out-Null }

$assignDriver = $null
if ($tripId) {
    $assignDriver = Invoke-Step "F2" "Assign Driver" POST "$baseUrl/api/v1/trips/$tripId/assign-driver" $adminTenant
    if ($assignDriver.Status -eq 409) { $fails.Add("F2: Assign Driver 409 (concurrency or no driver)") | Out-Null }
    if ($assignDriver.Status -eq 500) { $fails.Add("F2: Assign Driver 500") | Out-Null }
}
$assignStatus = if ($assignDriver.Ok) { ($assignDriver.Content | ConvertFrom-Json).status } else { $null }

if ($tripId -and $assignDriver.Ok) {
    Invoke-Step "F2" "Driver Arrive" POST "$baseUrl/api/v1/trips/$tripId/arrive" $driverAuth | Out-Null
    Invoke-Step "F2" "Driver Start" POST "$baseUrl/api/v1/trips/$tripId/start" $driverAuth | Out-Null
    $complete = Invoke-Step "F2" "Driver Complete" POST "$baseUrl/api/v1/trips/$tripId/complete" $driverAuth -Body '{"distanceKm":5,"durationMinutes":15}'
    $finalStatus = if ($complete.Ok) { ($complete.Content | ConvertFrom-Json).status } else { $null }
    Invoke-Step "F2" "GET /trips (final)" GET "$baseUrl/api/v1/trips/$tripId" $passengerAuth | Out-Null
    $pay = Invoke-Step "F2" "Create Payment" POST "$baseUrl/api/v1/payments" (@{ "Authorization" = "Bearer $passengerToken"; "Idempotency-Key" = "qa-pay-$(New-Guid)" }) -Body "{`"tripId`":`"$tripId`",`"amount`":12,`"currency`":`"USD`"}"
    $payStatus = if ($pay.Ok) { ($pay.Content | ConvertFrom-Json).status } else { $null }
}

# ========== FASE 3 — EDGE CASES ==========
# New trip for edge cases
$key2 = "qa-edge-$(New-Guid)"
$trip2 = Invoke-Step "F3" "Create Trip (edge)" POST "$baseUrl/api/v1/trips" (@{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId; "Idempotency-Key" = $key2 }) -Body '{"pickupLatitude":19.43,"pickupLongitude":-99.13,"dropoffLatitude":19.44,"dropoffLongitude":-99.14,"pickupAddress":"X","dropoffAddress":"Y","estimatedAmount":8,"currency":"USD"}'
$tripId2 = if ($trip2.Ok) { ($trip2.Content | ConvertFrom-Json).id } else { $null }

if ($tripId2) {
    $assign2 = Invoke-Step "F3" "Assign Driver (trip2)" POST "$baseUrl/api/v1/trips/$tripId2/assign-driver" $adminTenant
    if ($assign2.Ok) {
        $assignAgain = Invoke-Step "F3" "Assign Driver again (same trip)" POST "$baseUrl/api/v1/trips/$tripId2/assign-driver" $adminTenant
        if ($assignAgain.Status -eq 400 -and $assignAgain.Content -match "TRIP_INVALID_STATE") { $allResults.Add(@{ Phase = "F3"; Step = "Edge: Re-assign -> 400 TRIP_INVALID_STATE"; Status = 400; Ok = $true; Content = "OK" }) | Out-Null }
        elseif (-not $assignAgain.Ok) { $allResults.Add(@{ Phase = "F3"; Step = "Edge: Re-assign"; Status = $assignAgain.Status; Ok = $false; Content = $assignAgain.Content }) | Out-Null }
    }
}

# Trip without driver: create trip3, do NOT assign, driver tries Start -> expect 422 or 403
$key3 = "qa-edge3-$(New-Guid)"
$trip3 = Invoke-Step "F3" "Create Trip (no assign)" POST "$baseUrl/api/v1/trips" (@{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId; "Idempotency-Key" = $key3 }) -Body '{"pickupLatitude":19.43,"pickupLongitude":-99.13,"dropoffLatitude":19.44,"dropoffLongitude":-99.14,"pickupAddress":"P","dropoffAddress":"Q","estimatedAmount":7,"currency":"USD"}'
$tripId3 = if ($trip3.Ok) { ($trip3.Content | ConvertFrom-Json).id } else { $null }
if ($tripId3) {
    $startNoAssign = Invoke-Step "F3" "Start without Assign" POST "$baseUrl/api/v1/trips/$tripId3/start" $driverAuth
    if ($startNoAssign.Status -in 403, 422) { $allResults.Add(@{ Phase = "F3"; Step = "Edge: Start without assign"; Status = $startNoAssign.Status; Ok = $true; Content = "Expected" }) | Out-Null }
}

# Complete without Start: need a trip in Accepted, do Arrive then try Complete without Start
$key4 = "qa-edge4-$(New-Guid)"
$trip4 = Invoke-Step "F3" "Create Trip (complete no start)" POST "$baseUrl/api/v1/trips" (@{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId; "Idempotency-Key" = $key4 }) -Body '{"pickupLatitude":19.43,"pickupLongitude":-99.13,"dropoffLatitude":19.44,"dropoffLongitude":-99.14,"pickupAddress":"M","dropoffAddress":"N","estimatedAmount":9,"currency":"USD"}'
$tripId4 = if ($trip4.Ok) { ($trip4.Content | ConvertFrom-Json).id } else { $null }
if ($tripId4) {
    $assign4 = Invoke-Step "F3" "Assign (trip4)" POST "$baseUrl/api/v1/trips/$tripId4/assign-driver" $adminTenant
    if ($assign4.Ok) {
        Invoke-Step "F3" "Arrive (trip4)" POST "$baseUrl/api/v1/trips/$tripId4/arrive" $driverAuth | Out-Null
        $completeNoStart = Invoke-Step "F3" "Complete without Start" POST "$baseUrl/api/v1/trips/$tripId4/complete" $driverAuth -Body '{"distanceKm":1,"durationMinutes":5}'
        if ($completeNoStart.Status -eq 422) { $allResults.Add(@{ Phase = "F3"; Step = "Edge: Complete without Start -> 422"; Status = 422; Ok = $true; Content = "OK" }) | Out-Null }
    }
}

# Payment on non-completed trip (use tripId3 which has no driver / not completed)
if ($tripId3) {
    $payBad = Invoke-Step "F3" "Payment on non-completed trip" POST "$baseUrl/api/v1/payments" (@{ "Authorization" = "Bearer $passengerToken"; "Idempotency-Key" = "qa-bad-$(New-Guid)" }) -Body "{`"tripId`":`"$tripId3`",`"amount`":5,`"currency`":`"USD`"}"
    if ($payBad.Status -eq 400) { $allResults.Add(@{ Phase = "F3"; Step = "Edge: Payment non-completed -> 400"; Status = 400; Ok = $true; Content = "OK" }) | Out-Null }
}

# Double payment same trip different idempotency: use completed trip ($tripId) if we have it
if ($tripId -and $complete.Ok) {
    $pay2a = Invoke-Step "F3" "Payment 2 (idem key A)" POST "$baseUrl/api/v1/payments" (@{ "Authorization" = "Bearer $passengerToken"; "Idempotency-Key" = "qa-second-A" }) -Body "{`"tripId`":`"$tripId`",`"amount`":12,`"currency`":`"USD`"}"
    $pay2b = Invoke-Step "F3" "Payment 2 (idem key B)" POST "$baseUrl/api/v1/payments" (@{ "Authorization" = "Bearer $passengerToken"; "Idempotency-Key" = "qa-second-B" }) -Body "{`"tripId`":`"$tripId`",`"amount`":12,`"currency`":`"USD`"}"
}

# ========== FASE 4 — CONCURRENCIA ==========
$key5 = "qa-conc-$(New-Guid)"
$tripConc = Invoke-Step "F4" "Create Trip (conc)" POST "$baseUrl/api/v1/trips" (@{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = $tenantId; "Idempotency-Key" = $key5 }) -Body '{"pickupLatitude":19.43,"pickupLongitude":-99.13,"dropoffLatitude":19.44,"dropoffLongitude":-99.14,"pickupAddress":"C1","dropoffAddress":"C2","estimatedAmount":11,"currency":"USD"}'
$tripIdConc = if ($tripConc.Ok) { ($tripConc.Content | ConvertFrom-Json).id } else { $null }
$assignJob1 = $assignJob2 = $null
if ($tripIdConc) {
    $authHeader = "Bearer $accessToken"
    $assignJob1 = Start-Job -ScriptBlock {
        param($url, $tid, $bearer, $tenant)
        $h = @{ "Authorization" = $bearer; "X-Tenant-Id" = $tenant }
        try {
            $r = Invoke-WebRequest -Uri "$url/api/v1/trips/$tid/assign-driver" -Method POST -Headers $h -UseBasicParsing -TimeoutSec 15
            return @{ Status = $r.StatusCode; Ok = $true }
        } catch {
            return @{ Status = [int]$_.Exception.Response.StatusCode; Ok = $false }
        }
    } -ArgumentList $baseUrl, $tripIdConc, $authHeader, $tenantId
    $assignJob2 = Start-Job -ScriptBlock {
        param($url, $tid, $bearer, $tenant)
        $h = @{ "Authorization" = $bearer; "X-Tenant-Id" = $tenant }
        try {
            $r = Invoke-WebRequest -Uri "$url/api/v1/trips/$tid/assign-driver" -Method POST -Headers $h -UseBasicParsing -TimeoutSec 15
            return @{ Status = $r.StatusCode; Ok = $true }
        } catch {
            return @{ Status = [int]$_.Exception.Response.StatusCode; Ok = $false }
        }
    } -ArgumentList $baseUrl, $tripIdConc, $authHeader, $tenantId
    $r1 = Wait-Job $assignJob1 | Receive-Job
    $r2 = Wait-Job $assignJob2 | Receive-Job
    Remove-Job $assignJob1, $assignJob2 -Force -ErrorAction SilentlyContinue
    $allResults.Add(@{ Phase = "F4"; Step = "Assign concurrent 1"; Status = $r1.Status; Ok = $r1.Ok; Content = "" }) | Out-Null
    $allResults.Add(@{ Phase = "F4"; Step = "Assign concurrent 2"; Status = $r2.Status; Ok = $r2.Ok; Content = "" }) | Out-Null
    $one200 = ($r1.Status -eq 200) -or ($r2.Status -eq 200)
    $one409 = ($r1.Status -eq 409) -or ($r2.Status -eq 409)
    if (-not $one200 -or -not $one409) { $fails.Add("F4: Concurrency expected one 200 and one 409; got $($r1.Status) and $($r2.Status)") | Out-Null }
}

# ========== OUTPUT FOR REPORT ==========
$reportPath = "c:\Proyectos\RiderFlow\tests\QA_FULL_VALIDATION_RESULTS.json"
@{
    Timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    BaseUrl = $baseUrl
    Results = $allResults
    Fails = $fails
    TotalSteps = $allResults.Count
    FailCount = $fails.Count
} | ConvertTo-Json -Depth 5 | Set-Content $reportPath -Encoding utf8
Write-Host "Results written to $reportPath"
Write-Host "Total steps: $($allResults.Count); Fails: $($fails.Count)"
$fails | ForEach-Object { Write-Host "FAIL: $_" }
