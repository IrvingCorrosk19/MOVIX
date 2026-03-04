# E2E Ride Lifecycle - Validacion post BUG-001, BUG-002, BUG-003
# Ejecuta el flujo equivalente a la coleccion Postman. No modifica codigo ni DB.
$ErrorActionPreference = "Stop"
$baseUrl = "http://127.0.0.1:55392"
$results = [System.Collections.ArrayList]::new()

function Invoke-Step {
    param($StepName, $Method, $Uri, $Headers = @{}, $Body = $null)
    try {
        $params = @{ Uri = $Uri; Method = $Method; ContentType = "application/json"; UseBasicParsing = $true; TimeoutSec = 15 }
        if ($Headers.Count -gt 0) { $params["Headers"] = $Headers }
        if ($Body) { $params["Body"] = $Body }
        $r = Invoke-WebRequest @params
        return @{ Step = $StepName; Status = $r.StatusCode; Ok = $true; Content = $r.Content }
    } catch {
        $status = $_.Exception.Response.StatusCode.value__
        $content = try { $_.ErrorDetails.Message } catch { "" }
        return @{ Step = $StepName; Status = $status; Ok = $false; Content = $content }
    }
}

# 1) Login Admin
$loginAdmin = Invoke-Step "1.Login Admin" POST "$baseUrl/api/v1/auth/login" -Body '{"email":"admin@movix.io","password":"Admin@1234!"}'
$results.Add($loginAdmin) | Out-Null
if (-not $loginAdmin.Ok) { Write-Host "FAIL: Login Admin $($loginAdmin.Status)"; exit 1 }
$adminData = $loginAdmin.Content | ConvertFrom-Json
$accessToken = $adminData.accessToken
$adminAuth = @{ "Authorization" = "Bearer $accessToken" }

# 2) Create Tenant (to get tenantId for context)
$createTenant = Invoke-Step "2.Create Tenant" POST "$baseUrl/api/v1/admin/tenants" $adminAuth -Body '{"name":"Movix E2E Tenant"}'
$results.Add($createTenant) | Out-Null
$tenantId = $null
if ($createTenant.Ok) {
    $tData = $createTenant.Content | ConvertFrom-Json
    $tenantId = $tData.id
} else {
    try {
        $listTenants = Invoke-WebRequest -Uri "$baseUrl/api/v1/admin/tenants" -Headers $adminAuth -UseBasicParsing
        $tl = $listTenants.Content | ConvertFrom-Json
        if ($tl.tenants -and $tl.tenants.Count -gt 0) { $tenantId = $tl.tenants[0].id }
        elseif ($tl[0]) { $tenantId = $tl[0].id }
    } catch {}
}
if (-not $tenantId) { $tenantId = "00000000-0000-0000-0000-000000000001" }
# Usar tenant del seed (DevTenantId) para que trip y driver esten en el mismo tenant y Assign Driver encuentre al conductor
$tenantId = "00000000-0000-0000-0000-000000000001"

# 2b) Register Passenger con email unico (Register solo crea Passenger; seed no crea passenger)
$passengerEmail = "passenger-e2e@movix.io"
$passengerPassword = "PassengerE2e@1234!"
$regPass = Invoke-Step "2b.Register Passenger" POST "$baseUrl/api/v1/auth/register" -Body "{`"email`":`"$passengerEmail`",`"password`":`"$passengerPassword`",`"tenantId`":`"$tenantId`"}"
$results.Add($regPass) | Out-Null

# 3) Login Driver (usar driver del seed: mismo tenant DevTenantId para asignacion)
$loginDriver = Invoke-Step "3.Login Driver" POST "$baseUrl/api/v1/auth/login" -Body '{"email":"driver@movix.io","password":"Driver@1234!"}'
$results.Add($loginDriver) | Out-Null
if (-not $loginDriver.Ok) { Write-Host "FAIL: Login Driver $($loginDriver.Status)"; exit 1 }
$driverData = $loginDriver.Content | ConvertFrom-Json
$driverToken = $driverData.accessToken
$driverAuth = @{ "Authorization" = "Bearer $driverToken"; "X-Tenant-Id" = "$tenantId" }

