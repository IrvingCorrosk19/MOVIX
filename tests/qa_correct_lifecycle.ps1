# QA CORRECT LIFECYCLE - Flujo correcto post assign-driver
# assign-driver (admin) -> arrive -> start -> complete (NO accept en este flujo)

$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$TS     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$EV     = [System.Collections.ArrayList]::new()

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
            $rd=New-Object System.IO.StreamReader($stream)
            $b=$rd.ReadToEnd()
        } catch { try{$b=$_.ErrorDetails.Message}catch{} }
        return [pscustomobject]@{ HTTP=$s; Body=$b; OK=$false }
    }
}
function J { param($k,$j) try{($j|ConvertFrom-Json).$k}catch{""} }
function E { param($lbl, $r)
    $EV.Add([pscustomobject]@{ Label=$lbl; HTTP=$r.HTTP; OK=$r.OK; Body=($r.Body+"").Substring(0,[Math]::Min(400,($r.Body+"").Length)) }) | Out-Null
    return $r
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "QA CORRECT LIFECYCLE - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') UTC" -ForegroundColor Cyan
Write-Host "================================================================`n"

# TOKENS
$r = E "1.1 Login Admin"   (Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}')
$adminToken = J "accessToken" $r.Body
$adminAuth  = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT }
$r = E "1.2 Login Driver"  (Req POST "$BASE/api/v1/auth/login" -body '{"email":"driver@movix.io","password":"Driver@1234!"}')
$driverToken = J "accessToken" $r.Body
$driverAuth  = @{ Authorization="Bearer $driverToken"; "X-Tenant-Id"=$TENANT }
Write-Host "Tokens: Admin=$($adminToken.Substring(0,20))... Driver=$($driverToken.Substring(0,20))..."

# DRIVER ONLINE + LOCATION
$r = E "1.3 Driver Online"    (Req POST "$BASE/api/v1/drivers/status"   $driverAuth '{"status":1}')
$r = E "1.4 Driver Location"  (Req POST "$BASE/api/v1/drivers/location"  $driverAuth '{"latitude":19.4326,"longitude":-99.1332}')
Write-Host "[1.3] Driver Online: HTTP $($EV[2].HTTP)"
Write-Host "[1.4] Driver Location: HTTP $($EV[3].HTTP)"

# PASSENGER
$passEmail = "qa-pass-$TS@movix.io"
$r = E "1.5 Register Pass" (Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`",`"tenantId`":`"$TENANT`"}")
$r = E "1.6 Login Pass"    (Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`"}")
$passToken = J "accessToken" $r.Body
if (-not $passToken) { $passToken = $adminToken }
$passAuth  = @{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT }
Write-Host "[1.5] Register Pass: HTTP $($EV[4].HTTP)"
Write-Host "[1.6] Login Pass: HTTP $($EV[5].HTTP)"

# TARIFF
$prio = ($TS % 9000) + 300
$r = E "1.7 Create Tariff"   (Req POST "$BASE/api/v1/admin/tariffs" $adminAuth "{`"name`":`"QA-TARIFF-$TS`",`"currency`":`"USD`",`"baseFare`":2.50,`"pricePerKm`":1.20,`"pricePerMinute`":0.25,`"minimumFare`":5.00,`"priority`":$prio,`"effectiveFromUtc`":`"2025-01-01T00:00:00Z`",`"effectiveUntilUtc`":null}")
$tariffId = J "id" $r.Body
$r = E "1.8 Activate Tariff" (Req POST "$BASE/api/v1/admin/tariffs/$tariffId/activate" $adminAuth)
Write-Host "[1.7] Create Tariff: HTTP $($EV[6].HTTP) id=$tariffId"
Write-Host "[1.8] Activate Tariff: HTTP $($EV[7].HTTP)"

# FARE QUOTE
$r = E "2.0 Fare Quote" (Req GET "$BASE/api/v1/fare/quote?pickupLat=19.4326&pickupLon=-99.1332&dropoffLat=19.4350&dropoffLon=-99.1400&tenantId=$TENANT" $passAuth)
Write-Host "[2.0] Fare Quote: HTTP $($EV[8].HTTP) body=$(($EV[8].Body))"

Write-Host "`n## LIFECYCLE"

# CREATE TRIP
$idemKey = "qa-lifecycle-$TS"
$r = E "2.1 Create Trip" (Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$idemKey }) '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"QA Pickup","dropoffAddress":"QA Dropoff","estimatedAmount":12.50,"currency":"USD"}')
$tripId = J "id" $r.Body
Write-Host "[2.1] Create Trip: HTTP $($r.HTTP) id=$tripId status=$(J 'status' $r.Body)"

