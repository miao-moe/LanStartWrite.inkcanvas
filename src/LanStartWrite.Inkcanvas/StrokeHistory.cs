using System.Collections.Generic;
using Jalium.UI.Controls.Ink;

namespace LanStartWrite.Inkcanvas;

/// <summary>
/// 基于 Stroke 集合快照的撤销/重做管理器，参考 ICCE TimeMachine 的简化实现。
/// 每次提交操作前快照当前 Strokes，撤销时回滚到上一快照。
/// </summary>
internal sealed class StrokeHistory
{
    private readonly Stack<StrokeSnapshot[]> _undo = new();
    private readonly Stack<StrokeSnapshot[]> _redo = new();
    private readonly StrokeCollection _strokes;
    private const int MaxHistory = 100;

    public event Action<(bool canUndo, bool canRedo)>? StateChanged;

    public StrokeHistory(StrokeCollection strokes)
    {
        _strokes = strokes;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>在改变 Strokes 之前调用，记录当前状态。</summary>
    public void Snapshot()
    {
        var snap = CloneCurrent();
        _undo.Push(snap);
        if (_undo.Count > MaxHistory)
        {
            var arr = _undo.ToArray();
            _undo.Clear();
            for (var i = arr.Length - 1; i >= 0; i--)
                _undo.Push(arr[i]);
        }
        _redo.Clear();
        Emit();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        var current = CloneCurrent();
        _redo.Push(current);
        var prev = _undo.Pop();
        Restore(prev);
        Emit();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        var current = CloneCurrent();
        _undo.Push(current);
        var next = _redo.Pop();
        Restore(next);
        Emit();
    }

    public void Clear()
    {
        Snapshot();
        _strokes.Clear();
        Emit();
    }

    private StrokeSnapshot[] CloneCurrent()
    {
        var list = new List<StrokeSnapshot>(_strokes.Count);
        foreach (var s in _strokes)
            list.Add(StrokeSnapshot.From(s));
        return list.ToArray();
    }

    private void Restore(StrokeSnapshot[] snapshot)
    {
        _strokes.Clear();
        foreach (var snap in snapshot)
        {
            var stroke = snap.ToStroke();
            if (stroke is not null)
                _strokes.Add(stroke);
        }
    }

    private void Emit() => StateChanged?.Invoke((CanUndo, CanRedo));

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        Emit();
    }

    /// <summary>笔划快照：通过 StylusPointCollection + DrawingAttributes 重建，避免依赖 Stroke.Clone()。</summary>
    private sealed class StrokeSnapshot
    {
        private readonly StylusPointCollection _points;
        private readonly DrawingAttributes _attributes;

        private StrokeSnapshot(StylusPointCollection points, DrawingAttributes attributes)
        {
            _points = points;
            _attributes = attributes;
        }

        public static StrokeSnapshot From(Stroke stroke)
        {
            var pts = new StylusPointCollection();
            foreach (var sp in stroke.StylusPoints)
                pts.Add(new StylusPoint(sp.X, sp.Y));
            var da = CopyAttributes(stroke.DrawingAttributes);
            return new StrokeSnapshot(pts, da);
        }

        public Stroke? ToStroke()
        {
            if (_points.Count == 0)
                return null;
            return new Stroke(_points, _attributes);
        }

        private static DrawingAttributes CopyAttributes(DrawingAttributes source)
        {
            return new DrawingAttributes
            {
                Color = source.Color,
                Width = source.Width,
                Height = source.Height,
                StylusTip = source.StylusTip,
                FitToCurve = source.FitToCurve,
                BrushType = source.BrushType,
                IgnorePressure = source.IgnorePressure,
                IsHighlighter = source.IsHighlighter,
            };
        }
    }
}