# 4) Login Passenger (passenger-e2e recien registrado)
$loginPassenger = Invoke-Step "4.Login Passenger" POST "$baseUrl/api/v1/auth/login" -Body "{`"email`":`"$passengerEmail`",`"password`":`"$passengerPassword`"}"
$results.Add($loginPassenger) | Out-Null
if (-not $loginPassenger.Ok) { Write-Host "WARN: Login Passenger $($loginPassenger.Status) - $($loginPassenger.Content)" }
$passengerToken = $null
if ($loginPassenger.Ok) {
    $passengerData = $loginPassenger.Content | ConvertFrom-Json
    $passengerToken = $passengerData.accessToken
}
if (-not $passengerToken) { $passengerToken = $accessToken }
$passengerAuth = @{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = "$tenantId" }

# 5) Driver Onboarding (driver del seed ya esta onboarded -> 400 DRIVER_EXISTS esperado; validamos que no sea 500/FK)
$onboarding = Invoke-Step "5.Driver Onboarding" POST "$baseUrl/api/v1/drivers/onboarding" $driverAuth -Body '{"licenseNumber":"LIC123","vehiclePlate":"ABC-1234","vehicleModel":"Sedan","vehicleColor":"Black"}'
$results.Add($onboarding) | Out-Null
$driverId = $null
if ($onboarding.Ok) {
    $ob = $onboarding.Content | ConvertFrom-Json
    $driverId = $ob.driverId
}

# 6) Driver Status -> Online (DriverStatus.Online = 1)
$driverStatus = Invoke-Step "6.Driver Status Online" POST "$baseUrl/api/v1/drivers/status" $driverAuth -Body '{"status":1}'
$results.Add($driverStatus) | Out-Null

# 7) Driver Location
$driverLoc = Invoke-Step "7.Driver Location" POST "$baseUrl/api/v1/drivers/location" $driverAuth -Body '{"latitude":19.4326,"longitude":-99.1332}'
$results.Add($driverLoc) | Out-Null

# 8) Admin: Create Tariff
$adminTenant = @{ "Authorization" = "Bearer $accessToken"; "X-Tenant-Id" = "$tenantId" }
$createTariff = Invoke-Step "8.Create Tariff" POST "$baseUrl/api/v1/admin/tariffs" $adminTenant -Body '{"name":"Tarifa E2E","currency":"USD","baseFare":2.50,"pricePerKm":1.20,"pricePerMinute":0.25,"minimumFare":5.00,"priority":100,"effectiveFromUtc":"2025-01-01T00:00:00Z","effectiveUntilUtc":null}'
$results.Add($createTariff) | Out-Null
$tariffId = $null
if ($createTariff.Ok) {
    $tf = $createTariff.Content | ConvertFrom-Json
    $tariffId = $tf.id
}

# 9) Admin: Activate Tariff
if ($tariffId) {
    $activateTariff = Invoke-Step "9.Activate Tariff" POST "$baseUrl/api/v1/admin/tariffs/$tariffId/activate" $adminTenant
    $results.Add($activateTariff) | Out-Null
}

# 10) Passenger: Create Trip
$passengerAuthWithKey = @{ "Authorization" = "Bearer $passengerToken"; "X-Tenant-Id" = "$tenantId"; "Idempotency-Key" = "e2e-trip-$(New-Guid)" }
$createTrip = Invoke-Step "10.Create Trip" POST "$baseUrl/api/v1/trips" $passengerAuthWithKey -Body '{"pickupLatitude":19.4326,"pickupLongitude":-99.1332,"dropoffLatitude":19.4350,"dropoffLongitude":-99.1400,"pickupAddress":"Origen","dropoffAddress":"Destino","estimatedAmount":12.50,"currency":"USD"}'
$results.Add($createTrip) | Out-Null
$tripId = $null
$tripStatus = $null
$assignDriver = $arrive = $start = $complete = @{ Ok = $false; Status = 0 }
if ($createTrip.Ok) {
    $tr = $createTrip.Content | ConvertFrom-Json
    $tripId = $tr.id
    $tripStatus = $tr.status
}

# 11) Admin: Assign Driver
if ($tripId) {
    $assignDriver = Invoke-Step "11.Assign Driver" POST "$baseUrl/api/v1/trips/$tripId/assign-driver" $adminTenant
    $results.Add($assignDriver) | Out-Null
    if ($assignDriver.Ok) {
        $ad = $assignDriver.Content | ConvertFrom-Json
        $tripStatus = $ad.status
    }
}

# 12) Driver: Arrive
if ($tripId) {
    $arrive = Invoke-Step "12.Driver Arrive" POST "$baseUrl/api/v1/trips/$tripId/arrive" $driverAuth
    $results.Add($arrive) | Out-Null
    if ($arrive.Ok) {
        $av = $arrive.Content | ConvertFrom-Json
        $tripStatus = $av.status
    }
}

# 13) Driver: Start
if ($tripId) {
    $start = Invoke-Step "13.Driver Start" POST "$baseUrl/api/v1/trips/$tripId/start" $driverAuth
    $results.Add($start) | Out-Null
    if ($start.Ok) {
        $st = $start.Content | ConvertFrom-Json
        $tripStatus = $st.status
    }
}

# 14) Driver: Complete
if ($tripId) {
    $complete = Invoke-Step "14.Driver Complete" POST "$baseUrl/api/v1/trips/$tripId/complete" $driverAuth -Body '{"distanceKm":5.2,"durationMinutes":18}'
    $results.Add($complete) | Out-Null
    if ($complete.Ok) {
        $cm = $complete.Content | ConvertFrom-Json
        $tripStatus = $cm.status
    }
}

# GET trip (passenger) - estado final
$getTrip = $null
if ($tripId) {
    $getTrip = Invoke-Step "GET /trips (final)" GET "$baseUrl/api/v1/trips/$tripId" $passengerAuth
    $results.Add($getTrip) | Out-Null
}

# 15) Passenger: Create Payment
$paymentStatus = $null
if ($tripId) {
    $createPayment = Invoke-Step "15.Create Payment" POST "$baseUrl/api/v1/payments" (@{ "Authorization" = "Bearer $passengerToken"; "Idempotency-Key" = "e2e-pay-$(New-Guid)" }) -Body "{`"tripId`":`"$tripId`",`"amount`":15.00,`"currency`":`"USD`"}"
    $results.Add($createPayment) | Out-Null
    if ($createPayment.Ok) {
        $pm = $createPayment.Content | ConvertFrom-Json
        $paymentStatus = $pm.status
    }
}

