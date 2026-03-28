[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipGitHubRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)

    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)

    Write-Host "  OK   $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)

    throw $Message
}

function Assert-LastExit {
    param([string]$StepName)

    if ($LASTEXITCODE -ne 0) {
        Write-Fail "$StepName falhou com exit code $LASTEXITCODE"
    }
}

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [string[]]$CandidatePaths = @(),

        [string[]]$RegistryPaths = @()
    )

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
            if (-not (Test-Path -LiteralPath $normalizedValue)) {
                continue
            }

            if ((Get-Item -LiteralPath $normalizedValue).PSIsContainer) {
                $resolvedCandidate = Join-Path $normalizedValue $CommandName
                if (Test-Path -LiteralPath $resolvedCandidate) {
                    return (Resolve-Path -LiteralPath $resolvedCandidate).Path
                }

                continue
            }

            return (Resolve-Path -LiteralPath $normalizedValue).Path
        }
    }

    Write-Fail "Nao foi possivel localizar $CommandName"
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
        Write-Fail "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

function Get-GitStatusLines {
    $statusOutput = git status --short
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'Nao foi possivel consultar o estado do repositório git.'
    }

    return @($statusOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Show-GitStatusSummary {
    $statusLines = Get-GitStatusLines
    if ($statusLines.Count -eq 0) {
        Write-Ok 'Nenhuma alteracao pendente antes do inicio do release'
        return
    }

    Write-Host ''
    Write-Host '  Alteracoes detectadas e serao incluidas no commit de release:' -ForegroundColor Yellow
    $statusLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

function Commit-AndPushRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseVersion
    )

    Write-Step 'Commitando alteracoes da release'

    & git add -A
    Assert-LastExit 'git add'

    & git commit -m "release: v$ReleaseVersion"
    Assert-LastExit 'git commit'
    Write-Ok "Commit criado: release: v$ReleaseVersion"

    Write-Step 'Enviando commit para origin'
    & git push origin HEAD
    Assert-LastExit 'git push'
    Write-Ok 'Push concluido'
}

function Ensure-ChangelogSection {
    param(
        [string]$ChangelogPath,
        [string]$ReleaseVersion,
        [string]$ReleaseDate
    )

    $changelogContent = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
    $header = "## [$ReleaseVersion] - $ReleaseDate"

    if ($changelogContent -match [regex]::Escape($header)) {
        Write-Ok "Secao $header ja existe no CHANGELOG.md"
        return
    }

    $newSection = "`r`n$header`r`n`r`n### Added`r`n`r`n- `r`n`r`n"
    $updated = $changelogContent -replace "(?s)(---\s*)", "`$1$newSection"
    [System.IO.File]::WriteAllText($ChangelogPath, $updated, [System.Text.Encoding]::UTF8)
    Write-Ok "Secao $header adicionada ao CHANGELOG.md"
}

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) {
    $root = (Get-Location).Path
}

Push-Location $root
try {

$csprojPath = Join-Path $root 'TTT\TTT.csproj'
$installerPath = Join-Path $root 'installer.iss'
$changelogPath = Join-Path $root 'CHANGELOG.md'
$publishDir = Join-Path $root 'TTT\bin\x64\Release\net8.0-windows\win-x64\publish'
$installerOutputDir = Join-Path $root 'installer_output'

if (-not (Test-Path -LiteralPath $csprojPath)) {
    Write-Fail "Projeto nao encontrado: $csprojPath"
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    Write-Fail "Installer script nao encontrado: $installerPath"
}

if (-not (Test-Path -LiteralPath $changelogPath)) {
    Write-Fail "CHANGELOG.md nao encontrado: $changelogPath"
}

$dotnetPath = Resolve-ToolPath -CommandName 'dotnet.exe'

$isccCandidatePaths = @()
if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
    $isccCandidatePaths += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}

if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    $isccCandidatePaths += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
}

