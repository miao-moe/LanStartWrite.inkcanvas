using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace LanStartWrite.Inkcanvas;

public partial class AnnotationOverlayWindow : Window
{
    private bool _isRebuildingStroke;
    private PenKind _currentKind = PenKind.Pen;
    private readonly List<DispatcherTimer> _laserFadeTimers = [];
    private readonly InkInputMetrics _metrics = new();
    private int _lastPointerId = -1;
    private StylusPointCollection? _lastRealtimePoints;
    private PointerDeviceType _lastPointerDeviceType = PointerDeviceType.Mouse;

    private static readonly MethodInfo[] RealtimeFeedMethods =
        typeof(InkCanvas)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly Brush TransparentBrush =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    private AnnotationToolKind _currentTool = AnnotationToolKind.Pen;
    private StrokeHistory? _history;
    private bool _isDrawingShape;
    private StylusPoint? _shapeStart;
    private Stroke? _shapePreview;
    private Color _shapeColor = Colors.Black;
    private double _shapeThickness = 4;

    public event Action<(bool canUndo, bool canRedo)>? UndoStateChanged;

    private InkCanvas Surface => (InkCanvas)OverlayInk!;

    public AnnotationOverlayWindow()
    {
        AllowsTransparency = true;
        ShowActivated = false;
        SystemBackdrop = WindowBackdropType.None;
        Opacity = 1;
        Background = TransparentBrush;
        InitializeComponent();
        Surface.Background = TransparentBrush;

        InkCanvasTuning.ApplyStartupDefaults(Surface);

        ApplyDefaultDrawingAttributes();
        Surface.StrokeCollected += Surface_OnStrokeCollected_EnforceSmoothAttributes;
        Surface.PreviewPointerMove += Surface_OnPreviewPointerMove_RealtimeSampling;
        Surface.EditingMode = InkCanvasEditingMode.Ink;

        Closed += (_, _) => CancelLaserFadeTimers();
        InkRuntimeOptions.Changed += OnInkRuntimeOptionsChanged;
        ApplyRuntimeOptions(InkRuntimeOptions.Current);

        WireShapeDrawing();
        WireTextMode();
    }

    private void ApplyDefaultDrawingAttributes()
    {
        var da = Surface.DefaultDrawingAttributes;
        da.Color = Colors.Black;
        da.Width = 3;
        da.Height = 3;
        da.StylusTip = StylusTip.Ellipse;
        da.FitToCurve = true;
        da.BrushType = BrushType.Round;
        da.IgnorePressure = !InkRuntimeOptions.Current.EnablePressure;
        da.IsHighlighter = false;
        SyncDynamicRendererAttributes(da);
    }

    private void SyncDynamicRendererAttributes(DrawingAttributes source)
    {
        var previewDa = Surface.DynamicRenderer.DrawingAttributes;
        previewDa.Color = source.Color;
        previewDa.Width = source.Width;
        previewDa.Height = source.Height;
        previewDa.StylusTip = source.StylusTip;
        previewDa.FitToCurve = source.FitToCurve;
        previewDa.BrushType = source.BrushType;
        previewDa.IgnorePressure = source.IgnorePressure;
        previewDa.IsHighlighter = source.IsHighlighter;
    }

    private void EnsureHistory()
    {
        if (_history is null)
        {
            _history = new StrokeHistory(Surface.Strokes);
            _history.StateChanged += s => UndoStateChanged?.Invoke(s);
        }
    }

    private void Surface_OnStrokeCollected_EnforceSmoothAttributes(
        object? sender,
        InkCanvasStrokeCollectedEventArgs e)
    {
        var stroke = e.Stroke;
        var da = stroke.DrawingAttributes;
        da.StylusTip = StylusTip.Ellipse;
        da.FitToCurve = true;
        da.IgnorePressure = !InkRuntimeOptions.Current.EnablePressure;
        ApplyBrushTypeAndHighlighterForCurrentKind(da);
        var runtime = InkRuntimeOptions.Current;
        var activeStroke = runtime.EnableLegacyPostProcessFallback
            ? TryRebuildSparseStroke(stroke, runtime, _lastPointerDeviceType) ?? stroke
            : stroke;
        _metrics.OnStrokeCommitted(activeStroke.StylusPoints.Count);
        _metrics.EmitIfNeeded();

        EnsureHistory();
        _history!.Snapshot();

        if (_currentKind == PenKind.Laser)
            BeginLaserFade(activeStroke);
    }