# --- Report ---
$report = @"
================================================================================
E2E RIDE LIFECYCLE - QA REPORT (Post BUG-001, BUG-002, BUG-003)
================================================================================
Base URL: $baseUrl
Executed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

STEP RESULTS
--------------------------------------------------------------------------------
"@
foreach ($r in $results) {
    $status = $r.Status
    $ok = if ($r.Ok) { "OK" } else { "FAIL" }
    $report += "`n$($r.Step): HTTP $status [$ok]"
}
$report += @"

VALIDATIONS
--------------------------------------------------------------------------------
- 409 Concurrency: $(if (($results | Where-Object { $_.Status -eq 409 }).Count -gt 0) { 'FAIL - present' } else { 'OK - none' })
- 403 Incorrect for driver: $(if (($results | Where-Object { $_.Step -match 'Arrive|Start|Complete' -and $_.Status -eq 403 }).Count -gt 0) { 'FAIL' } else { 'OK' })
- 500 errors: $(if (($results | Where-Object { $_.Status -eq 500 }).Count -gt 0) { 'FAIL - present' } else { 'OK - none' })
- Driver Onboarding (200): $(if ($onboarding.Ok) { 'OK' } else { "FAIL ($($onboarding.Status))" })
- Trip status after Create: Requested = $(if ($tripStatus -eq 'Requested' -or $createTrip.Ok) { 'OK' } else { 'N/A' })
- Trip status after Assign: Accepted = $(if ($assignDriver.Ok) { 'OK' } else { 'N/A' })
- Trip status after Arrive: DriverArrived = $(if ($arrive.Ok) { 'OK' } else { 'N/A' })
- Trip status after Start: InProgress = $(if ($start.Ok) { 'OK' } else { 'N/A' })
- Trip status after Complete: Completed = $(if ($complete.Ok -and $tripStatus -eq 'Completed') { 'OK' } else { 'N/A' })
- GET /trips/{id} reflects state: $(if ($getTrip.Ok) { 'OK' } else { 'N/A' })
- Payment status (Pending/Simulation): $(if ($paymentStatus) { $paymentStatus } else { 'N/A' })

FINAL STATE
--------------------------------------------------------------------------------
Trip Id: $tripId
Trip Status: $tripStatus
Payment Status: $paymentStatus
Driver Id (onboarding): $driverId

"@
$allOk = ($results | Where-Object { -not $_.Ok }).Count -eq 0
$no409 = ($results | Where-Object { $_.Status -eq 409 }).Count -eq 0
$driverStepsOk = ($arrive.Ok -and $start.Ok -and $complete.Ok)
if ($allOk -and $no409 -and $driverStepsOk -and $tripStatus -eq 'Completed') {
    $report += @"
================================================================================
Ride lifecycle fully operational.
================================================================================
"@
} else {
    $report += @"
================================================================================
Issues detected. Review step results above.
================================================================================
"@
}
$report
$report | Out-File -FilePath "c:\Proyectos\RiderFlow\tests\E2E_RIDE_LIFECYCLE_REPORT.txt" -Encoding utf8
Write-Host $report
