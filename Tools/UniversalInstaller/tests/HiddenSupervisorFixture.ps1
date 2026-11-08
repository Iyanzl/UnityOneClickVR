param([string]$Mode,[string]$GameRoot,[switch]$SkinDiagnostics)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ConsoleProbe { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); }'
$hash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
@{mode=$Mode;root=$GameRoot;skin=[bool]$SkinDiagnostics;console=[ConsoleProbe]::GetConsoleWindow().ToInt64();cwd=(Get-Location).Path;scriptHash=$hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $GameRoot 'hidden-observed.json') -Encoding UTF8
