using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed class ArrangeableWorkspace:UserControl
{
    private readonly string _surface;
    private readonly WrapPanel _panels=new();
    private readonly List<PanelCard> _cards=[];
    private readonly Button _edit=new(){Content="Nézet rendezése – admin",HorizontalAlignment=HorizontalAlignment.Left};
    private readonly WrapPanel _actions=new(){Visibility=Visibility.Collapsed};
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,8)};
    private IWorkspaceLayoutContext? _context;private Guid? _company;private int _generation;private bool _loading,_saving,_editing;
    private UiWorkspaceLayout _saved=new(0,[],[]);
    public bool UsePageScrolling {get;set;}
    internal bool IsArranging=>_editing;
    internal UiPanelPlacement[] Placements=>_cards.Select(c=>new UiPanelPlacement(c.Id,c.DesiredWidth,c.DesiredHeight)).ToArray();
    public ArrangeableWorkspace(string surface="main")
    {
        _surface=surface;var root=new StackPanel();root.Children.Add(_edit);root.Children.Add(_actions);root.Children.Add(_message);root.Children.Add(_panels);Content=root;
        _message.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        foreach(var (label,action) in new (string,Action)[]{("Nézet mentése",()=>_=SaveAsync()),("Mégse",Cancel),("Alapnézet",()=>Apply([])),("Előző mentés",()=>Apply(_saved.History.LastOrDefault()?.Panels??[]))})
        {
            var button=new Button{Content=label,Margin=new(0,0,8,0)};button.Click+=(_,_)=>{if(_editing&&!_saving)action();};_actions.Children.Add(button);
        }
        _edit.Click+=(_,_)=>BeginArrange();
        SizeChanged+=(_,_)=>MeasureCards();
        DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();Unloaded+=(_,_)=>{if(_context is not null)_context.PropertyChanged-=ContextChanged;_generation++;_context=null;Cancel();};
    }
    public void AddPanel(string id,string title,FrameworkElement content,double defaultWidth=0)
    {
        if(_cards.Any(c=>c.Id==id))throw new ArgumentException("Ismételt panelazonosító.");
        var card=new PanelCard(this,id,title,content,defaultWidth);_cards.Add(card);_panels.Children.Add(card);MeasureCards();
    }
    public static ArrangeableWorkspace Split(StackPanel root,UIElement main,string id,string title)
    {
        var children=root.Children.Cast<UIElement>().ToArray();var index=Array.IndexOf(children,main);if(index<0)throw new ArgumentException("Hiányzó főpanel.");
        root.Children.Clear();var before=new StackPanel();var after=new StackPanel();foreach(var child in children.Take(index))before.Children.Add(child);foreach(var child in children.Skip(index+1))after.Children.Add(child);
        var workspace=new ArrangeableWorkspace();if(before.Children.Count>0)workspace.AddPanel("tools","Keresés és műveletek",before);
        workspace.AddPanel(id,title,(FrameworkElement)main);if(after.Children.Count>0)workspace.AddPanel("details","További adatok",after);return workspace;
    }
    private void Connect()
    {
        if(_context is not null)_context.PropertyChanged-=ContextChanged;
        _context=DataContext as IWorkspaceLayoutContext;if(_context is not null)_context.PropertyChanged+=ContextChanged;
        foreach(var card in _cards)card.DataContext=DataContext;
        _company=null;_generation++;_loading=false;_saved=new(0,[],[]);Apply([]);Cancel();ContextChanged(this,new(null));
    }
    private void ContextChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(_context?.WorkspaceCompany!=_company)
        {
            _company=_context?.WorkspaceCompany;_generation++;_saved=new(0,[],[]);_loading=false;Cancel();Apply([]);_=_company is null?Task.CompletedTask:LoadAsync();
        }
        var can=_context?.CanArrangeWorkspace==true&&_context.WorkspaceBusy==false&&!_saving&&!_loading;
        _edit.Visibility=!_editing&&_context?.CanArrangeWorkspace==true?Visibility.Visible:Visibility.Collapsed;_edit.IsEnabled=can;
        if(_editing&&!can&&!_saving)Cancel();
    }
    private async Task LoadAsync()
    {
        if(_context is not {} context||_company is not Guid company)return;var generation=_generation;_loading=true;
        try
        {
            var result=await context.SendWorkspaceAsync("ui.workspace.read",new UiWorkspaceRequest(company,context.WorkspacePageKey,_surface));
            if(generation!=_generation)return;
            _saved=result.Deserialize<UiWorkspaceLayout>()??new(0,[],[]);Apply(_saved.Panels);_message.Text="";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_generation)_message.Text="Alapnézet látható. Az egyéni nézet nem tölthető be: "+ex.Message;}
        finally{if(generation==_generation){_loading=false;ContextChanged(this,new(null));}}
    }
    internal bool BeginArrange()
    {
        if(_context?.CanArrangeWorkspace!=true||_context.WorkspaceBusy||_loading||_saving)return false;
        _editing=true;_edit.Visibility=Visibility.Collapsed;_actions.Visibility=Visibility.Visible;_message.Text="Húzd a panel fejlécét a sorrendhez, az alsó sarkát a mérethez. Dupla kattintás a sarokra: automatikus magasság. A változás a Nézet mentése gombbal marad meg.";
        foreach(var card in _cards)card.SetEditing(true);return true;
    }
    internal void MovePanel(string id,int target)
    {
        if(!_editing||_context?.CanArrangeWorkspace!=true)return;var card=_cards.Single(c=>c.Id==id);_cards.Remove(card);_cards.Insert(Math.Clamp(target,0,_cards.Count),card);RenderOrder();
    }
    internal void ResizePanel(string id,double width,double height)
    {
        if(!_editing||_context?.CanArrangeWorkspace!=true||!double.IsFinite(width)||!double.IsFinite(height))return;
        var card=_cards.Single(c=>c.Id==id);card.DesiredWidth=width==0?0:Math.Clamp(width,280,1800);card.DesiredHeight=height==0?0:Math.Clamp(height,160,1600);MeasureCards();
    }
    internal void Cancel(){_editing=false;_actions.Visibility=Visibility.Collapsed;foreach(var c in _cards)c.SetEditing(false);Apply(_saved.Panels);_edit.Visibility=_context?.CanArrangeWorkspace==true?Visibility.Visible:Visibility.Collapsed;_message.Text="";}
    internal async Task SaveAsync()
    {
        if(!_editing||_context?.CanArrangeWorkspace!=true||_context.WorkspaceBusy||_company is not Guid company||_saving)return;
        var context=_context;var generation=_generation;
        // The connection attaches the current session token to the request; the server authenticates it again.
        _saving=true;_actions.IsEnabled=false;
        try
        {
            var result=await context.SendWorkspaceAsync("ui.workspace.save",new UiWorkspaceSave(company,context.WorkspacePageKey,_surface,"",_saved.Revision,Placements));
            if(generation!=_generation)return;_saved=result.Deserialize<UiWorkspaceLayout>()??throw new InvalidOperationException("Hiányzó mentett nézet.");Cancel();_message.Text="A nézet cégenként mentve. Új megnyitáskor is ezt használjuk.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_generation)_message.Text="A nézet nem mentődött: "+ex.Message;}
        finally{_saving=false;_actions.IsEnabled=true;ContextChanged(this,new(null));}
    }
    private void Apply(UiPanelPlacement[] panels)
    {
        var defaults=_cards.OrderBy(c=>c.InitialOrder).ToArray();var ordered=panels.Select(p=>defaults.FirstOrDefault(c=>c.Id==p.Id)).OfType<PanelCard>().Distinct().Concat(defaults.Where(c=>panels.All(p=>p.Id!=c.Id))).ToArray();
        _cards.Clear();_cards.AddRange(ordered);
        foreach(var c in _cards){var p=panels.FirstOrDefault(p=>p.Id==c.Id);c.DesiredWidth=p?.Width??c.DefaultWidth;c.DesiredHeight=p?.Height??0;}
        RenderOrder();
    }
    private void RenderOrder(){_panels.Children.Clear();foreach(var c in _cards)_panels.Children.Add(c);MeasureCards();}
    private void MeasureCards(){var available=Math.Max(200,ActualWidth-12);foreach(var c in _cards){c.Width=Math.Min(available,c.DesiredWidth==0?available:c.DesiredWidth);c.Body.Height=UsePageScrolling||c.DesiredHeight==0?double.NaN:c.DesiredHeight;c.Body.VerticalScrollBarVisibility=UsePageScrolling?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;}}
    private sealed class PanelCard:Border
    {
        private readonly ArrangeableWorkspace _owner;private readonly DockPanel _header;private readonly Thumb _resize;private Point _down;
        public string Id {get;}public double DesiredWidth {get;set;}public double DesiredHeight {get;set;}public double DefaultWidth {get;}public int InitialOrder {get;}
        public ScrollViewer Body {get;}
        public PanelCard(ArrangeableWorkspace owner,string id,string title,FrameworkElement content,double width)
        {
            (_owner,Id,DefaultWidth,DesiredWidth,InitialOrder)=(owner,id,width,width,owner._cards.Count);VerticalAlignment=VerticalAlignment.Top;Margin=new(0,0,12,12);Padding=new(6);CornerRadius=new(8);BorderThickness=new(1);
            SetResourceReference(BorderBrushProperty,"StrokeBrush");SetResourceReference(BackgroundProperty,"SurfaceBrush");DataContext=owner.DataContext;
            SetBinding(VisibilityProperty,new Binding("Visibility"){Source=content});
            var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            _header=new DockPanel{LastChildFill=false,Visibility=Visibility.Collapsed,Margin=new(0,0,0,8),Cursor=Cursors.SizeAll};_header.Children.Add(new TextBlock{Text="↕ "+title,FontWeight=FontWeights.SemiBold,Padding=new(5)});
            foreach(var (text,delta) in new[]{("↑",-1),("↓",1)}){var b=new Button{Content=text,Padding=new(8,2,8,2),Margin=new(5,0,0,0)};b.Click+=(_,_)=>owner.MovePanel(id,owner._cards.IndexOf(this)+delta);_header.Children.Add(b);}grid.Children.Add(_header);
            Body=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(Body,1);grid.Children.Add(Body);Body.PreviewMouseWheel+=ChainWheel;
            _resize=new Thumb{Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=Cursors.SizeNWSE,Visibility=Visibility.Collapsed,ToolTip="Húzd a panel méretéhez; dupla kattintás: automatikus magasság"};var resizeGlyph=new FrameworkElementFactory(typeof(TextBlock));resizeGlyph.SetValue(TextBlock.TextProperty,"◢");resizeGlyph.SetValue(TextBlock.FontSizeProperty,22d);resizeGlyph.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");_resize.Template=new ControlTemplate(typeof(Thumb)){VisualTree=resizeGlyph};Grid.SetRow(_resize,1);grid.Children.Add(_resize);Child=grid;
            _resize.DragDelta+=(_,e)=>owner.ResizePanel(id,(DesiredWidth==0?ActualWidth:DesiredWidth)+e.HorizontalChange,(DesiredHeight==0?Body.ActualHeight:DesiredHeight)+e.VerticalChange);
            _resize.MouseDoubleClick+=(_,_)=>owner.ResizePanel(id,DesiredWidth,0);
            _header.PreviewMouseLeftButtonDown+=(_,e)=>_down=e.GetPosition(_header);
            _header.MouseMove+=(_,e)=>{if(owner._editing&&e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(_header)-_down).Length>8)DragDrop.DoDragDrop(_header,this,DragDropEffects.Move);};
            AllowDrop=true;DragOver+=(_,e)=>{e.Effects=owner._editing&&e.Data.GetData(typeof(PanelCard)) is PanelCard from&&from._owner==owner?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
            Drop+=(_,e)=>{if(owner._editing&&e.Data.GetData(typeof(PanelCard)) is PanelCard from&&from._owner==owner&&from!=this)owner.MovePanel(from.Id,owner._cards.IndexOf(this));e.Handled=true;};
        }
        private void ChainWheel(object sender,MouseWheelEventArgs e)
        {
            if(e.Handled)return;
            bool CanScroll(ScrollViewer view)=>e.Delta>0?view.VerticalOffset>0:view.VerticalOffset<view.ScrollableHeight;
            DependencyObject? node=e.OriginalSource as DependencyObject;
            while(node is not null&&node!=Body)
            {
                if(node is ScrollViewer inner&&CanScroll(inner))return;
                node=node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
            }
            if(CanScroll(Body))return;
            node=VisualTreeHelper.GetParent(Body);
            while(node is not null)
            {
                if(node is ScrollViewer outer&&CanScroll(outer))
                {
                    e.Handled=true;outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=Mouse.MouseWheelEvent,Source=outer});return;
                }
                node=VisualTreeHelper.GetParent(node);
            }
        }
        public void SetEditing(bool editing){_header.Visibility=_resize.Visibility=editing?Visibility.Visible:Visibility.Collapsed;Body.IsEnabled=!editing;}
    }
}
