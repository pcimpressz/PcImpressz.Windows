namespace PcImpressz.Contracts;

public static class CompanyRoles
{
    public static bool Valid(string role) => role is "admin" or "warehouse" or "sales" or "user";
    public static string Label(string role) => role switch { "admin"=>"Adminisztrátor", "warehouse"=>"Raktáros", "sales"=>"Eladó", "user"=>"Megtekintő", _=>"Ismeretlen" };
    public static bool Allows(string role,string permission) => role == "admin" || role switch
    {
        "warehouse" => permission is "partners.read" or "catalog.read" or "catalog.write" or "catalog.export" or "inventory.read" or "inventory.export" or "receiving.read" or "receiving.write" or "receiving.export",
        "sales" => permission is "partners.read" or "catalog.read" or "inventory.read" or "sales.read" or "sales.write" or "sales.export",
        "user" => permission is "partners.read" or "catalog.read" or "inventory.read",
        _ => false
    };
}
public sealed record CompanyUser(string UserName,string Role,bool Disabled);
public sealed record UiUpdateUser(Guid CompanyId,string Token,string UserName,string Role,bool Disabled,string? NewPassword=null);
public sealed record UiChangePassword(Guid CompanyId,string Token,string CurrentPassword,string NewPassword);
