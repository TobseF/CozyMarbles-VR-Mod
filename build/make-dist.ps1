# Collects everything that belongs into a GitHub release into ..\dist and zips it.
#
#   dist\CozyMarblesVR\                    mod only (for players who already have BepInEx 5 or use a mod manager)
#   dist\CozyMarblesVR-with-BepInEx\       mod + BepInEx 5 loader: unzip into the game folder, done
#   dist\CozyMarblesVR.zip
#   dist\CozyMarblesVR-with-BepInEx.zip
#
# Both archives mirror the game folder (BepInEx\..., Cozy Marbles_Data\...), so "extract into the game folder" is
# the whole installation - the layout installer scripts and mod managers expect. The asset names carry no version,
# which keeps https://github.com/<owner>/<repo>/releases/latest/download/CozyMarblesVR.zip stable for them.
#
#   -NoBuild   package what build.ps1 last installed into the game instead of building first
param([switch]$NoBuild)

. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }

$version = [regex]::Match((Get-Content (Join-Path $ModRoot 'src\Plugin\Plugin.cs') -Raw), 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Version constant not found in src\Plugin\Plugin.cs' }

$dist = Join-Path $ModRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

function Copy-To([string]$Source, [string]$TargetDir) {
    if (-not (Test-Path $Source)) { throw "Missing: $Source (run build\build.ps1 first)" }
    New-Item -ItemType Directory -Force $TargetDir | Out-Null
    Copy-Item $Source $TargetDir -Force
}

# ------------------------------------------------------------------ mod only
$mod = Join-Path $dist 'CozyMarblesVR'
$installedPlugin = Join-Path $GameRoot 'BepInEx\plugins\CozyMarblesVR'
$pluginOut = Join-Path $mod 'BepInEx\plugins\CozyMarblesVR'
foreach ($n in 'CozyMarblesVR.dll', 'CozyMarblesVR.Shim.dll', 'Unity.XR.Management.dll', 'Unity.XR.CoreUtils.dll', 'Unity.XR.OpenXR.dll') {
    Copy-To (Join-Path $installedPlugin $n) $pluginOut
}
Copy-To (Join-Path $GameRoot 'BepInEx\patchers\CozyMarblesVR.Patcher.dll') (Join-Path $mod 'BepInEx\patchers')

# Unity's OpenXR plugin: native parts the player loads from its data folder ...
$native = Join-Path $LibDir 'native'
Copy-To (Join-Path $native 'UnityOpenXR.dll')   (Join-Path $mod 'Cozy Marbles_Data\Plugins\x86_64')
Copy-To (Join-Path $native 'openxr_loader.dll') (Join-Path $mod 'Cozy Marbles_Data\Plugins\x86_64')
Copy-To (Join-Path $native 'UnitySubsystemsManifest.json') (Join-Path $mod 'Cozy Marbles_Data\UnitySubsystems\UnityOpenXR')
# ... and a copy next to the plugin: the patcher restores them from there after a game update or a mod-manager install
foreach ($n in 'UnityOpenXR.dll', 'openxr_loader.dll', 'UnitySubsystemsManifest.json') {
    Copy-To (Join-Path $native $n) (Join-Path $pluginOut 'native')
}

# documentation for the package
Copy-To (Join-Path $ModRoot 'README.md') $pluginOut
Copy-To (Join-Path $ModRoot 'THIRD-PARTY-NOTICES.md') $pluginOut
if (Test-Path (Join-Path $ModRoot 'LICENSE')) { Copy-To (Join-Path $ModRoot 'LICENSE') $pluginOut }
Set-Content (Join-Path $pluginOut 'VERSION.txt') "Cozy Marbles VR $version" -Encoding ASCII

# ------------------------------------------------------------------ mod + BepInEx
$full = Join-Path $dist 'CozyMarblesVR-with-BepInEx'
Copy-Item $mod $full -Recurse
# the BepInEx 5 loader exactly as installed in the game (only the loader itself - never configs, logs or other plugins)
foreach ($n in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') { Copy-To (Join-Path $GameRoot $n) $full }
$coreOut = Join-Path $full 'BepInEx\core'
Get-ChildItem (Join-Path $GameRoot 'BepInEx\core') -File | Where-Object Extension -ne '.xml' | ForEach-Object { Copy-To $_.FullName $coreOut }
$bepVersion = (Get-Item (Join-Path $GameRoot 'BepInEx\core\BepInEx.dll')).VersionInfo.FileVersion

# ------------------------------------------------------------------ archives
foreach ($dir in $mod, $full) {
    $zip = "$dir.zip"
    # Compress-Archive skips dot-files such as .doorstop_version, so zip with .NET directly
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($dir, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
}

Write-Host ''
Write-Host "Cozy Marbles VR $version  (BepInEx $bepVersion in the full package)" -ForegroundColor Green
Get-ChildItem $dist -Filter *.zip | ForEach-Object { Write-Host ("  {0,-34} {1,10:N0} bytes" -f $_.Name, $_.Length) }
Write-Host "Upload both zips as assets of a GitHub release tagged $version."
