param(
    [string]$TemporaryRoot=(Join-Path ([IO.Path]::GetTempPath()) ('UnityOneClickVR-loader-'+[Guid]::NewGuid().ToString('N'))),
    [string]$Package,
    [string]$LoaderArchive
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$installer=Join-Path $PSScriptRoot '../Tools/UniversalInstaller'
$fixture=New-Item -ItemType Directory -Path $TemporaryRoot
$packageRoot=Join-Path $fixture.FullName 'package'
$game=Join-Path $fixture.FullName 'game'
$checks=0

function Check([bool]$condition,[string]$description) {
    if (!$condition) { throw $description }
    $script:checks++
}
function Put([string]$path,[string]$value) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,$value)
}
function Snapshot {
    @(Get-ChildItem -LiteralPath $game -Recurse -File | Where-Object FullName -notlike ($game+'\_HybridVR\*') |
        Sort-Object FullName | ForEach-Object { $_.FullName.Substring($game.Length)+':'+(Get-UHash $_.FullName) })
}
function Assert-Original([string]$description) {
    Check (((Snapshot) -join "`n") -ceq ($before -join "`n")) $description
}

if ($Package) {
    Copy-Item -LiteralPath $Package -Destination $packageRoot -Recurse
} else {
    New-Item -ItemType Directory -Path $packageRoot | Out-Null
    foreach ($name in @('Universal.Core.ps1','HybridVrPreflight.ps1','Universal.ps1','Start-Skin-Diagnostics.cmd','templates')) {
        Copy-Item -LiteralPath (Join-Path $installer $name) -Destination $packageRoot -Recurse
    }
    Put (Join-Path $packageRoot 'HybridVR.Launcher.exe') 'fixture launcher; never executed'
    Put (Join-Path $packageRoot 'payload/vr.dll') 'fixture VR plugin; never executed'
    Put (Join-Path $packageRoot 'payload/loader.dll') 'fixture bundled loader; never executed'
    $catalog=@{
        schema=1; id='HybridVR-U01'; acceptedIl2CppLoaders=@(); protectedVrHashes=@(); unityLibraries=@()
        components=@(
            @{id='il2cpp-fixture';backend='IL2CPP';xrApi='OpenVR';files=@(
                @{target='BepInEx\plugins\FixtureVR.dll';source='payload/vr.dll';sha256=(Get-FileHash (Join-Path $packageRoot 'payload/vr.dll')).Hash})}
            @{id='loader-il2cpp785';files=@(
                @{target='BepInEx\core\BepInEx.Unity.IL2CPP.dll';source='payload/loader.dll';sha256=(Get-FileHash (Join-Path $packageRoot 'payload/loader.dll')).Hash})}
        )
    }
    Put (Join-Path $packageRoot 'catalog.json') ($catalog | ConvertTo-Json -Depth 8)
}
. (Join-Path $packageRoot 'Universal.Core.ps1')
. (Join-Path $packageRoot 'HybridVrPreflight.ps1')

