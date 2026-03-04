$BASE   = "http://127.0.0.1:55392"
$TENANT = "00000000-0000-0000-0000-000000000001"
$TRIPID = "0773698e-62bb-46d2-86f9-2a6c6d199f04"

$adminLogin = Invoke-WebRequest -Uri "$BASE/api/v1/auth/login" -Method POST `
    -ContentType "application/json" -UseBasicParsing `
    -Body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$tok = ($adminLogin.Content | ConvertFrom-Json).accessToken

# Capture 409 body
$wc = New-Object System.Net.WebClient
$wc.Headers["Authorization"] = "Bearer $tok"
$wc.Headers["X-Tenant-Id"] = $TENANT
$wc.Headers["Content-Type"] = "application/json"
try {
    $resp = $wc.UploadString("$BASE/api/v1/trips/$TRIPID/assign-driver", "POST", "")
    Write-Host "SUCCESS: $resp"
} catch [System.Net.WebException] {
    $stream = $_.Exception.Response.GetResponseStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $body = $reader.ReadToEnd()
    $code = [int]$_.Exception.Response.StatusCode
    Write-Host "HTTP: $code"
    Write-Host "BODY: $body"
}

# Check the server logs for the exception
Write-Host ""
Write-Host "=== SERVER LOGS (last 50 lines via preview) ==="
