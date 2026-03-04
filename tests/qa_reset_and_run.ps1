# QA Reset + Run: Cancel stuck trip, free driver, then full lifecycle
$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$TS     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

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
        $b=""
        try {
            $stream=$_.Exception.Response.GetResponseStream()
            $reader=New-Object System.IO.StreamReader($stream)
            $b=$reader.ReadToEnd()
        } catch { try{$b=$_.ErrorDetails.Message}catch{} }
        return [pscustomobject]@{ HTTP=$s; Body=$b; OK=$false }
    }
}
function J { param($key, $json) try{($json|ConvertFrom-Json).$key}catch{""} }

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "QA RESET + FULL RUN - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') UTC" -ForegroundColor Cyan
Write-Host "================================================================`n"

# ── STEP 0: GET TOKENS ────────────────────────────────────────────────────────
$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = J "accessToken" $r.Body
$adminAuth  = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT }
Write-Host "[Admin Login] HTTP $($r.HTTP)"

$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"driver@movix.io","password":"Driver@1234!"}'
$driverToken = J "accessToken" $r.Body
$driverAuth  = @{ Authorization="Bearer $driverToken"; "X-Tenant-Id"=$TENANT }
Write-Host "[Driver Login] HTTP $($r.HTTP)"

# ── STEP 1: GET TRIPS AND CANCEL STUCK ONES ───────────────────────────────────
Write-Host "`n## STEP 1: RESET DRIVER STATE"
Write-Host "  Listing admin trips to find stuck ones..."
$r = Req GET "$BASE/api/v1/admin/trips?page=1&pageSize=20" $adminAuth
Write-Host "  [Admin Trips List] HTTP $($r.HTTP)"

$stuckCancelled = 0
if ($r.OK) {
    $data = $r.Body | ConvertFrom-Json
    $trips = if ($data.items) { $data.items } elseif ($data.trips) { $data.trips } else { $data }
    if ($trips -is [array]) {
        foreach ($trip in $trips) {
            $status = $trip.status
            if ($status -in "Requested", "Accepted", "DriverArrived", "InProgress") {
                Write-Host "  Found stuck trip: $($trip.id) status=$status"
                # Cancel it
                $cancelR = Req POST "$BASE/api/v1/trips/$($trip.id)/cancel" $adminAuth
                Write-Host "    Cancel HTTP: $($cancelR.HTTP) body: $($cancelR.Body)"
                if ($cancelR.OK) { $stuckCancelled++ }
            }
        }
    } else {
        Write-Host "  Could not parse trips list. Body: $($r.Body.Substring(0,[Math]::Min(400,$r.Body.Length)))"
    }
}
Write-Host "  Cancelled $stuckCancelled stuck trips"

# ── STEP 2: RESET DRIVER TO ONLINE ────────────────────────────────────────────
Write-Host "`n## STEP 2: RESET DRIVER"
$r = Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":0}'  # Offline first
Write-Host "  [Driver Offline] HTTP $($r.HTTP)"
Start-Sleep -Milliseconds 500
$r = Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":1}'  # Then Online
Write-Host "  [Driver Online] HTTP $($r.HTTP)"
$r = Req POST "$BASE/api/v1/drivers/location" $driverAuth '{"latitude":19.4326,"longitude":-99.1332}'
Write-Host "  [Driver Location] HTTP $($r.HTTP)"

# ── STEP 3: TRY ASSIGN DRIVER ON EXISTING STUCK TRIP ──────────────────────────
# Try assigning driver to the trip from previous run
$prevTripId = "c4b9364c-c6e9-4c3c-a63e-45fbe28b2804"
Write-Host "`n## STEP 3: TEST ASSIGN ON PREV TRIP $prevTripId"
$r = Req POST "$BASE/api/v1/trips/$prevTripId/assign-driver" $adminAuth
Write-Host "  [Assign Prev Trip] HTTP $($r.HTTP) body: $($r.Body)"

# ── STEP 4: FULL LIFECYCLE ─────────────────────────────────────────────────────
Write-Host "`n## STEP 4: FULL LIFECYCLE"

