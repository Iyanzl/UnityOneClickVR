# Shared by the read-only doctor and installer. Compatible with Windows PowerShell 5.1.
function Get-HybridPeMachine([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { return 0 }
        $stream.Position = 0x3C
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset -gt $stream.Length - 6) { return 0 }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 0 }
        return $reader.ReadUInt16()
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

function Get-HybridUnityLayout([string]$Exe) {
    $root = Split-Path -Parent $Exe
    $data = Join-Path $root ([IO.Path]::GetFileNameWithoutExtension($Exe) + '_Data')
    if (!(Test-Path -LiteralPath $data -PathType Container)) { return $null }
    $player = Join-Path $root 'UnityPlayer.dll'
    $core = Join-Path $data 'Managed\UnityEngine.CoreModule.dll'
    $legacy = Join-Path $data 'Managed\UnityEngine.dll'
    if (!(Test-Path -LiteralPath $player) -and !(Test-Path -LiteralPath $core) -and !(Test-Path -LiteralPath $legacy)) { return $null }
    $il2cpp = (Test-Path -LiteralPath (Join-Path $root 'GameAssembly.dll')) -or
        (Test-Path -LiteralPath (Join-Path $data 'il2cpp_data\Metadata\global-metadata.dat'))
    $mono = (Test-Path -LiteralPath $core) -or (Test-Path -LiteralPath $legacy)
    $backend = 'Unknown'
    if ($il2cpp) { $backend = 'IL2CPP' } elseif ($mono) { $backend = 'Mono' }
    $version = $null
    if (Test-Path -LiteralPath $player) { $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($player).FileVersion }
    $machine = Get-HybridPeMachine $Exe
    $arch = 'Unknown'
    if ($machine -eq 0x8664) { $arch = 'x64' } elseif ($machine -eq 0x14c) { $arch = 'x86' }
    return [pscustomobject]@{ exe=$Exe; root=$root; data=$data; backend=$backend; architecture=$arch; unityVersion=$version }
}

function Resolve-HybridGame([string]$Exe) {
    $resolved = (Resolve-Path -LiteralPath $Exe -ErrorAction Stop).Path
    if ([IO.Path]::GetExtension($resolved) -ine '.exe' -or !(Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw 'Select a game EXE, not a directory or shortcut.'
    }
    $direct = Get-HybridUnityLayout $resolved
    if ($null -ne $direct) { return $direct }
    # Search only the launcher's own directory and two child levels. Never cross junctions.
    $folders = @((Split-Path -Parent $resolved))
    $candidates = New-Object 'System.Collections.Generic.List[object]'
    for ($depth = 0; $depth -le 2; $depth++) {
        $next = @()
        foreach ($folder in $folders) {
            foreach ($file in Get-ChildItem -LiteralPath $folder -Filter '*.exe' -File -ErrorAction Stop) {
                if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                $layout = Get-HybridUnityLayout $file.FullName
                if ($null -ne $layout) { $candidates.Add($layout) }
            }
            if ($depth -lt 2) {
                $next += @(Get-ChildItem -LiteralPath $folder -Directory -ErrorAction Stop | Where-Object {
                    !($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
                    $_.Name -notmatch '^(BepInEx|_HybridVRMod_Backup|_backups|backup|MonoBleedingEdge)$|_Data$'
                } | ForEach-Object FullName)
            }
        }
        $folders = $next
    }
    if ($candidates.Count -ne 1) {
        throw "Found $($candidates.Count) Unity executables. Select the actual EXE explicitly. Candidates: $(@($candidates | ForEach-Object exe) -join '; ')"
    }
    return $candidates[0]
}

function Read-HybridIni([string]$Path) {
    $values = @{}
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $values }
    $section = ''
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $text = $line.Trim()
        if (!$text -or $text.StartsWith('#') -or $text.StartsWith(';')) { continue }
        if ($text -match '^\[(.+)\]$') { $section = $Matches[1]; continue }
        $equals = $text.IndexOf('=')
        if ($equals -gt 0) { $values[$section + '/' + $text.Substring(0, $equals).Trim()] = $text.Substring($equals + 1).Trim() }
    }
    return $values
}

