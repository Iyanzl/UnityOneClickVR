using System;
using System.IO;

namespace HybridVR.Shared;

// Build-time PNG decoding avoids Unity 6's generated ReadOnlySpan image wrapper.
internal sealed class RawCursorAsset
{
    internal int Width { get; }
    internal int Height { get; }
    internal byte[] Pixels { get; }
    private RawCursorAsset(int width, int height, byte[] pixels)
    { Width = width; Height = height; Pixels = pixels; }

    internal static RawCursorAsset Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        if (width < 1 || width > 256 || height < 1 || height > 256)
            throw new InvalidDataException("Cursor dimensions invalid");
        int size = checked(width * height * 4);
        byte[] pixels = reader.ReadBytes(size);
        if (pixels.Length != size || stream.ReadByte() != -1)
            throw new InvalidDataException("Cursor RGBA payload invalid");
        return new RawCursorAsset(width, height, pixels);
    }
}
