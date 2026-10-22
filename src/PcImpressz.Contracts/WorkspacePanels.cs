namespace PcImpressz.Contracts;

public sealed record UiPanelPlacement(string Id,double Width=0,double Height=0);
public sealed record UiWorkspaceVersion(long Revision,UiPanelPlacement[] Panels,string Actor,DateTimeOffset At);
public sealed record UiWorkspaceLayout(long Revision,UiPanelPlacement[] Panels,UiWorkspaceVersion[] History)
{ public int SchemaVersion {get;init;}=1; }
public sealed record UiWorkspaceRequest(Guid CompanyId,string PageKey,string Surface);
public sealed record UiWorkspaceSave(Guid CompanyId,string PageKey,string Surface,string Token,long Revision,UiPanelPlacement[] Panels);
