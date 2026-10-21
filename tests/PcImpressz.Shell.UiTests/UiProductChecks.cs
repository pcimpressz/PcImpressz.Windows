using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.UiTests;
internal static class UiProductChecks
{
    public static void VerifyAppliedQuery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcimpressz-query-" + Guid.NewGuid().ToString("N"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new NamedPipeServerStream(DiagnosticChannel.ForDirectory(directory) + "-commands", PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var server = Task.Run(async () =>
        {
            await first.WaitForConnectionAsync(cancel.Token);
            var request = await LocalProtocol.ReadAsync<LocalRequest>(first, cancel.Token);
            var input = request.Payload.GetProperty("Input");
            if (input.GetProperty("search").GetString() != "régi") throw new InvalidOperationException("Rossz keresés ment ki.");
            seen.SetResult(); await release.Task.WaitAsync(cancel.Token);
            await LocalProtocol.WriteAsync(first, new LocalResponse(true, JsonSerializer.SerializeToElement(new { rows = Array.Empty<object>(), total = 0 })), cancel.Token);
        }, cancel.Token);
        var definition = new EntityPageDescriptor("products", "catalog", "Terméktörzs", "", "", [], [new("name", "Név")], new("list", "save", "archive", "export"));
        var vm = new EntityPageViewModel(definition, new HostConnection(directory, null), () => Guid.NewGuid(), cancel.Token, new ListLayoutStore(directory));
        void CancelTyping() => typeof(EntityPageViewModel).GetField("_typed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(vm)!.GetType().GetMethod("CancelPending")!.Invoke(typeof(EntityPageViewModel).GetField("_typed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm), null);
        vm.Search = "régi"; CancelTyping(); var loading = vm.LoadAsync(); Pump(seen.Task);
        vm.Search = "új"; CancelTyping(); release.SetResult(); Pump(Task.WhenAll(loading, server));
        if (!vm.AppliedSearchLabel.Contains("régi") || vm.AppliedSearchLabel.Contains("új"))
            throw new InvalidOperationException("A beírt keresés felülírta a megjelenített lista feltételét.");
        cancel.Cancel();
        var target = Path.Combine(Path.GetTempPath(), "pcimpressz-export-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(target, "eredeti");
        try
        {
            try { AtomicExport.WriteAsync(target, "új", new CancellationToken(true)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            if (File.ReadAllText(target) != "eredeti") throw new InvalidOperationException("A megszakadt export felülírta a célfájlt.");
            AtomicExport.WriteAsync(target, "új", CancellationToken.None).GetAwaiter().GetResult();
            if (File.ReadAllText(target) != "új") throw new InvalidOperationException("Az export nem véglegesült.");
        }
        finally { File.Delete(target); }
        Console.WriteLine("ui-ok applied query during delayed response and atomic export");
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        var timeout = DateTime.UtcNow.AddSeconds(12);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        { if (task.IsCompleted || DateTime.UtcNow > timeout) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); if (!task.IsCompleted) throw new TimeoutException("UI regression timeout"); task.GetAwaiter().GetResult(); }
        finally { timer.Stop(); }
    }
    public static void Render(string output, string skin)
    {
        var definition = new EntityPageDescriptor("products", "catalog", "Terméktörzs", "", "",
            [new("name", "Termék neve"), new("sku", "Saját cikkszám"), new("manufacturerPartNumber", "Gyártói cikkszám")],
            [new("name", "Termék"), new("sku", "Cikkszám"), new("manufacturerPartNumber", "Gyártói cikkszám"), new("quantity", "Saját készlet")], new("list", "save", "archive", "export"));
        definition = definition with { Labels = new EntityPageLabels { NewLabel="Új termék", EditTitle="Termék szerkesztése", CountNoun="termék", SearchHint="Több szó, ékezet nélkül is. Példa: vezetek nelkuli eger · gyartoi:=ABC-123" }, Filters = [new("all", "Aktív termékek")] };
        var stockDefinition = definition with { Id = "inventory", ModuleId = "inventory", ViewKind = "inventory" };
        var connection = new HostConnection(output, null); var layouts = new ListLayoutStore(Path.Combine(output, "product-layout"));
        var products = new EntityPageViewModel(definition, connection, () => null, CancellationToken.None, layouts);
        var stock = new OperationsPageViewModel(stockDefinition, connection, () => null, CancellationToken.None, layouts);
        products.SelectedRow = new EntityRow(JsonSerializer.SerializeToElement(new { id = Guid.NewGuid(), version = 1, name = "Vezeték nélküli egér", sku = "MOUSE-1", manufacturerPartNumber = "ABC-123", unitCode = "DB", quantity = 2 }));
        var workspace = new ProductWorkspaceViewModel(products, stock);
        var view = new ContentControl { Content = workspace, Width = 1100 };
        view.Measure(new Size(1100, double.PositiveInfinity)); var height = Math.Max(1000, Math.Min(2400, view.DesiredSize.Height));
        view.Arrange(new Rect(0, 0, 1100, height)); view.UpdateLayout();
        var surface = new Border { Background = (Brush)Application.Current.Resources["BackgroundBrush"], Child = view, Width = 1100, Height = height };
        surface.Measure(new Size(1100, height)); surface.Arrange(new Rect(0, 0, 1100, height)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1100, (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "products-" + skin + ".png"))) encoder.Save(file);
        workspace.SelectedStockCommand.Execute(null);
        if (workspace.SelectedTab != 1 || stock.SelectedProduct?.Sku != "MOUSE-1") throw new InvalidOperationException("Nem a kijelölt termék készlete nyílt meg.");
        stock.QuantityText = "3";
        workspace.AllStockCommand.Execute(null);
        if (stock.SelectedProduct?.Sku != "MOUSE-1") throw new InvalidOperationException("A készletnézet-váltás eldobta a szerkesztett műveletet.");
        Console.WriteLine("ui-ok products and selected stock " + skin);
    }
}
