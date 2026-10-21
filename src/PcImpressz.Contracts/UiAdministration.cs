namespace PcImpressz.Contracts;

public sealed record UiFieldLayout(string Id, string Label, int Span = 1, bool Visible = true);
public sealed record UiLayoutVersion(long Revision, UiFieldLayout[] Fields, string Actor, DateTimeOffset At);
public sealed record UiLayoutState(long Revision, UiFieldLayout[] Fields, UiLayoutVersion[] History)
{ public int SchemaVersion { get; init; } = 1; }
public sealed record UiPageRequest(Guid CompanyId, string PageKey);
public sealed record UiLayoutSave(Guid CompanyId, string PageKey, string Token, long Revision, UiFieldLayout[] Fields);
public sealed record UiLayoutRestore(Guid CompanyId, string PageKey, string Token, long Revision, long TargetRevision);
public sealed record UiLogin(Guid CompanyId, string UserName, string Password);
public sealed record UiCreateUser(Guid CompanyId, string Token, string UserName, string Password, string Role);
public sealed record UiSession(string Token, string UserName, string Role, DateTimeOffset ExpiresAt);
public sealed record UiAuthStatus(bool Configured);
