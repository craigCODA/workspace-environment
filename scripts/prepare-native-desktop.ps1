[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'WorkspaceEnvironment\versions'),
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA 'WorkspaceEnvironment'),
    [switch]$Activate
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$resolvedStateRoot = [System.IO.Path]::GetFullPath($StateRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$commit = (& git -C $repoRoot rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to determine the source commit.' }
$versionId = "$timestamp-$commit"
$stagingRoot = Join-Path $resolvedOutputRoot ".staging-$versionId"
$versionRoot = Join-Path $resolvedOutputRoot $versionId
$desktopRoot = Join-Path $stagingRoot 'desktop'

if (Test-Path -LiteralPath $stagingRoot) {
    throw "The staging directory already exists: $stagingRoot"
}
if (Test-Path -LiteralPath $versionRoot) {
    throw "The version directory already exists: $versionRoot"
}

New-Item -ItemType Directory -Path $desktopRoot -Force | Out-Null

Push-Location $repoRoot
try {
    & npm run build --workspace '@workspace/spatial-client'
    if ($LASTEXITCODE -ne 0) { throw 'The spatial client build failed.' }

    & dotnet publish 'apps/desktop-native/src/Workspace.Desktop/Workspace.Desktop.csproj' `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $desktopRoot `
        -p:Platform=x64 `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishReadyToRun=false
    if ($LASTEXITCODE -ne 0) { throw 'The native desktop publish failed.' }

    $hostRoot = Join-Path $desktopRoot 'host'
    & dotnet publish 'apps/host-windows/src/Workspace.Host/Workspace.Host.csproj' `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $hostRoot `
        -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw 'The Workspace Host publish failed.' }

    $files = [ordered]@{}
    $stagingPrefix = $stagingRoot.TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        if (-not $_.FullName.StartsWith($stagingPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A staged file escaped the version root: $($_.FullName)"
        }
        $relative = $_.FullName.Substring($stagingPrefix.Length).Replace('\', '/')
        $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        versionId = $versionId
        entryPoint = 'desktop/Workspace.Desktop.exe'
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $stagingRoot 'version-manifest.json') `
        -Encoding UTF8

    New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
    Move-Item -LiteralPath $stagingRoot -Destination $versionRoot

    if ($Activate) {
        $launcherRoot = Join-Path $resolvedStateRoot 'launcher'
        $launcherStaging = Join-Path $resolvedStateRoot ".launcher-staging-$versionId"
        New-Item -ItemType Directory -Path $launcherStaging -Force | Out-Null
        & dotnet publish 'apps/desktop-native/src/Workspace.Launcher/Workspace.Launcher.csproj' `
            --configuration $Configuration `
            --runtime win-x64 `
            --self-contained true `
            --output $launcherStaging
        if ($LASTEXITCODE -ne 0) { throw 'The launcher publish failed.' }
        if (Test-Path -LiteralPath $launcherRoot) {
            $launcherBackup = Join-Path $resolvedStateRoot "launcher.previous-$timestamp"
            Move-Item -LiteralPath $launcherRoot -Destination $launcherBackup
        }
        Move-Item -LiteralPath $launcherStaging -Destination $launcherRoot


        $activationPath = Join-Path $resolvedStateRoot 'activation.json'
        $activeVersion = $null
        $knownGoodVersion = $null
        if (Test-Path -LiteralPath $activationPath) {
            $current = Get-Content -Raw -LiteralPath $activationPath | ConvertFrom-Json
            $activeVersion = $current.activeVersion
            $knownGoodVersion = $current.knownGoodVersion
        }
        $activation = [ordered]@{
            schemaVersion = 1
            activeVersion = $activeVersion
            pendingVersion = $versionId
            knownGoodVersion = $knownGoodVersion
        }
        New-Item -ItemType Directory -Path $resolvedStateRoot -Force | Out-Null
        $activationTemp = Join-Path $resolvedStateRoot ".activation-$versionId.tmp"
        $activation | ConvertTo-Json | Set-Content -LiteralPath $activationTemp -Encoding UTF8
        Move-Item -LiteralPath $activationTemp -Destination $activationPath -Force
    }

    [pscustomobject]@{
        versionId = $versionId
        versionRoot = $versionRoot
        manifest = (Join-Path $versionRoot 'version-manifest.json')
        activated = [bool]$Activate
    } | ConvertTo-Json
}
finally {
    Pop-Location
}