New-Item -ItemType Directory -Path $game | Out-Null
if ($LoaderArchive) { Expand-Archive -LiteralPath $LoaderArchive -DestinationPath $game }
$exe=Join-Path $game ('LoaderReuse'+[Guid]::NewGuid().ToString('N')+'.exe')
$pe=New-Object byte[] 256
$pe[0]=0x4d;$pe[1]=0x5a;$pe[0x3c]=128;$pe[128]=0x50;$pe[129]=0x45;$pe[132]=0x64;$pe[133]=0x86
[IO.File]::WriteAllBytes($exe,$pe)
Add-Type -TypeDefinition '[assembly:System.Reflection.AssemblyFileVersion("2021.3.36.1")] public class LoaderReusePlayer {}' -OutputAssembly (Join-Path $game 'UnityPlayer.dll')
Put (Join-Path $game 'GameAssembly.dll') 'fixture, never executed'
Put (Join-Path $game ([IO.Path]::GetFileNameWithoutExtension($exe)+'_Data/il2cpp_data/Metadata/global-metadata.dat')) 'fixture metadata'
$core=Join-Path $game 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'
$proxy=Join-Path $game 'winhttp.dll'
$door=Join-Path $game 'doorstop_config.ini'
if (!$LoaderArchive) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $core) -Force | Out-Null
    Add-Type -TypeDefinition 'public class LoaderReuseAssembly {}' -OutputAssembly $core
    Put $proxy 'fixture bootstrap'
    Put $door "[General]`r`nenabled = true`r`n"
}
$coreHash=Get-UHash $core
if ($LoaderArchive) {
    Check ($coreHash -eq 'D77B1D354F74DB1602ED4DC10B4F0EC204387E483672CD5B66A65EDA223930D1') 'Official pre.2 core hash mismatch'
}
Put (Join-Path $game 'BepInEx/interop/UnityEngine.CoreModule.dll') 'fixture interop'
$interop=Join-Path $game 'BepInEx/interop/assembly-hash.txt'
Put $interop 'fixture interop hash'
Put (Join-Path $game 'BepInEx/plugins/ExistingMod.dll') 'unrelated mod'
Put (Join-Path $game 'BepInEx/config/BepInEx.cfg') "[Logging.Console]`r`nEnabled = true`r`n"
$before=Snapshot
$plan=Get-UPlan $packageRoot $exe
Check ($plan.canInstall -and $plan.unverifiedIl2CppLoader) 'Unlisted loader must warn without blocking preflight'
Check (@($plan.warnings | Where-Object { $_ -like '*not in the tested list*' -and $_ -like "*$coreHash*" }).Count -eq 1) 'Warning must identify fingerprint and untested status'
Check (@($plan.operations | Where-Object path -match '^(BepInEx\\(core|interop)\\|dotnet\\|winhttp\.dll$|doorstop_config\.ini$)').Count -eq 0) 'Plan must not replace the original loader or interop'

$entry=Join-Path $packageRoot 'Universal.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry -GameExe $exe -Yes
Check ($LASTEXITCODE -ne 0) 'Unattended install without explicit approval must fail'
Assert-Original 'Unapproved installation modified fixture files'
'n' | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry -GameExe $exe
Check ($LASTEXITCODE -eq 0) 'Decline must exit successfully'
Assert-Original 'Declining the warning modified fixture files'
'y' | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry -GameExe $exe
Check ($LASTEXITCODE -eq 0) 'Confirmed interactive reuse must install'
Check (Test-Path -LiteralPath (Join-Path $game 'Start-VR.bat') -PathType Leaf) 'VR launcher not installed'
foreach ($file in @($core,$proxy,$door,$interop,(Join-Path $game 'BepInEx/plugins/ExistingMod.dll'))) {
    Check ($before -contains ($file.Substring($game.Length)+':'+(Get-UHash $file))) "Existing file changed: $file"
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $game '_HybridVR/Universal.ps1') -Mode Uninstall -GameRoot $game -Yes
Check ($LASTEXITCODE -eq 0) 'Uninstall must restore original files'
Assert-Original 'Uninstall changed original files'

$catalog=Get-UCatalog $packageRoot
$catalog.acceptedIl2CppLoaders+=@($coreHash)
Put (Join-Path $packageRoot 'catalog.json') ($catalog | ConvertTo-Json -Depth 12)
$plan=Get-UPlan $packageRoot $exe
Check ($plan.canInstall -and !$plan.unverifiedIl2CppLoader) 'Listed loader must not require risk approval'

Remove-Item -LiteralPath $interop
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -like '*no generated interop*')) 'Missing interop must still block'
Put $interop 'fixture interop hash'
$mono=Join-Path $game 'BepInEx/core/BepInEx.dll'
Put $mono 'fixture wrong backend'
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -like '*other backend*')) 'Mixed backend must still block'
Remove-Item -LiteralPath $mono
Remove-Item -LiteralPath $door
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -like '*incomplete/custom bootstrap*')) 'Incomplete bootstrap must still block'
Put $door "[General]`r`nenabled = false`r`n"
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -like '*Doorstop is disabled*')) 'Disabled bootstrap must still block'
Put $door "[General]`r`nenabled = true`r`n"
Put $core 'not a managed assembly'
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -like '*not a readable BepInEx.Unity.IL2CPP assembly*')) 'Damaged core must block'
Remove-Item -LiteralPath $core
$plan=Get-UPlan $packageRoot $exe
Check (!$plan.canInstall -and ($plan.errors -contains 'Unknown bootstrap content will not be overwritten: winhttp.dll') -and
    ($plan.errors -contains 'Unknown bootstrap content will not be overwritten: doorstop_config.ini')) 'Orphaned bootstrap files must still block'
Write-Output "Passed $checks loader reuse checks. No game or loader was executed. Fixture: $TemporaryRoot"
