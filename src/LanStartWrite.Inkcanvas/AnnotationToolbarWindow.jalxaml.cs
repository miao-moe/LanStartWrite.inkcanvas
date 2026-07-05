using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace LanStartWrite.Inkcanvas;

public partial class AnnotationToolbarWindow : Window
{
    private static readonly Brush TransparentBrush =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    private static readonly Color PenBlack = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color PenRed = Color.FromRgb(0xD1, 0x34, 0x38);
    private static readonly Color PenBlue = Color.FromRgb(0x00, 0x78, 0xD4);
    private static readonly Color PenGreen = Color.FromRgb(0x10, 0x7C, 0x10);

    private bool _toolSync;
    private AnnotationOverlayWindow? _annotationOverlay;
    private bool _annotationOverlayVisible;
    private SettingsWindow? _settingsWindow;
    private PenSecondaryMenuWindow? _penMenuWindow;
    private bool _penMenuVisible;
    private Color _currentPenColor = PenBlack;
    private double _currentPenThickness = 4;
    private AnnotationToolKind _currentTool = AnnotationToolKind.Mouse;

    private RadioToolToggleButton MouseTool => (RadioToolToggleButton)MouseToolToggle!;
    private RadioToolToggleButton PenTool => (RadioToolToggleButton)PenToolToggle!;
    private RadioToolToggleButton HighlighterTool => (RadioToolToggleButton)HighlighterToolToggle!;
    private RadioToolToggleButton LaserTool => (RadioToolToggleButton)LaserToolToggle!;
    private RadioToolToggleButton EraseTool => (RadioToolToggleButton)EraseToolToggle!;
    private RadioToolToggleButton TextTool => (RadioToolToggleButton)TextToolToggle!;
    private RadioToolToggleButton ArrowTool => (RadioToolToggleButton)ArrowToolToggle!;
    private RadioToolToggleButton LineTool => (RadioToolToggleButton)LineToolToggle!;
    private RadioToolToggleButton RectangleTool => (RadioToolToggleButton)RectangleToolToggle!;
    private RadioToolToggleButton EllipseTool => (RadioToolToggleButton)EllipseToolToggle!;
    private AppBarButton UndoBtn => (AppBarButton)UndoButton!;
    private AppBarButton RedoBtn => (AppBarButton)RedoButton!;
    private AppBarButton ClearBtn => (AppBarButton)ClearButton!;
    private AppBarButton SaveBtn => (AppBarButton)SaveButton!;
    private AppBarButton MinimizeBtn => (AppBarButton)MinimizeButton!;
    private AppBarButton SettingsTool => (AppBarButton)SettingsToolbarButton!;

    public AnnotationToolbarWindow()
    {
        AllowsTransparency = true;
        InitializeComponent();
        SystemBackdrop = WindowBackdropType.None;
        Background = TransparentBrush;
        Opacity = 1;

        WireTools();
        ApplyToolbarIcons();
        WireDragHandle();
        WireSecondaryToolTriggers();
        Closed += (_, _) =>
        {
            _penMenuWindow?.Close();
            _penMenuWindow = null;
            _penMenuVisible = false;
            _settingsWindow?.Close();
            _settingsWindow = null;
            DisposeAnnotationOverlay();
        };
        Loaded += (_, _) => Topmost = true;

        _toolSync = true;
        MouseTool.IsChecked = true;
        _toolSync = false;
        SyncAnnotationOverlay();
    }

    private void WireTools()
    {
        foreach (var t in AllRadioTools())
            t.Checked += OnToolToggleChecked;
    }

