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

Write-Host "=== CONCURRENCY TEST ===" -ForegroundColor Cyan

# Auth
$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Admin Login] HTTP $($r.HTTP)"

$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"driver@movix.io","password":"Driver@1234!"}'
$driverToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Driver Login] HTTP $($r.HTTP)"

$adminAuth  = @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT }
$driverAuth = @{ Authorization="Bearer $driverToken"; "X-Tenant-Id"=$TENANT }

# Ensure driver online
$r = Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":1}'
Write-Host "[Driver Online] HTTP $($r.HTTP)"
$r = Req POST "$BASE/api/v1/drivers/location" $driverAuth '{"latitude":19.4326,"longitude":-99.1332}'
Write-Host "[Driver Location] HTTP $($r.HTTP)"

# Create fresh trip for concurrency
$key = "conc-$TS"
$r = Req POST "$BASE/api/v1/trips" @{ Authorization="Bearer $adminToken"; "X-Tenant-Id"=$TENANT; "Idempotency-Key"=$key } `
    '{"pickupLatitude":19.4329,"pickupLongitude":-99.1335,"dropoffLatitude":19.4353,"dropoffLongitude":-99.1403,"pickupAddress":"Conc A","dropoffAddress":"Conc B","estimatedAmount":10.00,"currency":"USD"}'
$concTripId = ($r.Body | ConvertFrom-Json).id
Write-Host "[Create Conc Trip] HTTP $($r.HTTP) id=$concTripId status=$(($r.Body|ConvertFrom-Json).status)"

if (-not $concTripId) {
    Write-Host "FAIL: Could not create trip. Body: $($r.Body)" -ForegroundColor Red
    exit 1
}

# Launch 2 concurrent assign-driver requests via background jobs
Write-Host ""
Write-Host "Launching 2 concurrent assign-driver requests..." -ForegroundColor Yellow

$j1 = Start-Job -ScriptBlock {
    param($b, $tid, $tok, $ten)
    $wc = New-Object System.Net.WebClient
    $wc.Headers["Authorization"] = "Bearer $tok"
    $wc.Headers["X-Tenant-Id"] = $ten
    $wc.Headers["Content-Type"] = "application/json"
    try {
        $body = $wc.UploadString("$b/api/v1/trips/$tid/assign-driver", "POST", "")
        return @{ HTTP=200; Body=$body }
    } catch [System.Net.WebException] {
        $s = [int]$_.Exception.Response.StatusCode
        $st = $_.Exception.Response.GetResponseStream()
        $rd = New-Object System.IO.StreamReader($st)
        return @{ HTTP=$s; Body=$rd.ReadToEnd() }
    }
} -ArgumentList $BASE, $concTripId, $adminToken, $TENANT

$j2 = Start-Job -ScriptBlock {
    param($b, $tid, $tok, $ten)
    $wc = New-Object System.Net.WebClient
    $wc.Headers["Authorization"] = "Bearer $tok"
    $wc.Headers["X-Tenant-Id"] = $ten
    $wc.Headers["Content-Type"] = "application/json"
    try {
        $body = $wc.UploadString("$b/api/v1/trips/$tid/assign-driver", "POST", "")
        return @{ HTTP=200; Body=$body }
    } catch [System.Net.WebException] {
        $s = [int]$_.Exception.Response.StatusCode
        $st = $_.Exception.Response.GetResponseStream()
        $rd = New-Object System.IO.StreamReader($st)
        return @{ HTTP=$s; Body=$rd.ReadToEnd() }
    }
} -ArgumentList $BASE, $concTripId, $adminToken, $TENANT

$j1 | Wait-Job -Timeout 30 | Out-Null
$j2 | Wait-Job -Timeout 30 | Out-Null
$cr1 = Receive-Job $j1
$cr2 = Receive-Job $j2
Remove-Job $j1, $j2

Write-Host "[Concurrent R1] HTTP $($cr1.HTTP)"
Write-Host "[Concurrent R1] BODY: $($cr1.Body)"
Write-Host "[Concurrent R2] HTTP $($cr2.HTTP)"
Write-Host "[Concurrent R2] BODY: $($cr2.Body)"
Write-Host ""

if ($cr1.HTTP -eq 200 -and $cr2.HTTP -eq 200) {
    $concResult = "CRITICAL_FAIL_DOUBLE_ASSIGN"
    Write-Host "RESULT: CRITICAL FAIL - Both 200 (double assign!)" -ForegroundColor Red
} elseif (($cr1.HTTP -eq 200 -and $cr2.HTTP -ne 200) -or ($cr2.HTTP -eq 200 -and $cr1.HTTP -ne 200)) {
    $winner = if ($cr1.HTTP -eq 200) { "R1=200 R2=$($cr2.HTTP)" } else { "R2=200 R1=$($cr1.HTTP)" }
    $concResult = "PASS"
    Write-Host "RESULT: PASS - Exactly one succeeded ($winner)" -ForegroundColor Green
} elseif ($cr1.HTTP -eq 409 -and $cr2.HTTP -eq 409) {
    $concResult = "FAIL_BOTH_409"
    Write-Host "RESULT: FAIL - Both 409 (no driver assigned)" -ForegroundColor Red
} else {
    $concResult = "INCONCLUSIVE"
    Write-Host "RESULT: INCONCLUSIVE R1=$($cr1.HTTP) R2=$($cr2.HTTP)"
}

# Check DB state after
Write-Host ""
Write-Host "=== DB STATE AFTER CONCURRENCY ===" -ForegroundColor Cyan
Write-Host "Trip ID: $concTripId"
Write-Host "R1: HTTP $($cr1.HTTP)"
Write-Host "R2: HTTP $($cr2.HTTP)"
Write-Host "Verdict: $concResult"

# Export results
$result = @{
    Timestamp    = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TripId       = $concTripId
    R1_HTTP      = $cr1.HTTP
    R1_Body      = $cr1.Body
    R2_HTTP      = $cr2.HTTP
    R2_Body      = $cr2.Body
    ConcResult   = $concResult
}
$result | ConvertTo-Json | Out-File "C:\Proyectos\RiderFlow\tests\qa_concurrency_result.json" -Encoding utf8
Write-Host "Saved: tests/qa_concurrency_result.json"
