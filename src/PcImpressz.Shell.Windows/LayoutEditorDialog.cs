using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal sealed class LayoutFieldDraft(UiFieldLayout initial, bool required) : Observable
{
    public string Id => initial.Id;
    private string _label = initial.Label;
    private int _span = initial.Span;
    private bool _visible = initial.Visible;
    public string Label { get => _label; set { _label = value; Changed(); } }
    public int Span { get => _span; set { _span = value; Changed(); } }
    public bool Visible { get => _visible; set { _visible = value; Changed(); } }
    public bool CanHide => !required;
    public UiFieldLayout Value => new(Id, Label, Span, Visible);
}

internal static class LayoutEditorDialog
{
    private static Window Window(string title, double width, double height)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = Math.Min(700,width), MinHeight = Math.Min(450,height),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current?.MainWindow };
        window.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush"); window.SetResourceReference(Control.ForegroundProperty, "TextBrush"); return window;
    }
    private static Button Button(string title) => new() { Content = title, Margin = new(0,0,8,8), Padding = new(12,7,12,7) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) };
    private static void AddInput(Panel panel, string label, Control control) { control.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush"); control.SetResourceReference(Control.ForegroundProperty,"TextBrush"); control.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush"); panel.Children.Add(Note(label)); control.Margin = new(0,0,0,12); control.MinHeight = 34; panel.Children.Add(control); }

    public static async Task ShowAsync(EntityPageViewModel page)
    {
        var company = page.LayoutCompanyId;
        var status = (await page.SendUiAsync("ui.auth.status", new { CompanyId = company })).Deserialize<UiAuthStatus>()!;
        var session = await Login(page, company, !status.Configured);
        if (session is null) return;
        try
        {
            if (session.Role != "admin") { page.Notify("A felületszerkesztéshez adminisztrátori szerepkör szükséges."); return; }
            var state = (await page.SendUiAsync("ui.layout.read", new UiPageRequest(company,page.LayoutPageKey))).Deserialize<UiLayoutState>()!;
            page.ApplyAppearance(state);
            BuildEditor(page,company,session,state).ShowDialog();
        }
        finally { await page.SendUiAsync("ui.auth.logout", new { CompanyId = company, session.Token }); }
    }
    private static Task<UiSession?> Login(EntityPageViewModel page, Guid company, bool bootstrap)
    {
        UiSession? result = null;
        var window = Window(bootstrap ? "Első céges adminisztrátor létrehozása" : "Adminisztrátori bejelentkezés", 540, 500);
        var panel = new StackPanel { Margin = new(26) };
        panel.Children.Add(Note(bootstrap ? "Ehhez a céghez még nincs admin. Hozd létre az első fiókot, és őrizd meg a jelszavát. Nincs beépített mesterjelszó." : "Az elrendezést csak az adott cég adminisztrátora módosíthatja."));
        var name = new TextBox{MaxLength=40}; var password = new PasswordBox{MaxLength=200}; var confirm = new PasswordBox{MaxLength=200};
        AddInput(panel,"Felhasználónév (3–40 karakter)",name); AddInput(panel,"Jelszó (legalább 12 karakter)",password);
        if(bootstrap) AddInput(panel,"Jelszó újra",confirm);
        var message=Note("");panel.Children.Add(message);
        var submit=Button(bootstrap?"Admin létrehozása":"Bejelentkezés");submit.IsDefault=true;panel.Children.Add(submit);
        var loginBusy=false;window.Closing+=(_,e)=>{if(loginBusy)e.Cancel=true;};
        submit.Click+=async(_,_)=>
        {
            loginBusy=true;submit.IsEnabled=false;
            try
            {
                if(bootstrap&&password.Password!=confirm.Password)throw new ArgumentException("A két jelszó nem egyezik.");
                result=(await page.SendUiAsync(bootstrap?"ui.auth.bootstrap":"ui.auth.login",new UiLogin(company,name.Text,password.Password))).Deserialize<UiSession>();
                loginBusy=false;window.DialogResult=true;
            }
            catch(Exception ex){message.Text=ex.Message;}
            finally{loginBusy=false;password.Clear();confirm.Clear();submit.IsEnabled=true;}
        };
        window.Content=panel;window.ShowDialog();return Task.FromResult(result);
    }
    internal static bool MoveField(ObservableCollection<LayoutFieldDraft> fields, LayoutFieldDraft field, int target)
    {
        var index=fields.IndexOf(field);if(index<0||target<0||target>=fields.Count)return false;fields.Move(index,target);return true;
    }
    internal static Window BuildEditor(EntityPageViewModel page,Guid company,UiSession session,UiLayoutState initial)
    {
        var state=initial;
        var drafts=new ObservableCollection<LayoutFieldDraft>();
        var window=Window("Feliratok és elrendezés – "+page.Definition.Title,1100,800);
        var root=new DockPanel{Margin=new(22)};
        var heading=Note("Admin: "+session.UserName+" · Húzd a mezőket új helyre, vagy használd a Fel/Le gombokat. A változtatás Mentésig csak előnézet. Kötelező mező nem rejthető el.");DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var message=Note("");bottom.Children.Add(message);var actions=new WrapPanel();bottom.Children.Add(actions);
        var save=Button("Mentés");var reset=Button("Alapértelmezett visszaállítása");var previous=Button("Előző elrendezés visszaállítása");var users=Button("Felhasználó létrehozása");var close=Button("Bezárás");
        foreach(var button in new[]{save,reset,previous,users,close})actions.Children.Add(button);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});root.Children.Add(body);
        var left=new StackPanel{Margin=new(0,0,20,0)};body.Children.Add(left);
        var list=new ListBox{ItemsSource=drafts,DisplayMemberPath="Label",Height=230,AllowDrop=true,Margin=new(0,0,0,10)};list.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");list.SetResourceReference(Control.ForegroundProperty,"TextBrush");list.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");left.Children.Add(list);
        var move=new WrapPanel();var up=Button("↑ Fel");var down=Button("↓ Le");move.Children.Add(up);move.Children.Add(down);left.Children.Add(move);
        var edit=new StackPanel();left.Children.Add(edit);
        var label=new TextBox{MaxLength=80};label.SetBinding(TextBox.TextProperty,new Binding("Label"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AddInput(edit,"Felirat",label);
        var span=new ComboBox{ItemsSource=new[]{1,2}};span.SetBinding(ComboBox.SelectedItemProperty,new Binding("Span"){Mode=BindingMode.TwoWay});AddInput(edit,"Szélesség: 1 = fél sor, 2 = teljes sor",span);
        var visible=new CheckBox{Content="Látható mező",Margin=new(0,4,0,12)};visible.SetBinding(CheckBox.IsCheckedProperty,new Binding("Visible"){Mode=BindingMode.TwoWay});visible.SetBinding(UIElement.IsEnabledProperty,new Binding("CanHide"));edit.Children.Add(visible);
        var preview=new Grid{Margin=new(12,0,0,0)};preview.ColumnDefinitions.Add(new());preview.ColumnDefinitions.Add(new());
        var scroll=new ScrollViewer{Content=preview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        void Render()
        {
            preview.Children.Clear();preview.RowDefinitions.Clear();int row=0,column=0;
            foreach(var field in drafts.Where(d=>d.Visible))
            {
                var wide=field.Span==2;if(wide&&column>0){row++;column=0;}
                while(preview.RowDefinitions.Count<=row)preview.RowDefinitions.Add(new(){Height=GridLength.Auto});
                var box=new StackPanel{Margin=new(5,0,5,16)};box.Children.Add(Note(field.Label));var sample=new TextBox{Text="Mintaérték",IsReadOnly=true,MinHeight=34};sample.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");sample.SetResourceReference(Control.ForegroundProperty,"TextBrush");sample.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");box.Children.Add(sample);
                Grid.SetRow(box,row);Grid.SetColumn(box,column);Grid.SetColumnSpan(box,wide?2:1);preview.Children.Add(box);
                if(wide||column==1){row++;column=0;}else column=1;
            }
        }
        void Load(UiLayoutState current)
        {
            drafts.Clear();foreach(var field in current.Fields){var draft=new LayoutFieldDraft(field,page.Definition.Fields.Single(f=>f.Id==field.Id).Required);draft.PropertyChanged+=(_,_)=>Render();drafts.Add(draft);}
            list.SelectedIndex=0;previous.IsEnabled=current.History.Length>0;Render();
        }
        list.SelectionChanged+=(_,_)=>edit.DataContext=list.SelectedItem;
        void Move(int offset){if(list.SelectedItem is not LayoutFieldDraft item)return;var index=drafts.IndexOf(item);var target=index+offset;if(target<0||target>=drafts.Count)return;MoveField(drafts,item,target);list.SelectedItem=item;Render();}
        up.Click+=(_,_)=>Move(-1);down.Click+=(_,_)=>Move(1);
        Point start=default;
        list.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(list);
        list.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&list.SelectedItem is LayoutFieldDraft item&&(e.GetPosition(list)-start).Length>10)DragDrop.DoDragDrop(list,new DataObject("PcImpressz.LayoutField",item),DragDropEffects.Move);};
        list.Drop+=(_,e)=>
        {
            if(e.Data.GetData("PcImpressz.LayoutField") is not LayoutFieldDraft item||!drafts.Contains(item))return;
            var target=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;
            var index=target?.DataContext is LayoutFieldDraft over?drafts.IndexOf(over):drafts.Count-1;
            MoveField(drafts,item,index);list.SelectedItem=item;Render();e.Handled=true;
        };
        var busy=false;
        async Task Persist(bool defaults,bool restore)
        {
            if(busy)return;busy=true;body.IsEnabled=false;actions.IsEnabled=false;
            try
            {
                var data=defaults||restore
                    ? await page.SendUiAsync("ui.layout.restore",new UiLayoutRestore(company,page.LayoutPageKey,session.Token,state.Revision,defaults?-1:state.History.Last().Revision))
                    : await page.SendUiAsync("ui.layout.save",new UiLayoutSave(company,page.LayoutPageKey,session.Token,state.Revision,drafts.Select(d=>d.Value).ToArray()));
                state=data.Deserialize<UiLayoutState>()!;page.ApplyAppearance(state);Load(state);message.Text="Az elrendezés mentve. Verzió: "+state.Revision;
            }
            catch(Exception ex){message.Text=ex.Message;}
            finally{busy=false;body.IsEnabled=true;actions.IsEnabled=true;}
        }
        save.Click+=async(_,_)=>await Persist(false,false);reset.Click+=async(_,_)=>await Persist(true,false);previous.Click+=async(_,_)=>await Persist(false,true);
        users.Click+=(_,_)=>CreateUser(page,company,session,window);
        close.Click+=(_,_)=>window.Close();
        window.Closing+=(_,e)=>{if(busy){e.Cancel=true;return;}if(!drafts.Select(d=>d.Value).SequenceEqual(state.Fields)&&MessageBox.Show(window,"Elveted a nem mentett elrendezés-módosításokat?","Szerkesztő bezárása",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;};
        Load(state);window.Content=root;return window;
    }
    private static void CreateUser(EntityPageViewModel page,Guid company,UiSession session,Window owner)
    {
        var window=Window("Új céges felhasználó",520,480);window.Owner=owner;var panel=new StackPanel{Margin=new(24)};
        var name=new TextBox();var password=new PasswordBox();var role=new ComboBox{ItemsSource=new[]{"user","admin"},SelectedIndex=0};
        AddInput(panel,"Felhasználónév",name);AddInput(panel,"Jelszó (legalább 12 karakter)",password);AddInput(panel,"Szerepkör: user = normál, admin = felületszerkesztő",role);
        var message=Note("");panel.Children.Add(message);var save=Button("Létrehozás");panel.Children.Add(save);
        var creating=false;window.Closing+=(_,e)=>{if(creating)e.Cancel=true;};
        save.Click+=async(_,_)=>{creating=true;save.IsEnabled=false;try{await page.SendUiAsync("ui.users.create",new UiCreateUser(company,session.Token,name.Text,password.Password,(string)role.SelectedItem));message.Text="Felhasználó létrehozva.";name.Clear();}catch(Exception ex){message.Text=ex.Message;}finally{creating=false;password.Clear();save.IsEnabled=true;}};
        window.Content=panel;window.ShowDialog();
    }
}
