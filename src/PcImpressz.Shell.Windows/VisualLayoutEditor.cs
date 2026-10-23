using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Documents;

namespace PcImpressz.Shell.Windows;

/// <summary>Edits the real running MainWindow, not a facsimile. Only whitelisted
/// layout/typography properties are persisted; business commands are untouched.</summary>
internal sealed class VisualLayoutEditor
{
    private readonly MainWindow _main;
    private Window? _inspector;
    private FrameworkElement? _selected;
    private readonly Dictionary<string, LayoutValues> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LayoutValues> _original = new(StringComparer.Ordinal);
    private TreeView? _tree;
    private readonly Dictionary<string, FrameworkElement> _elements = new(StringComparer.Ordinal);
    private string _selectedKey = "";
    private string _activeScreen = "";
    private bool _editing;
    private bool _loading;
    private TextBlock? _selectionLabel;
    private Slider? _font, _margin, _width, _height;
    private TextBox? _text;
    private TextBlock? _textHelp;
    private CheckBox? _bold;
    private ComboBox? _fontFamily, _alignment;
    private SelectionAdorner? _highlight;
    private AdornerLayer? _highlightLayer;
    private readonly Dictionary<string, LayoutValues> _undo = new(StringComparer.Ordinal);
    private Button? _undoButton;
    private ComboBox? _columnPicker;
    private TextBox? _columnHeader;
    private Slider? _columnWidth;
    private bool _loadingColumn;
    private DataGrid? _selectedGrid;
    private static readonly string[] Allowed =
    [
        "DesignerWindowFrame", "DesignerTitleBar", "DesignerBrand",
        "DesignerSidebar", "DesignerNavigation", "DesignerWorkspace",
        "DesignerPageHeader", "DesignerPageTitle", "DesignerPageDescription",
        "DesignerCurrentPage", "DesignerStatusBar"
    ];
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcImpressz", "WindowsEdition");
    private static readonly string FilePath = Path.Combine(Folder, "visual-layout-v2.json");

    private sealed class LayoutValues
    {
        public double? FontSize { get; set; }
        public double? Margin { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool? Bold { get; set; }
        public string? Text { get; set; }
        public string? FontFamily { get; set; }
        public string? HorizontalAlignment { get; set; }
    }

