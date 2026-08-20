using System;
using System.Collections.Generic;

namespace STS2Mobile.Patches;

public readonly struct TwoFingerTapResult
{
    public bool ConsumeOriginal { get; }
    public bool EmitRightClick { get; }
    public float X { get; }
    public float Y { get; }

    public TwoFingerTapResult(bool consumeOriginal, bool emitRightClick, float x, float y)
    {
        ConsumeOriginal = consumeOriginal;
        EmitRightClick = emitRightClick;
        X = x;
        Y = y;
    }
}

// Framework-free gesture state used by TouchInputPatches and the host regression
// suite. A first finger remains an ordinary touch until a near-simultaneous
// second finger arrives. From that point the sequence is consumed even if it
// later becomes a drag, hold, or three-finger gesture, preventing the emulated
// primary release from turning a rejected right-click gesture into a left click.
public sealed class TwoFingerTapGesture
{
    public const ulong MaxFingerJoinMilliseconds = 160;
    public const ulong MaxTapMilliseconds = 350;
    public const float MaxTravelPixels = 32f;

    private sealed class TouchPoint
    {
        public float StartX;
        public float StartY;
        public float X;
        public float Y;
        public bool Active;
    }

    private readonly Dictionary<int, TouchPoint> _touches = new();
    private int _activeCount;
    private int _maximumFingerCount;
    private bool _twoFingerSequence;
    private bool _tapEligible;
    private bool _primaryReleaseSeen;
    private bool _awaitingPrimaryRelease;
    private ulong _firstDownMilliseconds;

    public TwoFingerTapResult Touch(
        int index,
        bool pressed,
        float x,
        float y,
        ulong nowMilliseconds
    )
    {
        if (pressed)
            return Press(index, x, y, nowMilliseconds);
        return Release(index, x, y, nowMilliseconds);
    }

    public bool Move(int index, float x, float y, ulong nowMilliseconds)
    {
        if (!_touches.TryGetValue(index, out var point) || !point.Active)
            return _twoFingerSequence;

        UpdatePoint(point, x, y);
        if (ElapsedSinceFirstDown(nowMilliseconds) > MaxTapMilliseconds)
            _tapEligible = false;
        return _twoFingerSequence;
    }

    // Returns true when the event belongs to the primary mouse sequence Godot
    // emulates from the first finger. The first primary press is intentionally
    // allowed through; once a second finger takes over, its eventual release is
    // swallowed so the underlying control cannot also receive a left click.
    public bool SuppressPrimaryEvent(bool pressed, ulong nowMilliseconds)
    {
        _ = nowMilliseconds;

        if (_twoFingerSequence && _activeCount > 0)
        {
            if (!pressed)
                _primaryReleaseSeen = true;
            return true;
        }

        if (!_awaitingPrimaryRelease)
            return false;

        _awaitingPrimaryRelease = false;
        return !pressed;
    }

    public void Reset()
    {
        _touches.Clear();
        _activeCount = 0;
        _maximumFingerCount = 0;
        _twoFingerSequence = false;
        _tapEligible = false;
        _primaryReleaseSeen = false;
        _awaitingPrimaryRelease = false;
        _firstDownMilliseconds = 0;
    }

    private TwoFingerTapResult Press(int index, float x, float y, ulong nowMilliseconds)
    {
        // Android can omit the final release when the Activity loses its
        // surface. A later press must start clean instead of inheriting a stale
        // finger forever.
        if (_activeCount > 0 && ElapsedSinceFirstDown(nowMilliseconds) > MaxTapMilliseconds)
            BeginSequence(nowMilliseconds);

        if (_activeCount == 0)
            BeginSequence(nowMilliseconds);

        if (_touches.TryGetValue(index, out var existing) && existing.Active)
        {
            _tapEligible = false;
            UpdatePoint(existing, x, y);
            return new TwoFingerTapResult(_twoFingerSequence, false, 0, 0);
        }

        var point = new TouchPoint
        {
            StartX = x,
            StartY = y,
            X = x,
            Y = y,
            Active = true,
        };
        _touches[index] = point;
        _activeCount++;
        _maximumFingerCount = Math.Max(_maximumFingerCount, _activeCount);

        if (_activeCount == 2)
        {
            _twoFingerSequence = true;
            if (ElapsedSinceFirstDown(nowMilliseconds) > MaxFingerJoinMilliseconds)
                _tapEligible = false;
        }
        else if (_activeCount > 2)
        {
            _twoFingerSequence = true;
            _tapEligible = false;
        }

        return new TwoFingerTapResult(_twoFingerSequence, false, 0, 0);
    }

    private TwoFingerTapResult Release(int index, float x, float y, ulong nowMilliseconds)
    {
        if (!_touches.TryGetValue(index, out var point) || !point.Active)
            return new TwoFingerTapResult(_twoFingerSequence, false, 0, 0);

        UpdatePoint(point, x, y);
        point.Active = false;
        _activeCount--;

        bool consume = _twoFingerSequence;
        if (_activeCount != 0)
            return new TwoFingerTapResult(consume, false, 0, 0);

        bool emit =
            _twoFingerSequence
            && _tapEligible
            && _maximumFingerCount == 2
            && ElapsedSinceFirstDown(nowMilliseconds) <= MaxTapMilliseconds;
        float centerX = 0;
        float centerY = 0;
        if (emit)
        {
            int count = 0;
            foreach (var touch in _touches.Values)
            {
                centerX += touch.X;
                centerY += touch.Y;
                count++;
            }
            if (count != 2)
            {
                emit = false;
                centerX = 0;
                centerY = 0;
            }
            else
            {
                centerX /= count;
                centerY /= count;
            }
        }

        _awaitingPrimaryRelease = _twoFingerSequence && !_primaryReleaseSeen;
        EndSequence();
        return new TwoFingerTapResult(consume, emit, centerX, centerY);
    }

    private void BeginSequence(ulong nowMilliseconds)
    {
        _touches.Clear();
        _activeCount = 0;
        _maximumFingerCount = 0;
        _twoFingerSequence = false;
        _tapEligible = true;
        _primaryReleaseSeen = false;
        _awaitingPrimaryRelease = false;
        _firstDownMilliseconds = nowMilliseconds;
    }

    private void EndSequence()
    {
        _touches.Clear();
        _activeCount = 0;
        _maximumFingerCount = 0;
        _twoFingerSequence = false;
        _tapEligible = false;
        _primaryReleaseSeen = false;
        _firstDownMilliseconds = 0;
    }

    private void UpdatePoint(TouchPoint point, float x, float y)
    {
        point.X = x;
        point.Y = y;
        float deltaX = point.X - point.StartX;
        float deltaY = point.Y - point.StartY;
        if (deltaX * deltaX + deltaY * deltaY > MaxTravelPixels * MaxTravelPixels)
            _tapEligible = false;
    }

    private ulong ElapsedSinceFirstDown(ulong nowMilliseconds) =>
        nowMilliseconds >= _firstDownMilliseconds ? nowMilliseconds - _firstDownMilliseconds : 0;
}
