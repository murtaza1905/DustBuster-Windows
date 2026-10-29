#Requires -Version 5.1
<#
.SYNOPSIS
  Çözümü derler; -Test ile Core test paketini de çalıştırır. Uygulama projesi mevcutsa
  build\DustBuster altına kendi kendine yeten (self-contained) bir Release yayımlar.
.EXAMPLE
  .\scripts\build.ps1 -Test
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $root 'build.log'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Invoke-Dotnet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) başarısız (çıkış kodu $LASTEXITCODE). Günlük: $log" }
}

Push-Location $root
try {
    if (Test-Path $log) { Remove-Item $log -Force }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK bulunamadı. Önce .\scripts\bootstrap.ps1 çalıştır.'
    }
    Write-Step ".NET SDK $(dotnet --version)"

    Write-Step 'Paketler geri yükleniyor'
    Invoke-Dotnet @('restore', 'DustBuster.sln', '--nologo')

    Write-Step "Çözüm derleniyor ($Configuration)"
    Invoke-Dotnet @('build', 'DustBuster.sln', '-c', $Configuration, '--no-restore', '--nologo')

    if ($Test) {
        Write-Step 'Core testleri çalıştırılıyor'
        Invoke-Dotnet @('test', 'DustBuster.sln', '-c', $Configuration, '--no-build', '--nologo', '--logger', 'console;verbosity=normal')
    }

    $appProject = Join-Path $root 'src\DustBuster.App\DustBuster.App.csproj'
    if (Test-Path $appProject) {
        $out = Join-Path $root 'build\DustBuster'
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        Write-Step "Uygulama yayımlanıyor → $out"
        Invoke-Dotnet @('publish', $appProject, '-c', $Configuration, '-r', $Runtime, '--self-contained', 'true', '-o', $out, '--nologo',
                        '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true')
        $exe = Join-Path $out 'DustBuster.exe'
        if (-not (Test-Path $exe)) { throw "Yayımlama çıktısında DustBuster.exe yok: $out" }
        Write-Step "Hazır: $exe"
    }
    else {
        Write-Step 'Uygulama projesi henüz yok; yalnızca Core derlendi.'
    }
}
finally {
    Pop-Location
}
