# Windows PowerShell 5.1 compatible. No game execution during inspection/installation.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Confirm-UAction([string]$Prompt) {
    $answer=Read-Host ($Prompt+' [y/N]')
    return $null -ne $answer -and $answer.Trim() -ieq 'y'
}

function Get-UHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Assert-UNoLink([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Links/junctions are not supported: $cursor"
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
function Resolve-UChild([string]$Root, [string]$Relative) {
    if (!$Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '[:*?"<>|]' -or
        @($Relative.Split([char[]]'\/') | Where-Object { !$_ -or $_ -eq '.' -or $_ -eq '..' -or $_ -match '[. ]$|^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)' }).Count) {
        throw "Unsafe relative path: $Relative"
    }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $full = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (!$full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped root.' }
    Assert-UNoLink $full
    return $full
}
function Write-UJson([string]$Path, $Value) {
    $temporary = $Path + '.new'
    Assert-UNoLink $temporary
    Assert-UNoLink ($Path+'.previous')
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 15), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $Path) { Copy-Item -LiteralPath $Path -Destination ($Path+'.previous') -Force }
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Set-UIni([string]$Text, [string]$Section, [string]$Key, [string]$Value) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Text, '\r?\n')) { $lines.Add($line) }
    $inside = $false; $start = -1; $end = $lines.Count; $found = -1
    for ($i=0; $i -lt $lines.Count; $i++) {
        $s = $lines[$i].Trim()
        if ($s -match '^\[(.+)\]$') {
            if ($inside) { $end=$i; break }
            $inside = $Matches[1] -ieq $Section
            if ($inside) { $start=$i }
        } elseif ($inside -and !$s.StartsWith('#') -and !$s.StartsWith(';')) {
            $eq=$s.IndexOf('=')
            if ($eq -gt 0 -and $s.Substring(0,$eq).Trim() -ieq $Key) {
                if ($found -ne -1) { throw "Duplicate configuration key: [$Section] $Key" }
                $found=$i
            }
        }
    }
    $entry="$Key = $Value"
    if ($found -ge 0) { $lines[$found]=$entry }
    elseif ($start -ge 0) { $lines.Insert($end,$entry) }
    else { $lines.Add(''); $lines.Add("[$Section]"); $lines.Add($entry) }
    return ($lines -join "`r`n")
}
function Assert-UGameClosed($Game) {
    $name=[IO.Path]::GetFileNameWithoutExtension($Game.exe)
    # Same-named inaccessible processes also block. Never kill processes to install.
    if (@(Get-Process -Name $name -ErrorAction SilentlyContinue).Count) { throw "Close the game first: $name" }
}
function Test-UUnityZip([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $total=0L; $count=0; $core=$false
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            # Official base-library archives are flat, unlike a loader distribution.
            if ($entry.FullName -match '[/\\:]|^\.|[. ]$') { throw 'Invalid Unity base-library ZIP path.' }
            $total += $entry.Length; $count++
            if ($entry.Name -in @('UnityEngine.dll','UnityEngine.CoreModule.dll')) { $core=$true }
        }
        if (!$core -or $count -gt 2000 -or $total -gt 1GB) { throw 'Invalid Unity base-library archive.' }
    } finally { $zip.Dispose() }
}
function Get-UCatalog([string]$Package) {
    $catalog=Get-Content -LiteralPath (Resolve-UChild $Package 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($catalog.schema -ne 1 -or $catalog.id -ne 'HybridVR-U01') { throw 'Unsupported package catalog.' }
    foreach ($group in $catalog.components) {
        $seen=@{}
        foreach ($f in $group.files) {
            $null=Resolve-UChild $Package $f.source
            $null=Resolve-UChild $Package ($f.target.Replace('@DATA@','Game_Data'))
            if ($seen.ContainsKey($f.target) -or $f.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid catalog entry.' }
            $seen[$f.target]=$true
        }
    }
    return $catalog
}
function Save-UUnityCache([string]$Package, [string]$Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Unity base-library version.' }
    $catalog=Get-UCatalog $Package
    $known=@($catalog.unityLibraries | Where-Object version -eq $Version)
    if ($known.Count -gt 1) { throw 'Duplicate cache version in catalog.' }
    $cache=Resolve-UChild $Package ("cache\unity-libs\$Version.zip")
    if ($known.Count -eq 1 -and (Get-UHash $cache) -eq $known[0].sha256) { Test-UUnityZip $cache; return }
    $url="https://unity.bepinex.dev/libraries/$Version.zip"
    New-Item -ItemType Directory -Path (Split-Path -Parent $cache) -Force | Out-Null
    $temp=Resolve-UChild $Package ("cache\unity-libs\$Version."+[Guid]::NewGuid().ToString('N')+'.download')
    try {
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing -TimeoutSec 180
        Test-UUnityZip $temp
        $hash=Get-UHash $temp
        if ($known.Count -and $known[0].sha256 -ne $hash) { throw 'Official download differs from the pinned cache. Review before replacing.' }
        Assert-UNoLink $cache
        Copy-Item -LiteralPath $temp -Destination $cache -Force
        if ((Get-UHash $cache) -ne $hash) { throw 'Cache write verification failed.' }
        if (!$known.Count) {
            $catalog.unityLibraries+=@([pscustomobject]@{version=$Version;sha256=$hash;source=$url})
            Write-UJson (Resolve-UChild $Package 'catalog.json') $catalog
        }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}
function Get-UFiles([string]$Root) {
    if (!(Test-Path -LiteralPath $Root)) { return }
    Assert-UNoLink $Root
    # Check each directory before recursion, including links in unrelated mod folders.
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked game content requires manual review: $($item.FullName)" }
        if ($item.PSIsContainer) { Get-UFiles $item.FullName } else { $item }
    }
}
function Get-URendererAssessment($Game, $Component) {
    $manual=$Component.id -eq 'mono-m02' -or $Component.id -like 'il2cpp-*'
    $hints=[Collections.Generic.List[string]]::new()
    foreach ($name in @('Unity.RenderPipelines.Universal.Runtime.dll','Unity.RenderPipelines.HighDefinition.Runtime.dll')) {
        if (Test-Path -LiteralPath (Join-Path $Game.data ('Managed\'+$name))) { $hints.Add($name) }
    }
    $risks=@(Get-UFiles (Join-Path $Game.data 'Plugins') | Where-Object { $_.Name -eq 'ZibraLiquidsNative_Win.dll' } |
        ForEach-Object { [pscustomobject]@{kind='native-camera-callback';path=$_.FullName.Substring($Game.root.Length+1);sha256=(Get-UHash $_.FullName)} })
    $oldCamera=Read-HybridIni (Join-Path $Game.root 'BepInEx\config\raicuparta.uuvr-modern.cfg')
    $oldUi=Read-HybridIni (Join-Path $Game.root 'BepInEx\config\com.newunitymodder.unityvrmod.cfg')
    $player=Join-Path $Game.root 'UnityPlayer.dll'
    return [pscustomobject]@{
        schema=1; status=$(if($manual -and $risks.Count){'review-required'}else{'candidate-unverified'})
        selected=$(if($manual){'manual-openvr-original-camera'}else{'legacy-unityxr'})
        selectionReason='Only the renderer implemented by this package component is selectable; Unity version alone does not prove visual compatibility.'
        component=$Component.id; loader=$Game.backend; graphicsRequirement='Direct3D11'
        executableVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($Game.exe).FileVersion
        playerVersion=$(if(Test-Path -LiteralPath $player){[Diagnostics.FileVersionInfo]::GetVersionInfo($player).FileVersion}else{$null})
        activePipeline='unknown-until-runtime'; pipelineAssemblyHints=$hints.ToArray(); nativeRisks=$risks
        legacy=[pscustomobject]@{camera=$oldCamera['Camera/Camera Tracking Mode'];capture=$oldUi['General/OpenXR Hybrid UI Capture Mode'];
            tracking=$oldUi['General/OpenXR Hybrid Head Tracking Mode'];settingsMigrated=$false}
        trueUiOnlyAvailable=(!$manual); transparentUiVerified=$false
        uiCaptureAssessment='Runtime capability test required; a known uGUI adapter is not proof that all UI systems in a scene are covered.'
        sharedInteractionImplemented=$manual
        nativeFallbackRequired=($manual -and $risks.Count -gt 0)
        automaticFallbackAvailable=$false; headsetVerified=$false
        requiredChecks=@('runtime-init','active-controller-actions','scene-camera-rebind','stereo-submit','panel-and-ray','human-visual-confirmation')
    }
}
function Get-UPlan([string]$Package, [string]$GameExe, [switch]$ReplaceProtectedVr) {
    $catalog=Get-UCatalog $Package
    Assert-UNoLink $GameExe
    $game=Resolve-HybridGame $GameExe
    Assert-UNoLink $game.root
    Assert-UGameClosed $game
    if ($game.architecture -ne 'x64' -or $game.backend -notin @('Mono','IL2CPP')) { throw 'U01 supports Windows Unity x64 Mono/IL2CPP only.' }
    if (!$game.unityVersion -or $game.unityVersion -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw 'Cannot determine Unity version; no files changed.' }
    $version="$($Matches[1]).$($Matches[2]).$($Matches[3])"; $major=[int]$Matches[1]
    if ($major -lt 2019 -or $major -gt 6000 -or ($game.backend -eq 'IL2CPP' -and $major -lt 2020)) { throw "Unity $version is outside the U01 experimental range." }
    if (Test-Path -LiteralPath (Join-Path $game.root '_HybridVR\active.json')) { throw 'This game already has a U01 transaction. Uninstall it before reinstalling.' }
    $messages=[Collections.Generic.List[string]]::new()
    $warnings=[Collections.Generic.List[string]]::new()
    $errors=[Collections.Generic.List[string]]::new()
    $ops=[Collections.Generic.List[object]]::new(); $targets=@{}
    $dataName=Split-Path -Leaf $game.data
    $runtimeId=if($game.backend -eq 'Mono'){'mono-*'}else{'il2cpp-*'}
    $runtimes=@($catalog.components | Where-Object { $_.id -like $runtimeId -and $_.backend -eq $game.backend })
    if ($runtimes.Count -ne 1) { throw 'Expected one matching runtime in package.' }
    $component=$runtimes[0]
    $rendering=Get-URendererAssessment $game $component
    $warnings.Add("Renderer assessment: $($rendering.selected), $($rendering.status). Static inspection cannot validate pictures, callbacks or controller bindings.")
    if ($rendering.status -eq 'review-required') {
        $errors.Add('Known native camera-effect risk (Zibra Liquids) with manual original-camera rendering. Preserve the existing MOD; this candidate has no validated safe renderer fallback yet.')
    }
    if ($rendering.pipelineAssemblyHints.Count) { $warnings.Add('SRP assemblies detected; their presence does not prove the active render pipeline. Runtime verification is required.') }
    if ($rendering.legacy.camera -or $rendering.legacy.capture -or $rendering.legacy.tracking) {
        $warnings.Add('Legacy camera/capture settings preserved as evidence, not automatically translated into the new renderer. The preflight report records them separately.')
    }
    if ($ReplaceProtectedVr -and $component.id -ne 'mono-m02') { throw 'Protected runtime replacement is only available for the shared Mono candidate.' }
    $loader=@($catalog.components | Where-Object { $_.id -eq $(if($game.backend -eq 'Mono'){'loader-mono5'}else{'loader-il2cpp785'}) })[0]
    $isMono=Test-Path -LiteralPath (Join-Path $game.root 'BepInEx\core\BepInEx.dll')
    $isCpp=Test-Path -LiteralPath (Join-Path $game.root 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')
    if (($game.backend -eq 'Mono' -and $isCpp) -or ($game.backend -eq 'IL2CPP' -and $isMono)) { $errors.Add('Existing BepInEx belongs to the other backend; do not mix loaders.') }
    $existingLoader=$isMono -or $isCpp
    $loaderKnown=$false
    $unverifiedIl2CppLoader=$false
    if ($existingLoader) {
        if ($game.backend -eq 'Mono') {
            try { $loaderKnown=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $game.root 'BepInEx\core\BepInEx.dll')).Version.Major -eq 5 } catch { }
        } else {
            $loaderHash=Get-UHash (Join-Path $game.root 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')
            $loaderKnown=$catalog.acceptedIl2CppLoaders -contains $loaderHash
        }
        if (!$loaderKnown) {
            if ($game.backend -eq 'IL2CPP' -and $isCpp -and !$isMono) {
                $assembly=$null
                try { $assembly=[Reflection.AssemblyName]::GetAssemblyName((Resolve-UChild $game.root 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')) } catch { }
                if (!$assembly -or $assembly.Name -ne 'BepInEx.Unity.IL2CPP') {
                    $errors.Add('Existing IL2CPP loader is not a readable BepInEx.Unity.IL2CPP assembly; repair the original installation first.')
                } else {
                    $unverifiedIl2CppLoader=$true
                    $warnings.Add("Existing IL2CPP BepInEx (assembly version $($assembly.Version), SHA-256 $loaderHash) is not in the tested list. It will be reused without replacement; VR compatibility is not guaranteed.")
                }
            } else { $errors.Add('Unreviewed existing loader version. It was preserved; U01 refuses an automatic upgrade.') }
        }
        $messages.Add('Existing loader/core and other mods are preserved; no loader downgrade or upgrade.')
        if (!(Test-Path -LiteralPath (Join-Path $game.root 'doorstop_config.ini') -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $game.root 'winhttp.dll') -PathType Leaf)) { $errors.Add('Existing loader has an incomplete/custom bootstrap; manual review required.') }
        else {
            $door=Read-HybridIni (Join-Path $game.root 'doorstop_config.ini')
            if ($door['General/enabled'] -ine 'true') { $errors.Add('Existing Doorstop is disabled. Enable/review it before installation.') }
        }
    } else {
        foreach ($p in @('winhttp.dll','doorstop_config.ini','BepInEx\core','dotnet')) {
            $existing=Join-Path $game.root $p
            if ((Test-Path -LiteralPath $existing -PathType Leaf) -or @(Get-UFiles $existing).Count) { $errors.Add("Unknown bootstrap content will not be overwritten: $p") }
        }
    }
    $add = {
        param([string]$Relative,[string]$Source,$Hash,[string]$Action='write',$Text=$null)
        $path=Resolve-UChild $game.root $Relative
        if ($targets.ContainsKey($Relative)) { throw "Duplicate transaction target: $Relative" }
        $targets[$Relative]=$true
        $before=Get-UHash $path
        if ($Action -eq 'write' -and $before -eq $Hash) { return }
        $ops.Add([pscustomobject]@{path=$Relative;source=$Source;action=$Action;beforeHash=$before;afterHash=$Hash;text=$Text})
    }
    $groups=@($component)
    if (!$existingLoader) { $groups+=@($loader) }
    foreach ($g in $groups) {
        foreach ($f in $g.files) {
            $relative=$f.target.Replace('@DATA@',$dataName)
            $source=Resolve-UChild $Package $f.source
            if ((Get-UHash $source) -ne $f.sha256) { throw "Package integrity mismatch: $($f.source)" }
            $path=Resolve-UChild $game.root $relative
            if ($relative.StartsWith('BepInEx\config\') -and (Test-Path -LiteralPath $path)) { $messages.Add("Preserve existing settings: $relative"); continue }
            if ($existingLoader -and $relative.StartsWith('BepInEx\core\') -and (Test-Path -LiteralPath $path)) { continue }
            & $add $relative $source $f.sha256
        }
    }
    $vrNames=@('Uuvr.dll','Uuvr.Patcher.dll','Uuvr.OpenVR.dll','Uuvr.OpenVR.InputBridge.dll','Uuvr.OpenVR.NativeBridge.dll',
        'Uuvr.XR.OpenXR.dll','Uuvr.XR.OpenVR.dll','Uuvr.XR.Management.dll','UnityVRMod.dll','UnityVRMod.DesktopSettings.dll',
        'VrginModern.dll','QuestTouchMapper.dll','QuestTouchInspector.dll','HybridVR.Mono.Observer.dll','HybridVR.Mono.Runtime.dll')
    $existingVr=@($ops | Where-Object { $_.beforeHash -and $_.path -match '(^|\\)(openvr_api|openxr_loader|UnityOpenXR|XRSDKOpenVR|OVRPlugin)\.dll$' }).Count -gt 0
    foreach ($sub in @('BepInEx\plugins','BepInEx\patchers')) {
        foreach ($f in @(Get-UFiles (Join-Path $game.root $sub))) {
            if ($vrNames -notcontains $f.Name) { continue }
            if ($catalog.protectedVrHashes -contains (Get-UHash $f.FullName)) {
                if ($ReplaceProtectedVr) { $warnings.Add('Explicit protected-runtime compatibility test: the accepted dedicated MOD will be backed up and replaced, not merged. Use uninstall to restore it.') }
                else { $errors.Add('Accepted SiH-specific runtime found. This installation is protected; a reviewed Mono M02 test requires -ReplaceProtectedVr and -ReplaceExistingVr.') }
            }
            $existingVr=$true
            $r=$f.FullName.Substring($game.root.Length+1)
            $desired=@($component.files | Where-Object { $_.target.Replace('@DATA@',$dataName) -ieq $r })
            if ($desired.Count -eq 0) { & $add $r $null $null 'remove' }
        }
    }
    # Eager, journalled XR deployment replaces the old per-launch file-copy patcher.
    if ($game.backend -eq 'Mono') {
        foreach ($f in @(Get-UFiles (Join-Path $game.data 'Plugins'))) {
            if ($f.Name -notin @('openvr_api.dll','openxr_loader.dll','UnityOpenXR.dll','XRSDKOpenVR.dll','OVRPlugin.dll')) { continue }
            $existingVr=$true
            $r=$f.FullName.Substring($game.root.Length+1)
            $desired=@($component.files | Where-Object { $_.target.Replace('@DATA@',$dataName) -ieq $r })
            if ($desired.Count -eq 0) { & $add $r $null $null 'remove'; $existingVr=$true }
        }
    }
    if ($existingVr) { $warnings.Add('Existing VR components detected. Explicit -ReplaceExistingVr approval is required; old files/configs are backed up.') }
    foreach ($p in @('d3d11.dll','dxgi.dll')) {
        if (Test-Path -LiteralPath (Join-Path $game.root $p)) { $warnings.Add("Graphics proxy preserved: $p. ReShade/3Dmigoto compatibility is not guaranteed.") }
    }
    if ($game.backend -eq 'IL2CPP') {
        $meta=Join-Path $game.data 'il2cpp_data\Metadata\global-metadata.dat'
        if (!(Test-Path -LiteralPath $meta)) { $errors.Add('IL2CPP metadata is missing/protected; generic generation is unavailable.') }
        $interopReady=(Test-Path -LiteralPath (Join-Path $game.root 'BepInEx\interop\UnityEngine.CoreModule.dll') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $game.root 'BepInEx\interop\assembly-hash.txt') -PathType Leaf)
        $newLoaderHash=@($loader.files | Where-Object target -eq 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')[0].sha256
        $localZipSupported=(!$existingLoader) -or ((Get-UHash (Join-Path $game.root 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')) -eq $newLoaderHash)
        if ($localZipSupported) {
            $cache=Join-Path $Package ("cache\unity-libs\$version.zip")
            $cacheRecord=@($catalog.unityLibraries | Where-Object version -eq $version)
            if ($cacheRecord.Count -ne 1 -or !(Test-Path -LiteralPath $cache)) {
                $errors.Add("Offline base libraries missing for $version. Drag the EXE onto Prepare-Offline-Dependencies.cmd first (one approved download).")
            } elseif ((Get-UHash $cache) -ne $cacheRecord[0].sha256) { $errors.Add('Offline cache checksum mismatch.') }
            else {
                Test-UUnityZip $cache
                & $add ("BepInEx\unity-libs\$version.zip") $cache $cacheRecord[0].sha256
            }
        } elseif (!$interopReady) { $errors.Add('Existing older loader has no generated interop. First generation must be prepared separately; it will not be upgraded silently.') }
        else { $warnings.Add('Existing older loader/interop retained. U01 does not change its network policy; offline first-generation requires the 785 local-ZIP adapter.') }
    }
    $bepRelative='BepInEx\config\BepInEx.cfg'
    $bepPath=Resolve-UChild $game.root $bepRelative
    $cfg=if(Test-Path -LiteralPath $bepPath){[IO.File]::ReadAllText($bepPath)}else{''}
    $cfg=Set-UIni $cfg 'Logging.Console' 'Enabled' 'false'
    if ($game.backend -eq 'IL2CPP' -and $localZipSupported) {
        $cfg=Set-UIni $cfg 'IL2CPP' 'UnityBaseLibrariesSource' '{VERSION}.zip'
        # Do not disable updates: a game update must regenerate its own interop.
    }
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($cfg)
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $cfgHash=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','') } finally { $sha.Dispose() }
    & $add $bepRelative $null $cfgHash 'write' $cfg
    $warnings.Add('Automatic choice is a conservative backend default, not proof of correct cameras or UI. Runtime menu and manual compatibility settings remain available.')
    if ($game.backend -eq 'IL2CPP') { $warnings.Add('IL2CPP: full-frame panel is the safe default. Optional UiOnly support depends on runtime and UI capabilities; do not copy Mono capture DLLs. Use Start-VR.bat for D3D11; D3D12 is unsupported.') }
    elseif ($component.id -eq 'mono-m02') { $warnings.Add('Mono shared runtime: OpenVR operations/menu, follow cameras and hands. Requires D3D11 and SteamVR. Optional UiOnly depends on runtime/UI capabilities; old OpenXR/UUVR components are disabled, not co-loaded. Headset compatibility still needs validation.') }
    else { $warnings.Add('Mono C108: legacy settings/interaction. IL2CPP follow-camera features and the new menu are not yet unified. SummerInHeat SiH installations should remain on their accepted dedicated baseline.') }
    $readGuards=[Collections.Generic.List[object]]::new()
    foreach ($r in @((Split-Path -Leaf $game.exe),'UnityPlayer.dll','GameAssembly.dll','winhttp.dll','doorstop_config.ini','BepInEx\core\BepInEx.dll','BepInEx\core\BepInEx.Unity.IL2CPP.dll')) {
        $p=Resolve-UChild $game.root $r
        $readGuards.Add([pscustomobject]@{path=$r;sha256=(Get-UHash $p)})
    }
    # Include byte-identical existing runtime files, not just changed transaction entries.
    $vrPlugins=@($component.files | Where-Object { $_.target -match '^BepInEx\\(plugins|patchers)\\.+\.dll$' } |
        ForEach-Object { [pscustomobject]@{path=$_.target;sha256=$_.sha256} })
    return [pscustomobject]@{schema=1;package=$catalog.id;game=$game;unityVersion=$version;component=$component.id;xrApi=$component.xrApi;vrPlugins=$vrPlugins;rendering=$rendering;
        existingLoader=$existingLoader;unverifiedIl2CppLoader=$unverifiedIl2CppLoader;existingVr=$existingVr;protectedVrReplacement=[bool]$ReplaceProtectedVr;messages=$messages.ToArray();warnings=$warnings.ToArray();errors=$errors.ToArray();
        canInstall=($errors.Count -eq 0);operations=$ops.ToArray();guards=$readGuards.ToArray();headsetVerified=$false}
}

function Install-UPlan([string]$Package, $Plan, [switch]$ReplaceExistingVr, [switch]$AllowUnverifiedIl2CppLoader) {
    if (!$Plan.canInstall) { throw ($Plan.errors -join "`n") }
    if ($Plan.unverifiedIl2CppLoader -and !$AllowUnverifiedIl2CppLoader) { throw 'Reusing an unverified IL2CPP loader requires explicit approval.' }
    if ($Plan.existingVr -and !$ReplaceExistingVr) { throw 'Existing VR installation preserved. Rerun with explicit -ReplaceExistingVr only after reviewing the report.' }
    Assert-UGameClosed $Plan.game
    $root=$Plan.game.root
    $state=Resolve-UChild $root '_HybridVR\active.json'
    if (Test-Path -LiteralPath $state) { throw 'Installation already registered.' }
    foreach ($g in $Plan.guards) { if ((Get-UHash (Resolve-UChild $root $g.path)) -ne $g.sha256) { throw "Game changed since inspection: $($g.path)" } }
    foreach ($op in $Plan.operations) {
        if ((Get-UHash (Resolve-UChild $root $op.path)) -ne $op.beforeHash) { throw "Target changed since inspection: $($op.path)" }
        if ($op.source -and (Get-UHash $op.source) -ne $op.afterHash) { throw 'Package changed since inspection.' }
    }
    $id=Get-Date -Format 'yyyyMMdd-HHmmss-ffff'
    $relativeBackup="_HybridVR\backups\$id"
    $backup=Resolve-UChild $root $relativeBackup
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $journal=[pscustomobject]@{schema=1;package=$Plan.package;status='prepared';gameExe=(Split-Path -Leaf $Plan.game.exe);
        backend=$Plan.game.backend;component=$Plan.component;backup=$relativeBackup;entries=@();vrPlugins=@($Plan.vrPlugins);headsetVerified=$false}
    $all=[Collections.Generic.List[object]]::new()
    foreach ($op in $Plan.operations) { $all.Add($op) }
    foreach ($r in @('Universal.Core.ps1','HybridVrPreflight.ps1','Universal.ps1','Start-Skin-Diagnostics.cmd','HybridVR.Launcher.exe')) {
        $source=Resolve-UChild $Package $r
        $target='_HybridVR\'+$r
        $all.Add([pscustomobject]@{path=$target;source=$source;action='write';beforeHash=(Get-UHash (Resolve-UChild $root $target));afterHash=(Get-UHash $source);text=$null})
    }
    foreach ($name in @('Uninstall-HybridVR.cmd','Start-VR.bat','Start-Desktop.bat','Collect-HybridVR-Logs.cmd')) {
        $source=Resolve-UChild $Package ('templates\'+$name)
        $all.Add([pscustomobject]@{path=$name;source=$source;action='write';beforeHash=(Get-UHash (Resolve-UChild $root $name));afterHash=(Get-UHash $source);text=$null})
    }
    # Back up ALL originals before the first active-file mutation.
    foreach ($op in $all) {
        if ($op.beforeHash) {
            $dst=Resolve-UChild $backup ('original\'+$op.path)
            New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
            Copy-Item -LiteralPath (Resolve-UChild $root $op.path) -Destination $dst
            if ((Get-UHash $dst) -ne $op.beforeHash) { throw 'Backup validation failed; active files untouched.' }
        }
        if ($op.action -eq 'write') {
            $prepared=Resolve-UChild $backup ('prepared\'+$op.path)
            New-Item -ItemType Directory -Path (Split-Path -Parent $prepared) -Force | Out-Null
            if ($null -ne $op.text) { [IO.File]::WriteAllText($prepared,$op.text,[Text.UTF8Encoding]::new($false)) }
            else { Copy-Item -LiteralPath $op.source -Destination $prepared }
            if ((Get-UHash $prepared) -ne $op.afterHash) { throw "Staging verification failed: $($op.path); active files untouched." }
        }
        $journal.entries+=@([pscustomobject]@{path=$op.path;action=$op.action;beforeHash=$op.beforeHash;afterHash=$op.afterHash})
    }
    $journalPath=Join-Path $backup 'transaction.json'
    Write-UJson (Join-Path $backup 'preflight.json') $Plan
    Write-UJson $journalPath $journal
    Write-UJson $state ([pscustomobject]@{schema=1;transaction=($relativeBackup+'\transaction.json')})
    try {
        foreach ($op in $all) {
            $dst=Resolve-UChild $root $op.path
            # Recheck on each write. Never silently overwrite a concurrent user edit.
            if ((Get-UHash $dst) -ne $op.beforeHash) { throw "Concurrent edit: $($op.path)" }
            if ($op.action -eq 'remove') { Remove-Item -LiteralPath $dst -Force }
            else {
                New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
                $prepared=Resolve-UChild $backup ('prepared\'+$op.path)
                Move-Item -LiteralPath $prepared -Destination $dst -Force
            }
            if ((Get-UHash $dst) -ne $op.afterHash) { throw "Write verification failed: $($op.path)" }
        }
        $journal.status='installed-unverified'; Write-UJson $journalPath $journal
        return $journal
    } catch {
        $journal.status='install-failed'; Write-UJson $journalPath $journal
        # Recovery is explicit and journalled. Uninstall accepts partially applied operations.
        throw "Install stopped; originals are safe at $backup. Run package Universal.ps1 -Mode Uninstall -GameRoot `"$root`". Error: $($_.Exception.Message)"
    }
}
function Get-UTransaction([string]$Root) {
    $active=Get-Content -LiteralPath (Resolve-UChild $Root '_HybridVR\active.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($active.schema -ne 1 -or $active.transaction -notmatch '^_HybridVR\\backups\\[0-9-]+\\transaction.json$') { throw 'Invalid transaction reference.' }
    $path=Resolve-UChild $Root $active.transaction
    $j=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($j.schema -ne 1 -or $j.package -ne 'HybridVR-U01' -or ($j.backup+'\transaction.json') -cne $active.transaction) { throw 'Invalid transaction.' }
    $null=Resolve-UChild $Root $j.gameExe
    if ($j.gameExe -match '[/\\]' -or !($j.gameExe.EndsWith('.exe',[StringComparison]::OrdinalIgnoreCase))) { throw 'Invalid game executable.' }
    $seen=@{}
    foreach ($e in $j.entries) {
        $null=Resolve-UChild $Root $e.path
        if ($seen.ContainsKey($e.path) -or $e.action -notin @('write','remove')) { throw 'Invalid journal operation.' }
        $seen[$e.path]=$true
    }
    return $j
}
function Enter-ULaunchLock([string]$Root) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $id=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($Root).TrimEnd('\').ToUpperInvariant())))).Replace('-','') }
    finally { $sha.Dispose() }
    $mutex=[Threading.Mutex]::new($false,('Local\HybridVR.Launch.'+$id))
    $owned=$false
    try {
        try { $owned=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned=$true }
        if (!$owned) { throw 'Another desktop/VR launch or restore is active for this game.' }
        return $mutex
    } catch { $mutex.Dispose(); throw }
}
function Get-UVrPlugins($Journal) {
    if (!$Journal.PSObject.Properties['vrPlugins'] -or @($Journal.vrPlugins).Count -eq 0) {
        throw 'This installation predates separate desktop launch. Update the installer helpers before using this launcher.'
    }
    $seen=@{}
    foreach ($p in $Journal.vrPlugins) {
        if ($p.path -notmatch '^BepInEx\\(plugins|patchers)\\.+\.dll$' -or $p.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $seen.ContainsKey($p.path)) { throw 'Invalid VR plugin ownership list.' }
        $seen[$p.path]=$true
    }
    return @($Journal.vrPlugins)
}
function Restore-UDesktopSession([string]$Root, $Journal) {
    $state=Resolve-UChild $Root '_HybridVR\desktop-session.json'
    if (!(Test-Path -LiteralPath $state)) { return }
    Assert-UGameClosed ([pscustomobject]@{exe=(Resolve-UChild $Root $Journal.gameExe)})
    $s=Get-Content -LiteralPath $state -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($s.schema -ne 1 -or $s.installBackup -cne $Journal.backup -or $s.directory -notmatch '^_HybridVR\\desktop-sessions\\[a-f0-9]{32}$') { throw 'Invalid desktop recovery journal.' }
    $allowed=@{}; foreach($p in @(Get-UVrPlugins $Journal)) { $allowed[$p.path]=$p.sha256 }
    $seen=@{}
    foreach ($p in $s.files) {
        if (!$allowed.ContainsKey($p.path) -or $allowed[$p.path] -ne $p.sha256 -or $seen.ContainsKey($p.path)) { throw 'Invalid desktop recovery entry.' }
        $seen[$p.path]=$true
        $active=Resolve-UChild $Root $p.path
        $parked=Resolve-UChild $Root ($s.directory+'\'+$p.path)
        $a=Get-UHash $active; $b=Get-UHash $parked
        if (($a -and $b) -or (!$a -and !$b) -or ($a -and $a -ne $p.sha256) -or ($b -and $b -ne $p.sha256)) {
            throw "Desktop restore conflict; files preserved: $($p.path)"
        }
    }
    if ($seen.Count -ne $allowed.Count) { throw 'Incomplete desktop recovery journal.' }
    foreach ($p in $s.files) {
        $active=Resolve-UChild $Root $p.path
        $parked=Resolve-UChild $Root ($s.directory+'\'+$p.path)
        if (Test-Path -LiteralPath $parked -PathType Leaf) {
            if ((Get-UHash $parked) -ne $p.sha256 -or (Test-Path -LiteralPath $active)) { throw 'Concurrent edit during desktop recovery.' }
            New-Item -ItemType Directory -Path (Split-Path -Parent $active) -Force | Out-Null
            Move-Item -LiteralPath $parked -Destination $active
        }
        if ((Get-UHash $active) -ne $p.sha256) { throw 'Desktop restore verification failed.' }
    }
    Remove-Item -LiteralPath $state
}
function Suspend-UVrPlugins([string]$Root, $Journal) {
    Assert-UGameClosed ([pscustomobject]@{exe=(Resolve-UChild $Root $Journal.gameExe)})
    $state=Resolve-UChild $Root '_HybridVR\desktop-session.json'
    if (Test-Path -LiteralPath $state) { throw 'Recover the previous desktop session first.' }
    $plugins=@(Get-UVrPlugins $Journal)
    foreach ($p in $plugins) {
        if ((Get-UHash (Resolve-UChild $Root $p.path)) -ne $p.sha256) { throw "Changed VR plugin preserved: $($p.path)" }
    }
    $s=[pscustomobject]@{schema=1;installBackup=$Journal.backup;directory=('_HybridVR\desktop-sessions\'+[guid]::NewGuid().ToString('N'));files=$plugins}
    # A durable complete move plan allows recovery after interruption at any individual file.
    Write-UJson $state $s
    foreach ($p in $plugins) {
        $active=Resolve-UChild $Root $p.path
        $parked=Resolve-UChild $Root ($s.directory+'\'+$p.path)
        if ((Get-UHash $active) -ne $p.sha256 -or (Test-Path -LiteralPath $parked)) { throw 'Concurrent edit while parking VR plugins.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $parked) -Force | Out-Null
        Move-Item -LiteralPath $active -Destination $parked
        if ((Get-UHash $parked) -ne $p.sha256) { throw 'Desktop parking verification failed.' }
    }
}
function Start-UGame([string]$Root, [switch]$Desktop, [switch]$SkinDiagnostics) {
    if ($Desktop -and $SkinDiagnostics) { throw 'Skin diagnostics requires a VR launch.' }
    $mutex=Enter-ULaunchLock $Root
    try {
        $j=Get-UTransaction $Root
        if ($j.status -ne 'installed-unverified') { throw 'Installation is incomplete; restore before launching.' }
        $exe=Resolve-UChild $Root $j.gameExe
        Assert-UGameClosed ([pscustomobject]@{exe=$exe})
        Restore-UDesktopSession $Root $j
        foreach ($e in $j.entries | Where-Object { $_.path.EndsWith('.dll') }) {
            if ((Get-UHash (Resolve-UChild $Root $e.path)) -ne $e.afterHash) { throw "Installed DLL changed: $($e.path)" }
        }
        foreach ($p in @(Get-UVrPlugins $j)) {
            if ((Get-UHash (Resolve-UChild $Root $p.path)) -ne $p.sha256) { throw "Runtime DLL changed: $($p.path)" }
        }
        try {
            if ($Desktop) { Suspend-UVrPlugins $Root $j }
            $arguments=@{FilePath=$exe;WorkingDirectory=$Root;WindowStyle='Normal';PassThru=$true}
            if (!$Desktop) { $arguments.ArgumentList=if($SkinDiagnostics){'-force-d3d11 --hybrid-skin-diagnostics'}else{'-force-d3d11'} }
            # The user requested the interactive game; only the supervising PowerShell is hidden.
            $process=Start-Process @arguments
            try { $process.WaitForExit() } finally { $process.Dispose() }
        } finally {
            if ($Desktop) { Restore-UDesktopSession $Root $j }
        }
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
function Uninstall-U([string]$Root) {
    $mutex=Enter-ULaunchLock $Root
    try { return Uninstall-UCore $Root }
    finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
function Uninstall-UCore([string]$Root) {
    $root=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    $j=Get-UTransaction $root
    Assert-UGameClosed ([pscustomobject]@{exe=(Resolve-UChild $root $j.gameExe)})
    Restore-UDesktopSession $root $j
    $backup=Resolve-UChild $root $j.backup
    # Validate the complete restore BEFORE deleting/replacing any active file.
    foreach ($e in $j.entries) {
        if ($e.beforeHash -and (Get-UHash (Resolve-UChild $backup ('original\'+$e.path))) -ne $e.beforeHash) { throw "Missing/corrupt original: $($e.path)" }
        $current=Get-UHash (Resolve-UChild $root $e.path)
        if ($current -ne $e.afterHash -and $current -ne $e.beforeHash -and $e.path -notmatch '^BepInEx\\config\\') {
            throw "Changed file preserved; uninstall paused: $($e.path). Restore its installed bytes or review manually."
        }
    }
    $entries=@($j.entries); [array]::Reverse($entries)
    foreach ($e in $entries) {
        $dst=Resolve-UChild $root $e.path; $current=Get-UHash $dst
        if ($current -eq $e.beforeHash) { continue }
        if ($current -and $current -ne $e.afterHash) {
            $saved=Resolve-UChild $backup ('user-settings\'+$e.path)
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $dst -Destination $saved -Force
        }
        if ($e.beforeHash) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
            Copy-Item -LiteralPath (Resolve-UChild $backup ('original\'+$e.path)) -Destination $dst -Force
        } elseif (Test-Path -LiteralPath $dst -PathType Leaf) { Remove-Item -LiteralPath $dst -Force }
        if ((Get-UHash $dst) -ne $e.beforeHash) { throw "Restore validation failed: $($e.path)" }
    }
    $j.status='restored'; Write-UJson (Join-Path $backup 'transaction.json') $j
    Remove-Item -LiteralPath (Resolve-UChild $root '_HybridVR\active.json') -Force
    return $backup
}
