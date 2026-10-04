$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$output = [IO.Path]::GetFullPath((Join-Path $root 'publish'))
$separator = [IO.Path]::DirectorySeparatorChar
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + $separator
if (-not $output.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must stay inside the launcher project: $output"
}

$launcherOutput = Join-Path $output 'launcher'
$updaterOutput = Join-Path $output 'updater'
$archive = Join-Path $output 'UBFLauncher-update.zip'
$outputPrefix = $output.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + $separator
foreach ($candidate in @($launcherOutput, $updaterOutput, $archive)) {
    $fullCandidate = [IO.Path]::GetFullPath($candidate)
    if (-not $fullCandidate.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside publish/: $fullCandidate"
    }
    if (Test-Path -LiteralPath $fullCandidate) {
        Remove-Item -LiteralPath $fullCandidate -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $launcherOutput, $updaterOutput -Force | Out-Null
& dotnet publish (Join-Path $root 'UBFLauncher.csproj') -c Release -r win-x64 --self-contained true -o $launcherOutput
if ($LASTEXITCODE -ne 0) { throw "UBFLauncher publish failed with exit code $LASTEXITCODE." }

& dotnet publish (Join-Path $root 'UBFLauncherUpdater\UBFLauncherUpdater.csproj') -c Release -r win-x64 --self-contained true -o $updaterOutput
if ($LASTEXITCODE -ne 0) { throw "UBFLauncherUpdater publish failed with exit code $LASTEXITCODE." }

$launcherExe = Join-Path $launcherOutput 'UBFLauncher.exe'
$updaterExe = Join-Path $updaterOutput 'UBFLauncherUpdater.exe'
if (-not (Test-Path -LiteralPath $launcherExe -PathType Leaf)) { throw 'The current build did not produce UBFLauncher.exe.' }
if (-not (Test-Path -LiteralPath $updaterExe -PathType Leaf)) { throw 'The current build did not produce UBFLauncherUpdater.exe.' }
foreach ($asset in @('launcher.settings.json', 'Assets\background.mp4', 'Assets\logo.png',
        'Assets\menu_sd_laucher_badays.mp3', 'Assets\menu_sd_laucher_goodays.mp3')) {
    if (-not (Test-Path -LiteralPath (Join-Path $launcherOutput $asset) -PathType Leaf)) {
        throw "Required launcher resource is missing from the publish output: $asset"
    }
}
Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $launcherOutput 'UBFLauncherUpdater.exe') -Force

$debugSymbols = @(Get-ChildItem -LiteralPath $launcherOutput -Filter '*.pdb' -Recurse -File)
$debugSymbols | Remove-Item -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $launcherOutput,
    $archive,
    [IO.Compression.CompressionLevel]::Optimal,
    $false
)

$archiveInfo = Get-Item -LiteralPath $archive
$launcherVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($launcherExe).ProductVersion
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Launcher version: $launcherVersion"
Write-Host "Launcher publish: $launcherOutput"
Write-Host "Updater publish: $updaterOutput"
Write-Host "Launcher update package: $archive"
Write-Host "Package size: $($archiveInfo.Length) bytes"
Write-Host "Package SHA-256: $archiveHash"
