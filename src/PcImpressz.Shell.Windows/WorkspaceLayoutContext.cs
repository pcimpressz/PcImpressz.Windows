using System.ComponentModel;
using System.Text.Json;

namespace PcImpressz.Shell.Windows;
internal interface IWorkspaceLayoutContext:INotifyPropertyChanged
{
    Guid? WorkspaceCompany {get;}
    bool CanArrangeWorkspace {get;}
    bool WorkspaceBusy {get;}
    string WorkspacePageKey {get;}
    Task<JsonElement> SendWorkspaceAsync(string operation,object payload);
}
public sealed partial class EntityPageViewModel:IWorkspaceLayoutContext
{
    Guid? IWorkspaceLayoutContext.WorkspaceCompany=>_company();
    bool IWorkspaceLayoutContext.CanArrangeWorkspace=>_connection.Session is {Role:"admin"} session&&session.ExpiresAt>DateTimeOffset.UtcNow&&_connection.SessionCompany==_company();
    bool IWorkspaceLayoutContext.WorkspaceBusy=>IsBusy;
    string IWorkspaceLayoutContext.WorkspacePageKey=>Definition.ModuleId+"."+Definition.Id;
    Task<JsonElement> IWorkspaceLayoutContext.SendWorkspaceAsync(string operation,object payload)=>_connection.SendAsync(operation,payload,_shutdown);
}
internal sealed partial class ReceivingPageViewModel:IWorkspaceLayoutContext
{
    Guid? IWorkspaceLayoutContext.WorkspaceCompany=>_company();
    bool IWorkspaceLayoutContext.CanArrangeWorkspace=>_connection.Session is {Role:"admin"} session&&session.ExpiresAt>DateTimeOffset.UtcNow&&_connection.SessionCompany==_company();
    bool IWorkspaceLayoutContext.WorkspaceBusy=>IsBusy;
    string IWorkspaceLayoutContext.WorkspacePageKey=>Definition.ModuleId+"."+Definition.Id;
    Task<JsonElement> IWorkspaceLayoutContext.SendWorkspaceAsync(string operation,object payload)=>_connection.SendAsync(operation,payload,_shutdown);
}
internal sealed partial class OperationsPageViewModel:IWorkspaceLayoutContext
{
    Guid? IWorkspaceLayoutContext.WorkspaceCompany=>_company();
    bool IWorkspaceLayoutContext.CanArrangeWorkspace=>_connection.Session is {Role:"admin"} session&&session.ExpiresAt>DateTimeOffset.UtcNow&&_connection.SessionCompany==_company();
    bool IWorkspaceLayoutContext.WorkspaceBusy=>IsBusy;
    string IWorkspaceLayoutContext.WorkspacePageKey=>Definition.ModuleId+"."+Definition.Id;
    Task<JsonElement> IWorkspaceLayoutContext.SendWorkspaceAsync(string operation,object payload)=>_connection.SendAsync(operation,payload,_shutdown);
}
