using PcImpressz.Contracts;

namespace PcImpressz.Core.Security;
public sealed partial class UiAdministrationService
{
    private static string WorkspaceKey(string page,string surface)
    {
        if(string.IsNullOrWhiteSpace(page)||page.Length>81||page.Split('.') is not [var module,var name]||!PanelIdentifier(module)||!PanelIdentifier(name)||!PanelIdentifier(surface))throw new ArgumentException("Érvénytelen nézetazonosító.");
        return "panels/"+page+"/"+surface;
    }
    private static bool PanelIdentifier(string? value)=>value is {Length:>0 and <=40}&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
    public static void ValidatePanels(UiPanelPlacement[]? panels)
    {
        if(panels is null||panels.Length>32||panels.Any(p=>p is null||!PanelIdentifier(p.Id)||!double.IsFinite(p.Width)||!double.IsFinite(p.Height)||p.Width!=0&&p.Width is <280 or >1800||p.Height!=0&&p.Height is <160 or >1600)||panels.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=panels.Length)throw new ArgumentException("Érvénytelen panelméret vagy sorrend.");
    }
    public UiWorkspaceLayout ReadWorkspace(UiWorkspaceRequest request)
    {
        var key=WorkspaceKey(request.PageKey,request.Surface);
        return companies.Execute(request.CompanyId,tx=>
        {
            var state=tx.Read<UiWorkspaceLayout>("ui-layout",key)??new(0,[],[]);if(state.SchemaVersion!=1)throw new InvalidOperationException("A nézet újabb programmal készült.");ValidatePanels(state.Panels);return state;
        });
    }
    public UiWorkspaceLayout SaveWorkspace(UiWorkspaceSave request,string sid)
    {
        var actor=Admin(request.CompanyId,request.Token,sid);var key=WorkspaceKey(request.PageKey,request.Surface);ValidatePanels(request.Panels);
        return companies.Execute(request.CompanyId,tx=>
        {
            var before=tx.Read<UiWorkspaceLayout>("ui-layout",key)??new(0,[],[]);
            if(before.SchemaVersion!=1)throw new InvalidOperationException("A nézet újabb programmal készült; nem írható felül.");
            if(before.Revision!=request.Revision)throw new InvalidOperationException("A nézetet közben más is módosította. Töltsd újra a nézetet, majd rendezd ismét.");
            var history=(before.History??[]).Append(new(before.Revision,before.Panels,actor,clock.GetUtcNow())).TakeLast(10).ToArray();
            var saved=new UiWorkspaceLayout(checked(before.Revision+1),request.Panels,history);
            tx.Write("ui-layout",key,saved);return saved;
        });
    }
}
