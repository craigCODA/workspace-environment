[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
$resolvedSourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
$stateRoot = Join-Path $env:LOCALAPPDATA 'WorkspaceEnvironment'
$versionsRoot = Join-Path $stateRoot 'versions'

& (Join-Path $PSScriptRoot 'prepare-native-desktop.ps1') `
    -Configuration Release `
    -OutputRoot $versionsRoot `
    -StateRoot $stateRoot `
    -Activate
if ($LASTEXITCODE -ne 0) { throw 'Native preview staging failed.' }

$launcher = Join-Path $stateRoot 'launcher\Workspace.Launcher.exe'
if (-not (Test-Path -LiteralPath $launcher)) {
    throw "The native preview launcher is missing: $launcher"
}

$shell = New-Object -ComObject WScript.Shell
$shortcutLocations = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Workspace Environment Native Preview.lnk'),
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Workspace Environment Native Preview.lnk')
)
foreach ($shortcutPath in $shortcutLocations) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $launcher
    $shortcut.Arguments = "--source-root `"$resolvedSourceRoot`""
    $shortcut.WorkingDirectory = $resolvedSourceRoot
    $shortcut.Description = 'Voice-first Coda native preview'
    $shortcut.IconLocation = "$launcher,0"
    $shortcut.Save()
}

if (-not $SkipLaunch) {
    Start-Process -FilePath $launcher -ArgumentList @('--source-root', $resolvedSourceRoot)
}

[pscustomobject]@{
    launcher = $launcher
    sourceRoot = $resolvedSourceRoot
    desktopShortcut = $shortcutLocations[0]
    startMenuShortcut = $shortcutLocations[1]
    electronInstallPreserved = (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\workspace-environment\Workspace Environment.exe'))
} | ConvertTo-Json
