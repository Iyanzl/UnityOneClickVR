using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using HybridVR.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class MenuBitmapText : IDisposable
{
    private readonly MenuGlyphData _data;
    private readonly GUIStyle _style;
    private readonly Dictionary<char, Texture2D> _textures = new();
    private readonly List<string> _lines = new();
    internal MenuBitmapText()
    {
        using var stream = typeof(MenuBitmapText).Assembly.GetManifestResourceStream("HybridVR.MenuGlyphs.bin")
            ?? throw new FileNotFoundException("Embedded settings glyphs missing");
        _data = MenuGlyphData.Read(stream);
        _style = new GUIStyle();
        Uuvr.OpenVR.OpenVrDiagnostics.Write("settings font: embedded bitmap fallback, glyphs=" + _data.Glyphs.Count);
    }
    private MenuGlyphData.Glyph Glyph(char c) => _data.Glyphs.TryGetValue(c, out var g) ? g : _data.Glyphs['?'];
    private Texture2D Texture(char c)
    {
        if (!_data.Glyphs.ContainsKey(c)) c = '?';
        if (_textures.TryGetValue(c, out var existing) && existing != null) return existing;
        var pixels = Glyph(c).Pixels;
        var texture = OwnedUnityAsset.Retain(new Texture2D(_data.Width, _data.Height, TextureFormat.RGBA32, false));
        texture.name = "HybridVR settings glyph " + (int)c;
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { texture.LoadRawTextureData(pinned.AddrOfPinnedObject(), pixels.Length); texture.Apply(false, true); }
        catch { Object.Destroy(texture); throw; }
        finally { pinned.Free(); }
        texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
        _textures[c] = texture;
        return texture;
    }
    internal void Draw(Rect rect, string text, bool centered)
    {
        float scale = 16f / _data.Em, lineHeight = 18;
        float available = Mathf.Max(1, rect.width - 8);
        _lines.Clear();
        int start = 0; float length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            float next = Glyph(text[i]).Advance * scale;
            if (text[i] == '\n' || (length + next > available && i > start))
            {
                _lines.Add(text.Substring(start, i - start)); start = i; length = 0;
                if (text[i] == '\n') { start++; continue; }
            }
            length += next;
        }
        if (start < text.Length) _lines.Add(text.Substring(start));
        int count = Math.Min(_lines.Count, Math.Max(1, (int)(rect.height / lineHeight)));
        float top = centered ? Mathf.Max(0, (rect.height - count * lineHeight) / 2) : 2;
        GUI.BeginGroup(rect);
        try
        {
            for (int i = 0; i < count; i++)
            {
                string line = _lines[i]; float width = 0;
                foreach (char c in line) width += Glyph(c).Advance * scale;
                float x = centered ? Mathf.Max(4, (rect.width - width) / 2) : 4;
                foreach (char c in line)
                {
                    if (c != ' ')
                    {
                        _style.normal.background = Texture(c);
                        // Box is native-backed on stripped IL2CPP games; texture helpers may not be.
                        GUI.Box(new Rect(x - 2*scale, top + i*lineHeight, _data.Width*scale, _data.Height*scale), GUIContent.none, _style);
                    }
                    x += Glyph(c).Advance * scale;
                }
            }
        }
        finally { GUI.EndGroup(); }
    }
    public void Dispose()
    {
        foreach (var texture in _textures.Values) if (texture != null) Object.Destroy(texture);
        _textures.Clear(); _lines.Clear();
    }
}
