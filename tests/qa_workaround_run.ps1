# QA Workaround Run: Registers a fresh driver to bypass BUG-004 (CurrentTripId not reset on Online)
# Documents all findings including the bugs found

$BASE    = "http://127.0.0.1:55392"
$TENANT  = "00000000-0000-0000-0000-000000000001"
$TS      = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$Results = [ordered]@{}

function Req {
    param($method, $url, $headers = @{}, $body = $null)
    $p = @{ Uri=$url; Method=$method; UseBasicParsing=$true; ContentType="application/json"; TimeoutSec=25 }
    if ($headers.Count -gt 0) { $p["Headers"] = $headers }
    if ($body) { $p["Body"] = $body }
    try {
        $r = Invoke-WebRequest @p
        return [pscustomobject]@{ HTTP=[int]$r.StatusCode; Body=$r.Content; OK=$true }
    } catch {
        $s=0; try{$s=[int]$_.Exception.Response.StatusCode}catch{}
        $b=""; try{$b=$_.ErrorDetails.Message}catch{}
        # Try to read the stream body
        if ([string]::IsNullOrEmpty($b)) {
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $b = $reader.ReadToEnd()
            } catch {}
        }
        return [pscustomobject]@{ HTTP=$s; Body=$b; OK=$false }
    }
}
function J { param($key, $json) try{($json|ConvertFrom-Json).$key}catch{""} }
function Pass { param($label) Write-Host "  [$label] PASS" -ForegroundColor Green }
function Fail { param($label, $detail) Write-Host "  [$label] FAIL - $detail" -ForegroundColor Red }

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host "QA WORKAROUND RUN - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') UTC" -ForegroundColor Cyan
Write-Host "Purpose: Test lifecycle with fresh driver to isolate BUG-004" -ForegroundColor Cyan
Write-Host "================================================================`n"

# ── GET ADMIN TOKEN ──────────────────────────────────────────────────────────
$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = J "accessToken" $r.Body
$adminAuth  = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT }
$Results["admin_login"] = $r.HTTP
Write-Host "[Admin Login] HTTP $($r.HTTP)"

# ── REGISTER + ONBOARD FRESH DRIVER ─────────────────────────────────────────
Write-Host "`n## FRESH DRIVER SETUP (workaround for BUG-004)"
$driverEmail = "fresh-driver-$TS@movix.io"
$driverPass  = "FreshDriver@1234"

# Register
$r = Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$driverEmail`",`"password`":`"$driverPass`",`"tenantId`":`"$TENANT`",`"role`":2}"
Write-Host "  [Register Fresh Driver] HTTP $($r.HTTP) Body: $($r.Body.Substring(0,[Math]::Min(200,$r.Body.Length)))"
$Results["fresh_driver_register"] = $r.HTTP

# Login
$r = Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$driverEmail`",`"password`":`"$driverPass`"}"
Write-Host "  [Login Fresh Driver] HTTP $($r.HTTP)"
$freshDriverToken = J "accessToken" $r.Body
$Results["fresh_driver_login"] = $r.HTTP

if ([string]::IsNullOrEmpty($freshDriverToken)) {
    Write-Host "  ABORT: Could not get fresh driver token. Body: $($r.Body)"
    exit 1
}
$freshDriverAuth = @{ Authorization="Bearer $freshDriverToken"; "X-Tenant-Id"=$TENANT }

# Onboard
$r = Req POST "$BASE/api/v1/drivers/onboarding" $freshDriverAuth `
    '{"licenseNumber":"LIC-FRESH-001","vehiclePlate":"FRESH-01","vehicleModel":"Tesla","vehicleColor":"White"}'
Write-Host "  [Onboard Fresh Driver] HTTP $($r.HTTP) Body: $($r.Body.Substring(0,[Math]::Min(300,$r.Body.Length)))"
$Results["fresh_driver_onboard"] = $r.HTTP

if (-not $r.OK) {
    Write-Host "  WARN: Onboarding failed. May need admin to elevate role first."
    # Check if role needs to be set to Driver
    $r.Body
}

# Set Driver Online
$r = Req POST "$BASE/api/v1/drivers/status" $freshDriverAuth '{"status":1}'
Write-Host "  [Fresh Driver Online] HTTP $($r.HTTP) Body: $($r.Body)"
$Results["fresh_driver_online"] = $r.HTTP

# Driver Location
$r = Req POST "$BASE/api/v1/drivers/location" $freshDriverAuth '{"latitude":19.4326,"longitude":-99.1332}'
Write-Host "  [Fresh Driver Location] HTTP $($r.HTTP)"
$Results["fresh_driver_location"] = $r.HTTP

# ── REGISTER PASSENGER ────────────────────────────────────────────────────────
Write-Host "`n## PASSENGER SETUP"
$passEmail = "passenger-fresh-$TS@movix.io"
$r = Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$passEmail`",`"password`":`"PassFresh@1234`",`"tenantId`":`"$TENANT`"}"
Write-Host "  [Register Passenger] HTTP $($r.HTTP)"
$Results["passenger_register"] = $r.HTTP