function Test-HybridPreserveCore([string]$RelativePath, [string]$TargetPath) {
    return $RelativePath.StartsWith('BepInEx\core\', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $TargetPath -PathType Leaf)
}

function Get-HybridPreflight([string]$Exe, [string]$PayloadRoot) {
    $issues = New-Object 'System.Collections.Generic.List[object]'
    $report = [ordered]@{
        schema=1; checkedAt=[DateTimeOffset]::Now.ToString('o'); requestedExe=$Exe
        game=$null; canInstall=$false; runtimeAcceptance='NotTested'; running=@()
        activeConfig=@{}; files=@(); logs=@(); issues=@()
    }
    try { $game = Resolve-HybridGame $Exe; $report.game = $game }
    catch {
        $report.issues = @([pscustomobject]@{ code='GAME_SELECTION'; severity='error'; message=$_.Exception.Message })
        return [pscustomobject]$report
    }
    if ($game.backend -ne 'Mono' -or $game.architecture -ne 'x64') {
        $issues.Add([pscustomobject]@{code='UNSUPPORTED_LOADER';severity='error';message='Release installer supports Mono x64 only. IL2CPP native rendering remains experimental; do not copy the Mono payload.'})
    }
    $bep = Join-Path $game.root 'BepInEx'
    if ((Test-Path -LiteralPath (Join-Path $bep 'core\BepInEx.Unity.IL2CPP.dll')) -or
        (Test-Path -LiteralPath (Join-Path $bep 'core\BepInEx.Core.dll'))) {
        $issues.Add([pscustomobject]@{code='BEPINEX6_PRESENT';severity='error';message='Existing BepInEx 6 must not be overwritten by the BepInEx 5 package.'})
    }
    if (Test-Path -LiteralPath (Join-Path $game.root 'Uninstall-HybridVRMod.bat')) {
        $issues.Add([pscustomobject]@{code='INSTALL_REGISTERED';severity='error';message='Installation already registered. Preserve or uninstall the previous package before reinstalling; inspection is still available.'})
    }
    $report.running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and [IO.Path]::GetFullPath($_.Path) -ieq $game.exe } catch { $false }
    } | ForEach-Object Id)
    if ($report.running.Count) {
        $issues.Add([pscustomobject]@{code='GAME_RUNNING';severity='error';message='Read-only inspection allowed; close the game before installing.'})
    }
    $config = Join-Path $bep 'config'
    $internal = Read-HybridIni (Join-Path $config 'Unity VR Mod.Internal.cfg')
    $general = Read-HybridIni (Join-Path $config 'com.newunitymodder.unityvrmod.cfg')
    $uuvr = Read-HybridIni (Join-Path $config 'raicuparta.uuvr-modern.cfg')
    $report.activeConfig = [ordered]@{
        runtimeOwner=$internal['Internal/Runtime Backend']; safeModeStartsActive=$internal['Internal/Safe Mode Starts Active']
        cameraTracking=$uuvr['Camera/Camera Tracking Mode']; preferredApi=$uuvr['General/Preferred VR APi']
        headTracking=$general['General/OpenXR Hybrid Head Tracking Mode']; capture=$general['General/OpenXR Hybrid UI Capture Mode']
        projection=$general['General/OpenXR Enable Hybrid UI Projection']
    }
    $profilePath = Join-Path $config 'HybridUnityVr.profile.json'
    if (Test-Path -LiteralPath $profilePath) {
        try {
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            if ($profile.renderingBackend -and $report.activeConfig.runtimeOwner -and $profile.renderingBackend -ine $report.activeConfig.runtimeOwner) {
                $issues.Add([pscustomobject]@{code='PROFILE_OWNER_DRIFT';severity='warning';message='Profile JSON and active Internal.cfg disagree on the runtime owner. Runtime reads CFG; JSON is not proof of the applied backend.'})
            }
        } catch { $issues.Add([pscustomobject]@{code='PROFILE_INVALID';severity='warning';message='Profile JSON could not be parsed.'}) }
    }
    $files = New-Object 'System.Collections.Generic.List[object]'
    $names = @('Uuvr.dll','Uuvr.Patcher.dll','Uuvr.OpenVR.dll','Uuvr.OpenVR.NativeBridge.dll','Uuvr.OpenVR.InputBridge.dll','Nefarius.ViGEm.Client.dll','UnityVRMod.dll','UnityVRMod.DesktopSettings.dll','UnityGraphicsHelper.dll','openxr_loader.dll','UniverseLib.Mono.dll','VrginModern.dll','QuestTouchMapper.dll','XUnity.Common.dll','XUnity.AutoTranslator.Plugin.Core.dll')
    foreach ($sub in @('plugins','core','patchers','.')) {
        $dir = Join-Path $bep $sub
        if (!(Test-Path -LiteralPath $dir)) { continue }
        $candidates = if ($sub -eq 'plugins' -or $sub -eq 'core') {
            Get-ChildItem -LiteralPath $dir -File -Recurse
        } else {
            # Do not mistake CopyToGame's stored payload for an active patcher/native library.
            Get-ChildItem -LiteralPath $dir -File
        }
        foreach ($file in $candidates | Where-Object { $names -contains $_.Name }) {
            $version = $null
            $identity = $null
            try {
                $assembly = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
                $version = $assembly.Version.ToString()
                $identity = $assembly.Name
            } catch { }
            $files.Add([pscustomobject]@{path=$file.FullName.Substring($game.root.Length + 1);name=$file.Name;version=$version;assemblyName=$identity;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
        }
    }
    $report.files = @($files.ToArray())
    foreach ($group in $files | Group-Object name | Where-Object Count -gt 1) {
        $differentHashes = @($group.Group | Select-Object -ExpandProperty sha256 -Unique).Count -gt 1
        $code = if ($differentHashes) { 'CONFLICTING_DLL' } else { 'DUPLICATE_DLL' }
        $issues.Add([pscustomobject]@{code=$code;severity='warning';message="Multiple active copies of $($group.Name); conflicting hashes=$differentHashes. Review paths before changing the runtime."})
    }
    if ($report.activeConfig.runtimeOwner -eq 'UnityVRMod' -and @($files | Where-Object name -eq 'Uuvr.dll').Count) {
        $issues.Add([pscustomobject]@{code='RUNTIME_OWNER_REVIEW';severity='warning';message='Standalone UnityVRMod selected while UUVR is installed. Confirm only one XR renderer owns the cameras before testing.'})
    }
    if ($PayloadRoot -and (Test-Path -LiteralPath (Join-Path $PayloadRoot 'BepInEx\core'))) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PayloadRoot 'BepInEx\core') -File) {
            $target = Join-Path $bep ('core\' + $file.Name)
            if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                $issues.Add([pscustomobject]@{code='CORE_PRESERVED';severity='warning';message="Existing core dependency $($file.Name) differs from payload and will be preserved, not silently downgraded."})
            }
        }
    }
    foreach ($logName in @('LogOutput.log','UUVR.Il2Cpp.latest.log')) {
        $logPath = Join-Path $bep $logName
        if (!(Test-Path -LiteralPath $logPath)) { continue }
        $log = Get-Item -LiteralPath $logPath
        $lines = @(Get-Content -LiteralPath $logPath -TotalCount 220) + @(Get-Content -LiteralPath $logPath -Tail 1200)
        # PS 5.1 attaches provider metadata to Get-Content strings. Never serialize that object graph.
        $signals = @($lines | Where-Object {$_ -match '^build=|MissingFieldException|MissingMethodException|SEHException|AccessViolationException|InvalidTexture|Automatic OpenVR start|scene camera rebound|orthographic when XR|Loading \[UUVR|Loading \[Unity VR Mod|Loading \[UnityVRMod Desktop'} | Select-Object -Unique -First 16 | ForEach-Object { [string]::Copy($_) })
        $report.logs += @([pscustomobject]@{path=$logPath;lastWriteTime=$log.LastWriteTime.ToString('o');signals=$signals})
        if (@($signals | Where-Object {$_ -match 'MissingFieldException|MissingMethodException'}).Count) {
            $issues.Add([pscustomobject]@{code='LOG_DEPENDENCY_MISMATCH';severity='warning';message='Saved log contains a missing member error. Verify DLL hashes/API compatibility, not version numbers alone. The log may be from an older run.'})
        }
        if (@($lines | Where-Object {$_ -match 'InvalidTexture'}).Count) {
            $issues.Add([pscustomobject]@{code='LOG_TEXTURE_REJECTED';severity='warning';message='Saved compositor log rejects eye textures. Queued frames do not prove successful presentation. Runtime acceptance remains untested by this tool.'})
        }
    }
    $report.issues = @($issues.ToArray())
    $report.canInstall = @($issues | Where-Object severity -eq 'error').Count -eq 0
    return [pscustomobject]$report
}
