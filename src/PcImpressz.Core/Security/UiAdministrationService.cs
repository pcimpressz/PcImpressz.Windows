using System.Security.Cryptography;
using System.Text;
using PcImpressz.Contracts;
using PcImpressz.Core.Storage;

namespace PcImpressz.Core.Security;

// Credentials are installation-local and separate from portable company backups.
// The Windows owner remains a trusted local operator; this is not an OS sandbox.
public sealed class UiAdministrationService(ICompanyDatabase companies, ICompanyDatabase credentials, TimeProvider clock)
{
    private sealed record User(string UserName, string Role, string Salt, string Hash);
    private sealed record BoundSession(Guid Company, string Sid, UiSession Session);
    private readonly Dictionary<string, BoundSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (int Count, DateTimeOffset Until)> _failures = [];
    private readonly object _gate = new();
    private static string Name(string? name)
    {
        var value = (name ?? "").Trim().ToLowerInvariant();
        if (value.Length is < 3 or > 40 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("A felhasználónév 3–40 betű, szám, pont, kötőjel vagy aláhúzás lehet.");
        return value;
    }
    private static void Password(string? password)
    {
        if (password is null || password.Length is < 12 or > 200) throw new ArgumentException("A jelszó 12–200 karakter legyen.");
    }
    private static byte[] Hash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 210_000, HashAlgorithmName.SHA256, 32);
    private static User NewUser(string name, string password, string role)
    {
        Password(password); var salt = RandomNumberGenerator.GetBytes(16);
        return new(name, role, Convert.ToHexString(salt), Convert.ToHexString(Hash(password, salt)));
    }
    public UiAuthStatus Status(Guid company) => credentials.Execute(company, tx => new UiAuthStatus(tx.List("accounts", "user/").Count > 0));
    public UiSession Bootstrap(UiLogin input, string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        var name = Name(input.UserName); var user = NewUser(name, input.Password, "admin");
        credentials.Execute(input.CompanyId, tx =>
        {
            if (tx.List("accounts", "user/").Count != 0) throw new UnauthorizedAccessException("Az első admin már létre lett hozva. Jelentkezz be.");
            tx.Write("accounts", "user/" + name, user); return 0;
        });
        return Issue(input.CompanyId, sid, user);
    }
    public UiSession Login(UiLogin input, string sid)
    {
        var name = Name(input.UserName);
        if (input.Password is null || input.Password.Length > 200) throw new ArgumentException("Érvénytelen bejelentkezés.");
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_failures.TryGetValue(input.CompanyId, out var failure) && failure.Until > now)
                throw new UnauthorizedAccessException("Túl sok sikertelen próbálkozás. Egy perc múlva próbáld újra.");
            var user = credentials.Execute(input.CompanyId, tx => tx.Read<User>("accounts", "user/" + name));
            var salt = user is null ? new byte[16] : Convert.FromHexString(user.Salt);
            var hash = Hash(input.Password, salt);
            if (user is null || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(user.Hash)))
            {
                var count = failure.Until <= now && failure.Count >= 5 ? 1 : failure.Count + 1;
                _failures[input.CompanyId] = (count, count >= 5 ? now.AddMinutes(1) : DateTimeOffset.MinValue);
                throw new UnauthorizedAccessException("Hibás felhasználónév vagy jelszó.");
            }
            _failures.Remove(input.CompanyId); return Issue(input.CompanyId, sid, user);
        }
    }
    private UiSession Issue(Guid company, string sid, User user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        lock (_gate)
        {
            foreach (var token in _sessions.Where(p => p.Value.Session.ExpiresAt <= clock.GetUtcNow()).Select(p => p.Key).ToArray()) _sessions.Remove(token);
            if (_sessions.Count >= 128) throw new InvalidOperationException("Túl sok aktív bejelentkezés.");
            var session = new UiSession(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), user.UserName, user.Role, clock.GetUtcNow().AddMinutes(30));
            _sessions.Add(session.Token, new(company, sid, session)); return session;
        }
    }
    private string Admin(Guid company, string? token, string sid)
    {
        lock (_gate)
        {
            if (token is null || !_sessions.TryGetValue(token, out var bound) || bound.Company != company || bound.Sid != sid
                || bound.Session.Role != "admin" || bound.Session.ExpiresAt <= clock.GetUtcNow())
                throw new UnauthorizedAccessException("A művelethez érvényes adminisztrátori bejelentkezés szükséges.");
            return bound.Session.UserName;
        }
    }
    public void CreateUser(UiCreateUser input, string sid)
    {
        var actor = Admin(input.CompanyId, input.Token, sid); var name = Name(input.UserName);
        if (input.Role is not ("admin" or "user")) throw new ArgumentException("Ismeretlen szerepkör.");
        var user = NewUser(name, input.Password, input.Role);
        credentials.Execute(input.CompanyId, tx =>
        {
            if (tx.Get("accounts", "user/" + name) is not null) throw new ArgumentException("A felhasználó már létezik.");
            if (tx.List("accounts", "user/").Count >= 100) throw new ArgumentException("Legfeljebb 100 felhasználó hozható létre.");
            tx.Write("accounts", "user/" + name, user);
            tx.Write("accounts", "audit/" + Guid.NewGuid().ToString("N"), new { actor, name, input.Role, at = clock.GetUtcNow() }); return 0;
        });
    }
    public void Logout(Guid company, string token, string sid)
    {
        lock (_gate) if (_sessions.TryGetValue(token, out var session) && session.Company == company && session.Sid == sid) _sessions.Remove(token);
    }
    public static UiFieldLayout[] Defaults(EntityPageDescriptor page) => page.Fields.Select(f => new UiFieldLayout(f.Id, f.Label, f.Id == "name" || f.Kind == "multiline" ? 2 : 1)).ToArray();
    private static UiFieldLayout[] Validate(EntityPageDescriptor page, UiFieldLayout[]? fields)
    {
        if (fields is null || fields.Length != page.Fields.Length || fields.Any(f => f is null)
            || fields.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new ArgumentException("Az elrendezésnek minden mezőt pontosan egyszer kell tartalmaznia.");
        foreach (var field in fields)
        {
            var original = page.Fields.SingleOrDefault(f => f.Id == field.Id) ?? throw new ArgumentException("Ismeretlen mező.");
            if (field.Label is null || field.Label.Trim().Length is < 1 or > 80 || field.Label.Any(char.IsControl) || field.Span is < 1 or > 2 || original.Required && !field.Visible)
                throw new ArgumentException("Érvénytelen felirat/szélesség, vagy elrejtett kötelező mező.");
        }
        if (!fields.Any(f => f.Visible)) throw new ArgumentException("Legalább egy látható mező szükséges.");
        return fields.Select(f => f with { Label = f.Label.Trim() }).ToArray();
    }
    private static string Key(EntityPageDescriptor page) => page.ModuleId + "." + page.Id;
    public UiLayoutState Read(Guid company, EntityPageDescriptor page) => companies.Execute(company, tx =>
    {
        var state = tx.Read<UiLayoutState>("ui-layout", Key(page));
        if (state is null) return new(0, Defaults(page), []);
        if (state.SchemaVersion != 1) return new(state.Revision, Defaults(page), []) { SchemaVersion = state.SchemaVersion };
        try { return state with { Fields = Validate(page, state.Fields) }; }
        catch (ArgumentException) { return new(state.Revision, Defaults(page), state.History ?? []); }
    });
    public UiLayoutState Save(UiLayoutSave input, EntityPageDescriptor page, string sid)
    {
        var actor = Admin(input.CompanyId, input.Token, sid);
        if (input.PageKey != Key(page)) throw new ArgumentException("Eltérő adatlapazonosító.");
        var fields = Validate(page, input.Fields);
        return companies.Execute(input.CompanyId, tx =>
        {
            var before = tx.Read<UiLayoutState>("ui-layout", Key(page)) ?? new(0, Defaults(page), []);
            if (before.SchemaVersion != 1) throw new InvalidOperationException("Az elrendezés újabb programmal készült; nem írható felül.");
            if (input.Revision != before.Revision) throw new InvalidOperationException("Az elrendezés közben megváltozott. Nyisd meg újra a szerkesztőt.");
            var history = (before.History ?? []).Append(new(before.Revision, before.Fields, actor, clock.GetUtcNow())).TakeLast(10).ToArray();
            var after = new UiLayoutState(checked(before.Revision + 1), fields, history);
            tx.Write("ui-layout", Key(page), after);
            tx.Write("ui-layout", "audit/" + Guid.NewGuid().ToString("N"), new { actor, page = Key(page), revision = after.Revision, at = clock.GetUtcNow(), before = before.Fields, after = fields });
            return after;
        });
    }
    public UiLayoutState Restore(UiLayoutRestore input, EntityPageDescriptor page, string sid)
    {
        Admin(input.CompanyId, input.Token, sid);
        var state = Read(input.CompanyId, page);
        var fields = input.TargetRevision == -1 ? Defaults(page) : state.History.SingleOrDefault(h => h.Revision == input.TargetRevision)?.Fields
            ?? throw new ArgumentException("A korábbi elrendezés már nem található.");
        return Save(new(input.CompanyId, input.PageKey, input.Token, input.Revision, fields), page, sid);
    }
}