$r = Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$passEmail`",`"password`":`"PassFresh@1234`"}"
Write-Host "  [Login Passenger] HTTP $($r.HTTP)"
$passToken = J "accessToken" $r.Body
if ([string]::IsNullOrEmpty($passToken)) { $passToken = $adminToken; Write-Host "  WARN: fallback to admin token" }
$passAuth  = @{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT }
$Results["passenger_login"] = $r.HTTP

# ── CREATE TARIFF ─────────────────────────────────────────────────────────────
Write-Host "`n## TARIFF SETUP"
$prio = ($TS % 9000) + 200
$r = Req POST "$BASE/api/v1/admin/tariffs" $adminAuth `
    "{`"name`":`"Fresh QA $TS`",`"currency`":`"USD`",`"baseFare`":2.50,`"pricePerKm`":1.20,`"pricePerMinute`":0.25,`"minimumFare`":5.00,`"priority`":$prio,`"effectiveFromUtc`":`"2025-01-01T00:00:00Z`",`"effectiveUntilUtc`":null}"
$tariffId = J "id" $r.Body
Write-Host "  [Create Tariff] HTTP $($r.HTTP) id=$tariffId"
$Results["create_tariff"] = $r.HTTP

$r = Req POST "$BASE/api/v1/admin/tariffs/$tariffId/activate" $adminAuth
Write-Host "  [Activate Tariff] HTTP $($r.HTTP)"
$Results["activate_tariff"] = $r.HTTP

# ── FULL LIFECYCLE ────────────────────────────────────────────────────────────
Write-Host "`n## FULL RIDE LIFECYCLE (with fresh driver)"

# Create Trip
$idemKey = "fresh-trip-$TS"
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$idemKey }) `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen Fresh","dropoffAddress":"Destino Fresh","estimatedAmount":12.50,"currency":"USD"}'
$tripId     = J "id" $r.Body
$tripStatus = J "status" $r.Body
Write-Host "  [Create Trip] HTTP $($r.HTTP) id=$tripId status=$tripStatus"
$Results["create_trip"] = $r.HTTP
$createTripBody = $r.Body

# Assign Driver
$r = Req POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth
$assignStatus = J "status" $r.Body
Write-Host "  [Assign Driver] HTTP $($r.HTTP) status=$assignStatus body=$($r.Body.Substring(0,[Math]::Min(300,$r.Body.Length)))"
$Results["assign_driver"] = $r.HTTP
$assignDriverBody = $r.Body

# Driver Accept
if ($r.OK) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/accept" $freshDriverAuth
    $tripStatus = J "status" $r.Body
    Write-Host "  [Driver Accept] HTTP $($r.HTTP) status=$tripStatus"
    $Results["driver_accept"] = $r.HTTP
    $acceptBody = $r.Body
} else {
    Write-Host "  [Driver Accept] SKIP (assign failed)"
    $Results["driver_accept"] = "SKIP"
}

# Driver Arrive
if ($Results["driver_accept"] -eq 200) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/arrive" $freshDriverAuth
    $tripStatus = J "status" $r.Body
    Write-Host "  [Driver Arrive] HTTP $($r.HTTP) status=$tripStatus"
    $Results["driver_arrive"] = $r.HTTP
    $arriveBody = $r.Body
} else {
    Write-Host "  [Driver Arrive] SKIP"
    $Results["driver_arrive"] = "SKIP"
}

# Driver Start
if ($Results["driver_arrive"] -eq 200) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/start" $freshDriverAuth
    $tripStatus = J "status" $r.Body
    Write-Host "  [Driver Start] HTTP $($r.HTTP) status=$tripStatus"
    $Results["driver_start"] = $r.HTTP
    $startBody = $r.Body
} else {
    Write-Host "  [Driver Start] SKIP"
    $Results["driver_start"] = "SKIP"
}

# Driver Complete
if ($Results["driver_start"] -eq 200) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/complete" $freshDriverAuth '{"distanceKm":5.2,"durationMinutes":18}'
    $tripStatus = J "status" $r.Body
    Write-Host "  [Driver Complete] HTTP $($r.HTTP) status=$tripStatus"
    $Results["driver_complete"] = $r.HTTP
    $completeBody = $r.Body
} else {
    Write-Host "  [Driver Complete] SKIP"
    $Results["driver_complete"] = "SKIP"
}

# GET Trip Final
$r = Req GET "$BASE/api/v1/trips/$tripId" $passAuth
$finalStatus = J "status" $r.Body
Write-Host "  [GET Trip Final] HTTP $($r.HTTP) finalStatus=$finalStatus"
$Results["get_trip_final"] = $r.HTTP
$finalTripBody = $r.Body

# Payment
$paymentId = "N/A"
$paymentStatus = "N/A"
if ($Results["driver_complete"] -eq 200) {
    $r = Req POST "$BASE/api/v1/payments" (@{ Authorization="Bearer $passToken"; "Idempotency-Key"="fresh-pay-$TS" }) `
        "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}"
    $paymentId     = J "id" $r.Body
    $paymentStatus = J "status" $r.Body
    Write-Host "  [Create Payment] HTTP $($r.HTTP) id=$paymentId status=$paymentStatus body=$($r.Body)"
    $Results["create_payment"] = $r.HTTP
    $paymentBody = $r.Body
} else {
    Write-Host "  [Create Payment] SKIP"
    $Results["create_payment"] = "SKIP"
}

