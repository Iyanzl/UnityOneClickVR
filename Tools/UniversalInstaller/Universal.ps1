param(
    [ValidateSet('Inspect','Install','Uninstall','Launch','Desktop','Collect','Cache')][string]$Mode='Install',
    [string]$GameExe,
    [string]$GameRoot,
    [switch]$ReplaceExistingVr,
    [switch]$ReplaceProtectedVr,
    [switch]$SkinDiagnostics,
    [switch]$Yes
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Universal.Core.ps1')
. (Join-Path $PSScriptRoot 'HybridVrPreflight.ps1')
try {
    if ($Mode -in @('Uninstall','Launch','Desktop','Collect')) {
        if (!$GameRoot) { $GameRoot=Split-Path -Parent $PSScriptRoot }
        $GameRoot=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
        if ($Mode -eq 'Uninstall') {
            Write-Host "Uninstall HybridVR and restore owned originals in: $GameRoot"
            if (!$Yes -and !(Confirm-UAction 'Continue with uninstall?')) { Write-Host 'Cancelled. No game files changed.'; exit 0 }
            $backup=Uninstall-U $GameRoot
            Write-Host "Restored originals. Backups and generated interop/logs retained: $backup"
        } elseif ($Mode -in @('Launch','Desktop')) {
            Start-UGame $GameRoot -Desktop:($Mode -eq 'Desktop') -SkinDiagnostics:$SkinDiagnostics
        } else {
            $out=Resolve-UChild $GameRoot ('_HybridVR\reports\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
            New-Item -ItemType Directory -Path $out -Force | Out-Null
            foreach ($r in @('BepInEx\LogOutput.log','BepInEx\UUVR.Il2Cpp.latest.log','BepInEx\UUVR.Il2Cpp.submit.latest.log',
                'BepInEx\HybridVR.Mono.latest.log','BepInEx\config\codex.hybrid-mono.cfg','BepInEx\config\codex.hybridvr.mono.cfg',
                'BepInEx\config\HybridVR.OpenVR.startup.json','BepInEx\config\HybridVR.OpenVR\hybridvr.vrmanifest',
                'BepInEx\config\HybridVR.OpenVR\actions.json','BepInEx\config\HybridVR.OpenVR\oculus_touch.json',
                '_HybridVR\active.json','BepInEx\config\raicuparta.uuvr-modern.cfg','BepInEx\config\codex.hybrid-il2cpp.cfg',
                'BepInEx\config\com.newunitymodder.unityvrmod.cfg','BepInEx\config\codex.unityvrmod.desktop-settings.cfg')) {
                $p=Resolve-UChild $GameRoot $r
                if (Test-Path -LiteralPath $p -PathType Leaf) {
                    $dst=Resolve-UChild $out $r
                    New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
                    Copy-Item -LiteralPath $p -Destination $dst
                }
            }
            Write-Host "Local report only, no upload: $out"
        }
        exit 0
    }
    if (!$GameExe) {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog=[Windows.Forms.OpenFileDialog]::new()
        $dialog.Title='Select the Unity game EXE / Select game, not an installer'
        $dialog.Filter='Game executable (*.exe)|*.exe'
        if ($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { exit 0 }
        $GameExe=$dialog.FileName
        $dialog.Dispose()
    }
    if ($Mode -eq 'Cache') {
        $game=Resolve-HybridGame $GameExe
        if ($game.backend -ne 'IL2CPP' -or $game.unityVersion -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw 'Select an IL2CPP Unity game with a known version.' }
        $version="$($Matches[1]).$($Matches[2]).$($Matches[3])"
        $url="https://unity.bepinex.dev/libraries/$version.zip"
        Write-Host "This downloads ONLY Unity base libraries into this package cache: $url"
        Write-Host 'The game is not started or modified. An internet connection is needed only for this step.'
        if (!$Yes -and !(Confirm-UAction 'Download?')) { exit 0 }
        $cache=Resolve-UChild $PSScriptRoot ("cache\unity-libs\$version.zip")
        New-Item -ItemType Directory -Path (Split-Path -Parent $cache) -Force | Out-Null
        $temp=$cache+'.download'
        try {
            [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing -TimeoutSec 180
            Test-UUnityZip $temp
            $catalog=Get-UCatalog $PSScriptRoot
            $known=@($catalog.unityLibraries | Where-Object version -eq $version)
            $hash=Get-UHash $temp
            if ($known.Count -and $known[0].sha256 -ne $hash) { throw 'Official download differs from the pinned cache. Review before replacing.' }
            Copy-Item -LiteralPath $temp -Destination $cache -Force
            if (!$known.Count) {
                $catalog.unityLibraries+=@([pscustomobject]@{version=$version;sha256=$hash;source=$url})
                Write-UJson (Join-Path $PSScriptRoot 'catalog.json') $catalog
            }
            Write-Host "Offline cache ready: $version. Drag the game onto Install-HybridVR.cmd next."
        } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
        exit 0
    }
    if ($ReplaceProtectedVr -and !$ReplaceExistingVr) { throw '-ReplaceProtectedVr also requires explicit -ReplaceExistingVr approval.' }
    $plan=Get-UPlan $PSScriptRoot $GameExe -ReplaceProtectedVr:$ReplaceProtectedVr
    Write-Host "Game: $($plan.game.exe)"
    Write-Host "Detected: $($plan.game.backend) x64 / Unity $($plan.unityVersion)"
    Write-Host "Selected: $($plan.component) / $($plan.xrApi) / D3D11"
    Write-Host "Rendering candidate: $($plan.rendering.selected), $($plan.rendering.status); not a verified automatic match."
    foreach ($m in $plan.messages) { Write-Host "[Keep] $m" }
    foreach ($m in $plan.warnings) { Write-Host "[Notice] $m" -ForegroundColor Yellow }
    foreach ($m in $plan.errors) { Write-Host "[Blocked] $m" -ForegroundColor Red }
    Write-Host "Planned file changes: $($plan.operations.Count). Headset compatibility: not tested."
    $reports=Join-Path $PSScriptRoot 'reports'
    New-Item -ItemType Directory -Path $reports -Force | Out-Null
    Write-UJson (Join-Path $reports 'last-preflight.json') $plan
    if (!$plan.canInstall) { throw 'Installation blocked. No game files changed. See reports/last-preflight.json.' }
    if ($Mode -eq 'Inspect') { exit 0 }
    if ($plan.existingVr -and !$ReplaceExistingVr) {
        if ($Yes) { throw 'Existing VR needs explicit -ReplaceExistingVr approval.' }
        Write-Host 'This replaces only recognized VR files and preserves settings. Backups are retained.'
        if (!(Confirm-UAction 'Back up and replace the recognized existing VR components?')) { exit 0 }
        $ReplaceExistingVr=$true
    }
    if (!$Yes -and !(Confirm-UAction 'Install with backup?')) { exit 0 }
    $result=Install-UPlan $PSScriptRoot $plan -ReplaceExistingVr:$ReplaceExistingVr
    Write-Host "Installed. Backup: $($result.backup)" -ForegroundColor Green
    Write-Host 'Use Start-VR.bat for VR, Start-Desktop.bat for desktop, or Uninstall-HybridVR.cmd to uninstall.'
    Write-Host 'Desktop mode temporarily parks owned VR plugins, keeps other mods, and restores after exit. First IL2CPP launch still generates this game own interop.'
    Write-Host 'If it fails, use Uninstall-HybridVR.cmd. Keep the backup. No game was launched automatically.'
} catch {
    Write-Host ('[HybridVR] '+$_.Exception.Message) -ForegroundColor Red
    if ($Mode -in @('Launch','Desktop')) {
        $message=$_.Exception.Message
        try {
            $errorFile=Resolve-UChild $GameRoot '_HybridVR\last-launch-error.txt'
            [IO.File]::WriteAllText($errorFile,$message,[Text.UTF8Encoding]::new($false))
            Add-Type -AssemblyName System.Windows.Forms
            [Windows.Forms.MessageBox]::Show($message,'HybridVR launch blocked') | Out-Null
        } catch { }
    }
    exit 1
}
