[CmdletBinding()]
param(
    [ValidateRange(1, 10)]
    [int]$FixtureCount,
    [string]$DataDirectory,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
if ($FixtureCount -and $DataDirectory) {
    throw 'Choose fixture mode or a preview data directory, not both.'
}

$projectPath = Join-Path $PSScriptRoot 'src/ScrollWM.App/ScrollWM.App.csproj'
$applicationPath = Join-Path $PSScriptRoot 'src/ScrollWM.App/bin/Release/net10.0-windows/ScrollWM.App.exe'
$localSdkPath = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'

if ($Build -or -not (Test-Path -LiteralPath $applicationPath)) {
    $sdkPath = if (Test-Path -LiteralPath $localSdkPath) {
        $localSdkPath
    } else {
        $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $installedDotnet) {
            throw 'Install the .NET 10 SDK to build this project. An existing Release build can run without an SDK.'
        }
        $installedDotnet.Source
    }

    Push-Location -LiteralPath $PSScriptRoot
    try {
        & $sdkPath build $projectPath --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw 'Build failed. A compatible .NET 10 SDK is required; a runtime alone cannot build. See README.md.'
        }
    } finally {
        Pop-Location
    }
}

$applicationArguments = @()
if ($FixtureCount) {
    $applicationArguments = @('--fixture', $FixtureCount.ToString())
} elseif ($DataDirectory) {
    $applicationArguments = @('--data-dir', [System.IO.Path]::GetFullPath($DataDirectory))
}

# Launch the built apphost directly: no SDK lookup or PATH changes are needed.
& $applicationPath @applicationArguments
