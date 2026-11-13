# Compiles Unity's XR Management, XR Core Utils and OpenXR plugin packages (source packages from
# Unity's registry) against the game's own engine assemblies. Output goes to ..\lib.
#
#   -PackagesDir must contain  management\package, coreutils\package, openxr\package
#   (the extracted com.unity.xr.management / com.unity.xr.core-utils / com.unity.xr.openxr tarballs).
param([Parameter(Mandatory)][string]$PackagesDir)

. (Join-Path $PSScriptRoot 'common.ps1')
New-Item -ItemType Directory -Force $LibDir | Out-Null
$tmp = Join-Path $env:TEMP 'CozyMarblesVR_build'
New-Item -ItemType Directory -Force $tmp | Out-Null

# Lets the mod reach internal members of the XR assemblies (settings fields that Unity's editor normally fills in).
$ivt = Join-Path $tmp 'ModInternals.cs'
Set-Content $ivt '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CozyMarblesVR")]'

function Get-Sources([string]$Root, [string[]]$ExcludeDirs = @()) {
    Get-ChildItem $Root -Recurse -Filter *.cs | Where-Object {
        $rel = $_.FullName.Substring($Root.Length)
        -not ($ExcludeDirs | Where-Object { $rel -like "\$_\*" })
    } | ForEach-Object FullName
}

$unity = Get-UnityDefines
$inputSystem = Join-Path $Managed 'Unity.InputSystem.dll'

# 1) XR Management
$mgmt = Join-Path $LibDir 'Unity.XR.Management.dll'
Invoke-Csc -Out $mgmt -RspDir $tmp `
    -Sources (@(Get-Sources (Join-Path $PackagesDir 'management\package\Runtime')) + $ivt) `
    -References (Get-GameReferences) -Defines $unity

# 2) XR Core Utils
$core = Join-Path $LibDir 'Unity.XR.CoreUtils.dll'
Invoke-Csc -Out $core -RspDir $tmp `
    -Sources (@(Get-Sources (Join-Path $PackagesDir 'coreutils\package\Runtime')) + $ivt) `
    -References (Get-GameReferences @($inputSystem)) `
    -Defines ($unity + @('INCLUDE_PHYSICS_MODULE', 'INCLUDE_UGUI', 'INCLUDE_INPUT_SYSTEM', 'INCLUDE_LEGACY_INPUT_HELPERS',
                         'HAS_SET_LOCAL_POSITION_AND_ROTATION', 'HAS_GET_POSITION_AND_ROTATION'))

# 3) OpenXR plugin (main runtime assembly only; test/mock/quest sub-assemblies are left out)
$oxr = Join-Path $LibDir 'Unity.XR.OpenXR.dll'
Invoke-Csc -Out $oxr -RspDir $tmp -Unsafe `
    -Sources (@(Get-Sources (Join-Path $PackagesDir 'openxr\package\Runtime') @('ConformanceAutomation', 'MetaQuest', 'MockRuntime', 'OculusQuest', 'RuntimeDebugger', 'TestTooling')) + $ivt) `
    -References (Get-GameReferences @($inputSystem, $mgmt, $core)) `
    -Defines ($unity + @('INPUT_SYSTEM_POSE_VALID', 'INPUT_SYSTEM_BINDING_VALIDATOR', 'SPACEWARP_RIGHT_HAND_NDC_SUPPORTED', 'USE_INPUT_SYSTEM_POSE_CONTROL'))

# Native parts of the OpenXR plugin
$native = Join-Path $LibDir 'native'
New-Item -ItemType Directory -Force $native | Out-Null
Copy-Item (Join-Path $PackagesDir 'openxr\package\Runtime\windows\x64\UnityOpenXR.dll') $native -Force
Copy-Item (Join-Path $PackagesDir 'openxr\package\RuntimeLoaders\windows\x64\openxr_loader.dll') $native -Force
Copy-Item (Join-Path $PackagesDir 'openxr\package\Runtime\UnitySubsystemsManifest.json') $native -Force
Write-Host "XR dependencies ready in $LibDir"
