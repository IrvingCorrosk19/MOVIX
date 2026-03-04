<#
.SYNOPSIS
    Load test for POST /api/v1/trips/{id}/assign-driver — 200 concurrent requests.

.DESCRIPTION
    Simulates 200 concurrent assign-driver requests (one driver online, 200 trips).
    Detects: race conditions, multiple assignments (more than one 200), unexpected 500 errors.
    API must be running at $BASE before execution.

.EXAMPLE
    .\assign-driver-load.ps1
#>

$ErrorActionPreference = "Stop"
$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$ConcurrentRequests = 200
$TS     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  ASSIGN-DRIVER LOAD TEST — $ConcurrentRequests concurrent requests" -ForegroundColor Cyan
Write-Host "  Base: $BASE" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# 1) Login admin
Write-Host "[1] Login admin..." -ForegroundColor Yellow
$adminResp = Invoke-RestMethod -Uri "$BASE/api/v1/auth/login" -Method POST -ContentType "application/json" -Body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = $adminResp.accessToken
if (-not $adminToken) { Write-Host "FAIL: No admin token" -ForegroundColor Red; exit 1 }
Write-Host "    OK" -ForegroundColor Green

# 2) Login driver and set online
Write-Host "[2] Login driver and set online..." -ForegroundColor Yellow
$driverResp = Invoke-RestMethod -Uri "$BASE/api/v1/auth/login" -Method POST -ContentType "application/json" -Body '{"email":"driver@movix.io","password":"Driver@1234!"}'
$driverToken = $driverResp.accessToken
$authHeaders = @{ Authorization = "Bearer $driverToken"; "X-Tenant-Id" = $TENANT }
Invoke-RestMethod -Uri "$BASE/api/v1/drivers/status" -Method POST -Headers $authHeaders -ContentType "application/json" -Body '{"status":1}' | Out-Null
Write-Host "    OK" -ForegroundColor Green

# 3) Create trips
Write-Host "[3] Creating $ConcurrentRequests trips..." -ForegroundColor Yellow
$tripIds = [System.Collections.ArrayList]::new()
for ($i = 0; $i -lt $ConcurrentRequests; $i++) {
    $idemKey = "load-$TS-$i"
    $createHeaders = @{
        Authorization     = "Bearer $adminToken"
        "X-Tenant-Id"     = $TENANT
        "Idempotency-Key" = $idemKey
        "Content-Type"    = "application/json"
    }
    $body = @{
        pickupLatitude   = 19.43 + ($i * 0.00001)
        pickupLongitude  = -99.13 - ($i * 0.00001)
        dropoffLatitude  = 19.435
        dropoffLongitude = -99.14
        pickupAddress    = "Load $i"
        dropoffAddress   = "Load Drop $i"
        estimatedAmount  = 10.00 + ($i % 50)
        currency         = "USD"
    } | ConvertTo-Json
    try {
        $tr = Invoke-RestMethod -Uri "$BASE/api/v1/trips" -Method POST -Headers $createHeaders -Body $body -ContentType "application/json"
        if ($tr.id) { [void]$tripIds.Add($tr.id) }
    } catch {
        Write-Host "    WARN: Trip $i failed: $_" -ForegroundColor Yellow
    }
}
$tripCount = $tripIds.Count
Write-Host "    Created $tripCount trips" -ForegroundColor Green
if ($tripCount -eq 0) { Write-Host "FAIL: No trips" -ForegroundColor Red; exit 1 }

# 4) Launch 200 concurrent assign-driver requests
Write-Host "[4] Launching $tripCount concurrent assign-driver requests..." -ForegroundColor Yellow
$jobs = @()
for ($i = 0; $i -lt $tripCount; $i++) {
    $tid = $tripIds[$i]
    $jobs += Start-Job -ScriptBlock {
        param($BaseUrl, $TripId, $Token, $TenantId)
        $wc = New-Object System.Net.WebClient
        $wc.Headers["Authorization"] = "Bearer $Token"
        $wc.Headers["X-Tenant-Id"] = $TenantId
        $wc.Headers["Content-Type"] = "application/json"
        try {
            $body = $wc.UploadString("$BaseUrl/api/v1/trips/$TripId/assign-driver", "POST", "")
            return @{ HTTP = 200; Body = $body }
        } catch [System.Net.WebException] {
            $code = [int]$_.Exception.Response.StatusCode
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $body = $reader.ReadToEnd()
            $reader.Close(); $stream.Close()
            return @{ HTTP = $code; Body = $body }
        }
    } -ArgumentList $BASE, $tid, $adminToken, $TENANT
}
$jobs | Wait-Job -Timeout 120 | Out-Null

$results = $jobs | Receive-Job
Remove-Job $jobs -Force -ErrorAction SilentlyContinue

$total   = $results.Count
$http200 = ($results | Where-Object { $_.HTTP -eq 200 }).Count
$http409 = ($results | Where-Object { $_.HTTP -eq 409 }).Count
$http400 = ($results | Where-Object { $_.HTTP -eq 400 }).Count
$http500 = ($results | Where-Object { $_.HTTP -eq 500 }).Count
$other   = $total - $http200 - $http409 - $http400 - $http500

$DuplicateDriverAssignmentsDetected = ($http200 -gt 1)
$Unexpected500 = ($http500 -gt 0)
$SYSTEM_STABLE = ($http500 -eq 0 -and -not $DuplicateDriverAssignmentsDetected -and $http200 -le 1)

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  LOAD TEST SUMMARY" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "TotalRequests       = $total"
Write-Host "Success200         = $http200" -ForegroundColor $(if ($http200 -eq 1) { "Green" } elseif ($http200 -gt 1) { "Red" } else { "Yellow" })
Write-Host "Conflict409        = $http409"
Write-Host "InvalidState400    = $http400"
Write-Host "ServerErrors500    = $http500" -ForegroundColor $(if ($http500 -gt 0) { "Red" } else { "Green" })
if ($other -gt 0) { Write-Host "Other               = $other" -ForegroundColor Yellow }
Write-Host ""
Write-Host "DuplicateDriverAssignmentsDetected = $DuplicateDriverAssignmentsDetected" -ForegroundColor $(if ($DuplicateDriverAssignmentsDetected) { "Red" } else { "Green" })
Write-Host "SYSTEM_STABLE = $SYSTEM_STABLE" -ForegroundColor $(if ($SYSTEM_STABLE) { "Green" } else { "Red" })
Write-Host ""

if ($Unexpected500) { Write-Host "DETECTED: Unexpected 500 errors (race condition or server fault)." -ForegroundColor Red }
if ($DuplicateDriverAssignmentsDetected) { Write-Host "DETECTED: Multiple assignments — driver assigned to more than one trip." -ForegroundColor Red }
