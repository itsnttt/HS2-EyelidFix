param(
    [Parameter(Mandatory)][string] $GameDir,
    [string] $EditorDir = 'C:/Program Files/Unity/Hub/Editor/2018.4.11f1/Editor'
)
$ErrorActionPreference='Stop'
$data=Join-Path $EditorDir 'Data'
$core=Join-Path $GameDir 'BepInEx/core'
$managed=Join-Path $GameDir 'HoneySelect2_Data/Managed'
$out=Join-Path $PSScriptRoot 'build/BepInEx/plugins'
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@("$core/BepInEx.dll","$core/0Harmony.dll","$managed/Assembly-CSharp.dll","$managed/Assembly-CSharp-firstpass.dll","$managed/UnityEngine.dll","$managed/UnityEngine.CoreModule.dll","$managed/UnityEngine.AnimationModule.dll","$managed/UnityEngine.IMGUIModule.dll")
$argsList=@('-target:library','-sdk:4.6',("-out:$out/HS2_EyelidFix.dll")) + @($refs | ForEach-Object {"-r:$_"}) + @("$PSScriptRoot/src/HS2.EyelidFix/Plugin.cs")
& "$data/MonoBleedingEdge/bin/mono.exe" "$data/MonoBleedingEdge/lib/mono/4.5/mcs.exe" @argsList
if($LASTEXITCODE){throw 'Compilation failed'}
