param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PackageFile,
    [Parameter(Mandatory = $true)][string]$PackageDownloadUrl,
    [string]$ReleaseRepository = 'RPmods/ubf',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
function Test-SafeGameRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or $Path.Contains(':')) { return $false }
    foreach ($part in $Path.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ')) { return $false }
        if ($part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { return $false }
    }
    return $true
}
$root = (Resolve-Path -LiteralPath $GameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $root 'UBF.EXE') -PathType Leaf)) {
    throw 'The game directory must contain UBF.EXE at its root.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$') {
    throw 'Version must use major.minor.patch format, optionally with a prerelease suffix, for example 1.0.1-beta.'
}

$packagePath = (Resolve-Path -LiteralPath $PackageFile).Path
if ([IO.Path]::GetExtension($packagePath) -ne '.zip') { throw 'PackageFile must be a ZIP archive.' }
if ($packagePath.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep the ZIP outside the packaged game directory so it is not included in files.'
}

$releaseParts = $ReleaseRepository.Split('/')
if ($releaseParts.Count -ne 2 -or $releaseParts.Where({ [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
    throw 'ReleaseRepository must use OWNER/REPOSITORY format.'
}
$downloadUri = $null
if (-not [Uri]::TryCreate($PackageDownloadUrl, [UriKind]::Absolute, [ref]$downloadUri) -or
    $downloadUri.Scheme -ne 'https' -or
    $downloadUri.Host -ne 'github.com' -or
    -not $downloadUri.AbsolutePath.StartsWith("/$ReleaseRepository/releases/download/", [StringComparison]::OrdinalIgnoreCase) -or
    [Uri]::UnescapeDataString([IO.Path]::GetFileName($downloadUri.AbsolutePath)) -ne [IO.Path]::GetFileName($packagePath)) {
    throw "PackageDownloadUrl must be the real direct HTTPS GitHub Release asset URL for $ReleaseRepository and this ZIP filename."
}

$zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $files = foreach ($entry in $zip.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) }) {
        $relative = $entry.FullName.Replace('\', '/')
        if (-not (Test-SafeGameRelativePath $relative)) {
            throw "The package contains an unsafe path: $relative"
        }
        $sourcePath = [IO.Path]::GetFullPath((Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        if (-not $sourcePath.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "A package entry is missing from the build directory: $relative"
        }
        $sourceFile = Get-Item -LiteralPath $sourcePath
        if (($sourceFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The packaged game contains a symbolic link/reparse point that cannot be included safely: $($sourceFile.FullName)"
        }
        if ($sourceFile.Length -ne $entry.Length) {
            throw "Package/build size mismatch for $relative."
        }
        [ordered]@{
            path = $relative
            size = $entry.Length
            sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}
finally { $zip.Dispose() }

if (-not $files) { throw 'No game files were found.' }
if (-not ($files | Where-Object { $_.path -ieq 'UBF.exe' })) { throw 'The game file list must include UBF.exe at its root.' }

$packageInfo = Get-Item -LiteralPath $packagePath
$manifest = [ordered]@{
    version = $Version
    package = [ordered]@{
        fileName = $packageInfo.Name
        downloadUrl = $downloadUri.AbsoluteUri
        size = $packageInfo.Length
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    files = @($files)
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $root 'manifest.json' }
$manifestPath = [IO.Path]::GetFullPath($OutputPath)
$json = $manifest | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Manifest written: $manifestPath"
Write-Host "Files included: $($files.Count)"
Write-Host "Package file: $($packageInfo.Name) ($($packageInfo.Length) bytes)"
Write-Host "Package SHA-256: $($manifest.package.sha256)"
