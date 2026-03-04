<#
.SYNOPSIS
    Runs the Movix API and captures console logs, filtering "Concurrency conflict"
    lines into logs/concurrency-diagnostics.log.

.DESCRIPTION
    Starts the API via dotnet run, captures stdout/stderr, and appends any line
    containing "Concurrency conflict" to logs/concurrency-diagnostics.log.
    Run this in a separate terminal while you execute the stress test or
    concurrency repro script in another terminal.

.EXAMPLE
    .\log-capture.ps1
    # From repo root; or set $ApiProjectPath to src/Movix.Api
#>

$ErrorActionPreference = "Stop"
$ApiProjectPath = "src/Movix.Api"
$LogDir         = "logs"
$LogFile        = "concurrency-diagnostics.log"
$FilterPattern  = "Concurrency conflict"

# Resolve paths from script location (scripts/stress/)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$LogPath   = Join-Path $RepoRoot $LogDir
$LogPathFull = Join-Path $LogPath $LogFile
$ApiPath   = Join-Path $RepoRoot $ApiProjectPath

if (-not (Test-Path $ApiPath)) {
    Write-Host "API project not found at: $ApiPath" -ForegroundColor Red
    Write-Host "Run from repo root or set ApiProjectPath." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $LogPath)) {
    New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
    Write-Host "Created directory: $LogPath" -ForegroundColor Gray
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  LOG CAPTURE — Concurrency conflict lines" -ForegroundColor Cyan
Write-Host "  Output: $LogPathFull" -ForegroundColor Cyan
Write-Host "  Filter: $FilterPattern" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Starting API and capturing logs. Press Ctrl+C to stop." -ForegroundColor Yellow
Write-Host ""

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content -Path $LogPathFull -Value "--- Capture started at $timestamp ---"

& dotnet run --project $ApiPath 2>&1 | ForEach-Object {
    $line = $_
    Write-Host $line
    if ($line -match [System.Text.RegularExpressions.Regex]::Escape($FilterPattern)) {
        $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Add-Content -Path $LogPathFull -Value "[$ts] $line"
    }
}
