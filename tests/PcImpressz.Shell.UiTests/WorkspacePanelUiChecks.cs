using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.UiTests;
internal static class WorkspacePanelUiChecks
{
    public static void Render(string output,string skin)
    {
        var company=Guid.NewGuid();var state=new UiWorkspaceLayout(0,[],[]);var saves=0;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation=="ui.workspace.read")return Task.FromResult(JsonSerializer.SerializeToElement(state));
            if(operation=="ui.workspace.save"){var save=(UiWorkspaceSave)input;saves++;state=new(state.Revision+1,save.Panels,[]);return Task.FromResult(JsonSerializer.SerializeToElement(state));}
            return Task.FromResult(JsonSerializer.SerializeToElement(new{rows=Array.Empty<object>(),total=0}));
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Számlák","","",[],[new("number","Számlaszám")],new("list","save","reverse","export")){ViewKind="receiving"};
        var page=new ReceivingPageViewModel(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"panel-layout-check")));
        page.ManualCommand.Execute(null);page.Number="NEM-MENTETT-001";page.Lines.Single().Name="Megőrzendő termék";
        var workspace=new ArrangeableWorkspace("incoming");var number=new TextBox();number.SetBinding(TextBox.TextProperty,"Number");var form=new StackPanel();form.Children.Add(new TextBlock{Text="Számlabevételezés – nem mentett adat"});form.Children.Add(number);
        workspace.AddPanel("editor","Számlaadatok",form,600);workspace.AddPanel("list","Számlalista",new DataGrid{MinHeight=180,Columns={new DataGridTextColumn{Header="Számlaszám",Width=220},new DataGridTextColumn{Header="Szállító",Width=220}}});workspace.DataContext=page;
        var surface=new Border{Child=workspace,Background=(Brush)Application.Current.Resources["BackgroundBrush"]};surface.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"TextBrush");Arrange();
        if(!workspace.BeginArrange())throw new InvalidOperationException("Admin nem indíthat panelrendezést.");workspace.MovePanel("list",0);workspace.ResizePanel("editor",560,260);Arrange();
        if(workspace.Placements[0].Id!="list"||workspace.Placements.Single(p=>p.Id=="editor").Width!=560||page.Number!="NEM-MENTETT-001"||page.Lines.Single().Name!="Megőrzendő termék"||!page.IsDirty)throw new InvalidOperationException("Panelrendezés közben elveszett az üzleti piszkozat.");
        var bitmap=new RenderTargetBitmap(1100,850,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"workspace-panels-"+skin+".png")))encoder.Save(file);
        workspace.SaveAsync().GetAwaiter().GetResult();if(saves!=1||workspace.IsArranging||state.Panels[0].Id!="list")throw new InvalidOperationException("A panelnézet nem mentődött.");
        if(!workspace.BeginArrange())throw new InvalidOperationException("Mentés után nem indítható új elrendezés.");workspace.ResizePanel("editor",900,600);workspace.Cancel();if(workspace.Placements.Single(p=>p.Id=="editor").Width!=560||page.Number!="NEM-MENTETT-001")throw new InvalidOperationException("A Mégse nem állította vissza a nézetet.");
        var restored=new ArrangeableWorkspace("incoming");restored.AddPanel("editor","Számlaadatok",new TextBox(),600);restored.AddPanel("list","Lista",new TextBlock());restored.DataContext=page;
        if(restored.Placements[0].Id!="list"||restored.Placements[1].Width!=560)throw new InvalidOperationException("A mentett nézet nem töltődött vissza.");
        connection.SetSession(company,new("worker","worker","warehouse",DateTimeOffset.UtcNow.AddMinutes(30)));page.PermissionsChanged();if(workspace.BeginArrange())throw new InvalidOperationException("Nem admin felhasználó rendezhet panelt.");workspace.ResizePanel("editor",900,600);if(workspace.Placements.Single(p=>p.Id=="editor").Width!=560)throw new InvalidOperationException("Nem admin átírta a panelméretet.");
        surface.Child=null;var scrollContent=new StackPanel();scrollContent.Children.Add(workspace);scrollContent.Children.Add(new Border{Height=1000});var outer=new ScrollViewer{Content=scrollContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};outer.Measure(new Size(1100,400));outer.Arrange(new Rect(0,0,1100,400));outer.UpdateLayout();
        number.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=number});outer.UpdateLayout();if(outer.VerticalOffset<=0)throw new InvalidOperationException("A panel megakasztja az oldal egérgörgős görgetését.");
        Console.WriteLine("ui-ok panel movement resize save role guard and preserved invoice draft "+skin);
        void Arrange(){surface.Measure(new Size(1100,850));surface.Arrange(new Rect(0,0,1100,850));surface.UpdateLayout();}
    }
}
