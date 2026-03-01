# MOVIX Backend Ready - Gate validation script
# Run from repo root: .\scripts\validate-gates.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

Write-Host "Gate 1: dotnet build Release" -ForegroundColor Cyan
dotnet build Movix.sln -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`nGate 2: dotnet test" -ForegroundColor Cyan
dotnet test Movix.sln -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`nGate 3-5: docker-compose (API + Postgres + Redis, migrations, /health /ready)" -ForegroundColor Cyan
docker-compose up -d --build
Start-Sleep -Seconds 15
try {
    $health = Invoke-RestMethod -Uri "http://localhost:8080/health" -Method Get
    $ready = Invoke-RestMethod -Uri "http://localhost:8080/ready" -Method Get
    Write-Host "  /health: OK" -ForegroundColor Green
    Write-Host "  /ready: OK" -ForegroundColor Green
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    exit 1
} finally {
    docker-compose down
}

Write-Host "`nAll gates OK." -ForegroundColor Green