$isccPath = Resolve-ToolPath `
    -CommandName 'ISCC.exe' `
    -CandidatePaths $isccCandidatePaths `
    -RegistryPaths @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )

Show-GitStatusSummary

if (-not $Version) {
    $match = Select-String -Path $installerPath -Pattern '#define AppVersion\s+"([^"]+)"'
    if ($null -eq $match -or $match.Matches.Count -eq 0) {
        Write-Fail 'Nao foi possivel descobrir a versao atual em installer.iss'
    }

    $currentVersion = $match.Matches[0].Groups[1].Value
    Write-Host "";
    Write-Host "  Versao atual: $currentVersion" -ForegroundColor Yellow
    $Version = Read-Host '  Informe a nova versao (ex: 2.0.1)'
}

if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
    Write-Fail 'Formato de versao invalido. Use MAJOR.MINOR.PATCH, por exemplo 2.0.1'
}

$today = Get-Date -Format 'yyyy-MM-dd'

Write-Step 'Preparando CHANGELOG.md'
Ensure-ChangelogSection -ChangelogPath $changelogPath -ReleaseVersion $Version -ReleaseDate $today

$codeExe = Get-Command code -ErrorAction SilentlyContinue
if ($null -ne $codeExe) {
    & $codeExe.Source $changelogPath
}
else {
    Start-Process notepad.exe $changelogPath
}

Write-Host ""
Read-Host '  Atualize as notas da release no CHANGELOG.md e pressione ENTER para continuar'

Write-Step 'Atualizando versoes'

$csprojContent = Get-Content -LiteralPath $csprojPath -Raw -Encoding UTF8
$csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
[System.IO.File]::WriteAllText($csprojPath, $csprojContent, [System.Text.Encoding]::UTF8)

$installerContent = Get-Content -LiteralPath $installerPath -Raw -Encoding UTF8
$installerContent = $installerContent -replace '#define AppVersion\s+"[^"]+"', ("#define AppVersion   `"$Version`"")
[System.IO.File]::WriteAllText($installerPath, $installerContent, [System.Text.Encoding]::UTF8)

Write-Ok 'Versao atualizada em TTT.csproj e installer.iss'

Write-Step 'Compilando o projeto principal'
Invoke-ExternalCommand `
    -FilePath $dotnetPath `
    -ArgumentList @(
        'build',
        $csprojPath,
        '-c', 'Release',
        '-p:Platform=x64'
    ) `
    -FailureMessage 'dotnet build falhou.'
Write-Ok 'Build concluido'

Write-Step 'Limpando artefatos anteriores'
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path -LiteralPath $installerOutputDir) {
    Remove-Item -LiteralPath $installerOutputDir -Recurse -Force
}

Write-Step 'Publicando build self-contained'
Invoke-ExternalCommand `
    -FilePath $dotnetPath `
    -ArgumentList @(
        'publish',
        $csprojPath,
        '-c', 'Release',
        '-f', 'net8.0-windows',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:Platform=x64'
    ) `
    -FailureMessage 'dotnet publish falhou.'

if (-not (Test-Path -LiteralPath $publishDir)) {
    Write-Fail "Diretorio de publish nao foi gerado: $publishDir"
}

Write-Step 'Gerando instalador'
Push-Location $root
try {
    Invoke-ExternalCommand `
        -FilePath $isccPath `
        -ArgumentList @($installerPath) `
        -FailureMessage 'ISCC falhou.'
}
finally {
    Pop-Location
}

$installerExe = Get-ChildItem -LiteralPath $installerOutputDir -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $installerExe) {
    Write-Fail 'Nenhum instalador foi encontrado em installer_output'
}

Write-Ok "Instalador gerado: $($installerExe.FullName)"

if ($SkipGitHubRelease) {
    Write-Host ''
    Write-Host '  GitHub Release pulada por parametro.' -ForegroundColor Yellow
    return
}

Commit-AndPushRelease -ReleaseVersion $Version

Write-Step 'Publicando GitHub Release'

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $ghCommand) {
    Write-Host ''
    Write-Host '  gh CLI nao encontrado. A release nao foi publicada.' -ForegroundColor Yellow
    Write-Host '  Instale o GitHub CLI: https://cli.github.com/' -ForegroundColor Yellow
    return
}

$tagName = "v$Version"
${commitSha} = git rev-parse HEAD
Assert-LastExit 'git rev-parse HEAD'

& $ghCommand.Source release create $tagName $installerExe.FullName `
    --target $commitSha `
    --title "TTT v$Version" `
    --notes-file $changelogPath
Assert-LastExit 'gh release create'

Write-Ok "GitHub Release criada: $tagName"
}
finally {
    Pop-Location
}