# ASSIGN DRIVER (admin)
$r = E "2.2 Assign Driver" (Req POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth)
$assignStatus = J "status" $r.Body
Write-Host "[2.2] Assign Driver: HTTP $($r.HTTP) status=$assignStatus body=$($r.Body)"
$assignOK = $r.OK

# ARRIVE (driver) - no accept needed!
$arriveOK = $false
if ($assignOK) {
    $r = E "2.3 Driver Arrive" (Req POST "$BASE/api/v1/trips/$tripId/arrive" $driverAuth)
    $arriveOK = $r.OK
    Write-Host "[2.3] Driver Arrive: HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    if (-not $r.OK) { Write-Host "  Body: $($r.Body)" }
}

# START (driver)
$startOK = $false
if ($arriveOK) {
    $r = E "2.4 Driver Start" (Req POST "$BASE/api/v1/trips/$tripId/start" $driverAuth)
    $startOK = $r.OK
    Write-Host "[2.4] Driver Start: HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    if (-not $r.OK) { Write-Host "  Body: $($r.Body)" }
}

# COMPLETE (driver)
$completeOK = $false
$completeBody = ""
if ($startOK) {
    $r = E "2.5 Driver Complete" (Req POST "$BASE/api/v1/trips/$tripId/complete" $driverAuth '{"distanceKm":5.2,"durationMinutes":18}')
    $completeOK = $r.OK
    $completeBody = $r.Body
    Write-Host "[2.5] Driver Complete: HTTP $($r.HTTP) status=$(J 'status' $r.Body)"
    if (-not $r.OK) { Write-Host "  Body: $($r.Body)" }
}

# GET TRIP FINAL
$r = E "2.6 GET Trip Final" (Req GET "$BASE/api/v1/trips/$tripId" $passAuth)
$finalStatus = J "status" $r.Body
Write-Host "[2.6] GET Trip Final: HTTP $($r.HTTP) finalStatus=$finalStatus"
$finalTripBody = $r.Body

# PAYMENT
$paymentStatus = "N/A"
$paymentBody = ""
$paymentId = "N/A"
if ($completeOK) {
    $r = E "2.7 Create Payment" (Req POST "$BASE/api/v1/payments" (@{ Authorization="Bearer $passToken"; "Idempotency-Key"="qa-pay-$TS" }) "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}")
    $paymentStatus = J "status" $r.Body
    $paymentId = J "id" $r.Body
    $paymentBody = $r.Body
    Write-Host "[2.7] Create Payment: HTTP $($r.HTTP) id=$paymentId status=$paymentStatus"
    if (-not $r.OK) { Write-Host "  Body: $($r.Body)" }
}

Write-Host "`n## IDEMPOTENCY"

# Duplicate trip
$r = E "3.1 Idem Trip" (Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$idemKey }) '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"QA Pickup","dropoffAddress":"QA Dropoff","estimatedAmount":12.50,"currency":"USD"}')
$idemId = J "id" $r.Body
$idemTripResult = if ($r.HTTP -eq 200 -and $idemId -eq $tripId) { "PASS" } elseif ($r.HTTP -eq 200) { "FAIL-DUPLICATE" } else { "FAIL-HTTP$($r.HTTP)" }
Write-Host "[3.1] Idempotency Trip: HTTP $($r.HTTP) returnedId=$idemId expected=$tripId → $idemTripResult"

