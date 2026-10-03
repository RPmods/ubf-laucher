param(
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $BuildDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $root 'UBF.exe') -PathType Leaf)) {
    throw 'BuildDirectory must be the root of a cooked Windows package and contain UBF.exe.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$') {
    throw 'Version must use major.minor.patch format, optionally with a prerelease suffix, for example 1.0.1-beta.'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Split-Path -Parent $root }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep the output ZIP outside the packaged game directory.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$archive = Join-Path $output "UBF-v$Version.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
        if ($file.Extension -ieq '.pdb') { continue }
        $entryName = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally { $zip.Dispose() }

$archiveInfo = Get-Item -LiteralPath $archive
if ($archiveInfo.Length -ge 2GB) {
    throw "The package is $($archiveInfo.Length) bytes; GitHub Release assets must be smaller than 2 GiB."
}
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if (-not ($zip.Entries | Where-Object { $_.FullName -ieq 'UBF.exe' })) {
        throw 'The ZIP does not contain UBF.exe at its root; it cannot be installed by UBFLauncher.'
    }
    if ($zip.Entries | Where-Object { $_.FullName -match '\.pdb$' }) {
        throw 'Debug symbol files (.pdb) are not part of the game distribution ZIP.'
    }
}
finally { $zip.Dispose() }

Write-Host "Game package created: $archive"
Write-Host "Size: $($archiveInfo.Length) bytes"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant())"
