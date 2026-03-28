[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Framework = 'net8.0-windows',
    [string]$ProjectPath,
    [string]$InstallerScriptPath,
    [string]$IsccPath,
    [switch]$SkipClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)

    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [string]$CustomPath,

        [string[]]$CandidatePaths = @(),

        [string[]]$RegistryPaths = @()
    )

    if ($CustomPath) {
        if (-not (Test-Path -LiteralPath $CustomPath)) {
            throw "Tool path not found: $CustomPath"
        }

        return (Resolve-Path -LiteralPath $CustomPath).Path
    }

    $command = Get-Command -Name $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidatePath in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        if (Test-Path -LiteralPath $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    foreach ($registryPath in $RegistryPaths) {
        if (-not (Test-Path -LiteralPath $registryPath)) {
            continue
        }

        $item = Get-ItemProperty -LiteralPath $registryPath
        $possibleValues = @($item.InstallLocation, $item.DisplayIcon)

        foreach ($value in $possibleValues) {
            if ([string]::IsNullOrWhiteSpace($value)) {
                continue
            }

            $normalizedValue = $value -replace ',\d+$', ''
            if (Test-Path -LiteralPath $normalizedValue) {
                if ((Get-Item -LiteralPath $normalizedValue).PSIsContainer) {
                    $resolvedCandidate = Join-Path $normalizedValue $CommandName
                    if (Test-Path -LiteralPath $resolvedCandidate) {
                        return (Resolve-Path -LiteralPath $resolvedCandidate).Path
                    }
                }
                else {
                    return (Resolve-Path -LiteralPath $normalizedValue).Path
                }
            }
        }
    }

    throw "Unable to locate ${CommandName}. Install the required tool or pass -IsccPath explicitly."
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & $FilePath @ArgumentList

    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

$repoRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot 'TTT\TTT.csproj'
}

if ([string]::IsNullOrWhiteSpace($InstallerScriptPath)) {
    $InstallerScriptPath = Join-Path $repoRoot 'installer.iss'
}

$publishDir = Join-Path $repoRoot (Join-Path 'TTT\bin\x64' "$Configuration\$Framework\$RuntimeIdentifier\publish")
$installerOutputDir = Join-Path $repoRoot 'installer_output'

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

if (-not (Test-Path -LiteralPath $InstallerScriptPath)) {
    throw "Installer script not found: $InstallerScriptPath"
}

$dotnetPath = Resolve-ToolPath -CommandName 'dotnet.exe'
$isccCandidatePaths = @()

if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
    $isccCandidatePaths += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}

if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    $isccCandidatePaths += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
}

$isccResolvedPath = Resolve-ToolPath `
    -CommandName 'ISCC.exe' `
    -CustomPath $IsccPath `
    -CandidatePaths $isccCandidatePaths `
    -RegistryPaths @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )

Write-Step "Using dotnet: $dotnetPath"
Write-Step "Using ISCC: $isccResolvedPath"

if (-not $SkipClean) {
    Write-Step 'Cleaning previous publish and installer output folders'

    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    if (Test-Path -LiteralPath $installerOutputDir) {
        Remove-Item -LiteralPath $installerOutputDir -Recurse -Force
    }
}

Write-Step 'Publishing TTT for Release x64 self-contained'
Invoke-ExternalCommand `
    -FilePath $dotnetPath `
    -ArgumentList @(
        'publish',
        $ProjectPath,
        '-c', $Configuration,
        '-f', $Framework,
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '-p:Platform=x64'
    ) `
    -FailureMessage 'dotnet publish failed.'

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish output folder was not created: $publishDir"
}

Write-Step 'Compiling Inno Setup installer'
Push-Location $repoRoot
try {
    Invoke-ExternalCommand `
        -FilePath $isccResolvedPath `
        -ArgumentList @(
            "/DSourceDir=$publishDir",
            $InstallerScriptPath
        ) `
        -FailureMessage 'Inno Setup compilation failed.'
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $installerOutputDir)) {
    throw "Installer output folder was not created: $installerOutputDir"
}

$installerFile = Get-ChildItem -LiteralPath $installerOutputDir -Filter '*.exe' |
    Sort-Object -Property LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $installerFile) {
    throw 'Installer build completed, but no .exe was found in installer_output.'
}

Write-Step "Installer generated successfully: $($installerFile.FullName)"