# ── CONCURRENCY TEST (with driver freed after complete) ───────────────────────
Write-Host "`n## CONCURRENCY TEST"

# Create second driver for concurrency test
$driver2Email = "conc-driver-$TS@movix.io"
$r = Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$driver2Email`",`"password`":`"ConcDriver@1234`",`"tenantId`":`"$TENANT`",`"role`":2}"
Write-Host "  [Register Driver2] HTTP $($r.HTTP)"
$r = Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$driver2Email`",`"password`":`"ConcDriver@1234`"}"
$driver2Token = J "accessToken" $r.Body
$driver2Auth  = @{ Authorization="Bearer $driver2Token"; "X-Tenant-Id"=$TENANT }
Write-Host "  [Login Driver2] HTTP $($r.HTTP)"

# Onboard driver2
if ($driver2Token) {
    $r = Req POST "$BASE/api/v1/drivers/onboarding" $driver2Auth '{"licenseNumber":"LIC-CONC","vehiclePlate":"CONC-01","vehicleModel":"BMW","vehicleColor":"Blue"}'
    Write-Host "  [Onboard Driver2] HTTP $($r.HTTP)"
    $r = Req POST "$BASE/api/v1/drivers/status" $driver2Auth '{"status":1}'
    Write-Host "  [Driver2 Online] HTTP $($r.HTTP)"
    $r = Req POST "$BASE/api/v1/drivers/location" $driver2Auth '{"latitude":19.4326,"longitude":-99.1332}'
    Write-Host "  [Driver2 Location] HTTP $($r.HTTP)"
}

# Ensure original fresh driver is also online (after completing the trip, CurrentTripId should be null)
$r = Req POST "$BASE/api/v1/drivers/status" $freshDriverAuth '{"status":1}'
Write-Host "  [Fresh Driver Re-Online] HTTP $($r.HTTP)"

# Create concurrency trip
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"="conc-trip-$TS" }) `
    '{"pickupLatitude":19.4328,"pickupLongitude":-99.1334,"dropoffLatitude":19.4352,"dropoffLongitude":-99.1402,"pickupAddress":"Concurrency A","dropoffAddress":"Concurrency B","estimatedAmount":10.00,"currency":"USD"}'
$concTripId = J "id" $r.Body
Write-Host "  [Create Concurrency Trip] HTTP $($r.HTTP) id=$concTripId"

$concR1 = $null
$concR2 = $null
$concResult = "SKIP"

