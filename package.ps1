param (
	[switch]$NoArchive,
	[string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"

$DistDir = "$OutputDirectory/dist"
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$modInfo = Get-Content -Raw -Path "resources/info.json" | ConvertFrom-Json
$modId = $modInfo.Id
$modVersion = $modInfo.Version

if ($NoArchive) {
	$ZipWorkDir = "$OutputDirectory"
} else {
	$ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/$modId"

# Clean previous staging (so stale or empty folders don't linger)
if (Test-Path $ZipOutDir) { Remove-Item $ZipOutDir -Recurse -Force }
New-Item $ZipOutDir -ItemType Directory -Force | Out-Null

# Copy flat files
Copy-Item -Force -Path "resources/info.json","LICENSE" -Destination $ZipOutDir

# Copy ONLY the contents of build (so build/ itself is not a top-level folder in the package)
if (Test-Path "build") {
	# Enumerate children to avoid packaging the build folder root
	Get-ChildItem -LiteralPath "build" -Force | ForEach-Object {
		Copy-Item -Recurse -Force -LiteralPath $_.FullName -Destination $ZipOutDir
	}
	# Validate Assets copied (helpful diagnostic if something goes wrong)
	if (-not (Test-Path (Join-Path $ZipOutDir 'Assets'))) {
		Write-Warning "Assets folder missing from packaged output."
	}
} else {
	Write-Warning "build directory not found; skipping."
}

if (!$NoArchive)
{
	$FILE_NAME = "$DistDir/${modId}_$modVersion.zip"
	if (Test-Path $FILE_NAME) { Remove-Item $FILE_NAME -Force }
	Compress-Archive -CompressionLevel Fastest -Path "$ZipOutDir/*" -DestinationPath "$FILE_NAME"
	Write-Host "Created archive: $FILE_NAME"
}
else {
	Write-Host "Staged (no archive): $ZipOutDir"
}
