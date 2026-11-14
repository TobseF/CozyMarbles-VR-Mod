# Builds Cozy Marbles VR (shim, preloader patcher, plugin) and installs it into the game folder.
# Requires: BepInEx 5 in the game folder, the XR assemblies in ..\lib (see build-xr-deps.ps1),
# Visual Studio 2022 (Roslyn csc).
. (Join-Path $PSScriptRoot 'common.ps1')

$tmp = Join-Path $env:TEMP 'CozyMarblesVR_build'
New-Item -ItemType Directory -Force $tmp | Out-Null
$core = Join-Path $GameRoot 'BepInEx\core'
if (-not (Test-Path (Join-Path $core 'BepInEx.dll'))) { throw 'BepInEx is not installed in the game folder.' }

$pluginDir  = Join-Path $GameRoot 'BepInEx\plugins\CozyMarblesVR'
$patcherDir = Join-Path $GameRoot 'BepInEx\patchers'
New-Item -ItemType Directory -Force $pluginDir, $patcherDir | Out-Null
$data = Join-Path $GameRoot 'Cozy Marbles_Data'
$unity = Get-UnityDefines
function Src([string]$name) { (Get-ChildItem (Join-Path $ModRoot "src\$name") -Recurse -Filter *.cs).FullName }

# 1) Shim: tiny assembly the rewritten game code calls into
$shim = Join-Path $pluginDir 'CozyMarblesVR.Shim.dll'
Invoke-Csc -Out $shim -RspDir $tmp -Sources (Src 'Shim') -References (Get-GameReferences) -Defines $unity

# 2) Preloader patcher
Invoke-Csc -Out (Join-Path $patcherDir 'CozyMarblesVR.Patcher.dll') -RspDir $tmp -Sources (Src 'Patcher') `
    -References (Get-GameReferences @((Join-Path $core 'BepInEx.dll'), (Join-Path $core 'Mono.Cecil.dll'))) -Defines $unity

# 3) XR assemblies next to the plugin
foreach ($n in 'Unity.XR.Management.dll', 'Unity.XR.CoreUtils.dll', 'Unity.XR.OpenXR.dll') {
    Copy-Item (Join-Path $LibDir $n) $pluginDir -Force
}

# 4) Plugin
$refs = Get-GameReferences (@(
    (Join-Path $core 'BepInEx.dll'), (Join-Path $core '0Harmony.dll'), $shim,
    (Join-Path $LibDir 'Unity.XR.Management.dll'), (Join-Path $LibDir 'Unity.XR.OpenXR.dll')) +
    ('Unity.InputSystem.dll', 'Unity.RenderPipelines.Core.Runtime.dll', 'Unity.RenderPipelines.Universal.Runtime.dll',
     'Unity.Cinemachine.dll', 'Unity.TextMeshPro.dll', 'Assembly-CSharp.dll', 'TimR.Events.dll', 'TimR.Init.dll', 'UniRx.dll' | ForEach-Object { Join-Path $Managed $_ }))
# menu artwork (drawn by make-icons.py), embedded as CozyMarblesVR.Icons.<name>.png
$icons = Get-ChildItem (Join-Path $ModRoot 'src\Plugin\Icons') -Filter *.png | ForEach-Object { "`"$($_.FullName)`",CozyMarblesVR.Icons.$($_.Name)" }
Invoke-Csc -Out (Join-Path $pluginDir 'CozyMarblesVR.dll') -RspDir $tmp -Sources (Src 'Plugin') -References $refs -Defines $unity -Resources $icons

# 5) Native OpenXR runtime parts into the player's data folder
$plugins = Join-Path $data 'Plugins\x86_64'
$subsys  = Join-Path $data 'UnitySubsystems\UnityOpenXR'
New-Item -ItemType Directory -Force $subsys | Out-Null
Copy-Item (Join-Path $LibDir 'native\UnityOpenXR.dll'), (Join-Path $LibDir 'native\openxr_loader.dll') $plugins -Force
Copy-Item (Join-Path $LibDir 'native\UnitySubsystemsManifest.json') $subsys -Force
# ...and a copy next to the plugin, from which the patcher restores them if a game update removes them
$pluginNative = Join-Path $pluginDir 'native'
New-Item -ItemType Directory -Force $pluginNative | Out-Null
Copy-Item (Join-Path $LibDir 'native\*') $pluginNative -Force

Write-Host "Installed Cozy Marbles VR into $GameRoot"
