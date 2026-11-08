using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

// One-variable, opt-in drawing experiment. Input/pose sampling stays in LateUpdate.
internal sealed class MonoEyeRenderPhase : IDisposable
{
    private readonly Func<bool> _draw;
    private readonly EyeRenderFrameGate _gate = new();
    private bool _disposed;
    private int _nextCheck;
    private int _draws, _skipped;
    private MonoEyeRenderPhase(Func<bool> draw) => _draw = draw;
    internal static bool Requested(string[] args) => Array.Exists(args,
        a => a == "--hybrid-eye-phase=after-pre-late");

    internal static MonoEyeRenderPhase? Create(Func<bool> draw)
    {
        if (!Requested(Environment.GetCommandLineArgs())) return null;
        var schedule = new MonoEyeRenderPhase(draw);
        try
        {
            PlayerLoop.SetPlayerLoop(EyeRenderPhaseLoop.Add(PlayerLoop.GetCurrentPlayerLoop(), schedule.Tick));
            OpenVrDiagnostics.Write($"eye draw phase pending: after-pre-late; installedFrame={Time.frameCount}; awaiting live callback; original camera, input/pose sampling, skin flag and capture unchanged");
            return schedule;
        }
        catch (Exception error)
        {
            schedule.Dispose();
            OpenVrDiagnostics.Write("eye draw phase unavailable; using LateUpdate: " + error.Message);
            return null;
        }
    }
    internal bool TryQueue(int frame)
    {
        if (_disposed) return false;
        try
        {
            if (_gate.Missed(frame)) throw new InvalidOperationException("Previous drawing callback did not run");
            if (_gate.StartupExpired(frame)) throw new InvalidOperationException("No live callback within 120 frames; experiment did not activate");
            if (frame >= _nextCheck)
            {
                _nextCheck = frame + 120;
                if (EyeRenderPhaseLoop.Count(PlayerLoop.GetCurrentPlayerLoop()) != 1)
                    throw new InvalidOperationException("PlayerLoop changed by another owner");
            }
            return _gate.Queue(frame);
        }
        catch (Exception error)
        {
            OpenVrDiagnostics.Write($"eye draw phase fallback to LateUpdate: {error.Message}; frame={frame}, pending={_gate.PendingFrame}, lastCallback={_gate.LastCallbackFrame}, draws={_draws}, skipped={_skipped}");
            Dispose();
            return false;
        }
    }
    private void Tick()
    {
        if (_disposed) return;
        int frame = Time.frameCount;
        bool firstCallback = _gate.LastCallbackFrame < 0;
        bool take = _gate.Take(frame);
        if (firstCallback)
            OpenVrDiagnostics.Write($"eye draw phase callback observed: frame={frame}; future prepared frames may use after-pre-late");
        if (!take) return;
        if (_draw())
        {
            _draws++;
            if (_draws == 1 || _draws % 600 == 0)
                OpenVrDiagnostics.Write($"eye draw phase verified: after-pre-late; frame={frame}, stereoQueued={_draws}, skipped={_skipped}");
        }
        else _skipped++;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Cancel();
        OpenVrDiagnostics.Write($"eye draw phase stopped: stereoQueued={_draws}, skipped={_skipped}, lastCallback={_gate.LastCallbackFrame}");
        try { PlayerLoop.SetPlayerLoop(EyeRenderPhaseLoop.Remove(PlayerLoop.GetCurrentPlayerLoop())); }
        catch (Exception error) { OpenVrDiagnostics.Write("eye draw phase cleanup: " + error.Message); }
    }
}

internal sealed class EyeRenderFrameGate
{
    private int _pending = -1, _completed = -1;
    private int _firstRequest = -1;
    internal int PendingFrame => _pending;
    internal int LastCallbackFrame { get; private set; } = -1;
    internal bool Missed(int frame) => _pending >= 0 && _pending != frame;
    internal bool StartupExpired(int frame) => LastCallbackFrame < 0 && _firstRequest >= 0 && frame - _firstRequest >= 120;
    internal bool Queue(int frame)
    {
        if (frame < 0 || _pending >= 0 || _completed == frame) return false;
        if (_firstRequest < 0) _firstRequest = frame;
        // SetPlayerLoop is visible immediately, but executes only on the next full iteration.
        // Keep drawing A until the installed callback has actually run; never queue a stale pose.
        if (LastCallbackFrame < 0) { _completed = frame; return false; }
        _pending = frame;
        return true;
    }
    internal bool Take(int frame)
    {
        if (frame < 0) return false;
        LastCallbackFrame = frame;
        if (_pending != frame) return false;
        _pending = -1; _completed = frame;
        return true;
    }
    internal void Cancel() => _pending = -1;
}

internal static class EyeRenderPhaseLoop
{
    internal sealed class AfterPreLate { }
    internal static PlayerLoopSystem Add(PlayerLoopSystem original, Action draw)
    {
        int found = 0;
        var result = Insert(Remove(original), draw, ref found);
        if (found != 1) throw new NotSupportedException("Missing/ambiguous PreLateUpdate boundary: " + found);
        return result;
    }
    private static PlayerLoopSystem Insert(PlayerLoopSystem node, Action draw, ref int found)
    {
        if (node.subSystemList == null) return node;
        var children = new List<PlayerLoopSystem>();
        foreach (var child in node.subSystemList)
        {
            children.Add(Insert(child, draw, ref found));
            if (child.type != typeof(UnityEngine.PlayerLoop.PreLateUpdate)) continue;
            found++;
            // A sibling AFTER the whole phase includes late-physics nodes inserted by other owners.
            children.Add(new PlayerLoopSystem { type = typeof(AfterPreLate), updateDelegate = () => draw() });
        }
        node.subSystemList = children.ToArray();
        return node;
    }
    internal static PlayerLoopSystem Remove(PlayerLoopSystem node)
    {
        if (node.subSystemList == null) return node;
        var children = new List<PlayerLoopSystem>();
        foreach (var child in node.subSystemList)
            if (child.type != typeof(AfterPreLate)) children.Add(Remove(child));
        node.subSystemList = children.ToArray();
        return node;
    }
    internal static int Count(PlayerLoopSystem node)
    {
        int count = node.type == typeof(AfterPreLate) ? 1 : 0;
        if (node.subSystemList != null) foreach (var child in node.subSystemList) count += Count(child);
        return count;
    }
}
