using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class PartnerEditorChecks
{
    private static EntityPageDescriptor Definition=>new("partners","partners","Partnertörzs","","",[new("name","Cégnév",Required:true),new("taxId","Adószám"),new("country","Ország"),new("postalCode","Irányítószám"),new("city","Település"),new("address","Székhely"),new("email","E-mail"),new("industry","Iparág"),new("creditLimit","Hitelkeret","decimal"),new("notes","Megjegyzés","multiline")],[new("name","Partner"),new("taxId","Adószám")],new("list","save","archive","export"));
    public static async Task VerifyAsync(string output)
    {
        PartnerNameFormatting.Enabled=true;
        if(PartnerNameFormatting.Format("PC-IMPRESSZ KFT.")!="PC-Impressz Kft."||PartnerNameFormatting.Format("ÁRVÍZTŰRŐ UTCA 12. II.")!="Árvíztűrő Utca 12. II."||PartnerNameFormatting.Format("iPhone Kft.")!="iPhone Kft.")throw new InvalidOperationException("Hibás magyar névformázás.");
        PartnerNameFormatting.Enabled=false;if(PartnerNameFormatting.Format("BUDAPEST")!="BUDAPEST")throw new InvalidOperationException("A névformázás nem kapcsolható ki.");PartnerNameFormatting.Enabled=true;
        var preferenceStore=new AppearanceStore(Path.Combine(output,"name-format-check"));preferenceStore.Save(new AppearancePreferences{FormatPartnerNames=false});if(preferenceStore.Load().FormatPartnerNames)throw new InvalidOperationException("A névformázás beállítása nem marad meg.");
        var developmentCompany=Guid.NewGuid();var developmentShell=new ShellViewModel(new AppearanceStore(Path.Combine(output,"development-login")),output,null);
        Task<JsonElement> AutoLogin(string operation,object payload,CancellationToken ct)=>Task.FromResult(JsonSerializer.SerializeToElement(new UiSession("development-test-token","development","admin",DateTimeOffset.UtcNow.AddMinutes(30))));
        var developmentConnection=new HostConnection(output,null,AutoLogin);
        typeof(ShellViewModel).GetField("_connection",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(developmentShell,developmentConnection);
        typeof(ShellViewModel).GetField("_selectedCompany",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(developmentShell,new CompanyInfo(developmentCompany,"Fejlesztői próba",true,DateTimeOffset.UtcNow));
        typeof(ShellViewModel).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(developmentShell,new HostDiagnostic("Ready","test",output,"1.2","Active","",null,DateTimeOffset.UtcNow,1,[]){DevelopmentLoginEnabled=true});
        await (Task)typeof(ShellViewModel).GetMethod("PrepareAuthenticationAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(developmentShell,null)!;
        if(!developmentShell.IsSignedIn||!developmentShell.DevelopmentMode||developmentShell.ShowAccountControls||developmentShell.CanManageAccounts)throw new InvalidOperationException("A fejlesztői automatikus belépés nem működik.");developmentShell.Stop();
        var company=Guid.NewGuid();var lookups=0;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation!="partners.taxpayer")throw new InvalidOperationException("Váratlan írás: "+operation);lookups++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new BillingCustomer(Guid.Empty,"NAV Teszt Kft.","12345678-2-42","HU","1111","Budapest","Teszt utca 1."),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new EntityPageViewModel(Definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"partner-check")));
        page.Fields.Single(f=>f.Definition.Id=="taxId").Text="12345678";page.Fields.Single(f=>f.Definition.Id=="industry").Text="Megőrzendő bővített adat";
        await page.LookupPartnerNavAsync();if(lookups!=1||!page.IsDirty||page.Fields[0].Text!="NAV Teszt Kft."||page.Fields.Single(f=>f.Definition.Id=="industry").Text!="Megőrzendő bővített adat")throw new InvalidOperationException("Hibás NAV-adatlapkitöltés.");
        page.ConfirmDiscardOverride=()=>false;page.NewCommand.Execute(null);if(!page.IsDirty||page.Fields[0].Text!="NAV Teszt Kft.")throw new InvalidOperationException("A Nem válasz elvesztette az adatlapot.");
        page.SelectedRow=new EntityRow(JsonSerializer.SerializeToElement(new{id=Guid.NewGuid(),version=1,name="Másik partner"}));if(page.SelectedRow is not null||page.Fields[0].Text!="NAV Teszt Kft.")throw new InvalidOperationException("A Nem válasz partnerkiválasztáskor elvesztette az adatlapot.");
        var shell=new ShellViewModel(new AppearanceStore(Path.Combine(output,"partner-close")),output,null);
        var pages=(Dictionary<string,IWorkspacePage>)typeof(ShellViewModel).GetField("_entityPages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(shell)!;pages.Add("partners.partners",page);
        if(shell.CanClose(()=>false)||shell.CanClose(()=>false)||!page.IsDirty)throw new InvalidOperationException("A Nem válasz engedte a kilépést.");shell.Stop();
        var oldPartner=Guid.NewGuid();var newPartner=Guid.NewGuid();
        async Task<JsonElement> ReadOverview(string operation,object input,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)input;var id=request.Input.GetProperty("id").GetGuid();await Task.Delay(id==oldPartner?120:5,ct);
            return JsonSerializer.SerializeToElement(new{cards=new[]{new{label="Bizonylatok",value=id==oldPartner?"Régi":"Aktuális"}},notice=""});
        }
        var overviewConnection=new HostConnection(output,null,ReadOverview);overviewConnection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var overviewPage=new EntityPageViewModel(Definition with{DossierCommand="dossier"},overviewConnection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"overview-check")));
        overviewPage.SelectedRow=new EntityRow(JsonSerializer.SerializeToElement(new{id=oldPartner,version=1,name="Régi partner"}));overviewPage.SelectedRow=new EntityRow(JsonSerializer.SerializeToElement(new{id=newPartner,version=1,name="Aktuális partner"}));await Task.Delay(180);
        if(overviewPage.PartnerOverviewTitle!="Aktuális partner"||overviewPage.PartnerOverviewCards.Single().Value!="Aktuális")throw new InvalidOperationException("Késői partnerdosszié felülírta az aktuális partnert.");
        overviewConnection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));overviewPage.PermissionsChanged();if(overviewPage.PartnerOverviewCards.Count!=0)throw new InvalidOperationException("Admin dossziéösszesítő maradt a jogosultság elvesztése után.");
        page.ConfirmDiscardOverride=()=>true;page.DiscardChangesCommand.Execute(null);if(page.IsDirty||page.Fields[0].Text.Length!=0)throw new InvalidOperationException("Az explicit elvetés nem állította vissza az adatlapot.");
        Console.WriteLine("ui-ok partner NAV fills draft only, preserves extended fields and No cancels discard and exit");
    }
    public static void Render(string output,string skin)
    {
        var page=new EntityPageViewModel(Definition,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"partner-layout-"+skin)));
        var view=new EntityPageView{DataContext=page};var surface=new Border{Child=view,Padding=new(20),Background=(Brush)Application.Current.Resources["BackgroundBrush"]};
        Arrange();var grid=Descendants(view).OfType<DataGrid>().Single();var input=Descendants(view).OfType<TextBox>().First(t=>t.DataContext is EntityFieldViewModel f&&f.Definition.Id=="name");
        if(input.TransformToAncestor(view).Transform(new Point()).Y<=grid.TransformToAncestor(view).Transform(new Point(0,grid.ActualHeight)).Y||grid.ActualWidth<900)throw new InvalidOperationException("A szerkesztő nem a teljes szélességű lista alatt van.");
        var tax=Descendants(view).OfType<TextBox>().Single(t=>t.DataContext is EntityFieldViewModel f&&f.Definition.Id=="taxId");var lookup=Descendants(view).OfType<Button>().Single(b=>b.Content?.ToString()=="Lekérés");
        if(tax.TransformToAncestor(view).Transform(new Point()).Y>=input.TransformToAncestor(view).Transform(new Point()).Y||lookup.TransformToAncestor(view).Transform(new Point()).X<=tax.TransformToAncestor(view).Transform(new Point()).X||Math.Abs(lookup.TransformToAncestor(view).Transform(new Point()).Y-tax.TransformToAncestor(view).Transform(new Point()).Y)>5)throw new InvalidOperationException("Hibás adószámos lekérősor.");
        var advanced=Descendants(view).OfType<Expander>().Single(e=>e.Header?.ToString()=="Bővített adatok kitöltése");if(advanced.IsExpanded)throw new InvalidOperationException("A bővített mezők alapból nyitva vannak.");Snapshot("partner-basic-");advanced.IsExpanded=true;Arrange();Snapshot("partner-extended-");
        if(!Descendants(view).OfType<TextBox>().Any(t=>t.DataContext is EntityFieldViewModel f&&f.Definition.Id=="industry"&&t.ActualHeight>0))throw new InvalidOperationException("A bővített adat nem nyitható meg.");
        advanced.IsExpanded=false;
        page.SelectedRow=new EntityRow(JsonSerializer.SerializeToElement(new{id=Guid.NewGuid(),version=1,name="Minta Partner Kft.",taxId="12345678-2-42",city="Budapest",postalCode="1111",address="Minta utca 1.",email="proba@example.invalid",phone="Teszt telefonszám"}));Arrange();Snapshot("partner-selected-");
        var overviewTitle=Descendants(view).OfType<TextBlock>().Single(t=>System.Windows.Data.BindingOperations.GetBinding(t,TextBlock.TextProperty)?.Path?.Path=="PartnerOverviewTitle");
        var editorCard=FindParent<Border>(input);if(editorCard.ActualWidth>680||overviewTitle.TransformToAncestor(view).Transform(new Point()).X<=input.TransformToAncestor(view).Transform(new Point()).X+input.ActualWidth)throw new InvalidOperationException("A kompakt szerkesztő és áttekintő nem egymás mellett van.");
        surface.Measure(new Size(800,2400));surface.Arrange(new Rect(0,0,800,2400));surface.UpdateLayout();
        if(overviewTitle.TransformToAncestor(view).Transform(new Point()).Y<=input.TransformToAncestor(view).Transform(new Point()).Y+input.ActualHeight)throw new InvalidOperationException("Keskeny ablakban az áttekintő nem került a szerkesztő alá.");
        var narrowBitmap=new RenderTargetBitmap(800,2400,96,96,PixelFormats.Pbgra32);narrowBitmap.Render(surface);var narrowEncoder=new PngBitmapEncoder();narrowEncoder.Frames.Add(BitmapFrame.Create(narrowBitmap));using(var narrowFile=File.Create(Path.Combine(output,"partner-narrow-"+skin+".png")))narrowEncoder.Save(narrowFile);
        var directory=Path.Combine(output,"grouped-settings-"+skin);var shell=new ShellViewModel(new AppearanceStore(directory),directory,null);
        var template=(DataTemplate)Application.Current.FindResource(new DataTemplateKey(typeof(SettingsPage)));var settings=(FrameworkElement)template.LoadContent();settings.DataContext=shell;var settingsSurface=new Border{Child=settings,Padding=new(20),Background=(Brush)Application.Current.Resources["BackgroundBrush"]};settingsSurface.Measure(new Size(1100,3400));settingsSurface.Arrange(new Rect(0,0,1100,3400));settingsSurface.UpdateLayout();
        var groups=Descendants(settings).OfType<Expander>().ToArray();
        if(groups.Length!=8||!groups.Any(g=>g.Header?.ToString()=="Számlák · megjelenítés"))throw new InvalidOperationException("A beállítások modulcsoportjai hiányoznak.");
        var format=Descendants(settings).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="NAV-ból érkező nagybetűs nevek és címek formázása");format.IsChecked=false;if(shell.FormatPartnerNames||PartnerNameFormatting.Enabled)throw new InvalidOperationException("A formázási kapcsoló nincs bekötve.");format.IsChecked=true;
        var settingsBitmap=new RenderTargetBitmap(1100,3400,96,96,PixelFormats.Pbgra32);settingsBitmap.Render(settingsSurface);var settingsEncoder=new PngBitmapEncoder();settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));using(var settingsFile=File.Create(Path.Combine(output,"grouped-settings-"+skin+".png")))settingsEncoder.Save(settingsFile);shell.Stop();
        Console.WriteLine("ui-ok full-width partner list, editor below and collapsible advanced fields "+skin);
        void Arrange(){surface.Measure(new Size(1100,1900));surface.Arrange(new Rect(0,0,1100,1900));surface.UpdateLayout();}
        void Snapshot(string name){var bitmap=new RenderTargetBitmap(1100,1900,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+skin+".png"));encoder.Save(file);}
    }
    private static T FindParent<T>(DependencyObject child) where T:DependencyObject{var parent=VisualTreeHelper.GetParent(child);while(parent is not null){if(parent is T match)return match;parent=VisualTreeHelper.GetParent(parent);}throw new InvalidOperationException("Hiányzó szülőelem.");}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