    private void OnToolToggleChecked(object sender, RoutedEventArgs e)
    {
        if (_toolSync || sender is not RadioToolToggleButton active || active.IsChecked != true)
            return;

        _toolSync = true;
        try
        {
            foreach (var t in AllRadioTools())
                t.IsChecked = ReferenceEquals(t, active);
        }
        finally
        {
            _toolSync = false;
        }

        _currentTool = GetToolKindFromButton(active);
        SyncAnnotationOverlay();

        if (_currentTool is AnnotationToolKind.Pen or AnnotationToolKind.Highlighter or AnnotationToolKind.Laser)
        {
            var penKind = _currentTool switch
            {
                AnnotationToolKind.Highlighter => PenKind.Highlighter,
                AnnotationToolKind.Laser => PenKind.Laser,
                _ => PenKind.Pen,
            };
            EnsureAnnotationOverlay();
            _annotationOverlay!.SetPenKind(penKind);
        }

        if (_currentTool is not AnnotationToolKind.Pen
            and not AnnotationToolKind.Highlighter
            and not AnnotationToolKind.Laser)
        {
            HidePenSecondaryMenu();
        }
    }

    private AnnotationToolKind GetToolKindFromButton(RadioToolToggleButton btn)
    {
        if (ReferenceEquals(btn, MouseTool)) return AnnotationToolKind.Mouse;
        if (ReferenceEquals(btn, PenTool)) return AnnotationToolKind.Pen;
        if (ReferenceEquals(btn, HighlighterTool)) return AnnotationToolKind.Highlighter;
        if (ReferenceEquals(btn, LaserTool)) return AnnotationToolKind.Laser;
        if (ReferenceEquals(btn, EraseTool)) return AnnotationToolKind.Eraser;
        if (ReferenceEquals(btn, TextTool)) return AnnotationToolKind.Text;
        if (ReferenceEquals(btn, ArrowTool)) return AnnotationToolKind.Arrow;
        if (ReferenceEquals(btn, LineTool)) return AnnotationToolKind.Line;
        if (ReferenceEquals(btn, RectangleTool)) return AnnotationToolKind.Rectangle;
        if (ReferenceEquals(btn, EllipseTool)) return AnnotationToolKind.Ellipse;
        return AnnotationToolKind.Mouse;
    }

    private IEnumerable<RadioToolToggleButton> AllRadioTools()
    {
        yield return MouseTool;
        yield return PenTool;
        yield return HighlighterTool;
        yield return LaserTool;
        yield return EraseTool;
        yield return TextTool;
        yield return ArrowTool;
        yield return LineTool;
        yield return RectangleTool;
        yield return EllipseTool;
    }

    private void EnsureAnnotationOverlay()
    {
        if (_annotationOverlay is not null) return;
        _annotationOverlay = new AnnotationOverlayWindow();
        _annotationOverlay.UndoStateChanged += OnUndoStateChanged;
    }

    private void DisposeAnnotationOverlay()
    {
        if (_annotationOverlay is null)
            return;
        _annotationOverlay.UndoStateChanged -= OnUndoStateChanged;
        _annotationOverlay.Close();
        _annotationOverlay = null;
    }