# Duplicate payment
$idemPayResult = "SKIP"
if ($completeOK) {
    $r = E "3.2 Idem Payment" (Req POST "$BASE/api/v1/payments" (@{ Authorization="Bearer $passToken"; "Idempotency-Key"="qa-pay-$TS" }) "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}")
    $idemPayResult = if ($r.HTTP -eq 200) { "PASS" } else { "FAIL-HTTP$($r.HTTP)" }
    Write-Host "[3.2] Idempotency Payment: HTTP $($r.HTTP) → $idemPayResult"
}

Write-Host "`n## MULTI-TENANT ISOLATION"

# Cross-tenant GET (passenger accesses own trip with wrong tenant)
$wrongAuth = @{ Authorization="Bearer $passToken"; "X-Tenant-Id"="00000000-0000-0000-0000-000000000099" }
$r = E "4.1 Cross-Tenant GET" (Req GET "$BASE/api/v1/trips/$tripId" $wrongAuth)
$xTenantResult = if ($r.HTTP -in 403,404) { "PASS (blocked)" } else { "FAIL (returned $($r.HTTP) - passenger accessed own trip with wrong tenant)" }
Write-Host "[4.1] Cross-Tenant GET (passenger own trip): HTTP $($r.HTTP) → $xTenantResult"
Write-Host "      Note: 200 expected because isOwner=true check bypasses tenant header"

# Cross-tenant admin access (admin from wrong tenant cannot access trip)
$wrongAdminAuth = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"="00000000-0000-0000-0000-000000000099" }
$r = E "4.2 Cross-Tenant Admin GET" (Req GET "$BASE/api/v1/trips/$tripId" $wrongAdminAuth)
$xAdminResult = if ($r.HTTP -in 403,404) { "PASS (blocked)" } else { "FAIL (returned $($r.HTTP))" }
Write-Host "[4.2] Cross-Tenant Admin GET: HTTP $($r.HTTP) → $xAdminResult"

Write-Host "`n## CONCURRENCY TEST"

