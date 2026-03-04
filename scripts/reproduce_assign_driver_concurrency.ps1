<#
.SYNOPSIS
    Reproduces concurrent assign-driver calls to trigger DbUpdateConcurrencyException
    and capture the forensic log from UnitOfWork.

.DESCRIPTION
    Sends two simultaneous POST /api/v1/trips/{id}/assign-driver requests.
    Scenario A: Same trip - typically yields 200 + 400 (TRIP_INVALID_STATE).
    Scenario B: Two different trips - may yield 200 + 409 (CONCURRENCY_CONFLICT)
    when both requests select the same driver and one SaveChanges wins.

.EXAMPLE
    .\reproduce_assign_driver_concurrency.ps1
    # Ensure API is running at $BASE (default http://127.0.0.1:55392)
#>

$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$TS     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

function Invoke-AssignDriver {
    param([string]$BaseUrl, [string]$TripId, [string]$Token, [string]$TenantId)
    $wc = New-Object System.Net.WebClient
    $wc.Headers["Authorization"] = "Bearer $Token"
    $wc.Headers["X-Tenant-Id"] = $TenantId
    $wc.Headers["Content-Type"] = "application/json"
    try {
        $body = $wc.UploadString("$BaseUrl/api/v1/trips/$TripId/assign-driver", "POST", "")
        return @{ HTTP = 200; Body = $body }
    } catch [System.Net.WebException] {
        $statusCode = [int]$_.Exception.Response.StatusCode
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $body = $reader.ReadToEnd()
        $reader.Close()
        $stream.Close()
        return @{ HTTP = $statusCode; Body = $body }
    }
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  ASSIGN-DRIVER CONCURRENCY DIAGNOSTIC" -ForegroundColor Cyan
Write-Host "  Base: $BASE" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# 1) Authenticate admin
Write-Host "[1] Authenticating admin..." -ForegroundColor Yellow
try {
    $loginBody = '{"email":"admin@movix.io","password":"Admin@1234!"}'
    $loginResp = Invoke-RestMethod -Uri "$BASE/api/v1/auth/login" -Method POST -ContentType "application/json" -Body $loginBody
    $adminToken = $loginResp.accessToken
    if (-not $adminToken) { throw "No accessToken in response" }
    Write-Host "    OK" -ForegroundColor Green
} catch {
    Write-Host "    FAIL: $_" -ForegroundColor Red
    exit 1
}

# 2) Ensure at least one driver is online (driver login + status)
Write-Host "[2] Ensuring driver is online..." -ForegroundColor Yellow
try {
    $driverLogin = Invoke-RestMethod -Uri "$BASE/api/v1/auth/login" -Method POST -ContentType "application/json" -Body '{"email":"driver@movix.io","password":"Driver@1234!"}'
    $driverToken = $driverLogin.accessToken
    $headers = @{ Authorization = "Bearer $driverToken"; "X-Tenant-Id" = $TENANT }
    Invoke-RestMethod -Uri "$BASE/api/v1/drivers/status" -Method POST -Headers $headers -ContentType "application/json" -Body '{"status":1}' | Out-Null
    Write-Host "    OK" -ForegroundColor Green
} catch {
    Write-Host "    WARN: $_" -ForegroundColor Yellow
}

# 3) Create a trip (Requested)
Write-Host "[3] Creating trip..." -ForegroundColor Yellow
$idemKey = "concurrency-diag-$TS"
$tripBody = @{
    pickupLatitude   = 19.4329
    pickupLongitude  = -99.1335
    dropoffLatitude  = 19.4353
    dropoffLongitude = -99.1403
    pickupAddress    = "Diag Pickup"
    dropoffAddress   = "Diag Dropoff"
    estimatedAmount  = 10.00
    currency         = "USD"
} | ConvertTo-Json
$createHeaders = @{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = $TENANT; "Idempotency-Key" = $idemKey }
try {
    $tripResp = Invoke-RestMethod -Uri "$BASE/api/v1/trips" -Method POST -Headers $createHeaders -ContentType "application/json" -Body $tripBody
    $TripId = $tripResp.id
    Write-Host "    TripId = $TripId" -ForegroundColor Green
} catch {
    Write-Host "    FAIL: $_" -ForegroundColor Red
    exit 1
}

# 4) Send TWO simultaneous assign-driver requests (same trip)
Write-Host ""
Write-Host "[4] Sending TWO concurrent POST assign-driver (same trip)..." -ForegroundColor Yellow
$job1 = Start-Job -ScriptBlock {
    param($Base, $Tid, $Tok, $Ten)
    & {
        $wc = New-Object System.Net.WebClient
        $wc.Headers["Authorization"] = "Bearer $Tok"
        $wc.Headers["X-Tenant-Id"] = $Ten
        $wc.Headers["Content-Type"] = "application/json"
        try {
            $body = $wc.UploadString("$Base/api/v1/trips/$Tid/assign-driver", "POST", "")
            return @{ HTTP = 200; Body = $body }
        } catch [System.Net.WebException] {
            $s = [int]$_.Exception.Response.StatusCode
            $st = $_.Exception.Response.GetResponseStream()
            $rd = New-Object System.IO.StreamReader($st)
            $b = $rd.ReadToEnd(); $rd.Close(); $st.Close()
            return @{ HTTP = $s; Body = $b }
        }
    }
} -ArgumentList $BASE, $TripId, $adminToken, $TENANT

$job2 = Start-Job -ScriptBlock {
    param($Base, $Tid, $Tok, $Ten)
    & {
        $wc = New-Object System.Net.WebClient
        $wc.Headers["Authorization"] = "Bearer $Tok"
        $wc.Headers["X-Tenant-Id"] = $Ten
        $wc.Headers["Content-Type"] = "application/json"
        try {
            $body = $wc.UploadString("$Base/api/v1/trips/$Tid/assign-driver", "POST", "")
            return @{ HTTP = 200; Body = $body }
        } catch [System.Net.WebException] {
            $s = [int]$_.Exception.Response.StatusCode
            $st = $_.Exception.Response.GetResponseStream()
            $rd = New-Object System.IO.StreamReader($st)
            $b = $rd.ReadToEnd(); $rd.Close(); $st.Close()
            return @{ HTTP = $s; Body = $b }
        }
    }
} -ArgumentList $BASE, $TripId, $adminToken, $TENANT

$job1 | Wait-Job -Timeout 30 | Out-Null
$job2 | Wait-Job -Timeout 30 | Out-Null
$r1 = Receive-Job $job1
$r2 = Receive-Job $job2
Remove-Job $job1, $job2 -Force -ErrorAction SilentlyContinue

# 5) Print responses
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  RESULTS" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Request 1 -> HTTP $($r1.HTTP)" -ForegroundColor $(if ($r1.HTTP -eq 200) { "Green" } else { "Yellow" })
Write-Host "Request 2 -> HTTP $($r2.HTTP)" -ForegroundColor $(if ($r2.HTTP -eq 200) { "Green" } else { "Yellow" })
Write-Host ""
Write-Host "Response 1 body:" -ForegroundColor Gray
try { Write-Host ($r1.Body | ConvertFrom-Json | ConvertTo-Json -Depth 5 -Compress) } catch { Write-Host $r1.Body }
Write-Host ""
Write-Host "Response 2 body:" -ForegroundColor Gray
try { Write-Host ($r2.Body | ConvertFrom-Json | ConvertTo-Json -Depth 5 -Compress) } catch { Write-Host $r2.Body }

Write-Host ""
Write-Host "--- Scenario B: Two trips, two concurrent assign-driver (may yield 409 CONCURRENCY_CONFLICT) ---" -ForegroundColor Cyan
# Create second trip
$idemKey2 = "concurrency-diag-b-$TS"
$tripBody2 = @{ pickupLatitude=19.4330; pickupLongitude=-99.1336; dropoffLatitude=19.4354; dropoffLongitude=-99.1404; pickupAddress="Diag B Pickup"; dropoffAddress="Diag B Dropoff"; estimatedAmount=11.00; currency="USD" } | ConvertTo-Json
$createHeaders2 = @{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = $TENANT; "Idempotency-Key" = $idemKey2 }
try {
    $tripResp2 = Invoke-RestMethod -Uri "$BASE/api/v1/trips" -Method POST -Headers $createHeaders2 -ContentType "application/json" -Body $tripBody2
    $TripId2 = $tripResp2.id
} catch { Write-Host "Could not create second trip: $_" -ForegroundColor Yellow; exit 0 }
$jb1 = Start-Job -ScriptBlock { param($Base,$Tid,$Tok,$Ten) $wc=New-Object System.Net.WebClient; $wc.Headers["Authorization"]="Bearer $Tok"; $wc.Headers["X-Tenant-Id"]=$Ten; $wc.Headers["Content-Type"]="application/json"; try { $b=$wc.UploadString("$Base/api/v1/trips/$Tid/assign-driver","POST",""); return @{HTTP=200;Body=$b} } catch [System.Net.WebException] { $s=[int]$_.Exception.Response.StatusCode; $st=$_.Exception.Response.GetResponseStream(); $rd=New-Object System.IO.StreamReader($st); $b=$rd.ReadToEnd(); $rd.Close(); $st.Close(); return @{HTTP=$s;Body=$b} } } -ArgumentList $BASE,$TripId,$adminToken,$TENANT
$jb2 = Start-Job -ScriptBlock { param($Base,$Tid,$Tok,$Ten) $wc=New-Object System.Net.WebClient; $wc.Headers["Authorization"]="Bearer $Tok"; $wc.Headers["X-Tenant-Id"]=$Ten; $wc.Headers["Content-Type"]="application/json"; try { $b=$wc.UploadString("$Base/api/v1/trips/$Tid/assign-driver","POST",""); return @{HTTP=200;Body=$b} } catch [System.Net.WebException] { $s=[int]$_.Exception.Response.StatusCode; $st=$_.Exception.Response.GetResponseStream(); $rd=New-Object System.IO.StreamReader($st); $b=$rd.ReadToEnd(); $rd.Close(); $st.Close(); return @{HTTP=$s;Body=$b} } } -ArgumentList $BASE,$TripId2,$adminToken,$TENANT
$jb1 | Wait-Job -Timeout 30 | Out-Null; $jb2 | Wait-Job -Timeout 30 | Out-Null
$res1 = Receive-Job $jb1; $res2 = Receive-Job $jb2; Remove-Job $jb1,$jb2 -Force -ErrorAction SilentlyContinue
Write-Host "Scenario B - Request1 (trip $TripId)  -> $($res1.HTTP)" -ForegroundColor $(if($res1.HTTP -eq 200){"Green"}else{"Yellow"})
Write-Host "Scenario B - Request2 (trip $TripId2) -> $($res2.HTTP)" -ForegroundColor $(if($res2.HTTP -eq 200){"Green"}else{"Yellow"})
Write-Host "Response 2 body (Scenario B):" -ForegroundColor Gray
try { Write-Host ($res2.Body | ConvertFrom-Json | ConvertTo-Json -Compress) } catch { Write-Host $res2.Body }
Write-Host ""
Write-Host "Done. Check API console for forensic log (Concurrency conflict. Entity=... PrimaryKey=... ConcurrencyToken=...)." -ForegroundColor Cyan
