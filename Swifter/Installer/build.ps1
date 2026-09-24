#Requires -Version 7.0
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'x86', 'AnyCPU')]
    [string]$Platform = 'x64',
    [switch]$SelfContained,
    [switch]$Publish,
    [switch]$Installer,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-Location -Path $PSScriptRoot\..

$Project = Join-Path $PSScriptRoot 'Swifter.csproj'
$OutDir = Join-Path (Get-Location) 'bin' $Configuration 'net8.0-windows10.0.17763.0'
$DistDir = Join-Path (Get-Location) 'dist'
$Version = '1.0.0'

function Write-Step($Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-DotNet {
    try {
        $ver = & dotnet --version
        Write-Host "dotnet SDK $ver detected" -ForegroundColor DarkGray
    }
    catch {
        throw '.NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0'
    }
}

function Assert-WebView2Package {
    Write-Step 'Restoring NuGet packages (Microsoft.Web.WebView2, System.Data.SQLite.Core)'
    & dotnet restore $Project --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
}

function Invoke-Clean {
    Write-Step 'Cleaning build artifacts'
    foreach ($dir in @('bin', 'obj', 'dist')) {
        $p = Join-Path (Get-Location) $dir
        if (Test-Path $p) {
            Remove-Item -Path $p -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  removed $dir" -ForegroundColor DarkGray
        }
    }
}

function Invoke-Build {
    Write-Step "Building $Configuration | $Platform"
    $args = @(
        'build', $Project,
        '-c', $Configuration,
        '-p:Platform=' + $Platform,
        '--verbosity', 'minimal'
    )
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
}

function Invoke-Publish {
    Write-Step 'Publishing single-folder release'
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    $publishArgs = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
        '-p:PublishSingleFile=false',
        '-p:PublishReadyToRun=true',
        '-p:Version=' + $Version,
        '-o', (Join-Path $DistDir 'Swifter'),
        '--verbosity', 'minimal'
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    $exe = Join-Path $DistDir 'Swifter' 'Swifter.exe'
    if (-not (Test-Path $exe)) { throw "Publish output missing: $exe" }
    Write-Host "  published -> $exe" -ForegroundColor Green
}

function Invoke-Installer {
    Write-Step 'Compiling Inno Setup installer'
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        Write-Warning 'Inno Setup Compiler not found. Install from https://jrsoftware.org/isdl.php'
        return
    }
    $iss = Join-Path $PSScriptRoot 'SwifterSetup.iss'
    & $iscc "/DMyAppVersion=$Version" $iss
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }
    $setup = Join-Path $DistDir "SwifterSetup-$Version-x64.exe"
    if (Test-Path $setup) { Write-Host "  installer -> $setup" -ForegroundColor Green }
}

function Invoke-HealthChecks {
    Write-Step 'Post-build health checks'
    $required = @(
        'Swifter.exe',
        'Swifter.dll',
        'Microsoft.Web.WebView2.Core.dll',
        'System.Data.SQLite.dll',
        'Swifter.runtimeconfig.json'
    )
    $missing = @()
    foreach ($f in $required) {
        $path = Join-Path $OutDir $f
        if (-not (Test-Path $path)) { $missing += $f }
    }
    if ($missing.Count -gt 0) {
        Write-Warning ("Missing outputs: " + ($missing -join ', '))
    }
    else {
        Write-Host '  all required assemblies present' -ForegroundColor Green
    }

    $size = (Get-ChildItem $OutDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  build size: {0:N1} MB" -f ($size / 1MB)) -ForegroundColor DarkGray
}

try {
    Write-Host '╔══════════════════════════════════════╗' -ForegroundColor Magenta
    Write-Host '║     SWIFTER BROWSER BUILD PIPELINE    ║' -ForegroundColor Magenta
    Write-Host '╚══════════════════════════════════════╝' -ForegroundColor Magenta

    Assert-DotNet
    if ($Clean) { Invoke-Clean }
    Assert-WebView2Package
    Invoke-Build
    Invoke-HealthChecks
    if ($Publish) { Invoke-Publish }
    if ($Installer) { Invoke-Installer }

    Write-Step 'BUILD SUCCEEDED'
    Write-Host "Output: $OutDir" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "BUILD FAILED: $_" -ForegroundColor Red
    exit 1
}