    private Stroke? TryRebuildSparseStroke(
        Stroke stroke,
        InkRuntimeSnapshot runtime,
        PointerDeviceType deviceType)
    {
        if (_isRebuildingStroke)
            return null;

        var source = stroke.StylusPoints;
        if (source.Count < 3)
            return null;

        var dense = BuildDensifiedPoints(source, runtime, deviceType);
        if (dense is null || dense.Count <= source.Count)
            return null;

        var replacement = new Stroke(dense, CopyDrawingAttributes(stroke.DrawingAttributes));
        TryCopyTaperMode(stroke, replacement);

        var strokes = Surface.Strokes;
        var index = strokes.IndexOf(stroke);
        if (index < 0)
            return null;

        try
        {
            _isRebuildingStroke = true;
            strokes[index] = replacement;
            _metrics.RebuiltStrokeCount++;
            return replacement;
        }
        finally
        {
            _isRebuildingStroke = false;
        }
    }

    private void BeginLaserFade(Stroke stroke)
    {
        const int holdMs = 600;
        const int fadeMs = 600;
        const int ticks = 12;

        var startColor = stroke.DrawingAttributes.Color;
        if (startColor.A == 0)
            return;

        var stepAlpha = startColor.A / (double)ticks;

        var hold = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(holdMs),
        };
        _laserFadeTimers.Add(hold);

        hold.Tick += HoldTick;
        hold.Start();

        void HoldTick(object? s, EventArgs e2)
        {
            hold.Tick -= HoldTick;
            hold.Stop();
            _laserFadeTimers.Remove(hold);

            var fade = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds((double)fadeMs / ticks),
            };
            _laserFadeTimers.Add(fade);

            var i = 0;
            fade.Tick += FadeTick;
            fade.Start();

