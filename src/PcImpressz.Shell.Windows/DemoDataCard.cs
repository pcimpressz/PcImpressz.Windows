using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace PcImpressz.Shell.Windows;
public sealed class DemoDataCard:StackPanel
{
    public DemoDataCard()
    {
        SetBinding(VisibilityProperty,new Binding("IsDemoCompany"){Converter=new BooleanToVisibilityConverter()});
        var description=new TextBlock{Text="Próbáld ki a teljes munkafolyamatot: 20 termék, 5 vevő, 5 szállító, kategóriák, mértékegységek, két raktár készlete, bevételezések és eladások. Ismételt kattintás nem duplázza az adatokat.",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)};description.SetResourceReference(StyleProperty,"Muted");Children.Add(description);
        var button=new Button{Content="Feltöltés mintaadatokkal",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,16,0,0)};
        button.SetResourceReference(StyleProperty,"PrimaryButton");button.SetBinding(Button.CommandProperty,"SeedDemoCommand");button.SetBinding(IsEnabledProperty,"CanSeedDemo");Children.Add(button);
        var hint=new TextBlock{Text="A feltöltést a demócég adminja indíthatja.",Margin=new(0,8,0,0)};hint.SetResourceReference(StyleProperty,"Muted");Children.Add(hint);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,16)};message.SetResourceReference(StyleProperty,"Muted");message.SetBinding(TextBlock.TextProperty,"DemoMessage");Children.Add(message);
    }
}
