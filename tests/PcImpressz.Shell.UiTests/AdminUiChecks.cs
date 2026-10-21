using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.UiTests;
internal static class AdminUiChecks
{
    public static void Render(string output,string skin)
    {
        var definition=new EntityPageDescriptor("products","catalog","Termékadatlap","","",
            [new("name","Termék neve",Required:true),new("sku","Saját cikkszám"),new("manufacturerPartNumber","Gyártói cikkszám")],
            [new("name","Termék")],new("list","save","archive","export"));
        var page=new EntityPageViewModel(definition,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"admin-layout")));
        page.Fields[0].Text="Megőrzendő terméknév";page.Fields[1].Text="MOUSE-1";
        var initial=new UiLayoutState(0,definition.Fields.Select(f=>new UiFieldLayout(f.Id,f.Label,f.Id=="name"?2:1)).ToArray(),[]);
        var window=LayoutEditorDialog.BuildEditor(page,Guid.NewGuid(),new("test-only-token","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)),initial);
        var root=(FrameworkElement)window.Content;
        window.Content=null;var surface=new Border{Child=root,Background=(Brush)Application.Current.Resources["BackgroundBrush"]};surface.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"TextBrush");
        surface.Measure(new Size(1060,740));surface.Arrange(new Rect(0,0,1060,740));surface.UpdateLayout();
        var list=Descendants(root).OfType<ListBox>().Single();
        var drafts=(ObservableCollection<LayoutFieldDraft>)list.ItemsSource;
        drafts[0].Label="Hosszú terméknév és megnevezés";
        if(drafts[0].CanHide)throw new InvalidOperationException("A kötelező mező elrejthető.");
        if(!LayoutEditorDialog.MoveField(drafts,drafts[2],0)||LayoutEditorDialog.MoveField(drafts,drafts[0],-1))throw new InvalidOperationException("Hibás mezőmozgatás.");
        drafts[0].Span=2;list.SelectedIndex=0;
        if(page.Fields[0].DisplayLabel!="Termék neve")throw new InvalidOperationException("Az előnézet mentés nélkül megváltoztatta az adatlapot.");
        page.ApplyAppearance(new(1,drafts.Select(d=>d.Value).ToArray(),[]));
        if(page.Fields[0].Definition.Id!="manufacturerPartNumber"||page.Fields.Single(f=>f.Definition.Id=="name").Text!="Megőrzendő terméknév"||!page.IsDirty)
            throw new InvalidOperationException("Az elrendezés alkalmazása megváltoztatta a szerkesztett adatot.");
        root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1060,740,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"admin-layout-"+skin+".png")))encoder.Save(file);
        Console.WriteLine("ui-ok admin layout preview, reordering and draft preservation "+skin);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var item in Descendants(child))yield return item;}
    }
}
