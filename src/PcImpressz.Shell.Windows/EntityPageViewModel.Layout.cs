using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;
public sealed partial class EntityPageViewModel
{
    public ICommand EditLayoutCommand { get; private set; } = null!;
    public int AppearanceRevision { get; private set; }
    internal Guid LayoutCompanyId => CompanyId;
    internal string LayoutPageKey => Definition.ModuleId + "." + Definition.Id;
    private int _appearanceGeneration;
    private void InitializeLayoutEditor() => EditLayoutCommand = new RelayCommand(() => _ = EditLayoutAsync());
    internal Task<JsonElement> SendUiAsync(string operation, object payload) => _connection.SendAsync(operation, payload, _shutdown);
    private UiFieldLayout[] DefaultAppearance() => Definition.Fields.Select(f => new UiFieldLayout(f.Id,f.Label,f.Id=="name"||f.Kind=="multiline"?2:1)).ToArray();
    private void ResetAppearance()
    {
        _appearanceGeneration++;
        ApplyAppearance(new(0,DefaultAppearance(),[]));
    }
    internal void ApplyAppearance(UiLayoutState state)
    {
        var fields=Fields.ToDictionary(f=>f.Definition.Id,StringComparer.Ordinal);
        if(state.Fields.Length!=fields.Count||state.Fields.Select(f=>f.Id).Distinct().Count()!=fields.Count||state.Fields.Any(f=>!fields.ContainsKey(f.Id)))return;
        var ordered=state.Fields.Select(layout=>
        {
            var field=fields[layout.Id];field.DisplayLabel=layout.Label;field.Span=layout.Span;field.Visible=layout.Visible;return field;
        }).ToArray();
        Fields.Clear();foreach(var field in ordered)Fields.Add(field);
        AppearanceRevision++;Changed(nameof(AppearanceRevision));
    }
    private async Task LoadAppearanceAsync()
    {
        var generation=_appearanceGeneration;
        if(_company() is not Guid company)return;
        try
        {
            var data=await SendUiAsync("ui.layout.read",new UiPageRequest(company,LayoutPageKey));
            if(generation==_appearanceGeneration&&_company()==company&&data.Deserialize<UiLayoutState>() is { } state)ApplyAppearance(state);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        { if(generation==_appearanceGeneration)Notify("Az alapértelmezett elrendezés látható; az egyéni nézet nem tölthető be: "+ex.Message); }
    }
    private async Task EditLayoutAsync()
    {
        if(IsBusy)return;
        IsBusy=true;
        try{await LayoutEditorDialog.ShowAsync(this);}
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
}
