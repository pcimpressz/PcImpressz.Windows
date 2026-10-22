using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PcImpressz.Shell.Windows;
internal static class NiimbotLabelPrinter
{
    private static readonly Guid ServiceId=new("e7810a71-73ae-499d-8c15-faa9aef0c3f2"),CharacteristicId=new("bef8d6c9-9c21-4c9e-b632-bd58c1009f9f");
    private static ulong? _address;
    private static bool _printing;
    internal static byte[] Packet(byte command,params byte[] data)
    {
        if(data.Length>255)throw new ArgumentException("Túl hosszú nyomtatócsomag.");byte sum=(byte)(command^data.Length);foreach(var b in data)sum^=b;return new byte[]{0x55,0x55,command,(byte)data.Length}.Concat(data).Concat(new byte[]{sum,0xaa,0xaa}).ToArray();
    }
    internal static byte[][] Raster(string barcode,string name)
    {
        var page=BarcodeLabelPrinter.Build(barcode,name);page.Measure(new Size(page.Width,page.Height));page.Arrange(new Rect(0,0,page.Width,page.Height));page.UpdateLayout();
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(page),null,new Rect(0,0,320,96));
        var image=new RenderTargetBitmap(320,96,96,96,PixelFormats.Pbgra32);image.Render(visual);var pixels=new byte[320*96*4];image.CopyPixels(pixels,320*4,0);
        var rows=new byte[320][];for(var y=0;y<320;y++){var row=new byte[12];for(var x=0;x<96;x++){var offset=((95-x)*320+y)*4;if(pixels[offset+3]>0&&(pixels[offset]+pixels[offset+1]+pixels[offset+2])/3<128)row[x/8]|=(byte)(1<<(7-x%8));}rows[y]=row;}return rows;
    }
    private static byte[] U16(int n)=>[(byte)(n>>8),(byte)n];
    public static async Task PrintAsync(string barcode,string name)
    {
        if(_printing)throw new ArgumentException("Egy címke küldése már folyamatban van.");
        var rows=Raster(barcode,name);_printing=true;
        try
        {
            if(_address is null)_address=await SelectAsync();
            using var device=await BluetoothLEDevice.FromBluetoothAddressAsync(_address.Value)??throw new ArgumentException("A Niimbot nem érhető el. Kapcsold be, és bontsd a telefonos vagy böngészős kapcsolatát.");
            var services=await device.GetGattServicesForUuidAsync(ServiceId,BluetoothCacheMode.Uncached);
            if(services.Status!=GattCommunicationStatus.Success||services.Services.Count==0)throw new ArgumentException("A Niimbot Bluetooth-szolgáltatása nem érhető el.");
            using var service=services.Services[0];var characteristics=await service.GetCharacteristicsForUuidAsync(CharacteristicId,BluetoothCacheMode.Uncached);
            if(characteristics.Status!=GattCommunicationStatus.Success||characteristics.Characteristics.Count==0)throw new ArgumentException("A nyomtató adatcsatornája nem érhető el.");
            var characteristic=characteristics.Characteristics[0];TaskCompletionSource<bool>? reply=null;
            void Received(GattCharacteristic sender,GattValueChangedEventArgs args)=>reply?.TrySetResult(true);
            characteristic.ValueChanged+=Received;
            try
            {
                if(await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)!=GattCommunicationStatus.Success)throw new ArgumentException("A nyomtató állapotjelzése nem kapcsolható be.");
                async Task Send(byte command,byte[] data,bool wait=true)
                {
                    var pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);reply=pending;
                    using var writer=new DataWriter();writer.WriteBytes(Packet(command,data));
                    var option=characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)?GattWriteOption.WriteWithoutResponse:GattWriteOption.WriteWithResponse;
                    if(await characteristic.WriteValueAsync(writer.DetachBuffer(),option)!=GattCommunicationStatus.Success)throw new IOException("A címkeküldés megszakadt; ellenőrizd a nyomtatót, mielőtt ismét nyomtatsz.");
                    if(wait)await Task.WhenAny(pending.Task,Task.Delay(1000));
                }
                // D110-M: no PageStart; 13-byte SetPageSize; same sequence as the existing warehouse application.
                await Send(0x23,[1]);await Send(0x21,[3]);await Send(0x01,[0,1,0,0,0,0,0,0,0]);await Send(0xa3,[1],false);
                await Send(0x13,[..U16(320),..U16(96),0,1,0,0,0,0,0,0,0]);
                for(var i=0;i<rows.Length;i++)
                {
                    var count=rows[i].Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
                    if(count==0)await Send(0x84,[..U16(i),1],false);
                    else await Send(0x85,[..U16(i),0,..U16(count),1,..rows[i]],false);
                    if(i%8==0)await Task.Delay(8);
                }
                await Send(0xe3,[1]);await Task.Delay(1500);await Send(0xf3,[1]);await Send(0xdc,[1],false);
            }
            finally{characteristic.ValueChanged-=Received;}
        }
        catch{_address=null;throw;}
        finally{_printing=false;}
    }
    private static async Task<ulong> SelectAsync()
    {
        var found=new Dictionary<ulong,string>();var sync=new object();var watcher=new BluetoothLEAdvertisementWatcher{ScanningMode=BluetoothLEScanningMode.Active};
        watcher.Received+=(_,args)=>{var name=args.Advertisement.LocalName;if(name.StartsWith("D110",StringComparison.OrdinalIgnoreCase))lock(sync)found[args.BluetoothAddress]=name;};
        watcher.Start();try{await Task.Delay(6000);}finally{watcher.Stop();}
        KeyValuePair<ulong,string>[] choices;lock(sync)choices=found.ToArray();
        if(choices.Length==0)throw new ArgumentException("Nem található D110-M. Kapcsold be a Bluetooth-t és a nyomtatót; a telefonos / böngészős kapcsolat legyen bontva.");
        if(choices.Length==1)return choices[0].Key;
        var window=new Window{Title="Niimbot címkenyomtató",Width=430,Height=260,WindowStartupLocation=WindowStartupLocation.CenterScreen};var panel=new StackPanel{Margin=new(20)};var list=new ComboBox{ItemsSource=choices,DisplayMemberPath="Value",SelectedIndex=0};panel.Children.Add(list);var button=new Button{Content="Nyomtatás",Margin=new(0,16,0,0)};button.Click+=(_,_)=>window.DialogResult=true;panel.Children.Add(button);window.Content=panel;
        if(window.ShowDialog()!=true)throw new OperationCanceledException("A címkenyomtatás megszakítva.");return ((KeyValuePair<ulong,string>)list.SelectedItem).Key;
    }
}
