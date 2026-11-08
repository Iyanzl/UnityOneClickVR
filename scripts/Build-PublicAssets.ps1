param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath($Root)
$bitmap = [Drawing.Bitmap]::new(32, 48, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
try {
    $g.Clear([Drawing.Color]::Transparent)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    # Original arrow geometry, independent of the private screenshot-supplied cursor.
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(3,2),[Drawing.PointF]::new(3,36),[Drawing.PointF]::new(11,29),[Drawing.PointF]::new(18,44),[Drawing.PointF]::new(24,41),[Drawing.PointF]::new(17,27),[Drawing.PointF]::new(29,27))
    $g.FillPolygon([Drawing.Brushes]::White, $points)
    $pen = [Drawing.Pen]::new([Drawing.Color]::Black, 2)
    try { $g.DrawPolygon($pen, $points) } finally { $pen.Dispose() }
    $bitmap.Save((Join-Path $root 'arrow.png'), [Drawing.Imaging.ImageFormat]::Png)
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $root 'HybridVR/Assets/Cursor.rgba')))
    try {
        $writer.Write([int]32); $writer.Write([int]48)
        for($y=47;$y -ge 0;$y--){for($x=0;$x -lt 32;$x++){
            $p=$bitmap.GetPixel($x,$y)
            $writer.Write([byte]$p.R);$writer.Write([byte]$p.G);$writer.Write([byte]$p.B);$writer.Write([byte]$p.A)
        }}
    } finally { $writer.Dispose() }
} finally { $g.Dispose(); $bitmap.Dispose() }
$font = [Drawing.Font]::new('Noto Sans SC',22)
try { if($font.Name -ne 'Noto Sans SC'){throw 'Install Noto Sans SC (OFL-1.1) before generating the keyboard atlas.'} } finally { $font.Dispose() }
dotnet build (Join-Path $root 'Tools/KeyboardAssets/KeyboardAssets.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Keyboard asset tool build failed.'}
& (Join-Path $root 'artifacts/KeyboardAssets/KeyboardAssets.exe') (Join-Path $root 'HybridVR/Assets/Keyboard.rgba') (Join-Path $root 'artifacts/keyboard-preview.png')
if($LASTEXITCODE -ne 0){throw 'Keyboard atlas generation failed.'}
