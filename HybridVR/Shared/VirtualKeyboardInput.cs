using System;
using System.Collections.Generic;

namespace HybridVR.Shared;

internal interface IVirtualKeyboardOutput
{
    bool Send(VirtualKey key, bool down);
}

// Modifiers are one-shot selections; they are sent only around a real key press.
internal sealed class VirtualKeyboardInput
{
    private readonly IVirtualKeyboardOutput _output;
    private readonly HashSet<int> _selected = new();
    private readonly List<int> _sentModifiers = new();
    private int _key = -1;
    private bool _armed;
    private double _repeatAt;
    internal VirtualKeyboardInput(IVirtualKeyboardOutput output) { _output = output; }
    internal bool Selected(int index) => _selected.Contains(index) || _key == index;
    internal bool Busy => _key >= 0 || _sentModifiers.Count > 0;
    internal void Update(int index, bool pressed, bool available, double now)
    {
        if (!available) { Suspend(); return; }
        if (!pressed) { ReleaseKey(); _armed = true; return; }
        if (_key >= 0)
        {
            if (index != _key) { ReleaseKey(); return; }
            if (VirtualKeyboardLayout.Keys[_key].Repeat && now >= _repeatAt)
            {
                if (!_output.Send(VirtualKeyboardLayout.Keys[_key], true)) { Suspend(); return; }
                _repeatAt = now + .055;
            }
            return;
        }
        if (!_armed) return;
        _armed = false;
        if (index < 0 || index >= VirtualKeyboardLayout.Keys.Length) return;
        var key = VirtualKeyboardLayout.Keys[index];
        if (key.Modifier)
        {
            if (!_selected.Remove(index)) _selected.Add(index);
            return;
        }
        foreach (int modifier in _selected)
        {
            if (!_output.Send(VirtualKeyboardLayout.Keys[modifier], true)) { Suspend(); return; }
            _sentModifiers.Add(modifier);
        }
        if (!_output.Send(key, true)) { Suspend(); return; }
        _key = index; _repeatAt = now + .45;
    }
    private void ReleaseKey()
    {
        bool used = _key >= 0 || _sentModifiers.Count != 0;
        if (_key >= 0) _output.Send(VirtualKeyboardLayout.Keys[_key], false);
        _key = -1;
        for (int i = _sentModifiers.Count - 1; i >= 0; i--) _output.Send(VirtualKeyboardLayout.Keys[_sentModifiers[i]], false);
        _sentModifiers.Clear();
        if (used) _selected.Clear();
    }
    internal void Suspend() { ReleaseKey(); _selected.Clear(); _armed = false; }
}
