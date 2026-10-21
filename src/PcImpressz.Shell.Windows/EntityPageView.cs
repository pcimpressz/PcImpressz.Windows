using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PcImpressz.Shell.Windows;

// The module supplies field and column metadata; the Shell contains no partner assembly reference.
public sealed class EntityPageView : UserControl
{
    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(nameof(ViewMode), typeof(string), typeof(EntityPageView),
        new PropertyMetadata("split", (target, _) => ((EntityPageView)target)._applyLayout?.Invoke()));
    public string ViewMode { get => (string)GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }
    private Action? _applyLayout;
    public EntityPageView()
    {
        var root = new StackPanel();
        var toolbar = new WrapPanel { Margin = new(0,0,0,18) };
        var search = Input(); search.Width = 230; search.Margin = new(0,0,10,10);
        search.SetBinding(TextBox.TextProperty, new Binding("Search") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        toolbar.Children.Add(search);
        toolbar.Children.Add(ListGrid.MatchCount());
        var filter = new ComboBox { Width = 180, Margin = new(0,0,10,10), DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        filter.SetBinding(ComboBox.SelectedValueProperty, new Binding("Filter") { Mode = BindingMode.TwoWay });
        toolbar.Children.Add(filter);
        toolbar.Children.Add(ActionButton("Keresés", "SearchCommand"));
        var create = ActionButton("Új", "NewCommand"); toolbar.Children.Add(create);
        toolbar.Children.Add(ActionButton("CSV export", "ExportCommand"));
        toolbar.Children.Add(ListGrid.ColumnButton(() => DataContext as IColumnList));
        root.Children.Add(toolbar);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,16) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); message.SetBinding(TextBlock.TextProperty, new Binding("Message"));
        root.Children.Add(message);
        var applied = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) };
        applied.SetBinding(TextBlock.TextProperty, new Binding("AppliedSearchLabel")); root.Children.Add(applied);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var listPanel = new StackPanel();
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 360, MaxHeight = 480, GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new(0), FontSize = 13, RowHeight = 44, ColumnHeaderHeight = 40 };
        grid.SetResourceReference(Control.FontSizeProperty, "BodySize");
        grid.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "ElevatedBrush");
        grid.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        grid.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
            Application.Current?.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ElevatedBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,8,8,8)));
        headerStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        grid.ColumnHeaderStyle = headerStyle;
        ListGrid.Prepare(grid, () => DataContext as IColumnList);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10,4,10,4)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentSoftBrush")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        cellStyle.Triggers.Add(selected); grid.CellStyle = cellStyle;
        listPanel.Children.Add(grid);
        var pager = new DockPanel { Margin = new(0,14,0,0) };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        count.SetBinding(TextBlock.TextProperty, new Binding("CountLabel")); DockPanel.SetDock(count, Dock.Left); pager.Children.Add(count);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ActionButton("← Előző", "PreviousCommand")); buttons.Children.Add(ActionButton("Következő →", "NextCommand"));
        pager.Children.Add(buttons); listPanel.Children.Add(pager); body.Children.Add(listPanel);
        var editorBorder = new Border { Padding = new(24), CornerRadius = new(14), BorderThickness = new(1), Margin = new(20,0,0,0) };
        editorBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); editorBorder.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var editor = new StackPanel();
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,20) };
        title.SetBinding(TextBlock.TextProperty, new Binding("EditorTitle")); editor.Children.Add(title);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        fields.SetBinding(IsEnabledProperty, new Binding("CanWrite"));
        editor.Children.Add(fields);
        var taxHost=new StackPanel();editor.Children.Insert(1,taxHost);
        var advancedFields=new Grid();advancedFields.ColumnDefinitions.Add(new());advancedFields.ColumnDefinitions.Add(new());advancedFields.SetBinding(IsEnabledProperty,new Binding("CanWrite"));
        var advanced=new Expander{Header="Bővített adatok kitöltése",Content=advancedFields,Margin=new(0,16,0,0),IsExpanded=false};advanced.SetBinding(VisibilityProperty,new Binding("IsCompactMaster"){Converter=new BooleanToVisibilityConverter()});editor.Children.Add(advanced);
        var save = new WrapPanel { Margin = new(0,20,0,0) };
        var saveButton = ActionButton("Mentés", "SaveCommand"); saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Children.Add(saveButton);save.Children.Add(ActionButton("Módosítások elvetése","DiscardChangesCommand"));
        var secondaryActions=new WrapPanel();secondaryActions.Children.Add(ActionButton("Archiválás", "ArchiveCommand"));
        secondaryActions.Children.Add(ActionButton("Feliratok és elrendezés – admin", "EditLayoutCommand"));
        var dossier = ActionButton("Dosszié", "DossierCommand");
        dossier.SetBinding(VisibilityProperty, new Binding("HasDossier") { Converter = new BooleanToVisibilityConverter() });
        dossier.SetBinding(IsEnabledProperty, new Binding("CanOpenDossier"));
        secondaryActions.Children.Add(dossier); editor.Children.Add(save);editor.Children.Add(new Expander{Header="További műveletek",Content=secondaryActions,Margin=new(0,16,0,0)});
        editorBorder.Child = editor; Grid.SetColumn(editorBorder,1); body.Children.Add(editorBorder);
        var overviewBorder=new Border{Padding=new(22),CornerRadius=new(14),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Top,MaxWidth=540};overviewBorder.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");overviewBorder.SetResourceReference(Border.BorderBrushProperty,"StrokeBrush");overviewBorder.SetBinding(VisibilityProperty,new Binding("IsCompactMaster"){Converter=new BooleanToVisibilityConverter()});
        var overview=new StackPanel();overviewBorder.Child=overview;
        var overviewEyebrow=new TextBlock{Text="PARTNER ÁTTEKINTÉS",Margin=new(0,0,0,12)};overviewEyebrow.SetBinding(TextBlock.TextProperty,"OverviewEyebrow");overviewEyebrow.SetResourceReference(StyleProperty,"Eyebrow");overview.Children.Add(overviewEyebrow);
        var overviewName=new TextBlock{FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10)};overviewName.SetBinding(TextBlock.TextProperty,"PartnerOverviewTitle");overview.Children.Add(overviewName);
        var overviewSubtitle=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,18)};overviewSubtitle.SetResourceReference(StyleProperty,"Muted");overviewSubtitle.SetBinding(TextBlock.TextProperty,"PartnerOverviewSubtitle");overview.Children.Add(overviewSubtitle);
        var overviewContact=new TextBlock{TextWrapping=TextWrapping.Wrap,LineHeight=24,Margin=new(0,0,0,20)};overviewContact.SetBinding(TextBlock.TextProperty,"PartnerOverviewContact");overview.Children.Add(overviewContact);
        var cards=new ItemsControl();cards.SetBinding(ItemsControl.ItemsSourceProperty,"PartnerOverviewCards");var cardTemplate=new DataTemplate();var cardPanel=new FrameworkElementFactory(typeof(StackPanel));cardPanel.SetValue(MarginProperty,new Thickness(0,0,0,18));var cardLabel=new FrameworkElementFactory(typeof(TextBlock));cardLabel.SetBinding(TextBlock.TextProperty,new Binding("Label"));cardLabel.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);cardPanel.AppendChild(cardLabel);var cardValue=new FrameworkElementFactory(typeof(TextBlock));cardValue.SetBinding(TextBlock.TextProperty,new Binding("Value"));cardValue.SetValue(TextBlock.FontSizeProperty,21d);cardValue.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);cardValue.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);cardPanel.AppendChild(cardValue);cardTemplate.VisualTree=cardPanel;cards.ItemTemplate=cardTemplate;overview.Children.Add(cards);
        var overviewNotice=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};overviewNotice.SetResourceReference(StyleProperty,"Muted");overviewNotice.SetBinding(TextBlock.TextProperty,"PartnerOverviewNotice");overview.Children.Add(overviewNotice);
        var fullDossier=ActionButton("Részletes dosszié megnyitása","DossierCommand");fullDossier.SetBinding(IsEnabledProperty,"CanOpenDossier");fullDossier.SetBinding(VisibilityProperty,new Binding("HasDossier"){Converter=new BooleanToVisibilityConverter()});overview.Children.Add(fullDossier);var stockButton=ActionButton("Készlet és mozgások megnyitása","OpenProductStockCommand");stockButton.SetBinding(VisibilityProperty,new Binding("IsProductMaster"){Converter=new BooleanToVisibilityConverter()});overview.Children.Add(stockButton);overviewBorder.SizeChanged+=(_,_)=>{var width=Math.Max(0,overviewBorder.ActualWidth-46);foreach(var text in overview.Children.OfType<TextBlock>())text.MaxWidth=width;cards.MaxWidth=width;};body.Children.Add(overviewBorder);
        body.SetBinding(VisibilityProperty, new Binding("ShowPartnerList") { Converter = new BooleanToVisibilityConverter() });
        root.Children.Add(body);
        root.Children.Add(DossierPage());
        Content = root;
        var searchHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10), Visibility = Visibility.Collapsed }; root.Children.Insert(1, searchHelp);
        grid.MouseDoubleClick += (_, args) => { if (ViewMode == "list" && DataContext is EntityPageViewModel page && args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow) page.RequestEditSelected(); };
        void RenderFields(EntityPageViewModel vm)
        {
            taxHost.Children.Clear();
            if(vm.IsPartnerMaster&&vm.Fields.FirstOrDefault(f=>f.Definition.Id=="taxId") is {} taxField)
            {
                taxHost.Children.Add(new TextBlock{Text=taxField.Label,Margin=new(0,0,0,7)});
                var taxRow=new Grid{Margin=new(0,0,0,16)};taxRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});taxRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var taxInput=Input();taxInput.DataContext=taxField;taxInput.SetBinding(TextBox.TextProperty,new Binding("Text"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});taxInput.SetBinding(IsEnabledProperty,new Binding("CanWrite"){Source=vm});taxRow.Children.Add(taxInput);
                var lookup=new Button{Content="Lekérés",ToolTip="Név és székhely lekérése a NAV-tól",MinWidth=90,Margin=new(10,0,0,0)};lookup.SetBinding(Button.CommandProperty,new Binding("LookupPartnerNavCommand"){Source=vm});lookup.SetBinding(IsEnabledProperty,new Binding("CanLookupPartnerNav"){Source=vm});Grid.SetColumn(lookup,1);taxRow.Children.Add(lookup);taxHost.Children.Add(taxRow);
            }
            var basic=new HashSet<string>(StringComparer.Ordinal){"name","taxId","country","postalCode","city","address","email","phone","isCustomer","isSupplier","isActive","notes"};
            if(vm.IsProductMaster)basic=new HashSet<string>(StringComparer.Ordinal){"name","sku","manufacturerPartNumber","barcode","unitCode","categoryCode","saleNetUnitPrice","saleVatRate","isActive","isSalable","isPurchaseable"};
            RenderGroup(fields,vm.Fields.Where(f=>f.Visible&&(!vm.IsPartnerMaster||f.Definition.Id!="taxId")&&(!vm.IsCompactMaster||f.Definition.Required||basic.Contains(f.Definition.Id))));
            RenderGroup(advancedFields,vm.Fields.Where(f=>f.Visible&&vm.IsCompactMaster&&!f.Definition.Required&&!basic.Contains(f.Definition.Id)));
        }
        void RenderGroup(Grid target,IEnumerable<EntityFieldViewModel> group)
        {
            target.Children.Clear(); target.RowDefinitions.Clear();
            var fieldRow = 0; var fieldColumn = 0;
            foreach (var field in group)
            {
                var wide = field.Span == 2;
                if (wide && fieldColumn > 0) { fieldRow++; fieldColumn = 0; }
                while (target.RowDefinitions.Count <= fieldRow) target.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var panel = new StackPanel { Margin = new(fieldColumn == 0 ? 0 : 10,0,wide || fieldColumn == 1 ? 0 : 10,16), DataContext = field };
                if (field.Definition.Kind == "boolean")
                {
                    var check = new CheckBox { Content = field.Label, Margin = new(0,8,0,4) };
                    check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Checked") { Mode = BindingMode.TwoWay }); panel.Children.Add(check);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = field.Label, Margin = new(0,0,0,7) });
                    var input = Input(); input.SetBinding(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                    if (field.Definition.Kind == "multiline") { input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 85; }
                    panel.Children.Add(input);
                }
                Grid.SetRow(panel,fieldRow); Grid.SetColumn(panel,fieldColumn); Grid.SetColumnSpan(panel,wide ? 2 : 1); target.Children.Add(panel);
                if (wide || fieldColumn == 1) { fieldRow++; fieldColumn = 0; } else fieldColumn = 1;
            }
        }
        EntityPageViewModel? observed = null;
        System.ComponentModel.PropertyChangedEventHandler? observer = null;
        Unloaded += (_, _) => { if (observed is not null && observer is not null) observed.PropertyChanged -= observer; };
        Loaded += (_, _) => { if (observed is not null && observer is not null) { observed.PropertyChanged -= observer; observed.PropertyChanged += observer; RenderFields(observed); ListGrid.Show(grid, observed); } };
        DataContextChanged += (_, _) =>
        {
            if (observed is not null && observer is not null) observed.PropertyChanged -= observer;
            observed = null; observer = null;
            if (DataContext is not EntityPageViewModel vm) return;
            search.ToolTip = vm.Labels.SearchHint;
            searchHelp.Text = vm.Labels.SearchHint;
            searchHelp.Visibility = ViewMode != "editor" && vm.Definition.ModuleId == "catalog" && vm.Definition.Id == "products" ? Visibility.Visible : Visibility.Collapsed;
            create.Content = vm.Labels.NewLabel;
            filter.ItemsSource = vm.Filters;
            if (filter.SelectedValue is null) filter.SelectedValue = vm.Filters[0].Value;
            ListGrid.Show(grid, vm);
            RenderFields(vm);
            observer = (_, args) =>
            {
                if (args.PropertyName == nameof(EntityPageViewModel.AppearanceRevision)) RenderFields(vm);
                if (args.PropertyName == nameof(EntityPageViewModel.DossierRevision)) RenderDossier(vm);
                if (args.PropertyName == nameof(EntityPageViewModel.LayoutRevision)) ListGrid.Show(grid, vm);
            };
            observed = vm; vm.PropertyChanged += observer;
            _applyLayout?.Invoke();
            if (ViewMode != "editor") _ = vm.LoadAsync();
        };
        _applyLayout = () =>
        {
            var separate = ViewMode is "list" or "editor";
            var editorOnly = ViewMode == "editor";
            toolbar.Visibility = editorOnly ? Visibility.Collapsed : Visibility.Visible;
            create.Visibility = ViewMode == "list" ? Visibility.Collapsed : Visibility.Visible;
            applied.Visibility = editorOnly ? Visibility.Collapsed : Visibility.Visible;
            searchHelp.Visibility = !editorOnly && DataContext is EntityPageViewModel page && page.Definition.ModuleId == "catalog" && page.Definition.Id == "products" ? Visibility.Visible : Visibility.Collapsed;
            listPanel.Visibility = editorOnly ? Visibility.Collapsed : Visibility.Visible;
            editorBorder.Visibility = ViewMode == "list" ? Visibility.Collapsed : Visibility.Visible;
            var partner=DataContext is EntityPageViewModel {IsCompactMaster:true};
            var narrow = separate || partner || ActualWidth < 980;
            editorBorder.MaxWidth=partner?680:DataContext is EntityPageViewModel pageVm&&pageVm.Definition.ModuleId=="catalog"&&pageVm.Definition.Id=="products"?900:680;
            editorBorder.HorizontalAlignment=HorizontalAlignment.Stretch;editorBorder.VerticalAlignment=VerticalAlignment.Top;
            Grid.SetColumn(editorBorder,narrow ? 0 : 1); Grid.SetRow(editorBorder,editorOnly ? 0 : narrow ? 1 : 0);
            Grid.SetColumnSpan(listPanel,narrow ? 2 : 1); Grid.SetColumnSpan(editorBorder,narrow ? 2 : 1);
            editorBorder.Margin = separate ? new Thickness(0) : narrow ? new Thickness(0,22,0,0) : new Thickness(20,0,0,0);
            grid.MinHeight = ViewMode == "list" ? 480 : 360; grid.MaxHeight = ViewMode == "list" ? 680 : 480;
            if(partner)
            {
                var sideBySide=ActualWidth>=1000&&ViewMode!="list";
                body.ColumnDefinitions[0].Width=new(1.35,GridUnitType.Star);body.ColumnDefinitions[1].Width=new(1,GridUnitType.Star);
                Grid.SetColumn(listPanel,0);Grid.SetRow(listPanel,0);Grid.SetColumnSpan(listPanel,2);
                Grid.SetRow(editorBorder,1);Grid.SetColumn(editorBorder,0);Grid.SetColumnSpan(editorBorder,sideBySide?1:2);editorBorder.Margin=new(0,20,sideBySide?20:0,0);
                Grid.SetRow(overviewBorder,sideBySide?1:2);Grid.SetColumn(overviewBorder,sideBySide?1:0);Grid.SetColumnSpan(overviewBorder,sideBySide?1:2);overviewBorder.Margin=new(0,20,0,0);overviewBorder.HorizontalAlignment=HorizontalAlignment.Stretch;
            }
            else{body.ColumnDefinitions[0].Width=new(3,GridUnitType.Star);body.ColumnDefinitions[1].Width=new(2,GridUnitType.Star);}
            search.Width = ViewMode == "list" ? 360 : 230;
        };
        SizeChanged += (_, _) => _applyLayout();
        _applyLayout();
    }
    private WrapPanel cardHost = null!;
    private StackPanel sectionHost = null!;
    private UIElement DossierPage()
    {
        var panel = new StackPanel();
        var back = new Button { Content = "Vissza a partnerekhez", Margin = new(0, 0, 8, 14), HorizontalAlignment = HorizontalAlignment.Left };
        back.SetBinding(Button.CommandProperty, new Binding("DossierBackCommand"));
        panel.Children.Add(back);
        var title = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        title.SetBinding(TextBlock.TextProperty, new Binding("DossierTitle")); panel.Children.Add(title);
        var subtitle = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 16) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        subtitle.SetBinding(TextBlock.TextProperty, new Binding("DossierSubtitle")); panel.Children.Add(subtitle);
        var filterBox = new Border { Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), Margin = new(0, 0, 0, 16) };
        filterBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        filterBox.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var period = new WrapPanel();
        var periodLabel = new TextBlock { Text = "Időszak szűrése", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 10) };
        period.Children.Add(periodLabel);
        period.Children.Add(DateInput("PeriodFrom")); period.Children.Add(DateInput("PeriodTo"));
        var filter = ActionButton("Szűrés", "DossierFilterCommand"); filter.SetResourceReference(StyleProperty, "PrimaryButton");
        period.Children.Add(filter);
        period.Children.Add(ActionButton("Mind", "PeriodAllCommand"));
        period.Children.Add(ActionButton("Idén", "PeriodYearCommand"));
        period.Children.Add(ActionButton("Tavaly", "PeriodPreviousYearCommand"));
        period.Children.Add(ActionButton("3 hónap", "PeriodQuarterCommand"));
        filterBox.Child = period; panel.Children.Add(filterBox);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        notice.SetBinding(TextBlock.TextProperty, new Binding("DossierNotice")); panel.Children.Add(notice);
        cardHost = new WrapPanel(); sectionHost = new StackPanel();
        panel.Children.Add(cardHost); panel.Children.Add(sectionHost);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetBinding(VisibilityProperty, new Binding("IsDossierOpen") { Converter = new BooleanToVisibilityConverter() });
        return scroll;
    }
    private static DatePicker DateInput(string path)
    {
        var picker = new DatePicker { Width = 150, Margin = new(0, 0, 8, 10) };
        picker.SetBinding(DatePicker.SelectedDateProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return picker;
    }
    private void RenderDossier(EntityPageViewModel vm)
    {
        cardHost.Children.Clear(); sectionHost.Children.Clear();
        foreach (var card in vm.DossierCards)
        {
            var box = new Border { Padding = new(12), Margin = new(0, 0, 8, 8), CornerRadius = new(10), MinWidth = 150 };
            box.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            var stack = new StackPanel();
            var label = new TextBlock { Text = card.Label, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(label);
            stack.Children.Add(new TextBlock { Text = card.Value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 0) });
            box.Child = stack; cardHost.Children.Add(box);
        }
        foreach (var section in vm.DossierSections)
        {
            var block = new StackPanel { Margin = new(0, 8, 0, 0) };
            block.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, FontSize = 16 });
            var source = new TextBlock { Text = section.Source, Margin = new(0, 4, 0, 4), FontSize = 12 };
            source.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            block.Children.Add(source);
            block.Children.Add(new TextBlock { Text = section.Detail, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
            foreach (var line in section.Lines) block.Children.Add(line.Items.Length > 0 ? InvoiceRow(line) : PlainRow(line));
            sectionHost.Children.Add(block);
        }
    }
    private static UIElement InvoiceRow(EntityPageViewModel.DossierLineRow line)
    {
        var expander = new Expander { IsExpanded = false, Margin = new(0, 4, 0, 0) };
        expander.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = line.Title, FontWeight = FontWeights.SemiBold });
        var extra = new TextBlock { Text = line.Detail + " · kattints a tételekhez", FontSize = 12 };
        extra.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(extra);
        var amount = new TextBlock { Text = line.Amount, FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(amount, 1); header.Children.Add(text); header.Children.Add(amount);
        expander.Header = header;
        var items = new StackPanel { Margin = new(28, 6, 0, 8) };
        foreach (var item in line.Items)
        {
            var row = new Grid { Margin = new(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap });
            var quantity = new TextBlock { Text = item.Quantity, Margin = new(12, 0, 12, 0) };
            quantity.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Grid.SetColumn(quantity, 1);
            var net = new TextBlock { Text = item.Amount };
            Grid.SetColumn(net, 2);
            row.Children.Add(quantity); row.Children.Add(net); items.Children.Add(row);
        }
        expander.Content = items;
        return expander;
    }
    private static Grid PlainRow(EntityPageViewModel.DossierLineRow line)
    {
        var row = new Grid { Margin = new(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = line.Title, TextWrapping = TextWrapping.Wrap });
        var extra = new TextBlock { Text = line.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        extra.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(extra);
        var amount = new TextBlock { Text = line.Amount, FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 0, 0) };
        Grid.SetColumn(amount, 1); row.Children.Add(text); row.Children.Add(amount);
        return row;
    }
    private static TextBox Input()
    {
        var box = new TextBox { Padding = new(10), MinHeight = 38, BorderThickness = new(1) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush"); box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush"); return box;
    }
    private static Button ActionButton(string label, string command)
    {
        var button = new Button { Content = label, Margin = new(0,0,8,10) };
        button.SetBinding(Button.CommandProperty, new Binding(command));
        button.SetBinding(IsEnabledProperty, new Binding(command is "SaveCommand" or "ArchiveCommand" or "NewCommand" ? "CanWrite" : command=="EditLayoutCommand"?"CanAdmin":command=="ExportCommand"?"CanExport":"IsNotBusy"));
        return button;
    }
}
