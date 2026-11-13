param(
    [string]$PreviousPackagesPath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\Arkadia.App\Arkadia.App.csproj'
$publishPath = Join-Path $repositoryRoot 'src\Arkadia.App\bin\publish\win-x64'
$installerPath = Join-Path $repositoryRoot 'installer\ARKADIA.iss'
$velopackOutputPath = Join-Path $repositoryRoot 'dist\velopack'
$iconPath = Join-Path $repositoryRoot 'src\Arkadia.App\Assets\arkadia.ico'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw 'The application project does not define a Version.'
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "The application icon was not found at $iconPath."
}

$isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $isccPath) {
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $isccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $isccPath) {
    throw 'Inno Setup 6 or 7 is required to build the installer. Install it from https://jrsoftware.org/isdl.php, then run this script again.'
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$toolManifestPath = Join-Path $repositoryRoot '.config\dotnet-tools.json'
if (-not (Test-Path -LiteralPath $toolManifestPath)) {
    throw 'The local .NET tool manifest is required to build the Velopack release.'
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "Restoring the .NET tools failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path $velopackOutputPath | Out-Null
if ($PreviousPackagesPath -and (Test-Path -LiteralPath $PreviousPackagesPath)) {
    Get-ChildItem -LiteralPath $PreviousPackagesPath -Filter '*.nupkg' -File |
        Copy-Item -Destination $velopackOutputPath -Force
}

$currentPackagePrefix = "ARKADIA-$version-"
$generatedAssets = @(
    "$($currentPackagePrefix)full.nupkg",
    "$($currentPackagePrefix)delta.nupkg",
    'ARKADIA-win-Portable.zip',
    'ARKADIA-win-Setup.exe',
    'assets.win.json',
    'releases.win.json',
    'RELEASES'
)
foreach ($assetName in $generatedAssets) {
    $generatedAssetPath = Join-Path $velopackOutputPath $assetName
    if (Test-Path -LiteralPath $generatedAssetPath -PathType Leaf) {
        Remove-Item -LiteralPath $generatedAssetPath -Force
    }
}

$vpkArguments = @(
    'pack',
    '--packId', 'ARKADIA',
    '--packVersion', $version,
    '--packTitle', 'ARKADIA',
    '--packAuthors', 'ARKADIA',
    '--packDir', $publishPath,
    '--outputDir', $velopackOutputPath,
    '--mainExe', 'Arkadia.App.exe',
    '--runtime', 'win-x64',
    '--icon', $iconPath,
    '--channel', 'win',
    '--shortcuts', 'Desktop,StartMenuRoot'
)
if (-not [string]::IsNullOrWhiteSpace($env:ARKADIA_SIGNING_CERTIFICATE_THUMBPRINT)) {
    $signParams = "/fd SHA256 /s My /sha1 $($env:ARKADIA_SIGNING_CERTIFICATE_THUMBPRINT) /tr http://timestamp.digicert.com /td SHA256"
    $vpkArguments += @('--signParams', $signParams)
}

& dotnet tool run vpk -- @vpkArguments
if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed with exit code $LASTEXITCODE."
}

& $isccPath "/DAppVersion=$version" $installerPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Output "Inno Setup compatibility installer and Velopack releases created in $(Join-Path $repositoryRoot 'dist')."
