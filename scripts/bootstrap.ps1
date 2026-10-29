#Requires -Version 5.1
<#
.SYNOPSIS
  Geliştirme araçlarını kurar: .NET 8 SDK, Git ve Inno Setup (eksik olanlar winget ile).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipInnoSetup
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Test-Command([string]$Name) { return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue) }

function Install-WingetPackage([string]$Id, [string]$DisplayName) {
    if (-not (Test-Command 'winget')) {
        throw "winget bulunamadı. Microsoft Store'dan 'App Installer' kurulu olmalı: https://aka.ms/getwinget"
    }
    Write-Step "$DisplayName kuruluyor ($Id)"
    winget install --id $Id --exact --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) {
        throw "$DisplayName kurulamadı (winget çıkış kodu $LASTEXITCODE)"
    }
}

Write-Step "Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"

$policy = Get-ExecutionPolicy -Scope CurrentUser
if ($policy -eq 'Restricted' -or $policy -eq 'Undefined') {
    Write-Step 'PowerShell betiklerinin çalışmasına izin veriliyor (CurrentUser: RemoteSigned)'
    Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force
}

if (-not (Test-Command 'git')) { Install-WingetPackage 'Git.Git' 'Git' }
else { Write-Step "git: $(git --version)" }

$hasSdk8 = $false
if (Test-Command 'dotnet') {
    $sdks = & dotnet --list-sdks 2>$null
    $hasSdk8 = ($sdks | Where-Object { $_ -match '^8\.' }).Count -gt 0
}
if (-not $hasSdk8) { Install-WingetPackage 'Microsoft.DotNet.SDK.8' '.NET 8 SDK' }
else { Write-Step ".NET SDK: $(($sdks | Where-Object { $_ -match '^8\.' } | Select-Object -Last 1))" }

if (-not $SkipInnoSetup) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (-not (Test-Path $iscc)) { Install-WingetPackage 'JRSoftware.InnoSetup' 'Inno Setup 6' }
    else { Write-Step "Inno Setup: $iscc" }
}

Write-Step 'Hazır. Yeni bir PowerShell penceresi açıp şunu çalıştır:'
Write-Host '    cd (hub klasörü); .\scripts\build.ps1 -Test' -ForegroundColor Green
