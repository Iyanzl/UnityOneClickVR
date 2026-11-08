using System;
using System.Collections.Generic;
using System.IO;

namespace HybridVR.Shared;

// A bounded settings-only bitmap alphabet, independent of stripped native Font APIs.
internal sealed class MenuGlyphData
{
    internal sealed class Glyph
    {
        internal readonly int Advance;
        internal readonly byte[] Pixels;
        internal Glyph(int advance, byte[] pixels) { Advance = advance; Pixels = pixels; }
    }
    internal readonly Dictionary<char, Glyph> Glyphs = new();
    internal int Width, Height, Em;
    internal static MenuGlyphData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        if (reader.ReadInt32() != 0x31475648) throw new InvalidDataException("Menu glyph format");
        var result = new MenuGlyphData { Width = reader.ReadInt32(), Height = reader.ReadInt32(), Em = reader.ReadInt32() };
        int count = reader.ReadInt32();
        if (result.Width < 8 || result.Width > 64 || result.Height < 8 || result.Height > 64 ||
            result.Em < 8 || result.Em > 48 || count < 96 || count > 2048) throw new InvalidDataException("Menu glyph bounds");
        int size = checked(result.Width * result.Height * 4);
        for (int i = 0; i < count; i++)
        {
            char code = (char)reader.ReadUInt16();
            int advance = reader.ReadUInt16();
            if (advance < 1 || advance > result.Width || result.Glyphs.ContainsKey(code)) throw new InvalidDataException("Menu glyph metrics");
            byte[] pixels = reader.ReadBytes(size);
            if (pixels.Length != size) throw new InvalidDataException("Menu glyph pixels truncated");
            result.Glyphs.Add(code, new Glyph(advance, pixels));
        }
        if (!result.Glyphs.ContainsKey('?') || stream.ReadByte() != -1) throw new InvalidDataException("Menu glyph content");
        return result;
    }
}