    public VisualLayoutEditor(MainWindow main)
    {
        _main = main;
        _main.PreviewKeyDown += OnKeyDown;
        _main.PreviewMouseLeftButtonDown += OnMouseDown;
        _main.Loaded += (_, _) => { RefreshTree(); Load(); };
        _main.Closed += (_, _) => _inspector?.Close();
        if (_main.FindName("DesignerPageTitle") is TextBlock pageTitle)
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(pageTitle, (_, _) =>
                {
                    RefreshTree();
                    ApplyCurrentScreen();
                });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Toggle();
            e.Handled = true;
        }
        if (e.Key == Key.Escape && _editing)
        {
            EndEditing(); e.Handled = true;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing) return;
        var node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not MainWindow)
        {
            if (node is FrameworkElement fe && _elements.Values.Contains(fe))
            {
                Select(fe);
                e.Handled = true;
                return;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        e.Handled = true; // never execute business actions while editing
    }

    private void Toggle()
    {
        if (_editing) { EndEditing(); return; }
        _editing = true;
        _inspector = BuildInspector();
        _inspector.Owner = _main;
        _inspector.Show();
        _inspector.Closed += (_, _) => { ClearHighlight(); _editing = false; _inspector = null; };
        RefreshTree();
        ApplyCurrentScreen();
        if (_main.FindName("DesignerPageTitle") is FrameworkElement title) Select(title);
    }

    private Window BuildInspector()
    {
        var win = new Window
        {
            Title = "PC-Impressz – VALÓDI FELÜLET SZERKESZTÉSE",
            Width = 440, Height = 760, MinWidth = 390, MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Max(0, _main.Left + _main.ActualWidth - 410),
            Top = Math.Max(0, _main.Top + 70),
            FontFamily = new FontFamily("Segoe UI"), Background = Brushes.White,
            Foreground = Brushes.Black
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = panel; win.Content = scroll;
        panel.Children.Add(new TextBlock
        {
            Text = "A tényleges PC-Impressz ablakot szerkeszted.\nKattints egy elemre a főablakban.",
            TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 15)
        });
        _selectionLabel = new TextBlock { FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_selectionLabel);
        panel.Children.Add(new TextBlock { Text = "Aktuális képernyő elemei", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        var refresh = new Button { Content = "Képernyő elemeinek frissítése", Padding = new Thickness(7) };
        refresh.Click += (_, _) => RefreshTree();
        panel.Children.Add(refresh);
        _tree = new TreeView { Height = 215, Margin = new Thickness(0, 7, 0, 8) };
        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem item && item.Tag is string key && _elements.TryGetValue(key, out var fe)) Select(fe);
        };
        panel.Children.Add(_tree);
        _font = AddSlider(panel, "Betűméret (px)", 8, 40, 13);
        _margin = AddSlider(panel, "Külső margó (px, minden oldalon)", 0, 60, 10);
        _width = AddSlider(panel, "Szélesség (0 = automatikus)", 0, 480, 0);
        _height = AddSlider(panel, "Magasság (0 = automatikus)", 0, 160, 0);
        _bold = new CheckBox { Content = "Félkövér szöveg", Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(_bold);
        panel.Children.Add(new TextBlock { Text = "Betűtípus", Margin = new Thickness(0, 12, 0, 3) });
        _fontFamily = new ComboBox();
        foreach (var font in new[] { "Segoe UI", "Arial", "Calibri", "Consolas", "Georgia", "Verdana" })
            _fontFamily.Items.Add(font);
        panel.Children.Add(_fontFamily);
        panel.Children.Add(new TextBlock { Text = "Vízszintes igazítás", Margin = new Thickness(0, 12, 0, 3) });
        _alignment = new ComboBox();
        foreach (var alignment in new[] { "Left", "Center", "Right", "Stretch" })
            _alignment.Items.Add(alignment);
        panel.Children.Add(_alignment);
        panel.Children.Add(new TextBlock { Text = "Felirat / gombszöveg", Margin = new Thickness(0, 12, 0, 3) });
        _textHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 7) };
        panel.Children.Add(_textHelp);
        _text = new TextBox { MinHeight = 32, MaxLength = 100 };
        panel.Children.Add(_text);
        foreach (var slider in new[] { _font, _margin, _width, _height })
            slider.ValueChanged += (_, _) => UpdateSelected();
        _bold.Checked += (_, _) => UpdateSelected();
        _bold.Unchecked += (_, _) => UpdateSelected();
        _text.TextChanged += (_, _) => UpdateSelected();
        _fontFamily.SelectionChanged += (_, _) => UpdateSelected();
        _alignment.SelectionChanged += (_, _) => UpdateSelected();
        var save = new Button { Content = "Mentés a főprogramhoz", Padding = new Thickness(10), Margin = new Thickness(0, 22, 0, 6) };
        save.Click += (_, _) => Save();
        panel.Children.Add(save);
        _undoButton = new Button { Content = "Kijelölt elem visszavonása", Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 6) };
        _undoButton.Click += (_, _) => UndoSelected();
        panel.Children.Add(_undoButton);
        panel.Children.Add(new TextBlock { Text = "Táblázat oszlopai", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });
        _columnPicker = new ComboBox { IsEnabled = false };
        _columnPicker.SelectionChanged += (_, _) => LoadColumn();
        panel.Children.Add(_columnPicker);
        panel.Children.Add(new TextBlock { Text = "Oszlopfejléc", Margin = new Thickness(0, 8, 0, 2) });
        _columnHeader = new TextBox { IsEnabled = false, MaxLength = 100 };
        _columnHeader.TextChanged += (_, _) => UpdateColumn();
        panel.Children.Add(_columnHeader);
        panel.Children.Add(new TextBlock { Text = "Oszlopszélesség (60-600 px)", Margin = new Thickness(0, 8, 0, 2) });
        _columnWidth = new Slider { Minimum = 60, Maximum = 600, TickFrequency = 5, IsSnapToTickEnabled = true, IsEnabled = false };
        _columnWidth.ValueChanged += (_, _) => UpdateColumn();
        panel.Children.Add(_columnWidth);
        var reset = new Button { Content = "Minden szerkesztés visszaállítása", Padding = new Thickness(10) };
        reset.Click += (_, _) => Reset();
        panel.Children.Add(reset);
        panel.Children.Add(new TextBlock
        {
            Text = "Ctrl+Shift+D: szerkesztő be/ki. Esc: kilépés.\nA számlák, raktáradatok és üzleti parancsok nem módosulnak.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0)
        });
        return win;
    }

    private static Slider AddSlider(Panel panel, string label, double min, double max, double value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 2) });
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        panel.Children.Add(slider);
        return slider;
    }

    private void Select(FrameworkElement element)
    {
        _selected = element;
        ShowHighlight(element);
        SelectGrid(element as DataGrid);
        _loading = true;
        _selectedKey = _elements.FirstOrDefault(x => ReferenceEquals(x.Value, element)).Key ?? "";
        _selectionLabel!.Text = $"{element.GetType().Name}: {element.Name} ({_selectedKey})";
        if (_undoButton is not null) _undoButton.IsEnabled = _undo.ContainsKey(_selectedKey);
        _font!.IsEnabled = element is Control or TextBlock;
        _bold!.IsEnabled = element is Control or TextBlock;
        _text!.IsEnabled = TryReadCaption(element, out var caption, out _);
        _textHelp!.Text = _text.IsEnabled
            ? "A felirat szerkesztheto. A navigacios elemek eredeti azonositoja es parancsa valtozatlan."
            : "Ez kontener, dinamikus adathoz kotott vagy osszetett vezerlo. A szovege nem irhato at biztonsagosan. Valassz egy benne levo feliratot vagy gombot.";
        _font.Value = element is TextBlock tb ? Math.Clamp(tb.FontSize, 8, 40) :
            element is Control c ? Math.Clamp(c.FontSize, 8, 40) : 13;
        _margin!.Value = Math.Clamp(element.Margin.Left, 0, 60);
        _width!.Value = double.IsNaN(element.Width) ? 0 : Math.Clamp(element.Width, 0, 480);
        _height!.Value = double.IsNaN(element.Height) ? 0 : Math.Clamp(element.Height, 0, 160);
        _bold.IsChecked = element switch
        {
            TextBlock text => text.FontWeight == FontWeights.Bold,
            Control control => control.FontWeight == FontWeights.Bold,
            _ => false
        };
        _fontFamily!.IsEnabled = _font.IsEnabled;
        _fontFamily.SelectedItem = element is TextBlock tbf ? tbf.FontFamily.Source :
            element is Control ctrl ? ctrl.FontFamily.Source : null;
        _alignment!.SelectedItem = element.HorizontalAlignment.ToString();
        _text.Text = _text.IsEnabled ? caption : "";
        _loading = false;
    }

    private void UpdateSelected()
    {
        if (_loading || !_editing || _selected is null) return;
        var element = _selected;
        var values = new LayoutValues
        {
            Margin = _margin!.Value, Width = _width!.Value, Height = _height!.Value,
            FontSize = _font!.IsEnabled ? _font.Value : null,
            Bold = _bold!.IsEnabled ? _bold.IsChecked : null,
            Text = _text!.IsEnabled ? _text.Text : null,
            FontFamily = _fontFamily!.IsEnabled ? _fontFamily.SelectedItem as string : null,
            HorizontalAlignment = _alignment!.SelectedItem as string
        };
        if (string.IsNullOrEmpty(_selectedKey)) return;
        if (TryReadCaption(element, out _, out var captionTarget) &&
            captionTarget is TextBlock nav && NavigationLabelOverrides.TryGetOriginal(nav, out _))
            values.Text = null; // Navigation captions persist by original Title, not visual-tree position.
        if (!_undo.ContainsKey(_selectedKey))
            _undo[_selectedKey] = Capture(element);
        _values[_selectedKey] = values;
        if (_undoButton is not null) _undoButton.IsEnabled = true;
        if (_text!.IsEnabled) UpdateCaption(element, _text.Text);
        Apply(element, values);
    }

    private void SelectGrid(DataGrid? grid)
    {
        _selectedGrid = grid;
        _loadingColumn = true;
        _columnPicker!.Items.Clear();
        if (grid is not null)
            for (int i = 0; i < grid.Columns.Count; i++)
                _columnPicker.Items.Add($"{i + 1}: {grid.Columns[i].Header}");
        _columnPicker.IsEnabled = grid is not null && grid.Columns.Count > 0;
        _columnPicker.SelectedIndex = _columnPicker.IsEnabled ? 0 : -1;
        _loadingColumn = false;
        LoadColumn();
    }

    private void LoadColumn()
    {
        if (_loadingColumn || _columnPicker is null || _columnHeader is null || _columnWidth is null) return;
        _loadingColumn = true;
        int i = _columnPicker.SelectedIndex;
        bool valid = _selectedGrid is not null && i >= 0 && i < _selectedGrid.Columns.Count;
        _columnHeader.IsEnabled = valid;
        _columnWidth.IsEnabled = valid;
        _columnHeader.Text = valid ? _selectedGrid!.Columns[i].Header as string ?? "" : "";
        _columnWidth.Value = valid ? Math.Clamp(_selectedGrid!.Columns[i].ActualWidth, 60, 600) : 120;
        _loadingColumn = false;
    }

    private void UpdateColumn()
    {
        if (_loadingColumn || _selectedGrid is null || _columnPicker is null || _columnHeader is null || _columnWidth is null) return;
        int i = _columnPicker.SelectedIndex;
        if (i < 0 || i >= _selectedGrid.Columns.Count) return;
        var column = _selectedGrid.Columns[i];
        // Do not replace bound or complex header objects.
        if (column.Header is string) column.Header = _columnHeader.Text;
        column.Width = new DataGridLength(_columnWidth.Value, DataGridLengthUnitType.Pixel);
        _columnPicker.Items[i] = $"{i + 1}: {column.Header}";
        _columnPicker.SelectedIndex = i;
        _values[_selectedKey + "|column:" + i] = new LayoutValues
        {
            Text = column.Header as string,
            Width = _columnWidth.Value
        };
    }

    private void ApplyColumns()
    {
        foreach (var (key, grid) in _elements)
        {
            if (grid is not DataGrid dataGrid) continue;
            for (int i = 0; i < dataGrid.Columns.Count; i++)
            {
                if (!_values.TryGetValue(key + "|column:" + i, out var values)) continue;
                var column = dataGrid.Columns[i];
                if (values.Text is { Length: <= 100 } caption && column.Header is string)
                    column.Header = caption;
                if (values.Width is double width && double.IsFinite(width) && width is >= 60 and <= 600)
                    column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
            }
        }
    }

    private static LayoutValues Capture(FrameworkElement element)
    {
        TryReadCaption(element, out var caption, out var target);
        return new LayoutValues
        {
            Margin = element.Margin.Left,
            Width = double.IsNaN(element.Width) ? 0 : element.Width,
            Height = double.IsNaN(element.Height) ? 0 : element.Height,
            FontSize = element is TextBlock tb ? tb.FontSize : element is Control c ? c.FontSize : null,
            Bold = element is TextBlock t ? t.FontWeight == FontWeights.Bold :
                element is Control ctrl ? ctrl.FontWeight == FontWeights.Bold : null,
            FontFamily = element is TextBlock ft ? ft.FontFamily.Source :
                element is Control fc ? fc.FontFamily.Source : null,
            HorizontalAlignment = element.HorizontalAlignment.ToString(),
            Text = target is TextBlock nav && NavigationLabelOverrides.TryGetOriginal(nav, out _)
                ? null : TryReadCaption(element, out _, out _) ? caption : null
        };
    }

    private void UndoSelected()
    {
        if (_selected is null || !_undo.Remove(_selectedKey, out var previous)) return;
        Apply(_selected, previous);
        _values.Remove(_selectedKey);
        if (_undoButton is not null) _undoButton.IsEnabled = false;
        Select(_selected);
    }

    private void ClearHighlight()
    {
        if (_highlight is not null && _highlightLayer is not null)
            _highlightLayer.Remove(_highlight);
        _highlight = null;
        _highlightLayer = null;
    }

    private void ShowHighlight(FrameworkElement element)
    {
        ClearHighlight();
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return;
        _highlight = new SelectionAdorner(element, (dx, dy, resize) => DragSelected(element, dx, dy, resize));
        _highlightLayer = layer;
        layer.Add(_highlight);
    }

    private void DragSelected(FrameworkElement element, double dx, double dy, bool resize)
    {
        if (!_editing || !ReferenceEquals(element, _selected)) return;
        if (!_undo.ContainsKey(_selectedKey)) _undo[_selectedKey] = Capture(element);
        if (resize)
        {
            element.Width = Math.Clamp((double.IsNaN(element.Width) ? element.ActualWidth : element.Width) + dx, 30, 480);
            element.Height = Math.Clamp((double.IsNaN(element.Height) ? element.ActualHeight : element.Height) + dy, 20, 160);
        }
        else
        {
            // Relative offset via margin; does not reparent elements or modify business behavior.
            var m = element.Margin;
            element.Margin = new Thickness(Math.Clamp(m.Left + dx, 0, 60), Math.Clamp(m.Top + dy, 0, 60),
                Math.Clamp(m.Right - dx, 0, 60), Math.Clamp(m.Bottom - dy, 0, 60));
        }
        _values[_selectedKey] = Capture(element);
        if (_undoButton is not null) _undoButton.IsEnabled = true;
        Select(element);
    }

    private sealed class SelectionAdorner : Adorner
    {
        private readonly Pen _outline = new(Brushes.DeepSkyBlue, 2);
        private readonly Action<double, double, bool> _onDrag;
        private Point _last;
        private bool _dragging;
        private bool _resize;
        public SelectionAdorner(UIElement element, Action<double, double, bool> onDrag) : base(element)
        {
            _onDrag = onDrag;
            IsHitTestVisible = true;
            _outline.Freeze();
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
        {
            var point = parameters.HitPoint;
            var size = AdornedElement.RenderSize;
            bool corner = point.X >= size.Width - 14 && point.Y >= size.Height - 14;
            bool edge = point.X <= 7 || point.Y <= 7;
            return corner || edge ? new PointHitTestResult(this, point) : null;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _last = e.GetPosition(this);
            var size = AdornedElement.RenderSize;
            _resize = _last.X >= size.Width - 14 && _last.Y >= size.Height - 14;
            _dragging = true;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var current = e.GetPosition(this);
            var delta = current - _last;
            _last = current;
            _onDrag(delta.X, delta.Y, _resize);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var bounds = new Rect(AdornedElement.RenderSize);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            dc.DrawRectangle(null, _outline, bounds);
            dc.DrawRectangle(Brushes.DeepSkyBlue, null,
                new Rect(Math.Max(0, bounds.Width - 12), Math.Max(0, bounds.Height - 12), 12, 12));
            dc.DrawRectangle(Brushes.DeepSkyBlue, null, new Rect(0, 0, 7, 7));
        }
    }

    private static void Apply(FrameworkElement element, LayoutValues values)
    {
        if (values.Margin is double m && double.IsFinite(m) && m is >= 0 and <= 60)
            element.Margin = new Thickness(m);
        if (values.Width is double w && double.IsFinite(w) && w is >= 0 and <= 480)
            element.Width = w == 0 ? double.NaN : w;
        if (values.Height is double h && double.IsFinite(h) && h is >= 0 and <= 160)
            element.Height = h == 0 ? double.NaN : h;
        if (values.FontSize is double fs && double.IsFinite(fs) && fs is >= 8 and <= 40)
        {
            if (element is TextBlock tb) tb.FontSize = fs;
            else if (element is Control c) c.FontSize = fs;
        }
        if (values.Bold is bool bold)
        {
            if (element is TextBlock tb) tb.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            else if (element is Control c) c.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        }
        if (values.FontFamily is "Segoe UI" or "Arial" or "Calibri" or "Consolas" or "Georgia" or "Verdana")
        {
            var family = new FontFamily(values.FontFamily);
            if (element is TextBlock tb) tb.FontFamily = family;
            else if (element is Control c) c.FontFamily = family;
        }
        if (Enum.TryParse<System.Windows.HorizontalAlignment>(values.HorizontalAlignment, out var alignment))
            element.HorizontalAlignment = alignment;
        if (values.Text is { Length: <= 100 } text)
            UpdateCaption(element, text);
    }

    // Only plain local captions are editable. Never replace bindings, dynamic resources,
    // commands, template content, or labels referring to other controls.
    private static TextBlock? FindCaptionText(DependencyObject root)
    {
        // Search visual descendants only. Never inspect editable fields or grid rows.
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        int visited = 0;
        while (pending.Count > 0 && visited++ < 100)
        {
            var node = pending.Dequeue();
            if (!ReferenceEquals(node, root) && node is TextBlock text &&
                (TryReadStaticCaption(text, out _) || NavigationLabelOverrides.TryGetOriginal(text, out _)))
                return text;
            if (!ReferenceEquals(node, root) && node is TextBox or PasswordBox or DataGridRow) continue;
            foreach (var child in Children(node)) pending.Enqueue(child);
        }
        return null;
    }

    private static bool TryReadCaption(FrameworkElement element, out string caption, out TextBlock? target)
    {
        target = null;
        if (TryReadStaticCaption(element, out caption)) return true;
        if (element is TextBlock nav && NavigationLabelOverrides.TryGetOriginal(nav, out _))
        {
            caption = nav.Text;
            target = nav;
            return true;
        }
        if (element is Button or Label or GroupBox or CheckBox or RadioButton)
        {
            target = FindCaptionText(element);
            if (target is not null)
            {
                caption = target.Text;
                return true;
            }
        }
        caption = "";
        return false;
    }

    private static void UpdateCaption(FrameworkElement element, string caption)
    {
        if (!TryReadCaption(element, out _, out var target)) return;
        var editable = (FrameworkElement?)target ?? element;
        if (editable is TextBlock nav && NavigationLabelOverrides.TryGetOriginal(nav, out var original))
        {
            NavigationLabelOverrides.Set(original, caption);
            BindingOperations.GetBindingExpression(nav, TextBlock.TextProperty)?.UpdateTarget();
        }
        else SetStaticCaption(editable, caption);
    }

    private static bool TryReadStaticCaption(FrameworkElement element, out string caption)
    {
        caption = "";
        if (element is TextBlock text)
        {
            if (BindingOperations.IsDataBound(text, TextBlock.TextProperty)) return false;
            if (text.ReadLocalValue(TextBlock.TextProperty) is not string value) return false;
            if (text.Inlines.Count > 1) return false;
            caption = value;
            return true;
        }
        if (element is ContentControl content && element is Button or Label or GroupBox or CheckBox or RadioButton)
        {
            if (BindingOperations.IsDataBound(content, ContentControl.ContentProperty)) return false;
            if (content.ReadLocalValue(ContentControl.ContentProperty) is not string value) return false;
            caption = value;
            return true;
        }
        return false;
    }

    private static void SetStaticCaption(FrameworkElement element, string caption)
    {
        if (!TryReadStaticCaption(element, out _)) return;
        if (element is TextBlock text) text.SetCurrentValue(TextBlock.TextProperty, caption);
        else if (element is ContentControl content) content.SetCurrentValue(ContentControl.ContentProperty, caption);
    }

    private static void RefreshNavigationLabels(DependencyObject node)
    {
        if (node is TextBlock tb && NavigationLabelOverrides.TryGetOriginal(tb, out _))
            BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty)?.UpdateTarget();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            RefreshNavigationLabels(VisualTreeHelper.GetChild(node, i));
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++) yield return VisualTreeHelper.GetChild(parent, i);
    }

    private void RefreshTree()
    {
        _elements.Clear();
        _tree?.Items.Clear();
        _activeScreen = (_main.FindName("DesignerPageTitle") as TextBlock)?.Text ?? "Főablak";
        var screen = new TreeViewItem { Header = "Képernyő: " + _activeScreen, IsExpanded = true };
        _tree?.Items.Add(screen);
        // Traverse the actual live visual tree, including module content. Limit depth/size.
        int remaining = 650;
        void Walk(DependencyObject node, ItemsControl parent, string path, int depth)
        {
            if (depth > 18 || remaining <= 0) return;
            int index = 0;
            foreach (var child in Children(node))
            {
                if (--remaining < 0) break;
                string key = path + "/" + index++;
                ItemsControl nextParent = parent;
                if (child is FrameworkElement fe && fe is not Window)
                {
                    // Skip volatile data rows and templated table internals.
                    if (fe is DataGridRow or DataGridCell or DataGridColumnHeader or TextBox or PasswordBox) continue;
                    if (fe is TextBlock or Button or Border or Grid or StackPanel or DockPanel or ContentControl or Label or DataGrid or ScrollViewer or WrapPanel or CheckBox or RadioButton or GroupBox)
                    {
                        string identity = !string.IsNullOrWhiteSpace(fe.Name) ? fe.Name : fe.GetType().Name;
                        if (TryReadCaption(fe, out var preview, out _))
                            identity += "  [" + (preview.Length > 36 ? preview[..36] + "..." : preview) + "]";
                        string scope = key.StartsWith("/", StringComparison.Ordinal) ? key : "/" + key;
                        string id = _activeScreen + "|" + scope;
                        _elements[id] = fe;
                        var item = new TreeViewItem { Header = identity, Tag = id };
                        parent.Items.Add(item);
                        nextParent = item;
                    }
                }
                Walk(child, nextParent, key, depth + 1);
            }
        }
        Walk(_main, screen, "", 0);
        _selectionLabel?.SetCurrentValue(TextBlock.TextProperty, $"{_activeScreen}: {_elements.Count} szerkeszthető elem");
    }

    private void ApplyCurrentScreen()
    {
        foreach (var (key, value) in _values)
            if (_elements.TryGetValue(key, out var element)) Apply(element, value);
        ApplyColumns();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 131072) return;
            var values = JsonSerializer.Deserialize<Dictionary<string, LayoutValues>>(File.ReadAllText(FilePath));
            if (values is null || values.Count > 650) return;
            foreach (var (key, value) in values)
            {
                if (value is null) continue;
                _values[key] = value;
                if (_elements.TryGetValue(key, out var element)) Apply(element, value);
            }
            ApplyColumns();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(_main, "Az elrendezés nem tölthető be: " + ex.Message,
                "PC-Impressz Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
            NavigationLabelOverrides.Save();
            MessageBox.Show(_inspector!, "Elmentve. A módosítások a következő indításkor is betöltődnek.",
                "PC-Impressz Designer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(_inspector!, ex.Message, "Mentési hiba", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Reset()
    {
        if (MessageBox.Show(_inspector!, "Törlöd az összes mentett vizuális módosítást?",
                "Visszaállítás", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            _values.Clear();
            NavigationLabelOverrides.Reset();
            RefreshNavigationLabels(_main);
            MessageBox.Show(_inspector!, "A gyári elrendezés a főprogram következő indításakor áll vissza.");
        }
        catch (IOException ex) { MessageBox.Show(_inspector!, ex.Message); }
    }

    private void EndEditing()
    {
        ClearHighlight();
        _editing = false;
        _inspector?.Close();
    }
}
