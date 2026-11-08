using System;
using System.Collections.Generic;

namespace HybridVR.Shared;

// Menu actions do not travel through the game's mouse/IMGUI event pipeline.
internal sealed class MenuPointerInput
{
    private readonly struct Target
    {
        internal readonly float X, Y, W, H;
        internal Target(float x, float y, float w, float h) { X=x; Y=y; W=w; H=h; }
        internal bool Hit(float x, float y) => x >= X && x < X+W && y >= Y && y < Y+H;
    }
    private List<Target> _targets = new(), _building = new();
    private string _layout = "";
    private int _source, _pressed = -1, _pending = -1;
    private bool _armed, _held;
    internal float X { get; private set; }
    internal float Y { get; private set; }
    internal bool Valid { get; private set; }
    internal int Hover { get; private set; } = -1;
    internal void Update(int source, bool valid, float x, float y, bool held)
    {
        if (!valid || source != _source) { ResetInput(); _source=source; }
        Valid=valid; X=x; Y=y; Hover=valid ? Find(x,y) : -1;
        if (!valid) return;
        if (!held) _armed=true;
        if (held && !_held) _pressed=_armed ? Hover : -1;
        if (!held && _held)
        {
            if (_pressed >= 0 && _pressed == Hover) _pending=_pressed;
            _pressed=-1;
        }
        _held=held;
    }
    internal void BeginLayout(string layout)
    {
        if (_layout != layout) { Reset(); _layout=layout; }
        _building.Clear();
    }
    internal bool Button(float x, float y, float width, float height, out bool hover)
    {
        var target=new Target(x,y,width,height);
        int id=_building.Count;
        _building.Add(target);
        hover=Valid && target.Hit(X,Y);
        if (_pending != id) return false;
        _pending=-1;
        // A control that changed its location/size between frames is not the same target.
        return id < _targets.Count && Same(target,_targets[id]);
    }
    internal void EndLayout()
    {
        var old=_targets; _targets=_building; _building=old;
        _pending=-1;
    }
    private int Find(float x,float y)
    { for(int i=_targets.Count-1;i>=0;i--) if(_targets[i].Hit(x,y))return i; return -1; }
    private static bool Same(Target a, Target b) => a.X==b.X && a.Y==b.Y && a.W==b.W && a.H==b.H;
    private void ResetInput() { _armed=_held=false; _pressed=_pending=-1; Valid=false; Hover=-1; }
    internal void Reset() { ResetInput(); _targets.Clear(); _building.Clear(); _layout=""; }
}