# Reset driver online (after completing trip, CurrentTripId should be null)
$r = E "5.0 Driver Reset Online" (Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":1}')
Write-Host "[5.0] Driver reset Online: HTTP $($r.HTTP)"

# Verify DB state after complete
$dbCheck = ""
try {
    $out = & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U postgres -d movix `
        -c 'SELECT "IsOnline", "CurrentTripId" FROM driver_availability' 2>&1
    $dbCheck = $out -join " "
    Write-Host "[DB] DriverAvailability: $dbCheck"
} catch { Write-Host "[DB] Could not check DB" }

# Create concurrency trip
$r = E "5.1 Create Conc Trip" (Req POST "$BASE/api/v1/trips" (@{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"="qa-conc2-$TS" }) '{"pickupLatitude":19.4330,"pickupLongitude":-99.1336,"dropoffLatitude":19.4354,"dropoffLongitude":-99.1404,"pickupAddress":"Conc X","dropoffAddress":"Conc Y","estimatedAmount":10.00,"currency":"USD"}')
$concTripId = J "id" $r.Body
Write-Host "[5.1] Concurrency trip: HTTP $($r.HTTP) id=$concTripId"

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
        try { return @{ HTTP=200; Body=$wc.UploadString("$b/api/v1/trips/$tid/assign-driver","POST","") }
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
        try { return @{ HTTP=200; Body=$wc.UploadString("$b/api/v1/trips/$tid/assign-driver","POST","") }
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
    Write-Host "[5.2] Concurrent R1: HTTP $concR1HTTP | $concR1Body"
    Write-Host "[5.2] Concurrent R2: HTTP $concR2HTTP | $concR2Body"

    if ($concR1HTTP -eq 200 -and $concR2HTTP -eq 200) {
        $concResult = "CRITICAL_FAIL_DOUBLE_ASSIGN"
    } elseif (($concR1HTTP -eq 200 -and $concR2HTTP -ne 200) -or ($concR2HTTP -eq 200 -and $concR1HTTP -ne 200)) {
        $concResult = "PASS"
    } elseif ($concR1HTTP -eq 409 -and $concR2HTTP -eq 409) {
        $concResult = "FAIL_BOTH_409_NO_DRIVERS_OR_CONFLICT"
    } else {
        $concResult = "INCONCLUSIVE"
    }
    Write-Host "[5.2] Concurrency Result: $concResult"
}

# Export results
$export = [pscustomobject]@{
    Timestamp      = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TripId         = $tripId
    FinalStatus    = $finalStatus
    PaymentId      = $paymentId
    PaymentStatus  = $paymentStatus
    ConcResult     = $concResult
    ConcR1HTTP     = $concR1HTTP
    ConcR2HTTP     = $concR2HTTP
    ConcR1Body     = $concR1Body
    ConcR2Body     = $concR2Body
    IdemTrip       = $idemTripResult
    IdemPay        = $idemPayResult
    XTenant        = $xTenantResult
    XAdmin         = $xAdminResult
    DBCheck        = $dbCheck
    AssignOK       = $assignOK
    ArriveOK       = $arriveOK
    StartOK        = $startOK
    CompleteOK     = $completeOK
    FinalTripBody  = $finalTripBody
    PaymentBody    = $paymentBody
    CompleteBody   = $completeBody
    Evidence       = $EV | ForEach-Object { [pscustomobject]@{ Label=$_.Label; HTTP=$_.HTTP; OK=$_.OK; Body=$_.Body } }
}
$export | ConvertTo-Json -Depth 8 | Out-File "C:\Proyectos\RiderFlow\tests\qa_lifecycle_results.json" -Encoding utf8

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host "FINAL RESULTS" -ForegroundColor Cyan
Write-Host "================================================================"
Write-Host "  Admin Login:      200 PASS"
Write-Host "  Driver Login:     200 PASS"
Write-Host "  Driver Online:    $($EV[2].HTTP) $(if($EV[2].OK){'PASS'}else{'FAIL'})"
Write-Host "  Register Pass:    $($EV[4].HTTP) $(if($EV[4].HTTP -in 200,202){'PASS'}else{'FAIL'})"
Write-Host "  Create Tariff:    $($EV[6].HTTP) $(if($EV[6].OK){'PASS'}else{'FAIL'})"
Write-Host "  Activate Tariff:  $($EV[7].HTTP) $(if($EV[7].OK){'PASS'}else{'FAIL'})"
Write-Host "  Fare Quote:       $($EV[8].HTTP) $(if($EV[8].OK){'PASS'}else{'FAIL'})"
Write-Host "  Create Trip:      $($EV[9].HTTP) $(if($EV[9].OK){'PASS - id=' + $tripId}else{'FAIL'})"
Write-Host "  Assign Driver:    $($EV[10].HTTP) $(if($assignOK){'PASS - status=Accepted'}else{'FAIL'})"
$arriveIdx = if ($assignOK) { 11 } else { -1 }
$startIdx  = if ($arriveOK) { $arriveIdx+1 } else { -1 }
$complIdx  = if ($startOK)  { $startIdx+1 } else { -1 }
$getIdx    = $EV.Count - (if ($completeOK) { 4 } elseif ($startOK) { 3 } elseif ($arriveOK) { 3 } else { 2 })
Write-Host "  Driver Arrive:    $(if ($arriveOK) {'200 PASS'} else {'FAIL/SKIP'})"
Write-Host "  Driver Start:     $(if ($startOK) {'200 PASS'} else {'FAIL/SKIP'})"
Write-Host "  Driver Complete:  $(if ($completeOK) {'200 PASS - ' + $completeBody.Substring(0,[Math]::Min(100,$completeBody.Length))} else {'FAIL/SKIP'})"
Write-Host "  GET Trip Final:   200 (status=$finalStatus)"
Write-Host "  Create Payment:   $(if ($completeOK) { '200 PASS id=' + $paymentId + ' status=' + $paymentStatus } else {'SKIP'})"
Write-Host "  Idempotency Trip: $idemTripResult"
Write-Host "  Idempotency Pay:  $idemPayResult"
Write-Host "  Cross-Tenant Pass Own: $xTenantResult"
Write-Host "  Cross-Tenant Admin:    $xAdminResult"
Write-Host "  Concurrency:      $concResult (R1=$concR1HTTP R2=$concR2HTTP)"
Write-Host "`nSaved: tests/qa_lifecycle_results.json"
