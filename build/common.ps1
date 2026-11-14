# Shared helpers for the CozyMarblesVR build scripts.
$ErrorActionPreference = 'Stop'

$script:ModRoot  = Split-Path $PSScriptRoot -Parent
# The checkout normally lives inside the game folder (<game>\CozyMarblesVR). Set COZY_MARBLES_DIR to build from elsewhere.
$script:GameRoot = if ($env:COZY_MARBLES_DIR) { $env:COZY_MARBLES_DIR } else { Split-Path $script:ModRoot -Parent }
if (-not (Test-Path (Join-Path $script:GameRoot 'Cozy Marbles_Data'))) {
    throw "Cozy Marbles not found in '$($script:GameRoot)'. Put the repository into the game folder or set COZY_MARBLES_DIR."
}
$script:Managed  = Join-Path $script:GameRoot 'Cozy Marbles_Data\Managed'
$script:LibDir   = Join-Path $script:ModRoot 'lib'

function Find-Csc {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'Roslyn csc.exe not found (Visual Studio 2022 / Build Tools required).'
}

# Preprocessor symbols Unity 6000.3 defines for a Windows standalone Mono player.
function Get-UnityDefines {
    $d = @(
        'UNITY_STANDALONE', 'UNITY_STANDALONE_WIN', 'UNITY_64', 'ENABLE_MONO', 'ENABLE_VR',
        'ENABLE_INPUT_SYSTEM', 'ENABLE_LEGACY_INPUT_MANAGER',
        'NET_STANDARD', 'NET_STANDARD_2_0', 'NET_STANDARD_2_1', 'NETSTANDARD', 'NETSTANDARD2_1',
        'CSHARP_7_3_OR_NEWER', 'UNITY_6000', 'UNITY_6000_3', 'UNITY_6000_3_14'
    )
    foreach ($v in '5_3','5_4','5_5','5_6') { $d += "UNITY_${v}_OR_NEWER" }
    foreach ($y in 2017..2023) { foreach ($m in 1..4) { if ($y -ge 2020 -and $m -gt 3) { continue }; $d += "UNITY_${y}_${m}_OR_NEWER" } }
    foreach ($m in 0..3) { $d += "UNITY_6000_${m}_OR_NEWER" }
    return $d
}

# Reference set: BCL facades + Unity engine modules from the game's Managed folder.
function Get-GameReferences([string[]]$Extra = @()) {
    $names = @('mscorlib.dll', 'netstandard.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll',
               'System.Runtime.dll', 'System.Memory.dll', 'System.Buffers.dll')
    $refs = @()
    foreach ($n in $names) { $p = Join-Path $script:Managed $n; if (Test-Path $p) { $refs += $p } }
    $refs += (Get-ChildItem $script:Managed -Filter 'UnityEngine*.dll' | Where-Object { $_.Name -notmatch 'Purchasing' }).FullName
    $refs += $Extra
    return $refs
}

function Invoke-Csc {
    param(
        [Parameter(Mandatory)][string]$Out,
        [Parameter(Mandatory)][string[]]$Sources,
        [Parameter(Mandatory)][string[]]$References,
        [string[]]$Defines = @(),
        [switch]$Unsafe,
        [string[]]$Resources = @(),
        [string]$RspDir = $env:TEMP
    )
    $csc = Find-Csc
    $rsp = Join-Path $RspDir ("csc_" + [IO.Path]::GetFileNameWithoutExtension($Out) + ".rsp")
    $lines = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-debug-',
               '-langversion:latest', '-nowarn:0169,0649,0414,0618,0219,0108,0114,0067,1701,1702,0436,0162,0168,0612,8632',
               '-deterministic+', "-out:`"$Out`"")
    if ($Unsafe) { $lines += '-unsafe+' }
    if ($Defines.Count) { $lines += '-define:' + ($Defines -join ';') }
    foreach ($r in $References) { $lines += "-r:`"$r`"" }
    foreach ($s in $Sources) { $lines += "`"$s`"" }
    foreach ($res in $Resources) { $lines += "-resource:$res" }
    Set-Content -Path $rsp -Value $lines -Encoding UTF8
    $output = & $csc "@$rsp" 2>&1
    $errors = $output | Where-Object { $_ -match '\berror\b' }
    if ($LASTEXITCODE -ne 0) {
        ($errors | Select-Object -First 40) | ForEach-Object { Write-Host $_ }
        throw "csc failed for $Out ($($errors.Count) errors)"
    }
    Write-Host ("built {0}  ({1:N0} bytes)" -f (Split-Path $Out -Leaf), (Get-Item $Out).Length)
}
