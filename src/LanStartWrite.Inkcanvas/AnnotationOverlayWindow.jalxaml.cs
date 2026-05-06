using System.Diagnostics;
using System.Collections.Generic;
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

    private static readonly Brush TransparentBrush =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

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

        // MinPointDistance 若为实例字段，在 Surface 构造后再写一次。
        InkCanvasTuning.ApplyStartupDefaults(Surface);

        ApplyDefaultDrawingAttributes();
        Surface.StrokeCollected += Surface_OnStrokeCollected_EnforceSmoothAttributes;
        Surface.EditingMode = InkCanvasEditingMode.Ink;

        Closed += (_, _) => CancelLaserFadeTimers();

#if DEBUG
        Surface.PreviewPointerMove += Surface_OnPreviewPointerMove_InkDiag;
#endif
    }

#if DEBUG
    private static void Surface_OnPreviewPointerMove_InkDiag(object? sender, RoutedEventArgs e)
    {
        if (sender is not InkCanvas canvas || e is not PointerMoveEventArgs p)
            return;

        var inter = p.GetIntermediatePoints(canvas);
        Debug.WriteLine(
            $"[ink] device={p.Pointer.PointerDeviceType} intermediate={inter.Count}");
    }
#endif

    private void ApplyDefaultDrawingAttributes()
    {
        var da = Surface.DefaultDrawingAttributes;
        da.Color = Colors.Black;
        da.Width = 3;
        da.Height = 3;
        da.StylusTip = StylusTip.Ellipse;
        da.FitToCurve = true;
        da.BrushType = BrushType.Round;
        da.IgnorePressure = true;
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

    private void Surface_OnStrokeCollected_EnforceSmoothAttributes(
        object? sender,
        InkCanvasStrokeCollectedEventArgs e)
    {
        var stroke = e.Stroke;
        var da = stroke.DrawingAttributes;
        da.StylusTip = StylusTip.Ellipse;
        da.FitToCurve = true;
        da.IgnorePressure = true;
        ApplyBrushTypeAndHighlighterForCurrentKind(da);

        var activeStroke = TryRebuildSparseStroke(stroke) ?? stroke;

        if (_currentKind == PenKind.Laser)
            BeginLaserFade(activeStroke);
    }

    private Stroke? TryRebuildSparseStroke(Stroke stroke)
    {
        if (_isRebuildingStroke)
            return null;

        var source = stroke.StylusPoints;
        if (source.Count < 3)
            return null;

        var dense = BuildDensifiedPoints(source, minSegmentLength: 0.75);
        if (dense is null || dense.Count <= source.Count)
            return null;

        var replacement = new Stroke(dense, stroke.DrawingAttributes.Clone())
        {
            TaperMode = stroke.TaperMode,
        };

        var strokes = Surface.Strokes;
        var index = strokes.IndexOf(stroke);
        if (index < 0)
            return null;

        try
        {
            _isRebuildingStroke = true;
            strokes[index] = replacement;
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
    }

    public void SetPenKind(PenKind kind)
    {
        _currentKind = kind;
        var da = Surface.DefaultDrawingAttributes;
        ApplyBrushTypeAndHighlighterForCurrentKind(da);
        SyncDynamicRendererAttributes(da);
    }

    /// <summary>
    /// 荧光笔使用 <see cref="BrushType.Marker"/>（宽、半透明笔刷）并打开 <see cref="DrawingAttributes.IsHighlighter"/>；
    /// 书写笔与激光笔使用 <see cref="BrushType.Round"/>。
    /// </summary>
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

    private static StylusPointCollection? BuildDensifiedPoints(
        StylusPointCollection source,
        double minSegmentLength)
    {
        if (source.Count < 2)
            return null;

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
        Surface.EditingMode = InkCanvasEditingMode.Ink;
    }

    public void SetEraseMode()
    {
        Surface.EditingMode = InkCanvasEditingMode.EraseByStroke;
    }

    public void SetPenColor(Color color)
    {
        var da = Surface.DefaultDrawingAttributes;
        da.Color = color;
        Surface.DynamicRenderer.DrawingAttributes.Color = color;
    }

    public void SetPenThickness(double thickness)
    {
        var t = Math.Max(1, thickness);
        var da = Surface.DefaultDrawingAttributes;
        da.Width = t;
        da.Height = t;
        var previewDa = Surface.DynamicRenderer.DrawingAttributes;
        previewDa.Width = t;
        previewDa.Height = t;
    }
}
