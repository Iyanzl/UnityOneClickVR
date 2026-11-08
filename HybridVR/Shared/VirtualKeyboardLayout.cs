using System;
using System.Collections.Generic;

namespace HybridVR.Shared;

internal sealed class VirtualKey
{
    internal readonly string Label;
    internal readonly int Code;
    internal readonly float X, Y, Width, Height;
    internal readonly bool Extended, Modifier, Repeat;
    internal VirtualKey(string label, int code, float x, float y, float width = 1, float height = 1,
        bool extended = false, bool modifier = false, bool repeat = true)
    { Label = label; Code = code; X = x + .25f; Y = y + .3f; Width = width; Height = height;
      Extended = extended; Modifier = modifier; Repeat = repeat && !modifier; }
}

internal static class VirtualKeyboardLayout
{
    internal const float Width = 23.5f, Height = 7.1f, KeyGap = .07f;
    internal static readonly VirtualKey[] Keys = Build();
    private static VirtualKey[] Build()
    {
        var keys = new List<VirtualKey>();
        void Add(string s, int c, float x, float y, float w = 1, float h = 1,
            bool extended = false, bool modifier = false, bool repeat = true)
            => keys.Add(new VirtualKey(s, c, x, y, w, h, extended, modifier, repeat));
        Add("Esc", 0x1B, 0, 0);
        for (int i = 0; i < 12; i++) Add("F" + (i + 1), 0x70 + i, 2 + i + (i / 4) * .5f, 0);
        Add("PrtSc", 0x2C, 15.5f, 0, extended: true, repeat: false);
        Add("ScrLk", 0x91, 16.5f, 0, repeat: false); Add("Pause", 0x13, 17.5f, 0, repeat: false);
        Add("~\n`", 0xC0, 0, 1.5f);
        string[] symbols = { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")" };
        for (int i = 0; i < 10; i++) Add(symbols[i] + "\n" + ((i + 1) % 10), 0x30 + (i + 1) % 10, i + 1, 1.5f);
        Add("_\n-", 0xBD, 11, 1.5f); Add("+\n=", 0xBB, 12, 1.5f); Add("Backspace", 8, 13, 1.5f, 2);
        Add("Tab", 9, 0, 2.5f, 1.5f);
        string row = "QWERTYUIOP";
        for (int i = 0; i < row.Length; i++) Add(row[i].ToString(), row[i], 1.5f + i, 2.5f);
        Add("{\n[", 0xDB, 11.5f, 2.5f); Add("}\n]", 0xDD, 12.5f, 2.5f); Add("|\n\\", 0xDC, 13.5f, 2.5f, 1.5f);
        Add("Caps", 0x14, 0, 3.5f, 1.75f, repeat: false);
        row = "ASDFGHJKL";
        for (int i = 0; i < row.Length; i++) Add(row[i].ToString(), row[i], 1.75f + i, 3.5f);
        Add(":\n;", 0xBA, 10.75f, 3.5f); Add("\"\n'", 0xDE, 11.75f, 3.5f); Add("Enter", 0x0D, 12.75f, 3.5f, 2.25f);
        Add("Shift", 0xA0, 0, 4.5f, 2.25f, modifier: true);
        row = "ZXCVBNM";
        for (int i = 0; i < row.Length; i++) Add(row[i].ToString(), row[i], 2.25f + i, 4.5f);
        Add("<\n,", 0xBC, 9.25f, 4.5f); Add(">\n.", 0xBE, 10.25f, 4.5f); Add("?\n/", 0xBF, 11.25f, 4.5f);
        Add("Shift", 0xA1, 12.25f, 4.5f, 2.75f, modifier: true);
        Add("Ctrl", 0xA2, 0, 5.5f, 1.25f, modifier: true); Add("Win", 0x5B, 1.25f, 5.5f, 1.25f, extended: true, modifier: true);
        Add("Alt", 0xA4, 2.5f, 5.5f, 1.25f, modifier: true); Add("Space", 0x20, 3.75f, 5.5f, 6.25f);
        Add("Alt", 0xA5, 10, 5.5f, 1.25f, extended: true, modifier: true); Add("Win", 0x5C, 11.25f, 5.5f, 1.25f, extended: true, modifier: true);
        Add("Menu", 0x5D, 12.5f, 5.5f, 1.25f, extended: true); Add("Ctrl", 0xA3, 13.75f, 5.5f, 1.25f, extended: true, modifier: true);
        string[] nav = { "Insert", "Home", "PgUp", "Del", "End", "PgDn" };
        int[] codes = { 0x2D, 0x24, 0x21, 0x2E, 0x23, 0x22 };
        for (int i = 0; i < nav.Length; i++) Add(nav[i], codes[i], 15.5f + i % 3, 1.5f + i / 3, extended: true);
        Add("Up", 0x26, 16.5f, 4.5f, extended: true);
        Add("Left", 0x25, 15.5f, 5.5f, extended: true); Add("Down", 0x28, 16.5f, 5.5f, extended: true); Add("Right", 0x27, 17.5f, 5.5f, extended: true);
        Add("Num", 0x90, 19, 1.5f, extended: true, repeat: false); Add("/", 0x6F, 20, 1.5f, extended: true);
        Add("*", 0x6A, 21, 1.5f); Add("-", 0x6D, 22, 1.5f);
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
        { int digit = 7 - r * 3 + c; Add(digit.ToString(), 0x60 + digit, 19 + c, 2.5f + r); }
        Add("+", 0x6B, 22, 2.5f, 1, 2); Add("Enter", 0x0D, 22, 4.5f, 1, 2, extended: true);
        Add("0", 0x60, 19, 5.5f, 2); Add(".", 0x6E, 21, 5.5f);
        return keys.ToArray();
    }
    internal static int Hit(float u, float v)
    {
        if (float.IsNaN(u) || float.IsNaN(v) || u < 0 || u > 1 || v < 0 || v > 1) return -1;
        float x = u * Width, y = (1 - v) * Height;
        for (int i = 0; i < Keys.Length; i++)
        {
            var k = Keys[i];
            if (x >= k.X + KeyGap && x <= k.X + k.Width - KeyGap && y >= k.Y + KeyGap && y <= k.Y + k.Height - KeyGap) return i;
        }
        return -1;
    }
}
