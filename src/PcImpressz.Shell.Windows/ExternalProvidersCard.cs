using System.IO;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PcImpressz.Shell.Windows;

public sealed class ExternalProvidersCard : UserControl
{
    private readonly StackPanel _cards=new();
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12)};
    private ShellViewModel? _shell;
    private bool _busy;
    private bool _subscribed;
    private readonly DispatcherTimer _jobTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Guid? _jobCompany;
    private ProgressBar? _jobProgress;
    private TextBlock? _jobLabel;
    private bool _polling;
    private long _lastBytes;
    private DateTimeOffset _lastByteChange = DateTimeOffset.UtcNow;
    public ExternalProvidersCard()
    {
        _jobTimer.Tick += async (_, _) => await PollJobAsync();
        var panel=new StackPanel{Margin=new(0,16,0,0)};
        panel.Children.Add(new TextBlock{Text="Külső szolgáltatók",FontSize=22,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Nagyker-kapcsolatok és XML-állapotok. A zöld pipa sikeres XML-lekérést jelent. A többi adapter előkészített állapotú; az adatai már menthetők.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12)});
        var buttons=new WrapPanel();
        AddButton(buttons,"Állapotok frissítése",()=>RunAsync(LoadAsync));
        AddButton(buttons,"Régi raktár config.json átvétele",()=>RunAsync(ImportAsync));
        panel.Children.Add(buttons);panel.Children.Add(_message);panel.Children.Add(_cards);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Loaded+=async(_,_)=>{Subscribe();await RunAsync(LoadAsync);_jobTimer.Start();};
        Unloaded+=(_,_)=>{_jobTimer.Stop();if(_shell is not null)_shell.PropertyChanged-=ShellChanged;_subscribed=false;_cards.Children.Clear();};
        DataContextChanged+=async(_,_)=>{if(_shell is not null)_shell.PropertyChanged-=ShellChanged;_subscribed=false;_shell=DataContext as ShellViewModel;_cards.Children.Clear();if(IsLoaded){Subscribe();await RunAsync(LoadAsync);}};
    }
    private void Subscribe(){_shell=DataContext as ShellViewModel;if(_shell is not null&&!_subscribed){_shell.PropertyChanged+=ShellChanged;_subscribed=true;}}
    private async void ShellChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName is "SelectedCompany" or "IsCompanyAdmin"){_cards.Children.Clear();_message.Text="";if(!_busy)await RunAsync(LoadAsync);}
    }
    private async Task RunAsync(Func<Task> action)
    {
        if(_busy)return;
        if(_shell?.SelectedCompany is null||!_shell.IsCompanyAdmin){_message.Text="Válassz céget és jelentkezz be adminisztrátorként.";return;}
        var company=_shell.SelectedCompany.Id;_busy=true;IsEnabled=false;
        try{await action();}catch(Exception ex) when(ex is not OutOfMemoryException){if(_shell.SelectedCompany?.Id==company)_message.Text=ex.Message;}
        finally{_busy=false;IsEnabled=true;if(_shell.SelectedCompany?.Id!=company&&IsLoaded)await RunAsync(LoadAsync);}
    }
    private static async Task<int> CompMarketProxyDirectTestAsync(string proxyUrl)
    {
        if(!Uri.TryCreate(proxyUrl.Trim(),UriKind.Absolute,out var uri) || uri.Scheme!="http" || uri.Host!="217.113.49.52" || uri.Port!=8888)
            throw new ArgumentException("A proxy formátuma: http://felhasznalo:jelszo@217.113.49.52:8888");
        var userInfo=uri.UserInfo.Split(':',2);
        if(userInfo.Length!=2 || userInfo[0].Length==0 || userInfo[1].Length==0)
            throw new ArgumentException("Hiányzik a proxy felhasználóneve vagy jelszava.");
        var address=new UriBuilder(uri){UserName="",Password=""}.Uri;
        var proxy=new System.Net.WebProxy(address){Credentials=new System.Net.NetworkCredential(Uri.UnescapeDataString(userInfo[0]),Uri.UnescapeDataString(userInfo[1]))};
        using var handler=new System.Net.Http.HttpClientHandler{Proxy=proxy,UseProxy=true,AllowAutoRedirect=false,UseCookies=false};
        using var client=new System.Net.Http.HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
        try
        {
            using var response=await client.GetAsync("https://compmarket.hu/xml/termeklista.php",System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if(response.StatusCode==System.Net.HttpStatusCode.ProxyAuthenticationRequired)throw new InvalidOperationException("A VPS proxyhitelesítése sikertelen (407).");
            return (int)response.StatusCode;
        }
        catch(TaskCanceledException ex){throw new TimeoutException("A VPS/HTTPS kérés 15 másodpercen belül nem válaszolt.",ex);}
        catch(System.Net.Http.HttpRequestException ex){throw new InvalidOperationException("VPS HTTPS-kapcsolat hiba: "+ex.Message,ex);}
    }

    private Task<JsonElement> Send(Guid company,string command,object payload)=>_shell!.SupplierSettingsAsync(company,"quotes",command,payload);
    private async Task LoadAsync()
    {
        var company=_shell!.SelectedCompany!.Id;
        var result=await Send(company,"wholesale-list",new{});
        if(_shell.SelectedCompany?.Id==company)Render(result,company);
    }
    private void Render(JsonElement rows,Guid company)
    {
        _cards.Children.Clear();
        _jobCompany=null;_jobProgress=null;_jobLabel=null;
        foreach(var row in rows.EnumerateArray())
        {
            string Text(string key)=>row.GetProperty(key).GetString()??"";
            var id=Text("id");var name=Text("name");var prepared=row.GetProperty("prepared").GetBoolean();
            var body=new StackPanel{Margin=new(16)};
            body.Children.Add(new TextBlock{Text=Text("description"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
            var health=row.GetProperty("health");var status=prepared?"Bekötésre előkészítve":"Még nem volt XML-lekérés";
            if(health.ValueKind==JsonValueKind.Object)
            {
                static string Date(JsonElement h,string key)=>h.GetProperty(key).ValueKind==JsonValueKind.Null?"még nem volt":h.GetProperty(key).GetDateTimeOffset().ToLocalTime().ToString("yyyy.MM.dd. HH:mm:ss");
                var success=health.GetProperty("success").GetBoolean();status=success?"✓ Sikeres XML-lekérés":"✕ "+health.GetProperty("message").GetString();
                body.Children.Add(new TextBlock{Text="Utolsó próbálkozás: "+Date(health,"attemptedAt")+"\nUtolsó sikeres XML: "+Date(health,"succeededAt")+"\nTermékek: "+(health.GetProperty("products").ValueKind==JsonValueKind.Number?health.GetProperty("products").GetInt32().ToString("N0"):"—"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
                body.Children.Add(new TextBlock{Text=status,Foreground=success?Brushes.ForestGreen:Brushes.IndianRed,TextWrapping=TextWrapping.Wrap});
            }
            else body.Children.Add(new TextBlock{Text=status,Margin=new(0,0,0,8)});
            var enabled=new CheckBox{Content=prepared?"Használni kívánt nagyker (adapter előkészítve)":"Kapcsolat engedélyezése",IsChecked=row.GetProperty("enabled").GetBoolean(),Margin=new(0,10,0,8)};body.Children.Add(enabled);
            if(id=="compmarket")
            {
                body.Children.Add(new TextBlock{Text="Modulonkénti megjelenítés",FontWeight=FontWeights.SemiBold,Margin=new(0,12,0,6)});
                var hasVisibility=row.TryGetProperty("visibility",out var visibility)&&visibility.ValueKind==JsonValueKind.Object;
                bool Shown(string key,bool fallback)=>hasVisibility&&visibility.TryGetProperty(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ValueKind==JsonValueKind.False)?value.GetBoolean():fallback;
                foreach(var item in new[]{("catalog","Terméktörzs",true),("quotes","Árajánlat",true),("receiving","Bevételezés",false)})
                {
                    var key=item.Item1;
                    var checkbox=new CheckBox{Content=item.Item2,IsChecked=Shown(key,item.Item3),Margin=new(0,4,0,4)};
                    checkbox.Click+=async (_,_)=>
                    {
                        try
                        {
                            await RunAsync(async()=>
                            {
                                await Send(company,"wholesale-visibility-save",new{providerId=id,context=key,visible=checkbox.IsChecked==true});
                                _message.Text=item.Item2+": megjelenítési beállítás mentve.";
                            });
                        }
                        catch(Exception ex){_message.Text=ex.Message;}
                    };
                    body.Children.Add(checkbox);
                }
            }
            var texts=new Dictionary<string,TextBox>();var secrets=new Dictionary<string,PasswordBox>();
            foreach(var field in row.GetProperty("fields").EnumerateArray())
            {
                var key=field.GetProperty("key").GetString()!;var secret=field.GetProperty("secret").GetBoolean();
                body.Children.Add(new TextBlock{Text=field.GetProperty("label").GetString()+(secret?(field.GetProperty("hasValue").GetBoolean()?" · mentve, üresen megmarad":" · nincs mentve"):""),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,4)});
                Control input;
                if(secret){var box=new PasswordBox{MaxLength=2000};secrets[key]=box;input=box;}else{var box=new TextBox{Text=field.GetProperty("value").GetString()??"",MaxLength=2000};texts[key]=box;input=box;}
                input.MinHeight=34;input.SetResourceReference(BackgroundProperty,"ElevatedBrush");input.SetResourceReference(ForegroundProperty,"TextBrush");body.Children.Add(input);
            }
            body.Children.Add(new TextBlock{Text="Saját leírás / megjegyzés",Margin=new(0,12,0,4)});
            var note=new TextBox{Text=Text("note"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=70,MaxLength=2000};note.SetResourceReference(BackgroundProperty,"ElevatedBrush");note.SetResourceReference(ForegroundProperty,"TextBrush");body.Children.Add(note);
            var actions=new WrapPanel{Margin=new(0,12,0,0)};
            var progress=new ProgressBar{IsIndeterminate=true,Height=6,Visibility=Visibility.Collapsed,Margin=new(0,8,0,8)};
            var progressLabel=new TextBlock{TextWrapping=TextWrapping.Wrap};
            body.Children.Add(progressLabel);body.Children.Add(progress);
            if(id=="compmarket")
            {
                body.Children.Add(new TextBlock{Text="Nagyker termékkereső · letöltött XML-ből (nem írja a saját készletet)",FontWeight=FontWeights.SemiBold,Margin=new(0,16,0,6)});
                var searchPanel=new WrapPanel();
                var queryInput=new TextBox{Width=250,MinHeight=32,Margin=new(0,0,8,6),ToolTip="Terméknév, cikkszám vagy vonalkód (minimum 2 karakter)"};
                var contextInput=new ComboBox{Width=140,MinHeight=32,Margin=new(0,0,8,6),ItemsSource=new[]{"Terméktörzs","Árajánlat","Bevételezés"},SelectedIndex=1};
                var markupInput=new TextBox{Text="20",Width=54,MinHeight=32,Margin=new(0,0,8,6),ToolTip="Árrés százalékban"};
                searchPanel.Children.Add(queryInput);searchPanel.Children.Add(contextInput);
                searchPanel.Children.Add(new TextBlock{Text="Árrés %:",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,5,6)});
                searchPanel.Children.Add(markupInput);
                var results=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,
                    Height=245,Margin=new(0,6,0,6),HeadersVisibility=DataGridHeadersVisibility.Column,
                    SelectionMode=DataGridSelectionMode.Single};
                void Column(string title,string field,double width)
                {
                    results.Columns.Add(new DataGridTextColumn{Header=title,Binding=new System.Windows.Data.Binding(field),Width=new DataGridLength(width)});
                }
                Column("Cikkszám","sku",150);Column("Megnevezés","name",330);
                Column("Nettó beszerzés","purchaseNet",115);Column("Nettó eladás","saleNet",115);
                Column("Készletjelzés","availability",130);
                var resultInfo=new TextBlock{Text="Keresés a sikeresen letöltött CompMarket XML-ben.",TextWrapping=TextWrapping.Wrap};
                AddButton(searchPanel,"Keresés",()=>RunAsync(async()=>
                {
                    var query=queryInput.Text.Trim();
                    if(query.Length<2)throw new ArgumentException("Legalább két karaktert írj a keresőbe.");
                    if(!decimal.TryParse(markupInput.Text,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.CurrentCulture,out var margin) || margin<0 || margin>1000)
                        throw new ArgumentException("Az árrés 0 és 1000 közötti szám lehet.");
                    var view=contextInput.SelectedIndex switch{0=>"catalog",2=>"receiving",_=>"quotes"};
                    resultInfo.Text="Keresés folyamatban…";
                    var rows=await Send(company,"wholesale-search-cached",new{query,context=view,providers=new[]{"compmarket"},marginPercent=margin,vatRate=27});
                    if(_shell?.SelectedCompany?.Id!=company)return;
                    var found=new List<object>();
                    foreach(var item in rows.EnumerateArray())
                        found.Add(new{sku=item.GetProperty("sku").GetString(),name=item.GetProperty("name").GetString(),
                            purchaseNet=item.GetProperty("purchaseNet").GetDecimal().ToString("N2"),
                            saleNet=item.GetProperty("saleNet").GetDecimal().ToString("N2"),
                            availability=item.GetProperty("availability").GetString()});
                    results.ItemsSource=found;
                    resultInfo.Text=found.Count+" találat (legfeljebb 40). A kapcsolókkal letiltott modulban nincs találat.";
                }));
                body.Children.Add(searchPanel);body.Children.Add(resultInfo);body.Children.Add(results);
            }
            if(id=="compmarket")
            {
                _jobCompany=company;_jobProgress=progress;_jobLabel=progressLabel;
                progress.IsIndeterminate=false;progress.Minimum=0;progress.Maximum=100;
                progressLabel.Text="XML letöltése a Mentés és XML lekérése gombbal indul. Itt látszik a százalék és a letöltött méret.";
            }
            async Task Save(bool test)
            {
                if(_shell?.SelectedCompany?.Id!=company)throw new InvalidOperationException("A kiválasztott cég megváltozott.");
                var values=texts.ToDictionary(p=>p.Key,p=>p.Value.Text);foreach(var pair in secrets)values[pair.Key]=pair.Value.Password;
                var result=await Send(company,"wholesale-save",new{providerId=id,description=note.Text,enabled=enabled.IsChecked==true,values});
                // Keep unsaved UI state intact until the user explicitly reloads.
                // Passwords are not echoed back by the server.
                if(_shell.SelectedCompany?.Id!=company)return;
                foreach(var box in secrets.Values)box.Clear();
                _message.Text=name+": beállítások mentve.";
                if(id=="compmarket")
                {
                    try
                    {
                        var proxy=values.GetValueOrDefault("nagyker_proxy")??"";
                        if(string.IsNullOrWhiteSpace(proxy)){var previous=await _shell.SupplierSettingsAsync(company,"receiving","supplier-status",new{});proxy=previous.GetProperty("proxyUrl").GetString()??"";}
                        await _shell.SupplierSettingsAsync(company,"receiving","supplier-save",new{customerId=values.GetValueOrDefault("compmarket_pid"),authCode=values.GetValueOrDefault("compmarket_authcode"),proxyUrl=proxy,enabled=enabled.IsChecked==true});
                    }
                    catch(Exception ex) when(ex is not OutOfMemoryException){_message.Text+=" A számlaegyeztetés külön kapcsolatának mentése nem sikerült: "+ex.Message;}
                }
                if(test)
                {
                    var state=await Send(company,"compmarket-job-start",new{});
                    progressLabel.Text="XML letöltése elindult. A százalék és a méret másodpercenként frissül.";
                    progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;
                    _jobTimer.Start();
                    _=state;
                }
                // Never rebuild the entire editor after save: it would discard user input.
                // A manual refresh reloads the stored state and the latest XML health.
            }
            AddButton(actions,"Kapcsolat mentése",()=>RunAsync(()=>Save(false)));
            if(!prepared)
            {
                AddButton(actions,"Proxykapcsolat tesztelése",()=>RunAsync(async()=>
                {
                    progress.Visibility=Visibility.Visible;
                    progressLabel.Text="Proxy és HTTPS-alagút ellenőrzése (XML letöltése nélkül)…";
                    try
                    {
                        var proxyText=secrets.TryGetValue("nagyker_proxy",out var proxyInput)?proxyInput.Password:"";
                        if(string.IsNullOrWhiteSpace(proxyText))throw new InvalidOperationException("A teszthez írd be újra a teljes VPS-proxycímet (felhasználónévvel és jelszóval) a CompMarket mezőbe.");
                        var status=await CompMarketProxyDirectTestAsync(proxyText);
                        progressLabel.Text="Proxyteszt sikeres. HTTP: "+status;
                        
                    }
                    finally { progress.Visibility=Visibility.Collapsed; }
                }));
                AddButton(actions,"Mentés és XML lekérése",()=>RunAsync(()=>Save(true)));
            }
            body.Children.Add(actions);
            var expander=new Expander{Header=name+" · "+(row.GetProperty("used").GetBoolean()?"használt nagyker · ":"")+status,Content=body,Margin=new(0,0,0,10)};
            var border=new Border{Child=expander,Padding=new(12)};border.SetResourceReference(StyleProperty,"Card");_cards.Children.Add(border);
        }
    }
    private async Task PollJobAsync()
    {
        if(_polling || !IsLoaded || _jobCompany is not Guid company || _shell?.SelectedCompany?.Id!=company || _jobLabel is null)return;
        _polling=true;
        try
        {
            var state=await Send(company,"compmarket-job-status",new{});
            if(_jobCompany!=company || _shell?.SelectedCompany?.Id!=company || _jobLabel is null)return;
            var stage=state.GetProperty("stage").GetString()??"idle";
            var bytes=state.GetProperty("bytes").GetInt64();
            var total=state.GetProperty("totalBytes");
            var percent=state.GetProperty("percent");
            var elapsed=DateTimeOffset.UtcNow-state.GetProperty("startedAt").GetDateTimeOffset();
            var mb=(bytes/1048576d).ToString("N1");
            var hasTotal=total.ValueKind==JsonValueKind.Number&&total.GetInt64()>0;
            var totalText=hasTotal?(total.GetInt64()/1048576d).ToString("N1")+" MB":"a szerver nem adta meg a teljes méretet";
            var active=stage is "queued" or "connecting" or "retrying" or "downloading" or "parsing";
            _jobProgress!.Visibility=active?Visibility.Visible:Visibility.Collapsed;
            _jobProgress.IsIndeterminate=!active || stage is "queued" or "connecting" or "retrying" || percent.ValueKind!=JsonValueKind.Number;
            if(percent.ValueKind==JsonValueKind.Number)_jobProgress.Value=percent.GetInt32();
            if(bytes!=_lastBytes){_lastBytes=bytes;_lastByteChange=DateTimeOffset.UtcNow;}
            var sinceChange=(DateTimeOffset.UtcNow-_lastByteChange).TotalSeconds;
            var percentText=percent.ValueKind==JsonValueKind.Number?percent.GetInt32()+"%":"méret nélkül";
            var sizeLine="Letöltve: "+mb+" MB / "+totalText;
            _jobLabel.Text=stage switch
            {
                "queued"=>"XML letöltése\nElőkészítés…",
                "connecting"=>"XML letöltése\nKapcsolódás a CompMarkethez a VPS-proxyn keresztül…\nEltelt: "+elapsed.ToString(@"hh\:mm\:ss")+(elapsed.TotalSeconds>20?"\nA nagy XML összeállítása a szerveren percig tarthat, amíg az első bájt megérkezik.":""),
                "retrying"=>"XML letöltése\nA proxy vagy a szerver megszakította a kapcsolatot. Automatikus újrapróbálás…",
                "downloading"=>"XML letöltése\n"+percentText+"\n"+sizeLine+"\nEltelt: "+elapsed.ToString(@"hh\:mm\:ss")+(sinceChange>30?"\n30 másodperce nem érkezett új adat.":""),
                "parsing"=>"XML ellenőrzése\n"+percentText+"\n"+sizeLine,
                "completed"=>"XML elkészült. Termékek: "+state.GetProperty("products").GetInt32().ToString("N0")+". A terméktörzs és az árajánlat keresőjében látszik.",
                "failed"=>"XML-letöltési hiba: "+state.GetProperty("error").GetString(),
                _=>"XML-letöltés még nem indult."
            };
            if(stage=="failed" && _shell is not null)_message.Text=_jobLabel.Text;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        { if(_jobCompany==company && _jobLabel is not null)_jobLabel.Text="XML-állapot nem kérdezhető le: "+ex.Message; }
        finally{_polling=false;}
    }
    private async Task ImportAsync()
    {
        var picker=new OpenFileDialog{Title="Régi raktár nagyker-beállításai",Filter="JSON konfiguráció (*.json)|*.json"};
        if(picker.ShowDialog()!=true)return;
        var info=new FileInfo(picker.FileName);if(info.Length>1_000_000)throw new IOException("Túl nagy konfigurációfájl.");
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(picker.FileName));
        var company=_shell!.SelectedCompany!.Id;
        var rows=await Send(company,"wholesale-list",new{});var count=0;
        foreach(var row in rows.EnumerateArray())
        {
            if(_shell.SelectedCompany?.Id!=company)return;
            var values=new Dictionary<string,string>();
            foreach(var field in row.GetProperty("fields").EnumerateArray())
            {
                var key=field.GetProperty("key").GetString()!;
                if(config.RootElement.TryGetProperty(key,out var value)&&value.ValueKind is JsonValueKind.String or JsonValueKind.Number)values[key]=value.ToString();
                else if(!field.GetProperty("secret").GetBoolean())values[key]=field.GetProperty("value").GetString()??"";
            }
            if(!values.Keys.Any(key=>config.RootElement.TryGetProperty(key,out _)))continue;
            await Send(company,"wholesale-save",new{providerId=row.GetProperty("id").GetString(),description=row.GetProperty("note").GetString(),enabled=row.GetProperty("enabled").GetBoolean(),values});count++;
        }
        _message.Text=count==0?"A fájlban nincs átvehető nagyker-beállítás.":count+" nagyker beállításai átvéve. A kapcsolatteszt külön indítható.";
        await LoadAsync();
    }
    private static void AddButton(Panel panel,string caption,Func<Task> action)
    {var button=new Button{Content=caption,Margin=new(0,0,8,8)};button.Click+=async(_,_)=>await action();panel.Children.Add(button);}
}
