param([string]$TemporaryRoot=(Join-Path ([IO.Path]::GetTempPath()) ('UnityOneClickVR-cache-'+[Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../Tools/UniversalInstaller/Universal.Core.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $TemporaryRoot | Out-Null
$catalog=@{schema=1;id='HybridVR-U01';components=@();unityLibraries=@()}
[IO.File]::WriteAllText((Join-Path $TemporaryRoot 'catalog.json'),($catalog | ConvertTo-Json))
$input=Join-Path $TemporaryRoot 'input'
New-Item -ItemType Directory -Path $input | Out-Null
[IO.File]::WriteAllText((Join-Path $input 'UnityEngine.CoreModule.dll'),'fixture only, never executed')
$script:download=Join-Path $TemporaryRoot 'fixture.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($input,$script:download)
$script:calls=0
function Invoke-WebRequest {
    param($Uri,$OutFile,[switch]$UseBasicParsing,$TimeoutSec)
    if($Uri -ne 'https://unity.bepinex.dev/libraries/2021.3.36.zip'){throw 'Unexpected network destination'}
    $script:calls++
    Copy-Item -LiteralPath $script:download -Destination $OutFile
}
function Check([bool]$value,[string]$name){if(!$value){throw $name};$script:checks++}
$script:checks=0
$rejected=$false;try{Save-UUnityCache $TemporaryRoot '../escape'}catch{$rejected=$true}
Check $rejected 'Unsafe version rejected'
Check ($script:calls -eq 0) 'Unsafe version never downloads'
Save-UUnityCache $TemporaryRoot '2021.3.36'
$path=Join-Path $TemporaryRoot 'cache/unity-libs/2021.3.36.zip'
Check ((Get-UHash $path) -eq (Get-UHash $script:download)) 'Downloaded archive preserved'
Check ($script:calls -eq 1) 'One initial download'
$known=(Get-UCatalog $TemporaryRoot).unityLibraries
Check ($known.Count -eq 1 -and $known[0].source -eq 'https://unity.bepinex.dev/libraries/2021.3.36.zip') 'Official origin recorded'
Save-UUnityCache $TemporaryRoot '2021.3.36'
Check ($script:calls -eq 1) 'Offline cache hit makes no network call'
[IO.File]::WriteAllText($path,'tampered cache')
Save-UUnityCache $TemporaryRoot '2021.3.36'
Check ($script:calls -eq 2 -and (Get-UHash $path) -eq $known[0].sha256) 'Corrupt cache repaired only from pinned bytes'
Remove-Item -LiteralPath $path
$altered=Join-Path $TemporaryRoot 'altered.zip'
[IO.File]::WriteAllText((Join-Path $input 'extra.txt'),'different official archive fixture')
[IO.Compression.ZipFile]::CreateFromDirectory($input,$altered)
$script:download=$altered
$rejected=$false;try{Save-UUnityCache $TemporaryRoot '2021.3.36'}catch{$rejected=$true}
Check $rejected 'Different bytes rejected for a pinned version'
Check (!(Test-Path -LiteralPath $path)) 'Rejected download does not become an active cache'
Check (@(Get-ChildItem (Join-Path $TemporaryRoot 'cache/unity-libs') -Filter '*.download').Count -eq 0) 'Temporary download cleaned'
Write-Output "Passed $script:checks cache checks. No network calls or game files used. Fixture: $TemporaryRoot"
