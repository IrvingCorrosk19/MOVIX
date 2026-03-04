$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$WRONG_TENANT = "00000000-0000-0000-0000-000000000099"
$TS = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

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

Write-Host "=== MULTI-TENANT ISOLATION TEST ===" -ForegroundColor Cyan

# Auth - admin
$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Admin Login] HTTP $($r.HTTP)"

# Register a REAL passenger so we have real isOwner scenario
$passEmail = "qa-pass-mt-$TS@movix.io"
$r = Req POST "$BASE/api/v1/auth/register" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`",`"tenantId`":`"$TENANT`"}"
Write-Host "[Register Passenger] HTTP $($r.HTTP)"
$r = Req POST "$BASE/api/v1/auth/login" -body "{`"email`":`"$passEmail`",`"password`":`"QaPass@1234`"}"
$passToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Passenger Login] HTTP $($r.HTTP)"

$adminAuth     = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT }
$passAuth      = @{ Authorization="Bearer $passToken";  "X-Tenant-Id"=$TENANT }
$adminWrongTen = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$WRONG_TENANT }
$passWrongTen  = @{ Authorization="Bearer $passToken";  "X-Tenant-Id"=$WRONG_TENANT }

# Create a trip as passenger
$key = "mt-trip-$TS"
$r = Req POST "$BASE/api/v1/trips" @{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$key } `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"MT Pickup","dropoffAddress":"MT Dropoff","estimatedAmount":12.50,"currency":"USD"}'
$tripId = ($r.Body | ConvertFrom-Json).id
Write-Host "[Create Trip as Passenger] HTTP $($r.HTTP) id=$tripId"

if (-not $tripId) {
    Write-Host "FAIL: no tripId" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "--- Test 1: GET trip with CORRECT tenant (should 200) ---"
$r = Req GET "$BASE/api/v1/trips/$tripId" $passAuth
Write-Host "  [Correct Tenant] HTTP $($r.HTTP)"
$t1Result = if ($r.HTTP -eq 200) { "PASS (200 correct)" } else { "FAIL (expected 200, got $($r.HTTP))" }
Write-Host "  Result: $t1Result"

Write-Host ""
Write-Host "--- Test 2: GET trip as PASSENGER with WRONG tenant (BUG-005: isOwner bypass) ---"
$r = Req GET "$BASE/api/v1/trips/$tripId" $passWrongTen
Write-Host "  [Passenger Wrong Tenant] HTTP $($r.HTTP) Body=$($r.Body.Substring(0,[Math]::Min(200,$r.Body.Length)))"
$t2Expected = "403 or 404"
$t2Result = if ($r.HTTP -eq 403 -or $r.HTTP -eq 404) { "PASS (enforced tenant isolation)" } `
            elseif ($r.HTTP -eq 200) { "FAIL BUG-005 (200 - owner bypasses tenant)" } `
            else { "FAIL (HTTP $($r.HTTP))" }
Write-Host "  Result: $t2Result"

Write-Host ""
Write-Host "--- Test 3: GET trip as ADMIN with WRONG tenant ---"
$r = Req GET "$BASE/api/v1/trips/$tripId" $adminWrongTen
Write-Host "  [Admin Wrong Tenant] HTTP $($r.HTTP) Body=$($r.Body.Substring(0,[Math]::Min(200,$r.Body.Length)))"
$t3Result = if ($r.HTTP -eq 403 -or $r.HTTP -eq 404) { "PASS (enforced tenant isolation)" } `
            elseif ($r.HTTP -eq 200) { "FAIL BUG-005 (200 - JWT tenant not header)" } `
            else { "FAIL (HTTP $($r.HTTP))" }
Write-Host "  Result: $t3Result"

Write-Host ""
Write-Host "--- Test 4: CREATE trip with WRONG tenant header ---"
$key2 = "mt-wrong-$TS"
$r = Req POST "$BASE/api/v1/trips" @{ Authorization="Bearer $passToken"; "X-Tenant-Id"=$WRONG_TENANT; "Idempotency-Key"=$key2 } `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Wrong MT","dropoffAddress":"Wrong MT","estimatedAmount":12.50,"currency":"USD"}'
Write-Host "  [Create Wrong Tenant] HTTP $($r.HTTP) Body=$($r.Body.Substring(0,[Math]::Min(200,$r.Body.Length)))"
$t4Result = if ($r.HTTP -eq 403 -or $r.HTTP -eq 400 -or $r.HTTP -eq 401) { "PASS (blocked)" } `
            elseif ($r.HTTP -eq 200) { "FAIL (trip created with mismatched tenant)" } `
            else { "FAIL (HTTP $($r.HTTP))" }
Write-Host "  Result: $t4Result"

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "T1 (Correct Tenant GET 200):           $t1Result"
Write-Host "T2 (Passenger wrong tenant - BUG-005): $t2Result"
Write-Host "T3 (Admin wrong tenant - BUG-005):     $t3Result"
Write-Host "T4 (Create with wrong tenant):         $t4Result"

$result = @{
    Timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TripId    = $tripId
    T1        = $t1Result
    T2        = $t2Result
    T3        = $t3Result
    T4        = $t4Result
}
$result | ConvertTo-Json | Out-File "C:\Proyectos\RiderFlow\tests\qa_multitenant_result.json" -Encoding utf8
Write-Host "Saved: tests/qa_multitenant_result.json"
