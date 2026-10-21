using PcImpressz.Contracts;
using PcImpressz.Core.Storage;

namespace PcImpressz.Core.Security;
public sealed partial class UiAdministrationService
{
    public UiSession Authenticate(Guid company,string? token,string sid)
    {
        lock(_gate)
        {
            if(token is null || !_sessions.TryGetValue(token,out var bound) || bound.Company!=company || bound.Sid!=sid || bound.Session.ExpiresAt<=clock.GetUtcNow())
                throw new UnauthorizedAccessException("Jelentkezz be a kiválasztott céghez. A munkamenet hiányzik vagy lejárt.");
            if(bound.Development)
            {
                if(developmentAccess?.Invoke(sid)!=true)throw new UnauthorizedAccessException("A fejlesztői mód kikapcsolva; jelentkezz be rendesen.");
                return bound.Session;
            }
            var user=credentials.Execute(company,tx=>tx.Read<User>("accounts","user/"+bound.Session.UserName));
            if(user is null || user.Disabled || user.Role!=bound.Session.Role)throw new UnauthorizedAccessException("A fiók jogosultsága megváltozott. Jelentkezz be újra.");
            return bound.Session;
        }
    }
    public UiSession RequireAdmin(Guid company,string? token,string sid)
    {
        var session=Authenticate(company,token,sid);
        if(session.Role!="admin")throw new UnauthorizedAccessException("Ehhez a művelethez adminisztrátori jogosultság szükséges.");
        return session;
    }
    public CompanyUser[] ListUsers(Guid company,string token,string sid)
    {
        RequireAdmin(company,token,sid);
        return credentials.Execute(company,tx=>tx.List("accounts","user/").Select(body=>System.Text.Json.JsonSerializer.Deserialize<User>(body,CompanyDocuments.Json)!).OrderBy(u=>u.UserName).Select(u=>new CompanyUser(u.UserName,u.Role,u.Disabled)).ToArray());
    }
    private void Revoke(Guid company,string name)
    {
        foreach(var token in _sessions.Where(p=>p.Value.Company==company&&p.Value.Session.UserName==name).Select(p=>p.Key).ToArray())_sessions.Remove(token);
    }
    public void UpdateUser(UiUpdateUser input,string sid)
    {
        lock(_gate)
        {
            var actor=RequireAdmin(input.CompanyId,input.Token,sid).UserName;
            var name=Name(input.UserName);
            if(!CompanyRoles.Valid(input.Role))throw new ArgumentException("Ismeretlen szerepkör.");
            if(input.NewPassword is not null)Password(input.NewPassword);
            credentials.Execute(input.CompanyId,tx=>
            {
                var before=tx.Read<User>("accounts","user/"+name)??throw new ArgumentException("Ismeretlen felhasználó.");
                if(before.Role=="admin"&&!before.Disabled&&(input.Role!="admin"||input.Disabled))
                {
                    var admins=tx.List("accounts","user/").Select(body=>System.Text.Json.JsonSerializer.Deserialize<User>(body,CompanyDocuments.Json)!).Count(u=>u.Role=="admin"&&!u.Disabled);
                    if(admins<=1)throw new ArgumentException("Az utolsó aktív admin nem tiltható le és szerepköre nem vonható vissza.");
                }
                var after=input.NewPassword is null?before with{Role=input.Role,Disabled=input.Disabled}:NewUser(name,input.NewPassword,input.Role) with{Disabled=input.Disabled};
                tx.Write("accounts","user/"+name,after);
                tx.Write("accounts","audit/"+Guid.NewGuid().ToString("N"),new{actor,name,action="update-user",beforeRole=before.Role,afterRole=input.Role,input.Disabled,passwordChanged=input.NewPassword is not null,at=clock.GetUtcNow()});return 0;
            });
            Revoke(input.CompanyId,name);
        }
    }
    public void ChangePassword(UiChangePassword input,string sid)
    {
        lock(_gate)
        {
            var actor=Authenticate(input.CompanyId,input.Token,sid).UserName;
            Password(input.NewPassword);
            if(input.CurrentPassword is null || input.CurrentPassword.Length>200)throw new ArgumentException("Érvénytelen jelszó.");
            credentials.Execute(input.CompanyId,tx=>
            {
                var user=tx.Read<User>("accounts","user/"+actor)!;
                if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Hash(input.CurrentPassword,Convert.FromHexString(user.Salt)),Convert.FromHexString(user.Hash)))
                    throw new UnauthorizedAccessException("A jelenlegi jelszó hibás.");
                tx.Write("accounts","user/"+actor,NewUser(actor,input.NewPassword,user.Role));
                tx.Write("accounts","audit/"+Guid.NewGuid().ToString("N"),new{actor,action="change-password",at=clock.GetUtcNow()});return 0;
            });
            Revoke(input.CompanyId,actor);
        }
    }
}
