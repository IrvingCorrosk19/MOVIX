$BASE    = "http://127.0.0.1:55392"
$TENANT  = "00000000-0000-0000-0000-000000000001"

function Req {
    param($method, $url, $headers = @{}, $body = $null)
    $p = @{ Uri = $url; Method = $method; UseBasicParsing = $true; ContentType = "application/json"; TimeoutSec = 20 }
    if ($headers.Count -gt 0) { $p["Headers"] = $headers }
    if ($body) { $p["Body"] = $body }
    try {
        $r = Invoke-WebRequest @p
        return [pscustomobject]@{ HTTP = [int]$r.StatusCode; Body = $r.Content; OK = $true }
    } catch {
        $s = 0; try { $s = [int]$_.Exception.Response.StatusCode } catch {}
        $b = ""; try { $b = $_.ErrorDetails.Message } catch {}
        return [pscustomobject]@{ HTTP = $s; Body = $b; OK = $false }
    }
}

Write-Host "=== DIAGNOSTIC RUN ===" -ForegroundColor Cyan

# 1. Get tokens
$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$adminToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Admin Login] HTTP $($r.HTTP) | token=$($adminToken.Substring(0,20))..."

$r = Req POST "$BASE/api/v1/auth/login" -body '{"email":"driver@movix.io","password":"Driver@1234!"}'
$driverToken = ($r.Body | ConvertFrom-Json).accessToken
Write-Host "[Driver Login] HTTP $($r.HTTP)"

$adminAuth  = @{ Authorization = "Bearer $adminToken";  "X-Tenant-Id" = $TENANT }
$driverAuth = @{ Authorization = "Bearer $driverToken"; "X-Tenant-Id" = $TENANT }

# 2. Reset driver Online
$r = Req POST "$BASE/api/v1/drivers/status" $driverAuth '{"status":1}'
Write-Host "[Driver Online] HTTP $($r.HTTP) Body=$($r.Body)"

# 3. Inspect DriverAvailability via postgres
Write-Host "`n=== DB STATE: DriverAvailability ===" -ForegroundColor Yellow
$driverInfo = Req GET "$BASE/api/v1/drivers/me" $driverAuth
Write-Host "[Driver Me] HTTP $($driverInfo.HTTP) Body=$($driverInfo.Body)"

# 4. Create a fresh trip
$ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$key = "qa-diag-$ts"
$r = Req POST "$BASE/api/v1/trips" (@{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = $TENANT; "Idempotency-Key" = $key }) `
    '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Diag","dropoffAddress":"Diag2","estimatedAmount":12.50,"currency":"USD"}'
$tripId = ($r.Body | ConvertFrom-Json).id
Write-Host "[Create Trip] HTTP $($r.HTTP) TripId=$tripId"

# 5. Assign Driver - FULL response body
Write-Host "`n=== ASSIGN DRIVER ===" -ForegroundColor Yellow
$r = Req POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth
Write-Host "[Assign Driver] HTTP $($r.HTTP)"
Write-Host "[Assign Driver] OK: $($r.OK)"
Write-Host "[Assign Driver] BODY: $($r.Body)"

# 6. If assign failed, retry once (to confirm consistent behavior)
if (-not $r.OK) {
    Write-Host "`n=== RETRY ASSIGN DRIVER ===" -ForegroundColor Yellow
    $r2 = Req POST "$BASE/api/v1/trips/$tripId/assign-driver" $adminAuth
    Write-Host "[Assign Driver Retry] HTTP $($r2.HTTP) BODY: $($r2.Body)"
}

# 7. Cross-tenant test
Write-Host "`n=== CROSS-TENANT TEST ===" -ForegroundColor Yellow
if ($tripId) {
    $wrongAuth = @{ Authorization = "Bearer $adminToken"; "X-Tenant-Id" = "00000000-0000-0000-0000-000000000099" }
    $xr = Req GET "$BASE/api/v1/trips/$tripId" $wrongAuth
    Write-Host "[GET Trip Wrong Tenant] HTTP $($xr.HTTP)"
    Write-Host "[GET Trip Wrong Tenant] Body: $($xr.Body.Substring(0,[Math]::Min(300,$xr.Body.Length)))"
}

# 8. Check API logs for concurrency exception
Write-Host "`n=== DONE ===" -ForegroundColor Cyan