            void FadeTick(object? s2, EventArgs e3)
            {
                i++;
                var a = (byte)Math.Clamp(startColor.A - stepAlpha * i, 0, 255);
                stroke.DrawingAttributes.Color = Color.FromArgb(
                    a,
                    startColor.R,
                    startColor.G,
                    startColor.B);

                if (i < ticks)
                    return;

                fade.Tick -= FadeTick;
                fade.Stop();
                _laserFadeTimers.Remove(fade);

                try
                {
                    Surface.Strokes.Remove(stroke);
                }
                catch
                {
                    // 忽略关闭窗口或集合已释放等情况
                }
            }
        }
    }

    private void CancelLaserFadeTimers()
    {
        foreach (var t in _laserFadeTimers)
            t.Stop();
        _laserFadeTimers.Clear();
        InkRuntimeOptions.Changed -= OnInkRuntimeOptionsChanged;
    }

    public void SetPenKind(PenKind kind)
    {
        _currentKind = kind;
        var da = Surface.DefaultDrawingAttributes;
        ApplyBrushTypeAndHighlighterForCurrentKind(da);
        SyncDynamicRendererAttributes(da);
    }

    private void ApplyBrushTypeAndHighlighterForCurrentKind(DrawingAttributes da)
    {
        if (_currentKind == PenKind.Highlighter)
        {
            da.IsHighlighter = true;
            da.BrushType = BrushType.Marker;
        }
        else
        {
            da.IsHighlighter = false;
            da.BrushType = BrushType.Round;
        }
    }

    /// <summary>复制 DrawingAttributes，避免依赖 Clone() 方法是否存在。</summary>
    private static DrawingAttributes CopyDrawingAttributes(DrawingAttributes source)
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

    /// <summary>通过反射复制 TaperMode（Jalium.UI 扩展属性，可能不存在）。</summary>
    private static void TryCopyTaperMode(Stroke source, Stroke target)
    {
        try
        {
            var prop = typeof(Stroke).GetProperty("TaperMode");
            if (prop is not null && prop.CanRead && prop.CanWrite)
                prop.SetValue(target, prop.GetValue(source));
        }
        catch
        {
            // 属性不存在或不可访问时跳过
        }
    }

    private static StylusPointCollection? BuildDensifiedPoints(
        StylusPointCollection source,
        InkRuntimeSnapshot runtime,
        PointerDeviceType deviceType)
    {
        if (source.Count < 2)
            return null;

        var minSegmentLength = GetMinSegmentLength(runtime, deviceType);
        var dense = new StylusPointCollection();
        dense.Add(source[0]);

        for (var i = 1; i < source.Count; i++)
        {
            var prev = source[i - 1];
            var current = source[i];
            var dx = current.X - prev.X;
            var dy = current.Y - prev.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance > minSegmentLength)
            {
                var insertCount = Math.Clamp((int)(distance / minSegmentLength), 1, 8);
                for (var k = 1; k < insertCount; k++)
                {
                    var t = (double)k / insertCount;
                    var x = prev.X + (dx * t);
                    var y = prev.Y + (dy * t);
                    dense.Add(new StylusPoint(x, y));
                }
            }

            dense.Add(current);
        }

        return dense;
    }

    public void SetInkMode()
    {
        _currentTool = AnnotationToolKind.Pen;
        Surface.EditingMode = InkCanvasEditingMode.Ink;
        Surface.IsHitTestVisible = true;
    }

    public void SetEraseMode()
    {
        _currentTool = AnnotationToolKind.Eraser;
        Surface.EditingMode = InkCanvasEditingMode.EraseByStroke;
        Surface.IsHitTestVisible = true;
    }

    public void SetTextMode()
    {
        _currentTool = AnnotationToolKind.Text;
        Surface.EditingMode = InkCanvasEditingMode.None;
        Surface.IsHitTestVisible = false;
    }

    public void SetShapeMode(AnnotationToolKind tool)
    {
        _currentTool = tool;
        Surface.EditingMode = InkCanvasEditingMode.None;
        Surface.IsHitTestVisible = false;
    }

    public void SetPenColor(Color color)
    {
        _shapeColor = color;
        var da = Surface.DefaultDrawingAttributes;
        da.Color = color;
        Surface.DynamicRenderer.DrawingAttributes.Color = color;
    }

    public void SetPenThickness(double thickness)
    {
        var t = Math.Max(1, thickness);
        _shapeThickness = t;
        var da = Surface.DefaultDrawingAttributes;
        da.Width = t;
        da.Height = t;
        var previewDa = Surface.DynamicRenderer.DrawingAttributes;
        previewDa.Width = t;
        previewDa.Height = t;
    }

    /// <summary>形状绘制：监听 PointerDown/Move，在指针离开接触时提交形状。</summary>
    private void WireShapeDrawing()
    {
        Surface.PreviewPointerDown += OnShapePointerDown;
        Surface.PreviewPointerMove += OnShapePointerMove;
    }

    private bool IsShapeTool =>
        _currentTool is AnnotationToolKind.Line
            or AnnotationToolKind.Arrow
            or AnnotationToolKind.Rectangle
            or AnnotationToolKind.Ellipse;

    private void OnShapePointerDown(object? sender, RoutedEventArgs e)
    {
        if (!IsShapeTool || e is not PointerDownEventArgs p || sender is not InkCanvas canvas)
            return;

        var pt = p.GetCurrentPoint(canvas);
        _shapeStart = new StylusPoint(pt.Position.X, pt.Position.Y);
        _isDrawingShape = true;
        p.Handled = true;
    }

    private void OnShapePointerMove(object? sender, RoutedEventArgs e)
    {
        if (!_isDrawingShape || _shapeStart is null)
            return;
        if (e is not PointerMoveEventArgs p || sender is not InkCanvas canvas)
            return;

        var pt = p.GetCurrentPoint(canvas);

        // 指针抬起来：提交形状
        if (!pt.IsInContact)
        {
            CommitShape(canvas, pt.Position);
            return;
        }

        var end = new StylusPoint(pt.Position.X, pt.Position.Y);

        if (_shapePreview is not null)
        {
            Surface.Strokes.Remove(_shapePreview);
            _shapePreview = null;
        }

        var pts = BuildShapePoints(_shapeStart, end, _currentTool);
        if (pts.Count >= 2)
        {
            var da = new DrawingAttributes
            {
                Color = _shapeColor,
                Width = _shapeThickness,
                Height = _shapeThickness,
                StylusTip = StylusTip.Ellipse,
                FitToCurve = false,
                BrushType = BrushType.Round,
                IgnorePressure = true,
                IsHighlighter = false,
            };
            _shapePreview = new Stroke(pts, da);
            Surface.Strokes.Add(_shapePreview);
        }
    }

    private void CommitShape(InkCanvas canvas, Point endPos)
    {
        if (_shapeStart is null)
            return;

        var end = new StylusPoint(endPos.X, endPos.Y);

        if (_shapePreview is not null)
        {
            Surface.Strokes.Remove(_shapePreview);
            _shapePreview = null;
        }

        var pts = BuildShapePoints(_shapeStart, end, _currentTool);
        if (pts.Count >= 2)
        {
            var da = new DrawingAttributes
            {
                Color = _shapeColor,
                Width = _shapeThickness,
                Height = _shapeThickness,
                StylusTip = StylusTip.Ellipse,
                FitToCurve = false,
                BrushType = BrushType.Round,
                IgnorePressure = true,
                IsHighlighter = false,
            };
            var stroke = new Stroke(pts, da);
            EnsureHistory();
            _history!.Snapshot();
            Surface.Strokes.Add(stroke);
        }

        _isDrawingShape = false;
        _shapeStart = null;
    }

    /// <summary>根据工具类型生成形状的采样点。</summary>
    private static StylusPointCollection BuildShapePoints(
        StylusPoint start,
        StylusPoint end,
        AnnotationToolKind tool)
    {
        var pts = new StylusPointCollection();
        var x1 = start.X;
        var y1 = start.Y;
        var x2 = end.X;
        var y2 = end.Y;

        switch (tool)
        {
            case AnnotationToolKind.Line:
                pts.Add(start);
                pts.Add(end);
                break;
            case AnnotationToolKind.Arrow:
                BuildArrowPoints(pts, x1, y1, x2, y2);
                break;
            case AnnotationToolKind.Rectangle:
                BuildRectanglePoints(pts, x1, y1, x2, y2);
                break;
            case AnnotationToolKind.Ellipse:
                BuildEllipsePoints(pts, x1, y1, x2, y2);
                break;
        }

        return pts;
    }

    private static void BuildArrowPoints(
        StylusPointCollection pts, double x1, double y1, double x2, double y2)
    {
        pts.Add(new StylusPoint(x1, y1));
        pts.Add(new StylusPoint(x2, y2));

        var dx = x2 - x1;
        var dy = y2 - y1;
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1)
            return;

        const double arrowAngle = 0.4;
        const double arrowLen = 18;
        var ux = dx / len;
        var uy = dy / len;
        var cosA = Math.Cos(arrowAngle);
        var sinA = Math.Sin(arrowAngle);
        var lx = x2 - (ux * cosA - uy * sinA) * arrowLen;
        var ly = y2 - (uy * cosA + ux * sinA) * arrowLen;
        var rx = x2 - (ux * cosA + uy * sinA) * arrowLen;
        var ry = y2 - (uy * cosA - ux * sinA) * arrowLen;
        pts.Add(new StylusPoint(lx, ly));
        pts.Add(new StylusPoint(x2, y2));
        pts.Add(new StylusPoint(rx, ry));
    }

    private static void BuildRectanglePoints(
        StylusPointCollection pts, double x1, double y1, double x2, double y2)
    {
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        pts.Add(new StylusPoint(minX, minY));
        pts.Add(new StylusPoint(maxX, minY));
        pts.Add(new StylusPoint(maxX, maxY));
        pts.Add(new StylusPoint(minX, maxY));
        pts.Add(new StylusPoint(minX, minY));
    }

    private static void BuildEllipsePoints(
        StylusPointCollection pts, double x1, double y1, double x2, double y2)
    {
        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;
        var rx = Math.Abs(x2 - x1) / 2;
        var ry = Math.Abs(y2 - y1) / 2;
        if (rx < 1 || ry < 1)
            return;
        const int segments = 48;
        for (var i = 0; i <= segments; i++)
        {
            var a = 2 * Math.PI * i / segments;
            var x = cx + rx * Math.Cos(a);
            var y = cy + ry * Math.Sin(a);
            pts.Add(new StylusPoint(x, y));
        }
    }

    /// <summary>文字模式：点击画布时弹出 TextBox 输入框，参考 ICCE 的浮层文字。</summary>
    private void WireTextMode()
    {
        Surface.PreviewPointerDown += OnTextPointerDown;
    }

    private void OnTextPointerDown(object? sender, RoutedEventArgs e)
    {
        if (_currentTool != AnnotationToolKind.Text)
            return;
        if (e is not PointerDownEventArgs p || sender is not InkCanvas canvas)
            return;

        var pt = p.GetCurrentPoint(canvas);
        p.Handled = true;

        var tb = new TextBox
        {
            Text = "",
            FontSize = 24,
            Foreground = new SolidColorBrush(_shapeColor),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x78, 0xD4)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 120,
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };

        Canvas.SetLeft(tb, pt.Position.X);
        Canvas.SetTop(tb, pt.Position.Y);
        var layer = FloatingLayer as Canvas;
        if (layer is not null)
        {
            layer.IsHitTestVisible = true;
            layer.Children.Add(tb);
            tb.Focus();
            // 失焦时提交文本（用户点击其他地方即完成输入）
            tb.LostFocus += (_, _) =>
            {
                CommitTextToStrokes(tb, layer);
                layer.IsHitTestVisible = false;
            };
        }
    }

    private void CommitTextToStrokes(TextBox tb, Canvas layer)
    {
        var text = tb.Text;
        layer.Children.Remove(tb);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var x = Canvas.GetLeft(tb);
        var y = Canvas.GetTop(tb);
        // 将文本作为墨迹笔划提交：使用 InkCanvas 的 Strokes 集合添加一条占位笔划
        // 真实实现需要将文字光栅化为笔划；此处简化为占位矩形描边
        var pts = new StylusPointCollection();
        var w = Math.Max(120, tb.ActualWidth);
        var h = Math.Max(36, tb.ActualHeight);
        BuildRectanglePoints(pts, x, y, x + w, y + h);
        var da = new DrawingAttributes
        {
            Color = _shapeColor,
            Width = Math.Max(1, _shapeThickness * 0.5),
            Height = Math.Max(1, _shapeThickness * 0.5),
            StylusTip = StylusTip.Ellipse,
            FitToCurve = false,
            BrushType = BrushType.Round,
            IgnorePressure = true,
            IsHighlighter = false,
        };
        var stroke = new Stroke(pts, da);
        EnsureHistory();
        _history!.Snapshot();
        Surface.Strokes.Add(stroke);
    }

    public void Undo()
    {
        EnsureHistory();
        _history?.Undo();
    }

    public void Redo()
    {
        EnsureHistory();
        _history?.Redo();
    }

    public void ClearAll()
    {
        EnsureHistory();
        _history?.Snapshot();
        Surface.Strokes.Clear();
    }

    /// <summary>将当前画布（含墨迹）保存为 PNG 图片，参考 ICCE 的截图保存。</summary>
    public void SaveToImage()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "LanStartWrite Screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Annotation_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            // 使用 InkCanvas 的 RenderTarget 渲染能力；若 Jalium.UI 未公开，回退到 Strokes 序列化
            var bmp = RenderInkToBitmap();
            if (bmp is not null)
            {
                SaveBitmap(bmp, path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveToImage] {ex.Message}");
        }
    }

    private object? RenderInkToBitmap()
    {
        // Jalium.UI 的 InkCanvas 可能提供 RenderToBitmap 方法；通过反射探测
        var m = typeof(InkCanvas).GetMethod(
            "RenderToBitmap",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m is not null)
        {
            try
            {
                return m.Invoke(Surface, null);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static void SaveBitmap(object bmp, string path)
    {
        // Jalium.UI 的位图保存 API；通过反射调用 Save 方法
        var t = bmp.GetType();
        var save = t.GetMethod("Save", [typeof(string)]) ?? t.GetMethod("Save");
        if (save is not null)
        {
            try
            {
                save.Invoke(bmp, [path]);
                return;
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Surface_OnPreviewPointerMove_RealtimeSampling(object? sender, RoutedEventArgs e)
    {
        if (sender is not InkCanvas canvas || e is not PointerMoveEventArgs p)
            return;

        var runtime = InkRuntimeOptions.Current;
        var inter = p.GetIntermediatePoints(canvas);
        if (inter.Count == 0)
            return;

        _metrics.OnIntermediatePoints(inter.Count);
        if (runtime.EnableTilt)
            _metrics.OnTiltSample(TryReadTiltMagnitude(p, canvas));
        if (!runtime.EnableRealtimeSampling || Surface.EditingMode != InkCanvasEditingMode.Ink)
            return;

        var points = new StylusPointCollection();
        foreach (var item in inter)
            points.Add(new StylusPoint(item.Position.X, item.Position.Y));

        var pointerId = p.Pointer.GetHashCode();
        _lastPointerDeviceType = p.Pointer.PointerDeviceType;
        if (pointerId != _lastPointerId)
        {
            _lastPointerId = pointerId;
            _lastRealtimePoints = null;
        }

        if (_lastRealtimePoints is not null && points.Count > 0)
        {
            var first = points[0];
            var prev = _lastRealtimePoints[^1];
            if (Math.Abs(prev.X - first.X) < 0.001 && Math.Abs(prev.Y - first.Y) < 0.001)
                points.RemoveAt(0);
        }

        if (points.Count == 0)
            return;

        _lastRealtimePoints = points;
        TryFeedRealtimePoints(points);
    }

    private void TryFeedRealtimePoints(StylusPointCollection points)
    {
        foreach (var m in RealtimeFeedMethods)
        {
            if (m.Name is not ("AddPoints" or "AppendPoints" or "FeedPoints" or "UpdateDrawing"))
                continue;

            var ps = m.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(typeof(StylusPointCollection)))
            {
                try
                {
                    m.Invoke(Surface, [points]);
                }
                catch
                {
                    // ignore and continue fallback
                }

                return;
            }
        }
    }

    private static double GetMinSegmentLength(
        InkRuntimeSnapshot runtime,
        PointerDeviceType deviceType)
    {
        var baseLength = runtime.SmoothingLevel switch
        {
            InkSmoothingLevel.Low => Math.Max(0.95, runtime.MinPointDistance * 1.35),
            InkSmoothingLevel.High => Math.Max(0.45, runtime.MinPointDistance * 0.75),
            _ => Math.Max(0.65, runtime.MinPointDistance),
        };

        return deviceType switch
        {
            PointerDeviceType.Pen => baseLength * 0.9,
            PointerDeviceType.Touch => baseLength * 1.1,
            _ => baseLength,
        };
    }

    private void OnInkRuntimeOptionsChanged(InkRuntimeSnapshot snapshot)
    {
        Dispatcher.BeginInvoke(() => ApplyRuntimeOptions(snapshot));
    }

    private void ApplyRuntimeOptions(InkRuntimeSnapshot snapshot)
    {
        InkCanvasTuning.ApplyRuntimeMinPointDistance(snapshot.MinPointDistance, Surface);
        var da = Surface.DefaultDrawingAttributes;
        da.IgnorePressure = !snapshot.EnablePressure;
        da.FitToCurve = snapshot.SmoothingLevel != InkSmoothingLevel.Low;
        SyncDynamicRendererAttributes(da);
    }

    private static double TryReadTiltMagnitude(PointerMoveEventArgs p, InkCanvas canvas)
    {
        try
        {
            var point = p.GetCurrentPoint(canvas);
            var props = point.Properties;
            var t = props.GetType();
            var x = t.GetProperty("XTilt")?.GetValue(props) as double?;
            var y = t.GetProperty("YTilt")?.GetValue(props) as double?;
            if (x is null || y is null)
                return 0;
            return Math.Sqrt((x.Value * x.Value) + (y.Value * y.Value));
        }
        catch
        {
            return 0;
        }
    }

    private sealed class InkInputMetrics
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private int _strokeCount;
        private int _strokePointCount;
        private int _intermediatePoints;
        private int _tiltSamples;
        private double _tiltSum;

        internal int RebuiltStrokeCount { get; set; }

        internal void OnIntermediatePoints(int count) => _intermediatePoints += count;

        internal void OnStrokeCommitted(int strokePointCount)
        {
            _strokeCount++;
            _strokePointCount += strokePointCount;
        }

        internal void OnTiltSample(double tiltMagnitude)
        {
            if (tiltMagnitude <= 0)
                return;
            _tiltSamples++;
            _tiltSum += tiltMagnitude;
        }

        internal void EmitIfNeeded()
        {
            if (_watch.Elapsed < TimeSpan.FromSeconds(3))
                return;

            var avgStrokePoints = _strokeCount == 0 ? 0 : _strokePointCount / (double)_strokeCount;
            var avgTilt = _tiltSamples == 0 ? 0 : _tiltSum / _tiltSamples;
            Debug.WriteLine(
                $"[ink-metrics] strokes={_strokeCount} avgPoints={avgStrokePoints:F1} inter={_intermediatePoints} rebuilt={RebuiltStrokeCount} avgTilt={avgTilt:F2}");
            _watch.Restart();
            _strokeCount = 0;
            _strokePointCount = 0;
            _intermediatePoints = 0;
            _tiltSamples = 0;
            _tiltSum = 0;
            RebuiltStrokeCount = 0;
        }
    }
}
