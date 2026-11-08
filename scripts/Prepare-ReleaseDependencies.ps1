param([string]$Destination = (Join-Path $PSScriptRoot '../dependencies/release-notices'))
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$items=@(
    @{name='BepInEx5-source.zip';url='https://codeload.github.com/BepInEx/BepInEx/zip/refs/tags/v5.4.23.5'},
    @{name='BepInEx6-source.zip';url='https://codeload.github.com/BepInEx/BepInEx/zip/6abdba47eeebe08552282e7a58ef0f4a9ab60b62'},
    @{name='Il2CppInterop-source.zip';url='https://codeload.github.com/BepInEx/Il2CppInterop/zip/dbda1cb353b0f4253345dc45136d170b9e50a5a0'},
    @{name='UnityDoorstop-source.zip';url='https://codeload.github.com/NeighTools/UnityDoorstop/zip/refs/tags/v4.5.0'},
    @{name='SteamVR-Unity-LICENSE.txt';url='https://raw.githubusercontent.com/ValveSoftware/steamvr_unity_plugin/fad02abee8ed45791993e92e420b340f63940aca/LICENSE'},
    @{name='VRGIN-LICENSE.txt';url='https://raw.githubusercontent.com/Eusth/VRGIN/8f08b27e97be67e012f985899865412eadb5a9f0/LICENSE'},
    @{name='ViGEmClient-LICENSE.txt';url='https://raw.githubusercontent.com/nefarius/ViGEm.NET/master/LICENSE'},
    @{name='NewtonsoftJson-LICENSE.txt';url='https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/13.0.3/LICENSE.md'},
    @{name='DotNet-LICENSE.txt';url='https://raw.githubusercontent.com/dotnet/runtime/0ec02c8c96e2eda06dc5b5edfdbdba0f36415082/LICENSE.TXT'},
    @{name='DotNet-THIRD-PARTY-NOTICES.txt';url='https://raw.githubusercontent.com/dotnet/runtime/0ec02c8c96e2eda06dc5b5edfdbdba0f36415082/THIRD-PARTY-NOTICES.TXT'},
    @{name='AsmResolver-LICENSE.txt';url='https://raw.githubusercontent.com/Washi1337/AsmResolver/124e161a9bc0ebb83b4d97276ae3da3c9cfe2fc5/LICENSE.md'},
    @{name='AssetRipper-CIL-LICENSE.txt';url='https://raw.githubusercontent.com/AssetRipper/AssetRipper.CIL/06216e69825ccd6536dc17e9ca81c4d539ce9321/LICENSE'},
    @{name='AssetRipper-Primitives-LICENSE.txt';url='https://raw.githubusercontent.com/AssetRipper/AssetRipper.Primitives/d759b3db98bf2a1cb8c2181faf5d405610aac585/License.md'},
    @{name='Cpp2IL-LICENSE.txt';url='https://raw.githubusercontent.com/SamboyCoding/Cpp2IL/558ddd98642010897d54316b51fbaa7889fda093/LICENSE'},
    @{name='Disarm-LICENSE.txt';url='https://raw.githubusercontent.com/SamboyCoding/Disarm/8574010015c8c56bb7faad7e407c50b0abe9cc72/LICENSE'},
    @{name='CapstoneNET-LICENSE.txt';url='https://raw.githubusercontent.com/ds5678/Capstone.NET/b90e380c14e857865c94a355954236e678bd557c/LICENSE'},
    @{name='Iced-LICENSE.txt';url='https://raw.githubusercontent.com/icedland/iced/601c40570d6c986c8e1a6a2e483dd2fdf5ade552/LICENSE.txt'},
    @{name='HarmonyX-LICENSE.txt';url='https://raw.githubusercontent.com/BepInEx/HarmonyX/v2.10.2/LICENSE'},
    @{name='MonoCecil-LICENSE.txt';url='https://raw.githubusercontent.com/jbevain/cecil/0.11.4/LICENSE.txt'},
    @{name='MonoMod-LICENSE.txt';url='https://raw.githubusercontent.com/MonoMod/MonoMod/v22.07.31.01/LICENSE'},
    @{name='Semver-LICENSE.txt';url='https://raw.githubusercontent.com/adamreeve/semver.net/master/LICENSE'},
    @{name='Dobby-LICENSE.txt';url='https://raw.githubusercontent.com/jmpews/Dobby/master/LICENSE'},
    @{name='Capstone-LICENSE.txt';url='https://raw.githubusercontent.com/capstone-engine/capstone/4.0.2/LICENSE.TXT'},
    @{name='Capstone-LLVM-LICENSE.txt';url='https://raw.githubusercontent.com/capstone-engine/capstone/4.0.2/LICENSE_LLVM.TXT'},
    @{name='UnityPluginSDK-LICENSE.md';url='https://raw.githubusercontent.com/Unity-Technologies/Graphics/master/LICENSE.md'}
)
$records=[Collections.Generic.List[object]]::new()
$failed=[Collections.Generic.List[string]]::new()
foreach($item in $items){
    $path=Join-Path $out $item.name
    try {
        if(!(Test-Path -LiteralPath $path)){ Invoke-WebRequest $item.url -OutFile $path -TimeoutSec 120 }
        if((Get-Item -LiteralPath $path).Length -eq 0){throw 'Empty download'}
        $records.Add([pscustomobject]@{file=$item.name;url=$item.url;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash})
        Write-Output ('Ready: '+$item.name)
    } catch { $failed.Add($item.name); Write-Output ('FAILED: '+$item.name+' '+$_.Exception.Message) }
}
[IO.File]::WriteAllText((Join-Path $out 'sources.json'),($records | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
if($failed.Count){throw ('Missing dependencies: '+($failed -join ', '))}
