using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;
public sealed class InvoiceBlockEditor:System.ComponentModel.INotifyPropertyChanged
{
    public Guid Id {get;set;}=Guid.NewGuid();
    private string _name="Új számlatömb";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Name {get=>_name;set{_name=value;PropertyChanged?.Invoke(this,new(nameof(Name)));}}
    public string Prefix {get;set;}="";
    public long StartNumber {get;set;}=1;
}
public sealed class InvoicingSettingsCard:UserControl
{
    private readonly ComboBox _provider=new(){MinWidth=260,DisplayMemberPath="Label",SelectedValuePath="Id"};
    private readonly ComboBox _default=new(){MinWidth=240,DisplayMemberPath="Name"};
    private readonly ObservableCollection<InvoiceBlockEditor> _blocks=[];
    private readonly PasswordBox _key=new(){MinWidth=280,MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly CheckBox _test=new(){Content="A kulcs a Bizonylat.hu tesztcégéhez tartozik",IsChecked=true,Margin=new(0,8,0,8)};
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,8)};
    private readonly Button _save=new(){Content="Számlázási beállítások mentése",IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,0,0)};
    private Guid? _loadedCompany;private ShellViewModel? _shell;
    public InvoicingSettingsCard()
    {
        _key.MinHeight=36;_key.Padding=new(8,4,8,4);_key.SetResourceReference(BackgroundProperty,"ElevatedBrush");_key.SetResourceReference(ForegroundProperty,"TextBrush");_key.SetResourceReference(BorderBrushProperty,"StrokeBrush");
        _provider.MaxWidth=650;_provider.HorizontalAlignment=HorizontalAlignment.Left;_default.MaxWidth=650;_default.HorizontalAlignment=HorizontalAlignment.Left;
        var root=new StackPanel{MaxWidth=1000,HorizontalAlignment=HorizontalAlignment.Left};
        root.Children.Add(new TextBlock{Text="Számlázó szolgáltató és számlatömbök",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        root.Children.Add(new TextBlock{Text="A kiállító szolgáltató adja a tényleges számlaszámot. A NAV külön egyeztet. A kezdősorszám tervezett beállítás: csak szolgáltatói támogatással érvényesíthető.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var load=new Button{Content="Kiválasztott cég beállításainak betöltése",HorizontalAlignment=HorizontalAlignment.Left};load.Click+=async(_,_)=>await LoadAsync();root.Children.Add(load);
        _provider.ItemsSource=new[]{new{Id="bizonylat",Label="Bizonylat.hu – fejlesztési adapter"},new{Id="szamlazz",Label="Számlázz.hu – tervezett integráció"},new{Id="billingo",Label="Billingo – tervezett integráció"}};_provider.SelectedValue="bizonylat";
        Caption(root,"Választott számlázó",_provider);
        root.Children.Add(new TextBlock{Text="A Számlázz.hu és Billingo még nem állít ki számlát. A Bizonylat.hu adapter tesztelése és a pénztári bekötés külön következő lépés.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12)});
        var grid=new DataGrid{ItemsSource=_blocks,AutoGenerateColumns=false,CanUserAddRows=false,MinHeight=140,MaxHeight=260,MinWidth=650};
        foreach(var (name,path,width) in new[]{("Tömb neve","Name",250d),("Előtag","Prefix",200d),("Tervezett kezdősorszám","StartNumber",200d)})grid.Columns.Add(new DataGridTextColumn{Header=name,Binding=new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},Width=width});
        Caption(root,"Számlatömbök",grid);
        var actions=new WrapPanel();var add=new Button{Content="Új számlatömb",Margin=new(0,8,10,8)};add.Click+=(_,_)=>{if(_blocks.Count<20)_blocks.Add(new());};actions.Children.Add(add);
        var remove=new Button{Content="Kijelölt tömb törlése",Margin=new(0,8,0,8)};remove.Click+=(_,_)=>{if(grid.SelectedItem is InvoiceBlockEditor block)_blocks.Remove(block);};actions.Children.Add(remove);root.Children.Add(actions);
        _default.ItemsSource=_blocks;Caption(root,"Alapértelmezett számlatömb",_default);
        Caption(root,"Bizonylat.hu API-kulcs (üresen a mentett kulcs marad)",_key);root.Children.Add(_test);
        root.Children.Add(new TextBlock{Text="A tesztcég kiválasztása a Bizonylat.hu oldalon történik. Ez a jelölés nem alakít át éles kulcsot tesztkulccsá. A kulcs titkosítva, céghez és Windows-felhasználóhoz kötve tárolódik.",TextWrapping=TextWrapping.Wrap});
        _save.Click+=async(_,_)=>
        {
            if(_shell?.SelectedCompany?.Id is not Guid company||company!=_loadedCompany)return;
            if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true)){_message.Text="Javítsd a hibás számlatömb-adatot.";return;}
            IsEnabled=false;
            try
            {
                var result=await _shell.BillingSettingsAsync(company,new{settings=new{provider=_provider.SelectedValue?.ToString()??"bizonylat",defaultBlockId=(_default.SelectedItem as InvoiceBlockEditor)?.Id,blocks=_blocks.ToArray()},apiKey=_key.Password,testMode=_test.IsChecked==true},true);
                if(_shell.SelectedCompany?.Id!=company)return;
                _message.Text="Beállítások mentve. A tényleges számlakiállítás még nincs élesítve.";_key.Clear();
            }
            catch(Exception ex){_message.Text=ex.Message;}
            finally{IsEnabled=true;}
        };
        root.Children.Add(_save);root.Children.Add(_message);Content=root;
        DataContextChanged+=(_,_)=>
        {
            if(_shell is not null)_shell.PropertyChanged-=ShellChanged;
            _shell=DataContext as ShellViewModel;if(_shell is not null)_shell.PropertyChanged+=ShellChanged;Clear();
        };
    }
    private void ShellChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName is "SelectedCompany" or "IsCompanyAdmin")Clear();}
    private void Clear(){_loadedCompany=null;_provider.SelectedValue="bizonylat";_test.IsChecked=true;_blocks.Clear();_key.Clear();_save.IsEnabled=false;_message.Text="Válassz céget, majd töltsd be a beállításait.";}
    private async Task LoadAsync()
    {
        if(_shell?.SelectedCompany?.Id is not Guid company)return;IsEnabled=false;
        try
        {
            var result=await _shell.BillingSettingsAsync(company,new{},false);if(_shell.SelectedCompany?.Id!=company)return;
            var settings=result.GetProperty("settings");_blocks.Clear();foreach(var block in settings.GetProperty("blocks").EnumerateArray())_blocks.Add(new(){Id=block.GetProperty("id").GetGuid(),Name=block.GetProperty("name").GetString()??"",Prefix=block.GetProperty("prefix").GetString()??"",StartNumber=block.GetProperty("startNumber").GetInt64()});
            _provider.SelectedValue=settings.GetProperty("provider").GetString();
            _default.SelectedItem=settings.TryGetProperty("defaultBlockId",out var id)&&id.ValueKind==JsonValueKind.String?_blocks.FirstOrDefault(b=>b.Id==id.GetGuid()):null;
            var connection=result.GetProperty("connection");_test.IsChecked=connection.GetProperty("testMode").GetBoolean();_key.Clear();
            _loadedCompany=company;_save.IsEnabled=true;_message.Text=connection.GetProperty("configured").GetBoolean()?"A Bizonylat API-kulcs mentve van; nem jelenik meg újra.":"API-kulcs még nincs mentve. A tömbök kulcs nélkül is előkészíthetők.";
        }
        catch(Exception ex){Clear();_message.Text=ex.Message;}
        finally{IsEnabled=true;}
    }
    private static void Caption(Panel root,string text,UIElement content){root.Children.Add(new TextBlock{Text=text,Margin=new(0,12,0,5)});root.Children.Add(content);}
}
