param([string]$Root = (Split-Path $PSScriptRoot -Parent), [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$chars = [System.Collections.Generic.SortedSet[char]]::new()
32..126 | ForEach-Object { [void]$chars.Add([char]$_) }
$files = @('HybridVR\Adapters\Il2CppOpenVr\InteractionMenu.cs','HybridVR\Adapters\Il2CppOpenVr\MenuLanguage.cs','HybridVR\Shared\SettingsSections.cs')
foreach ($file in $files) {
    $source = [IO.File]::ReadAllText((Join-Path $Root $file))
    $source = [regex]::Replace($source, '\\u([0-9a-fA-F]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
    foreach ($c in $source.ToCharArray()) { if ([int]$c -gt 126 -and -not [char]::IsControl($c)) { [void]$chars.Add($c) } }
}
$path = Join-Path $Root 'HybridVR\Assets\MenuGlyphs.bin'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
$bitmap = [Drawing.Bitmap]::new(40,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
$font = [Drawing.Font]::new('Noto Sans SC',32,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
if ($font.Name -ne 'Noto Sans SC') { throw 'Install the OFL-licensed Noto Sans SC build font; do not ship substituted glyphs' }
$format = [Drawing.StringFormat]::GenericTypographic.Clone()
$format.FormatFlags = $format.FormatFlags -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces
$g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$sheet = $null; $sg = $null; $index = 0
if ($Preview) {
    $sheet = [Drawing.Bitmap]::new(800, [int]([Math]::Ceiling($chars.Count / 20.0)*48))
    $sg = [Drawing.Graphics]::FromImage($sheet); $sg.Clear([Drawing.Color]::FromArgb(24,26,30))
}
try {
    $writer.Write([int]0x31475648); $writer.Write([int]40); $writer.Write([int]48); $writer.Write([int]32); $writer.Write([int]$chars.Count)
    foreach ($c in $chars) {
        $g.Clear([Drawing.Color]::Transparent)
        $advance = [Math]::Clamp([int][Math]::Ceiling($g.MeasureString([string]$c,$font,100,$format).Width),1,40)
        $g.DrawString([string]$c,$font,[Drawing.Brushes]::White,[Drawing.PointF]::new(2,0),$format)
        if ($sg) { $sg.DrawImageUnscaled($bitmap,($index%20)*40,[int][Math]::Floor($index/20.0)*48); $index++ }
        $writer.Write([uint16][int]$c); $writer.Write([uint16]$advance)
        $pixels = [byte[]]::new(40*48*4)
        for ($y=0; $y -lt 48; $y++) { for ($x=0; $x -lt 40; $x++) {
            $i=(($y*40)+$x)*4; $p=$bitmap.GetPixel($x,47-$y)
            $pixels[$i]=255; $pixels[$i+1]=255; $pixels[$i+2]=255; $pixels[$i+3]=$p.A
        } }
        $writer.Write($pixels)
    }
    if ($sheet) { $sheet.Save($Preview,[Drawing.Imaging.ImageFormat]::Png) }
} finally { $writer.Dispose(); $g.Dispose(); $bitmap.Dispose(); $font.Dispose(); $format.Dispose(); if($sg){$sg.Dispose()}; if($sheet){$sheet.Dispose()} }
Write-Output "Generated settings glyph asset: $($chars.Count) glyphs, $path"
