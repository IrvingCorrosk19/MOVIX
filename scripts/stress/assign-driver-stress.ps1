<#
.SYNOPSIS
    Stress test for POST /api/v1/trips/{id}/assign-driver with 50 concurrent requests.

.DESCRIPTION
    Ensures one driver is online, creates 50 trips, then launches 50 concurrent
    assign-driver requests (one per trip). Only one request should succeed (200);
    the rest should return 409 CONCURRENCY_CONFLICT. Detects duplicate driver
    assignment (more than one 200 = BUG).

.EXAMPLE
    .\assign-driver-stress.ps1
    # API must be running at $BASE (default http://127.0.0.1:55392)
#>

$ErrorActionPreference = "Stop"
$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$ConcurrentRequests = 50
$TS     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  ASSIGN-DRIVER STRESS TEST — $ConcurrentRequests concurrent requests" -ForegroundColor Cyan
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

# 3) Create multiple trips (one per concurrent request)
Write-Host "[3] Creating $ConcurrentRequests trips..." -ForegroundColor Yellow
$tripIds = [System.Collections.ArrayList]::new()
for ($i = 0; $i -lt $ConcurrentRequests; $i++) {
    $idemKey = "stress-$TS-$i"
    $createHeaders = @{
        Authorization   = "Bearer $adminToken"
        "X-Tenant-Id"   = $TENANT
        "Idempotency-Key" = $idemKey
        "Content-Type"  = "application/json"
    }
    $body = @{
        pickupLatitude   = 19.43 + ($i * 0.0001)
        pickupLongitude  = -99.13 - ($i * 0.0001)
        dropoffLatitude  = 19.435
        dropoffLongitude = -99.14
        pickupAddress    = "Stress $i"
        dropoffAddress   = "Stress Drop $i"
        estimatedAmount  = 10.00 + $i
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
if ($tripCount -eq 0) { Write-Host "FAIL: No trips created" -ForegroundColor Red; exit 1 }

# 4) Launch concurrent assign-driver requests (Start-Job)
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
$jobs | Wait-Job -Timeout 60 | Out-Null

# 5) Collect responses
$results = $jobs | Receive-Job
Remove-Job $jobs -Force -ErrorAction SilentlyContinue

$total = $results.Count
$http200 = ($results | Where-Object { $_.HTTP -eq 200 }).Count
$http409 = ($results | Where-Object { $_.HTTP -eq 409 }).Count
$http400 = ($results | Where-Object { $_.HTTP -eq 400 }).Count
$http500 = ($results | Where-Object { $_.HTTP -eq 500 }).Count
$other   = $total - $http200 - $http409 - $http400 - $http500

# Duplicate driver assignment: more than one 200 means same driver assigned to multiple trips (BUG)
$DuplicateDriverAssignmentsDetected = ($http200 -gt 1)

# SYSTEM_STABLE: true only when no 500, no duplicate assignments, and only one successful assign per driver
$SYSTEM_STABLE = ($http500 -eq 0 -and -not $DuplicateDriverAssignmentsDetected -and $http200 -le 1)

# 6) Print summary table
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  SUMMARY" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "TotalRequests       = $total"
Write-Host "Success200          = $http200" -ForegroundColor $(if ($http200 -eq 1) { "Green" } elseif ($http200 -gt 1) { "Red" } else { "Yellow" })
Write-Host "Conflict409         = $http409" -ForegroundColor Gray
Write-Host "InvalidState400     = $http400" -ForegroundColor Gray
Write-Host "ServerErrors500     = $http500" -ForegroundColor $(if ($http500 -gt 0) { "Red" } else { "Green" })
if ($other -gt 0) { Write-Host "Other (4xx/5xx)    = $other" -ForegroundColor Yellow }
Write-Host ""
Write-Host "DuplicateDriverAssignmentsDetected = $DuplicateDriverAssignmentsDetected" -ForegroundColor $(if ($DuplicateDriverAssignmentsDetected) { "Red" } else { "Green" })
Write-Host ""
Write-Host "SYSTEM_STABLE = $SYSTEM_STABLE" -ForegroundColor $(if ($SYSTEM_STABLE) { "Green" } else { "Red" })
Write-Host ""

if ($http500 -gt 0) {
    Write-Host "BUG: HTTP 500 occurred. Check API logs." -ForegroundColor Red
}
if ($DuplicateDriverAssignmentsDetected) {
    Write-Host "BUG: More than one request returned 200 — driver assigned to multiple trips." -ForegroundColor Red
}
if ($SYSTEM_STABLE) {
    Write-Host "PASS: No 500, no duplicate assignments, only one successful assign per driver." -ForegroundColor Green
}
