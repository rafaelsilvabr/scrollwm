[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$DotNetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot "artifacts/releases/v$Version"
$publishDirectory = Join-Path $releaseRoot 'publish'
$packageDirectory = Join-Path $releaseRoot 'portable'
$assetDirectory = Join-Path $releaseRoot 'assets'
$releaseNotes = Join-Path $repositoryRoot "docs/releases/v$Version.md"

[xml]$properties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
if ($properties.Project.PropertyGroup.Version -ne $Version) {
    throw 'Release version must match Directory.Build.props.'
}
if (-not (Test-Path -LiteralPath $releaseNotes)) {
    throw "Missing release notes: docs/releases/v$Version.md"
}
if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release output already exists: $releaseRoot. Use a clean checkout to rebuild."
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory, $assetDirectory -Force | Out-Null
    Invoke-DotNet -Arguments @('restore', 'ScrollWM.slnx')
    Invoke-DotNet -Arguments @('build', 'ScrollWM.slnx', '--configuration', 'Release', '--no-restore', '--disable-build-servers', '-maxcpucount:1')
    Invoke-DotNet -Arguments @('test', 'ScrollWM.slnx', '--configuration', 'Release', '--no-build', '--no-restore', '--disable-build-servers', '-maxcpucount:1', '--logger', 'trx', '--results-directory', (Join-Path $releaseRoot 'test-results'))
    Invoke-DotNet -Arguments @(
        'publish', 'src/ScrollWM.App/ScrollWM.App.csproj', '--configuration', 'Release',
        '--runtime', 'win-x64', '--self-contained', 'true', '--output', $publishDirectory,
        '--disable-build-servers', '-maxcpucount:1',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
        "-p:Version=$Version"
    )

    $publishedExe = Join-Path $publishDirectory 'ScrollWM.App.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'The published executable is missing.' }
    $unexpectedFiles = Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Where-Object { $_.FullName -ne $publishedExe }
    if ($unexpectedFiles) { throw 'Publish output contains unexpected companion files; review bundling before release.' }
    $exeVersion = (Get-Item -LiteralPath $publishedExe).VersionInfo.FileVersion
    if ($exeVersion -ne "$Version.0") { throw "Executable version mismatch: $exeVersion" }

    $exeAsset = Join-Path $assetDirectory "ScrollWM-$Version-win-x64.exe"
    Copy-Item -LiteralPath $publishedExe -Destination $exeAsset
    Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $packageDirectory 'ScrollWM.exe')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $packageDirectory
    Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $packageDirectory 'README.txt')
    $zipAsset = Join-Path $assetDirectory "ScrollWM-$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipAsset -CompressionLevel Optimal

    $checksumLines = foreach ($asset in @($exeAsset, $zipAsset)) {
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($asset))"
    }
    $checksumLines | Set-Content -LiteralPath (Join-Path $assetDirectory 'SHA256SUMS.txt') -Encoding ascii
    Get-ChildItem -LiteralPath $assetDirectory | Select-Object Name, Length
} finally {
    Pop-Location
}
