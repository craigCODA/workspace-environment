[CmdletBinding()]
param(
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot '.native-package'
}
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$payloadRoot = Join-Path $resolvedOutputRoot 'payload'
$versionsRoot = Join-Path $payloadRoot 'versions'

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    Remove-Item -LiteralPath $resolvedOutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

& (Join-Path $PSScriptRoot 'prepare-native-desktop.ps1') `
    -Configuration Release `
    -OutputRoot $versionsRoot `
    -StateRoot $payloadRoot `
    -Activate
if ($LASTEXITCODE -ne 0) {
    throw 'Native Workspace Environment staging failed.'
}

$launcher = Join-Path $payloadRoot 'launcher\Workspace.Launcher.exe'
$activation = Join-Path $payloadRoot 'activation.json'
if (-not (Test-Path -LiteralPath $launcher)) {
    throw "Native launcher is missing: $launcher"
}
if (-not (Test-Path -LiteralPath $activation)) {
    throw "Native activation state is missing: $activation"
}

$launchCommand = @'
@echo off
setlocal
set "ROOT=%~dp0payload"
"%ROOT%\launcher\Workspace.Launcher.exe" --state-root "%ROOT%" --versions-root "%ROOT%\versions"
endlocal
'@
Set-Content -LiteralPath (Join-Path $resolvedOutputRoot 'Launch Workspace Environment.cmd') `
    -Value $launchCommand `
    -Encoding ASCII

$readme = @'
Workspace Environment Native Coda Preview

1. Extract the complete artifact folder.
2. Double-click "Launch Workspace Environment.cmd".
3. ChatGPT (Codex) uses your ChatGPT subscription through the Codex CLI. If Codex is installed but signed out, Workspace will start the Codex login flow.
4. Grok (xAI API) requires XAI_API_KEY in your Windows environment.

This is the native WinUI/WebView2 build. It contains DesktopCoordinator, local voice, agent-provider runtime, capability broker, the Windows Workspace Host, and the spatial client.

The Electron build is a development fallback and does not host the native Coda agent runtime.
'@
Set-Content -LiteralPath (Join-Path $resolvedOutputRoot 'README.txt') `
    -Value $readme `
    -Encoding UTF8

[pscustomobject]@{
    packageRoot = $resolvedOutputRoot
    launcher = $launcher
    activation = $activation
} | ConvertTo-Json
