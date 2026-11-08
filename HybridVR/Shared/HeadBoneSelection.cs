using System;
using System.Collections.Generic;
using System.Text;

namespace HybridVR.Shared;

// Deliberately conservative. Hair/end/twist bones are never head anchors.
internal static class HeadBoneSelection
{
    internal static int Score(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int colon = name.LastIndexOf(':');
        if (colon >= 0) name = name.Substring(colon + 1);
        var key = new StringBuilder();
        foreach (char c in name) if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        return key.ToString() switch
        {
            "head" => 100,
            "jbipchead" or "bip001head" or "bip01head" or "biphead" or "cfjhead" or "cmjhead" => 95,
            "headbone" or "bonehead" => 90,
            "\u982d" => 90,
            _ => 0
        };
    }

    internal static int Select(IReadOnlyList<string> names)
    {
        int best = 0, index = -1;
        bool ambiguous = false;
        for (int i = 0; i < names.Count; i++)
        {
            int score = Score(names[i]);
            if (score > best) { best = score; index = i; ambiguous = false; }
            else if (score > 0 && score == best) ambiguous = true;
        }
        return ambiguous ? -1 : index;
    }
}
