param(
    [Parameter(Mandatory=$true)][string]$BasePackage,
    [Parameter(Mandatory=$true)][string]$MonoArtifacts,
    [Parameter(Mandatory=$true)][string]$Il2CppArtifacts,
    [string]$Il2CppRendererArtifacts,
    [Parameter(Mandatory=$true)][string]$DependencyNotices,
    [Parameter(Mandatory=$true)][string]$SourceArchive,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'Tools/UniversalInstaller/Universal.Core.ps1')
$base=[IO.Path]::GetFullPath($BasePackage)
if(!$Il2CppRendererArtifacts){$Il2CppRendererArtifacts=Join-Path (Split-Path $Il2CppArtifacts -Parent) 'Uuvr.OpenVR'}
$out=[IO.Path]::GetFullPath($Destination)
if((Test-Path -LiteralPath $out) -or $out.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use a new destination outside the base package.'}
Assert-UNoLink $out
foreach($name in @('BepInEx5-source.zip','BepInEx6-source.zip','Il2CppInterop-source.zip','UnityDoorstop-source.zip','SteamVR-Unity-LICENSE.txt','VRGIN-LICENSE.txt','DotNet-LICENSE.txt','DotNet-THIRD-PARTY-NOTICES.txt','ViGEmClient-LICENSE.txt','NewtonsoftJson-LICENSE.txt')){
 if(!(Test-Path -LiteralPath (Join-Path $DependencyNotices $name) -PathType Leaf)){throw "Missing required source/notice: $name"}
}
New-Item -ItemType Directory -Path $out | Out-Null
$catalog=Get-UCatalog $base
foreach($component in $catalog.components){foreach($f in $component.files){
    $from=Resolve-UChild $base $f.source
    if((Get-UHash $from) -ne $f.sha256){throw "Base payload checksum mismatch: $($f.source)"}
    $name=Split-Path -Leaf $f.target
    $replacement=$null
    if($component.id -eq 'mono-m02' -and $name -eq 'HybridVR.Mono.Runtime.dll'){$replacement=Join-Path $MonoArtifacts $name}
    if($component.id -eq 'il2cpp-gc13' -and $name -in @('Uuvr.dll','Uuvr.OpenVR.dll','Uuvr.deps.json','Uuvr.OpenVR.deps.json')){
        $replacement=Join-Path $(if($name -like 'Uuvr.OpenVR*'){$Il2CppRendererArtifacts}else{$Il2CppArtifacts}) $name
    }
    if($replacement){if(!(Test-Path -LiteralPath $replacement -PathType Leaf)){throw "Missing built artifact: $name"};$from=$replacement}
    $to=Resolve-UChild $out $f.source
    New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to
    $f.sha256=Get-UHash $to
}}
# Runtime/reference caches and development reports from the base package are never copied.
New-Item -ItemType Directory -Path (Join-Path $out 'cache/unity-libs') -Force | Out-Null
$catalog.release='M033-R20-public-full'
$catalog.created=Get-Date -Format 'yyyy-MM-dd'
$catalog.stage='public-prerelease'
foreach($lib in $catalog.unityLibraries){$lib.source="https://unity.bepinex.dev/libraries/$($lib.version).zip"}
$catalog.limitations=@('Windows x64 D3D11 only','Compatibility varies by game; automatic selection is a candidate, not a guarantee',
 'Optional OnlyUI is game-dependent; FullFrame is the safe fallback','First IL2CPP installation downloads official base libraries with approval; cached installations can be offline',
 'English, Chinese and Japanese; existing language preferences preserved','Public cursor/keyboard artwork replaces private assets; no new headset acceptance test performed')
[IO.File]::WriteAllText((Join-Path $out 'catalog.json'),($catalog | ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
foreach($name in @('Universal.Core.ps1','Universal.ps1','HybridVrPreflight.ps1','Install-HybridVR.bat','Install-HybridVR.cmd','Inspect-HybridVR.cmd','Prepare-Offline-Dependencies.cmd','Start-Skin-Diagnostics.cmd')){
 Copy-Item -LiteralPath (Join-Path $root ('Tools/UniversalInstaller/'+$name)) -Destination $out
}
Copy-Item -LiteralPath (Join-Path $root 'Tools/UniversalInstaller/templates') -Destination $out -Recurse
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll ("/out:"+(Join-Path $out 'HybridVR.Launcher.exe')) (Join-Path $root 'Tools/UniversalInstaller/HiddenLauncher.cs')
if($LASTEXITCODE -ne 0){throw 'Hidden launcher compilation failed'}
foreach($name in @('README.md','LICENSE','ACKNOWLEDGMENTS.md','THIRD_PARTY_NOTICES.md','UPSTREAM_VERSIONS.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $out}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $out -Recurse
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $out -Recurse
foreach($name in @('LICENSE-OFL-Noto.txt','MenuGlyphs.NOTICE.md','SihHands.NOTICE.md')){
 Copy-Item -LiteralPath (Join-Path $root ('HybridVR/Assets/'+$name)) -Destination (Join-Path $out 'licenses')
}
New-Item -ItemType Directory -Path (Join-Path $out 'sources/upstream') -Force | Out-Null
Copy-Item -LiteralPath $SourceArchive -Destination (Join-Path $out 'sources/UnityOneClickVR-source.zip')
foreach($f in Get-ChildItem -LiteralPath $DependencyNotices -File){
    $to=if($f.Extension -eq '.zip'){Join-Path $out ('sources/upstream/'+$f.Name)}else{Join-Path $out ('licenses/'+$f.Name)}
    Copy-Item -LiteralPath $f.FullName -Destination $to
}
$dlls=foreach($f in Get-ChildItem -LiteralPath (Join-Path $out 'payload') -Recurse -File -Filter '*.dll'){
 [pscustomobject]@{path=$f.FullName.Substring($out.Length+1).Replace('\','/');sha256=(Get-UHash $f.FullName);version=$f.VersionInfo.ProductVersion}
}
[IO.File]::WriteAllText((Join-Path $out 'BINARY_INVENTORY.json'),($dlls | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
$manifest=foreach($f in Get-ChildItem -LiteralPath $out -Recurse -File | Sort-Object FullName){(Get-UHash $f.FullName)+'  '+$f.FullName.Substring($out.Length+1).Replace('\','/')}
[IO.File]::WriteAllLines((Join-Path $out 'MANIFEST.sha256'),$manifest,[Text.UTF8Encoding]::new($false))
Write-Output "Complete package: $out"
