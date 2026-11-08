using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using HybridVR.Shared;

if (args.Length != 2) throw new ArgumentException("new-atlas.rgba preview.png");
const int scale = 80;
int width = (int)Math.Round(VirtualKeyboardLayout.Width * scale), height = (int)Math.Round(VirtualKeyboardLayout.Height * scale);
using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bitmap))
{
    g.Clear(Color.FromArgb(26, 29, 32));
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    using var background = new SolidBrush(Color.FromArgb(53, 57, 61));
    using var text = new SolidBrush(Color.FromArgb(242, 244, 246));
    using var outline = new Pen(Color.FromArgb(102, 108, 112), 1);
    using var font = new Font("Noto Sans SC", 22, FontStyle.Regular, GraphicsUnit.Pixel);
    using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    foreach (var key in VirtualKeyboardLayout.Keys)
    {
        float gap = VirtualKeyboardLayout.KeyGap;
        var rect = new RectangleF((key.X + gap) * scale, (key.Y + gap) * scale, (key.Width - 2 * gap) * scale, (key.Height - 2 * gap) * scale);
        g.FillRectangle(background, rect); g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawString(key.Label, font, text, rect, format);
    }
}
bitmap.Save(args[1], ImageFormat.Png);
var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
try
{
    using var output = new BinaryWriter(File.Create(args[0]));
    output.Write(width); output.Write(height);
    var row = new byte[width * 4];
    for (int y = height - 1; y >= 0; y--)
    {
        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
        for (int x = 0; x < width; x++)
        { output.Write(row[x*4+2]); output.Write(row[x*4+1]); output.Write(row[x*4]); output.Write((byte)255); }
    }
}
finally { bitmap.UnlockBits(data); }
Console.WriteLine($"Keyboard atlas: {VirtualKeyboardLayout.Keys.Length} keys, {width}x{height}, bottom-up RGBA");