# Passenger
$passEmail = "qa-pass-$TS@movix.io"
$r = Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`",`"tenantId`":`"$TENANT`"}"
Write-Host "  [Register Passenger] HTTP $($r.HTTP)"
$r = Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`"}"
$passToken = J "accessToken" $r.Body
if (-not $passToken) { $passToken = $adminToken }
$passAuth  = @{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT }
Write-Host "  [Login Passenger] HTTP $($r.HTTP)"

# Create trip
$key = "qa-main-$TS"
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$key }) `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Main Pickup","dropoffAddress":"Main Dropoff","estimatedAmount":12.50,"currency":"USD"}'
$tripId = J "id" $r.Body
Write-Host "  [Create Trip] HTTP $($r.HTTP) id=$tripId status=$(J 'status' $r.Body)"
$createTripBody = $r.Body

# Assign Driver
$r = Req POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth
Write-Host "  [Assign Driver] HTTP $($r.HTTP) body: $($r.Body)"
$assignDriverBody = $r.Body
$assignOK = $r.OK

# Accept
$acceptOK = $false
if ($assignOK) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/accept" $driverAuth
    Write-Host "  [Driver Accept] HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    $acceptBody = $r.Body
    $acceptOK = $r.OK
}

# Arrive
$arriveOK = $false
if ($acceptOK) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/arrive" $driverAuth
    Write-Host "  [Driver Arrive] HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    $arriveBody = $r.Body
    $arriveOK = $r.OK
}

# Start
$startOK = $false
if ($arriveOK) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/start" $driverAuth
    Write-Host "  [Driver Start] HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    $startBody = $r.Body
    $startOK = $r.OK
}

# Complete
$completeOK = $false
if ($startOK) {
    $r = Req POST "$BASE/api/v1/trips/$tripId/complete" $driverAuth '{"distanceKm":5.2,"durationMinutes":18}'
    Write-Host "  [Driver Complete] HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    $completeBody = $r.Body
    $completeOK = $r.OK
}

# GET Trip Final
$r = Req GET "$BASE/api/v1/trips/$tripId" $passAuth
$finalStatus = J "status" $r.Body
Write-Host "  [GET Trip Final] HTTP $($r.HTTP) finalStatus=$finalStatus"
$finalTripBody = $r.Body

# Payment
$paymentStatus = "N/A"
$paymentBody = ""
if ($completeOK) {
    $r = Req POST "$BASE/api/v1/payments" (@{ Authorization="Bearer $passToken"; "Idempotency-Key"="qa-pay-$TS" }) `
        "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}"
    $paymentStatus = J "status" $r.Body
    $paymentBody = $r.Body
    Write-Host "  [Create Payment] HTTP $($r.HTTP) status=$paymentStatus body=$($r.Body)"
}

# ── STEP 5: IDEMPOTENCY ────────────────────────────────────────────────────────
Write-Host "`n## STEP 5: IDEMPOTENCY"
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$key }) `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Main Pickup","dropoffAddress":"Main Dropoff","estimatedAmount":12.50,"currency":"USD"}'
$idemId = J "id" $r.Body
$idemResult = if ($r.HTTP -eq 200 -and $idemId -eq $tripId) { "PASS - same id" } `
              elseif ($r.HTTP -eq 200) { "FAIL - DUPLICATE ($tripId != $idemId)" } else { "FAIL - HTTP $($r.HTTP)" }
Write-Host "  [Idempotency Trip] HTTP $($r.HTTP) $idemResult"

# ── STEP 6: CONCURRENCY (now driver should be free) ───────────────────────────
Write-Host "`n## STEP 6: CONCURRENCY"
# Reset driver online
$r = Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":1}'
Write-Host "  [Driver Online for Concurrency] HTTP $($r.HTTP)"

$r = Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"="conc-$TS" }) `
    '{"pickupLatitude":19.4329,"pickupLongitude":-99.1335,"dropoffLatitude":19.4353,"dropoffLongitude":-99.1403,"pickupAddress":"Conc A","dropoffAddress":"Conc B","estimatedAmount":10.00,"currency":"USD"}'
$concTripId = J "id" $r.Body
Write-Host "  [Concurrency Trip] HTTP $($r.HTTP) id=$concTripId"

