# Builds the Thunderstore upload zip.
#
#   powershell -ExecutionPolicy Bypass -File tools/package.ps1
#
# Produces dist/ExtraInventorySlotUpgrade-<version>.zip with the layout Thunderstore expects:
#   manifest.json, icon.png, README.md, CHANGELOG.md at the root, DLL under plugins/.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$manifest = Get-Content "manifest.json" -Raw | ConvertFrom-Json
$version = $manifest.version_number
$dll = "bin\Release\ExtraInventorySlotUpgrade.dll"

foreach ($required in @("manifest.json", "icon.png", "README.md", "CHANGELOG.md", "LICENSE", $dll)) {
    if (-not (Test-Path $required)) {
        throw "Missing $required. Run 'dotnet build -c Release' first."
    }
}

# Thunderstore's package format requires the icon to be exactly 256x256.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Resolve-Path "icon.png"))
$iconSize = "$($icon.Width)x$($icon.Height)"
$icon.Dispose()
if ($iconSize -ne "256x256") { throw "icon.png must be 256x256, found $iconSize." }

New-Item -ItemType Directory -Force "dist" | Out-Null
$zip = "dist\ExtraInventorySlotUpgrade-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

# Written entry by entry rather than with Compress-Archive: on Windows PowerShell 5.1 that cmdlet
# writes backslash path separators, which the zip spec forbids and some mod managers choke on.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open((Join-Path $root $zip), "Create")
try {
    $entries = [ordered]@{
        "manifest.json" = "manifest.json"
        "icon.png"      = "icon.png"
        "README.md"     = "README.md"
        "CHANGELOG.md"  = "CHANGELOG.md"
        "LICENSE"       = "LICENSE"
        "plugins/ExtraInventorySlotUpgrade.dll" = $dll
    }
    foreach ($entry in $entries.GetEnumerator()) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Resolve-Path $entry.Value), $entry.Key) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Packaged $zip"
