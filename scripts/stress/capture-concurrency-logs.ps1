<#
.SYNOPSIS
    Starts the Movix API and captures console output; writes lines containing
    "Concurrency conflict" to logs/concurrency-diagnostics.log.

.DESCRIPTION
    Runs the API via dotnet run, streams stdout/stderr, and appends any line
    matching "Concurrency conflict" (UnitOfWork forensic log) to
    logs/concurrency-diagnostics.log. Run in one terminal; run stress or
    concurrency repro in another terminal against this API.

.EXAMPLE
    .\capture-concurrency-logs.ps1
    # From repo root
#>

$ErrorActionPreference = "Stop"
$ApiProjectPath = "src/Movix.Api"
$LogDir         = "logs"
$LogFile        = "concurrency-diagnostics.log"
$FilterPattern  = "Concurrency conflict"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$LogPath   = Join-Path $RepoRoot $LogDir
$LogPathFull = Join-Path $LogPath $LogFile
$ApiPath   = Join-Path $RepoRoot $ApiProjectPath

if (-not (Test-Path $ApiPath)) {
    Write-Host "API project not found: $ApiPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $LogPath)) {
    New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  CONCURRENCY LOG CAPTURE" -ForegroundColor Cyan
Write-Host "  Output: $LogPathFull" -ForegroundColor Cyan
Write-Host "  Filter: $FilterPattern" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Starting API. Press Ctrl+C to stop." -ForegroundColor Yellow
Write-Host ""

$startTs = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content -Path $LogPathFull -Value "--- Capture started at $startTs ---"

& dotnet run --project $ApiPath 2>&1 | ForEach-Object {
    $line = $_
    Write-Host $line
    if ($line -match [System.Text.RegularExpressions.Regex]::Escape($FilterPattern)) {
        $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Add-Content -Path $LogPathFull -Value "[$ts] $line"
    }
}