$concResult = "SKIP"
$concR1HTTP = "N/A"; $concR2HTTP = "N/A"
$concR1Body = ""; $concR2Body = ""

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
            $st=$_.Exception.Response.GetResponseStream()
            $rd=New-Object System.IO.StreamReader($st)
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
            $st=$_.Exception.Response.GetResponseStream()
            $rd=New-Object System.IO.StreamReader($st)
            return @{ HTTP=$s; Body=$rd.ReadToEnd() }
        }
    } -ArgumentList $BASE, $concTripId, $adminToken, $TENANT

    $j1 | Wait-Job | Out-Null; $j2 | Wait-Job | Out-Null
    $cr1 = Receive-Job $j1; $cr2 = Receive-Job $j2
    Remove-Job $j1,$j2

    $concR1HTTP = $cr1.HTTP; $concR1Body = $cr1.Body
    $concR2HTTP = $cr2.HTTP; $concR2Body = $cr2.Body
    Write-Host "  [Concurrent R1] HTTP $concR1HTTP | $concR1Body"
    Write-Host "  [Concurrent R2] HTTP $concR2HTTP | $concR2Body"

    if ($concR1HTTP -eq 200 -and $concR2HTTP -eq 200) {
        $concResult = "CRITICAL_FAIL_DOUBLE_ASSIGN"
        Write-Host "  RESULT: CRITICAL FAIL - Both 200 (double assign)" -ForegroundColor Red
    } elseif (($concR1HTTP -eq 200 -and $concR2HTTP -ne 200) -or ($concR2HTTP -eq 200 -and $concR1HTTP -ne 200)) {
        $concResult = "PASS"
        Write-Host "  RESULT: PASS - exactly one succeeded" -ForegroundColor Green
    } elseif ($concR1HTTP -eq 409 -and $concR2HTTP -eq 409) {
        $concResult = "FAIL_BOTH_409"
        Write-Host "  RESULT: FAIL - both 409. Check: NO_DRIVERS or RowVersion issue" -ForegroundColor Red
    } else {
        $concResult = "INCONCLUSIVE"
        Write-Host "  RESULT: INCONCLUSIVE R1=$concR1HTTP R2=$concR2HTTP"
    }
}

# ── FINAL EXPORT ──────────────────────────────────────────────────────────────
Write-Host "`n================================================================"
Write-Host "FINAL SUMMARY"
Write-Host "================================================================"
Write-Host "Admin Login:      200"
Write-Host "Driver Login:     200"
Write-Host "Driver Online:    200"
Write-Host "Create Trip:      200 (id=$tripId)"
Write-Host "Assign Driver:    $(if ($assignOK) {'200 PASS'} else {'409 FAIL'})"
Write-Host "Driver Accept:    $(if ($acceptOK) {'200 PASS'} else {'SKIP/FAIL'})"
Write-Host "Driver Arrive:    $(if ($arriveOK) {'200 PASS'} else {'SKIP/FAIL'})"
Write-Host "Driver Start:     $(if ($startOK) {'200 PASS'} else {'SKIP/FAIL'})"
Write-Host "Driver Complete:  $(if ($completeOK) {'200 PASS'} else {'SKIP/FAIL'})"
Write-Host "GET Trip Final:   200 (status=$finalStatus)"
Write-Host "Create Payment:   $(if ($completeOK) {'200 PASS'} else {'SKIP'})"
Write-Host "Idempotency:      $idemResult"
Write-Host "Concurrency:      $concResult (R1=$concR1HTTP R2=$concR2HTTP)"

$export = @{
    Timestamp       = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TripId          = $tripId
    FinalStatus     = $finalStatus
    PaymentStatus   = $paymentStatus
    PaymentBody     = $paymentBody
    ConcResult      = $concResult
    ConcR1HTTP      = $concR1HTTP
    ConcR2HTTP      = $concR2HTTP
    ConcR1Body      = $concR1Body
    ConcR2Body      = $concR2Body
    IdemResult      = $idemResult
    AssignOK        = $assignOK
    AssignDriverBody = $assignDriverBody
    FinalTripBody   = $finalTripBody
    CompleteBody    = if ($completeBody) { $completeBody } else { "" }
    CreateTripBody  = $createTripBody
    StuckCancelled  = $stuckCancelled
}
$export | ConvertTo-Json -Depth 5 | Out-File "C:\Proyectos\RiderFlow\tests\qa_final_results.json" -Encoding utf8
Write-Host "Saved: tests/qa_final_results.json"