if ($concTripId) {
    $j1 = Start-Job -ScriptBlock {
        param($b,$tid,$tok,$ten)
        $wc = New-Object System.Net.WebClient
        $wc.Headers["Authorization"] = "Bearer $tok"
        $wc.Headers["X-Tenant-Id"] = $ten
        $wc.Headers["Content-Type"] = "application/json"
        try {
            $body = $wc.UploadString("$b/api/v1/trips/$tid/assign-driver", "POST", "")
            return @{ HTTP=200; Body=$body }
        } catch [System.Net.WebException] {
            $s=[int]$_.Exception.Response.StatusCode
            $stream=$_.Exception.Response.GetResponseStream()
            $rd=New-Object System.IO.StreamReader($stream)
            return @{ HTTP=$s; Body=$rd.ReadToEnd() }
        }
    } -ArgumentList $BASE, $concTripId, $adminToken, $TENANT

    $j2 = Start-Job -ScriptBlock {
        param($b,$tid,$tok,$ten)
        $wc = New-Object System.Net.WebClient
        $wc.Headers["Authorization"] = "Bearer $tok"
        $wc.Headers["X-Tenant-Id"] = $ten
        $wc.Headers["Content-Type"] = "application/json"
        try {
            $body = $wc.UploadString("$b/api/v1/trips/$tid/assign-driver", "POST", "")
            return @{ HTTP=200; Body=$body }
        } catch [System.Net.WebException] {
            $s=[int]$_.Exception.Response.StatusCode
            $stream=$_.Exception.Response.GetResponseStream()
            $rd=New-Object System.IO.StreamReader($stream)
            return @{ HTTP=$s; Body=$rd.ReadToEnd() }
        }
    } -ArgumentList $BASE, $concTripId, $adminToken, $TENANT

    $j1 | Wait-Job | Out-Null
    $j2 | Wait-Job | Out-Null
    $concR1 = Receive-Job $j1
    $concR2 = Receive-Job $j2
    Remove-Job $j1,$j2

    Write-Host "  [Concurrent R1] HTTP $($concR1.HTTP) | $($concR1.Body)"
    Write-Host "  [Concurrent R2] HTTP $($concR2.HTTP) | $($concR2.Body)"

    if ($concR1.HTTP -eq 200 -and $concR2.HTTP -eq 200) {
        $concResult = "CRITICAL_FAIL_DOUBLE_ASSIGN"
        Write-Host "  RESULT: CRITICAL FAIL - BOTH 200 (double assign!)" -ForegroundColor Red
    } elseif (($concR1.HTTP -eq 200 -and $concR2.HTTP -ne 200) -or ($concR2.HTTP -eq 200 -and $concR1.HTTP -ne 200)) {
        $concResult = "PASS"
        $winner = if ($concR1.HTTP -eq 200) { "R1" } else { "R2" }
        $loser  = if ($concR1.HTTP -ne 200) { "R1=$($concR1.HTTP)" } else { "R2=$($concR2.HTTP)" }
        Write-Host "  RESULT: PASS - $winner succeeded, $loser rejected" -ForegroundColor Green
    } elseif ($concR1.HTTP -eq 409 -and $concR2.HTTP -eq 409) {
        $concResult = "FAIL_BOTH_409"
        Write-Host "  RESULT: FAIL - both 409 (RowVersion or NO_DRIVERS_AVAILABLE)" -ForegroundColor Red
    } else {
        $concResult = "INCONCLUSIVE"
        Write-Host "  RESULT: INCONCLUSIVE R1=$($concR1.HTTP) R2=$($concR2.HTTP)"
    }
}

# ── IDEMPOTENCY ──────────────────────────────────────────────────────────────
Write-Host "`n## IDEMPOTENCY"
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$idemKey }) `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen Fresh","dropoffAddress":"Destino Fresh","estimatedAmount":12.50,"currency":"USD"}'
$idemId = J "id" $r.Body
$idemResult = if ($r.HTTP -eq 200 -and $idemId -eq $tripId) { "PASS" } elseif ($r.HTTP -eq 200 -and $idemId -ne $tripId) { "FAIL_DUPLICATE" } else { "FAIL_$($r.HTTP)" }
Write-Host "  [Idem Trip] HTTP $($r.HTTP) returned=$idemId original=$tripId result=$idemResult"

# ── SUMMARY ──────────────────────────────────────────────────────────────────
Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host "FINAL RESULTS" -ForegroundColor Cyan
Write-Host "================================================================"
$Results.GetEnumerator() | ForEach-Object {
    $pass = $_.Value -eq 200 -or $_.Value -eq 202 -or $_.Value -eq "SKIP"
    $color = if ($pass) { "Green" } else { "Red" }
    Write-Host ("  {0,-30} {1}" -f $_.Key, $_.Value) -ForegroundColor $color
}
Write-Host "  Concurrency:                   $concResult"
Write-Host "  Idempotency:                   $idemResult"
Write-Host "  Final Trip Status:             $finalStatus"
Write-Host "  Payment Status:                $paymentStatus"

# Export for document generation
$export = [pscustomobject]@{
    Timestamp        = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TripId           = $tripId
    FinalStatus      = $finalStatus
    PaymentId        = $paymentId
    PaymentStatus    = $paymentStatus
    ConcResult       = $concResult
    ConcR1HTTP       = if ($concR1) { $concR1.HTTP } else { "N/A" }
    ConcR2HTTP       = if ($concR2) { $concR2.HTTP } else { "N/A" }
    ConcR1Body       = if ($concR1) { $concR1.Body } else { "" }
    ConcR2Body       = if ($concR2) { $concR2.Body } else { "" }
    IdemResult       = $idemResult
    Results          = $Results
    AssignDriverBody = $assignDriverBody
    FinalTripBody    = $finalTripBody
    PaymentBody      = if ($paymentBody) { $paymentBody } else { "" }
    CompleteBody     = if ($completeBody) { $completeBody } else { "" }
}
$export | ConvertTo-Json -Depth 8 | Out-File "C:\Proyectos\RiderFlow\tests\qa_workaround_results.json" -Encoding utf8
Write-Host "`nSaved: tests/qa_workaround_results.json"