    private void OnUndoStateChanged((bool canUndo, bool canRedo) state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UndoBtn.IsEnabled = state.canUndo;
            RedoBtn.IsEnabled = state.canRedo;
        });
    }

    private void SyncAnnotationOverlay()
    {
        if (_currentTool == AnnotationToolKind.Mouse)
        {
            _annotationOverlay?.Hide();
            _annotationOverlayVisible = false;
            HidePenSecondaryMenu();
            return;
        }

        EnsureAnnotationOverlay();
        var overlay = _annotationOverlay!;

        switch (_currentTool)
        {
            case AnnotationToolKind.Pen:
            case AnnotationToolKind.Highlighter:
            case AnnotationToolKind.Laser:
                overlay.SetInkMode();
                overlay.SetPenColor(_currentPenColor);
                overlay.SetPenThickness(_currentPenThickness);
                break;
            case AnnotationToolKind.Eraser:
                overlay.SetEraseMode();
                break;
            case AnnotationToolKind.Text:
                overlay.SetTextMode();
                break;
            case AnnotationToolKind.Arrow:
            case AnnotationToolKind.Line:
            case AnnotationToolKind.Rectangle:
            case AnnotationToolKind.Ellipse:
                overlay.SetShapeMode(_currentTool);
                overlay.SetPenColor(_currentPenColor);
                overlay.SetPenThickness(_currentPenThickness);
                break;
        }

        overlay.Show();
        RaiseToolbarAboveAnnotationOverlay();
        _annotationOverlayVisible = true;
    }

    private void RaiseToolbarAboveAnnotationOverlay()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var t = Topmost;
            Topmost = false;
            Topmost = t;
            Activate();
            if (_penMenuWindow is not null)
                _penMenuWindow.Topmost = Topmost;
        });
    }

    private void ApplyToolbarIcons()
    {
        var iconFg = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        MouseTool.Icon = new SymbolIcon(Symbol.TouchPointer) { Foreground = iconFg };
        PenTool.Icon = new SymbolIcon(Symbol.InkingTool) { Foreground = iconFg };
        HighlighterTool.Icon = new SymbolIcon(Symbol.Highlight) { Foreground = iconFg };
        LaserTool.Icon = new SymbolIcon(Symbol.Pointer) { Foreground = iconFg };
        EraseTool.Icon = new SymbolIcon(Symbol.EraseTool) { Foreground = iconFg };
        TextTool.Icon = new SymbolIcon(Symbol.Font) { Foreground = iconFg };
        ArrowTool.Icon = new SymbolIcon(Symbol.ArrowUpRight) { Foreground = iconFg };
        LineTool.Icon = new SymbolIcon(Symbol.Sort) { Foreground = iconFg };
        RectangleTool.Icon = new SymbolIcon(Symbol.Rectangle) { Foreground = iconFg };
        EllipseTool.Icon = new SymbolIcon(Symbol.Circle) { Foreground = iconFg };
        UndoBtn.Icon = new SymbolIcon(Symbol.Undo) { Foreground = iconFg };
        RedoBtn.Icon = new SymbolIcon(Symbol.Redo) { Foreground = iconFg };
        ClearBtn.Icon = new SymbolIcon(Symbol.Delete) { Foreground = iconFg };
        SaveBtn.Icon = new SymbolIcon(Symbol.Save) { Foreground = iconFg };
        MinimizeBtn.Icon = new SymbolIcon(Symbol.Remove) { Foreground = iconFg };
        SettingsTool.Icon = new SymbolIcon(Symbol.Settings) { Foreground = iconFg };
        var grip = new SymbolIcon(Symbol.GripperBarVertical)
        {
            IsHitTestVisible = false,
            Foreground = iconFg,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DragHandleChrome!.Child = grip;
    }

    private void WireDragHandle()
    {
        var h = DragHandleChrome!;
        h.PreviewPointerDown += DragHandle_OnPreviewPointerDown;
    }

    private void WireSecondaryToolTriggers()
    {
        PenTool.Reactivated += (_, _) => TogglePenSecondaryMenu();
        HighlighterTool.Reactivated += (_, _) => TogglePenSecondaryMenu();
        LaserTool.Reactivated += (_, _) => TogglePenSecondaryMenu();
    }

    private void EnsurePenSecondaryMenuWindow()
    {
        if (_penMenuWindow is not null)
            return;

        _penMenuWindow = new PenSecondaryMenuWindow();
        var penKind = _currentTool switch
        {
            AnnotationToolKind.Highlighter => PenKind.Highlighter,
            AnnotationToolKind.Laser => PenKind.Laser,
            _ => PenKind.Pen,
        };
        _penMenuWindow.SetCurrentState(_currentPenColor, _currentPenThickness, penKind);
        _penMenuWindow.PenColorChanged += c =>
        {
            _currentPenColor = c;
            ApplyPenSettingsToOverlay();
        };
        _penMenuWindow.PenThicknessChanged += t =>
        {
            _currentPenThickness = t;
            ApplyPenSettingsToOverlay();
        };
        _penMenuWindow.PenKindChanged += k =>
        {
            EnsureAnnotationOverlay();
            _annotationOverlay!.SetPenKind(k);
            SwitchToPenKindTool(k);
        };
        _penMenuWindow.Closed += (_, _) =>
        {
            _penMenuWindow = null;
            _penMenuVisible = false;
        };
    }

    private void SwitchToPenKindTool(PenKind kind)
    {
        _toolSync = true;
        try
        {
            foreach (var t in AllRadioTools())
                t.IsChecked = false;
            var target = kind switch
            {
                PenKind.Highlighter => HighlighterTool,
                PenKind.Laser => LaserTool,
                _ => PenTool,
            };
            target.IsChecked = true;
            _currentTool = GetToolKindFromButton(target);
        }
        finally
        {
            _toolSync = false;
        }
        SyncAnnotationOverlay();
    }

    private void PositionPenSecondaryMenu()
    {
        if (_penMenuWindow is null)
            return;

        _penMenuWindow.Left = Left + 4;
        _penMenuWindow.Top = Top + Height + 2;
        _penMenuWindow.Topmost = Topmost;
    }

    private void ShowPenSecondaryMenu()
    {
        if (_currentTool is not AnnotationToolKind.Pen
            and not AnnotationToolKind.Highlighter
            and not AnnotationToolKind.Laser)
            return;

        EnsurePenSecondaryMenuWindow();
        var penKind = _currentTool switch
        {
            AnnotationToolKind.Highlighter => PenKind.Highlighter,
            AnnotationToolKind.Laser => PenKind.Laser,
            _ => PenKind.Pen,
        };
        _penMenuWindow!.SetCurrentState(_currentPenColor, _currentPenThickness, penKind);
        PositionPenSecondaryMenu();
        _penMenuWindow.Show();
        _penMenuVisible = true;
    }

    private void HidePenSecondaryMenu()
    {
        _penMenuWindow?.Hide();
        _penMenuVisible = false;
    }

    private void TogglePenSecondaryMenu()
    {
        if (_currentTool is not AnnotationToolKind.Pen
            and not AnnotationToolKind.Highlighter
            and not AnnotationToolKind.Laser)
            return;

        if (_penMenuWindow is not null && _penMenuVisible)
        {
            _penMenuWindow.Hide();
            _penMenuVisible = false;
            return;
        }

        ShowPenSecondaryMenu();
    }

    private void ApplyPenSettingsToOverlay()
    {
        if (_annotationOverlay is null)
            return;
        _annotationOverlay.SetPenColor(_currentPenColor);
        _annotationOverlay.SetPenThickness(_currentPenThickness);
    }

    private void DragHandle_OnPreviewPointerDown(object sender, RoutedEventArgs e)
    {
        if (e is not PointerDownEventArgs p || sender is not FrameworkElement fe)
            return;

        var pt = p.GetCurrentPoint(fe);
        var props = pt.Properties;
        var ok = pt.PointerDeviceType switch
        {
            PointerDeviceType.Mouse => props.IsLeftButtonPressed
                || props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed,
            PointerDeviceType.Touch => pt.IsInContact,
            PointerDeviceType.Pen => pt.IsInContact && !props.IsEraser,
            _ => false,
        };
        if (!ok)
            return;

        p.Handled = true;
        DragMove();
        PositionPenSecondaryMenu();
    }

    private void UndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _annotationOverlay?.Undo();
    }

    private void RedoButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _annotationOverlay?.Redo();
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _annotationOverlay?.ClearAll();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _annotationOverlay?.SaveToImage();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WindowState = WindowState.Minimized;
    }

    private void SettingsToolbarButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var w = new SettingsWindow { Owner = this };
        _settingsWindow = w;

        var prevToolbarTopmost = Topmost;
        Topmost = false;
        if (_penMenuWindow is not null)
            _penMenuWindow.Topmost = false;
        HidePenSecondaryMenu();
        DisposeAnnotationOverlay();
        _annotationOverlayVisible = false;

        w.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Topmost = prevToolbarTopmost;
            if (_penMenuWindow is not null)
                _penMenuWindow.Topmost = prevToolbarTopmost;
            SyncAnnotationOverlay();
        };

        w.Show();
        w.Activate();
    }
}
