# Decompiles the game's code to C# for reading (never commit the output: it is the game's code).
# Uses the ILSpy engine (ICSharpCode.Decompiler.dll) that ships with Visual Studio 2022 - nothing to install.
#
#   .\build\decompile.ps1                                   whole Assembly-CSharp -> %TEMP%\CozyMarblesVR_decompiled\Assembly-CSharp
#   .\build\decompile.ps1 -Assembly TimR.Events.dll         another assembly from Cozy Marbles_Data\Managed
#   .\build\decompile.ps1 -Type TimR.Snap.SnapManager       a single type, printed to the console
param(
    [string]$Assembly = 'Assembly-CSharp.dll',
    [string]$Type,
    [string]$OutDir
)
. (Join-Path $PSScriptRoot 'common.ps1')

function Find-Decompiler {
    foreach ($edition in 'Community', 'Professional', 'Enterprise') {
        $base = "C:\Program Files\Microsoft Visual Studio\2022\$edition\Common7\IDE\CommonExtensions\Microsoft\VBCSharp\LanguageServices"
        foreach ($p in "$base\Core\ICSharpCode.Decompiler.dll", "$base\ICSharpCode.Decompiler.dll") { if (Test-Path $p) { return $p } }
    }
    throw 'ICSharpCode.Decompiler.dll not found (comes with Visual Studio 2022). Alternative: dotnet tool install -g ilspycmd'
}

Add-Type -Path (Find-Decompiler)
$path = if ([IO.Path]::IsPathRooted($Assembly)) { $Assembly } else { Join-Path $Managed $Assembly }
if (-not (Test-Path $path)) { throw "Assembly not found: $path" }

$file = [ICSharpCode.Decompiler.Metadata.PEFile]::new($path)
$resolver = [ICSharpCode.Decompiler.Metadata.UniversalAssemblyResolver]::new($path, $false, '.NETStandard,Version=v2.1')
$resolver.AddSearchDirectory($Managed)
$settings = [ICSharpCode.Decompiler.DecompilerSettings]::new([ICSharpCode.Decompiler.CSharp.LanguageVersion]::CSharp10_0)
$settings.ThrowOnAssemblyResolveErrors = $false

if ($Type) {
    $dec = [ICSharpCode.Decompiler.CSharp.CSharpDecompiler]::new($file, $resolver, $settings)
    $dec.DecompileTypeAsString([ICSharpCode.Decompiler.TypeSystem.FullTypeName]::new($Type))
    return
}

if (-not $OutDir) { $OutDir = Join-Path (Join-Path $env:TEMP 'CozyMarblesVR_decompiled') ([IO.Path]::GetFileNameWithoutExtension($path)) }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$wp = [ICSharpCode.Decompiler.CSharp.ProjectDecompiler.WholeProjectDecompiler]::new($settings, $resolver, $resolver, $null)
$wp.DecompileProject($file, $OutDir) | Out-Null
$n = (Get-ChildItem $OutDir -Recurse -Filter *.cs | Measure-Object).Count
"decompiled $n files into $OutDir"
