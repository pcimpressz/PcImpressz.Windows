using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed partial class ShellViewModel
{
    private string _navTax = "", _navUser = "", _navMessage = "A kapcsolat a kiválasztott céghez tartozik.";
    private bool _navTest, _navConfigured, _navBusy;
    private int _navGeneration;

    public string NavTaxNumber { get => _navTax; set { _navTax = value; Changed(); } }
    public string NavUserName { get => _navUser; set { _navUser = value; Changed(); } }
    public bool NavTestMode { get => _navTest; set { _navTest = value; Changed(); } }
    public string NavMessage { get => _navMessage; private set { _navMessage = value; Changed(); } }
    public string NavCompanyLabel => SelectedCompany is null ? "NAV Online Számla" : "NAV Online Számla · " + SelectedCompany.Name;
    public bool NavConfigured { get => _navConfigured; private set { _navConfigured = value; Changed(); Changed(nameof(NavSecretHint)); } }
    public string NavSecretHint => NavConfigured
        ? "A jelszó és a kulcsok meg vannak mentve. Üresen hagyva nem cserélődnek."
        : "Első mentéskor a jelszó, az aláírókulcs és a cserekulcs is kell.";
    public int NavGeneration => _navGeneration;
    public bool NavBusy { get => _navBusy; private set { _navBusy = value; Changed(); Changed(nameof(NavIdle)); } }
    public bool NavIdle => !NavBusy;

    public async Task LoadNavAsync()
    {
        Changed(nameof(NavCompanyLabel));
        if (SelectedCompany is not { } company) { ClearNav("Először válassz céget."); return; }
        if (NavBusy) return;
        NavBusy = true;
        try
        {
            var status = await _connection.SendAsync("module.execute", new ModuleCommandRequest(company.Id, "receiving", "nav-status",
                JsonSerializer.SerializeToElement(new { page = 0, pageSize = 1 })), _shutdown.Token);
            ShowNav(status);
            NavMessage = NavConfigured ? "A mentett kapcsolat titkosítva van, csak ehhez a Windows-felhasználóhoz." : NavSecretHint;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ClearNav(ex.Message); }
        finally { NavBusy = false; }
    }

    public async Task SaveNavAsync(string password, string signKey, string exchangeKey)
    {
        if (SelectedCompany is not { } company) { NavMessage = "Először válassz céget."; return; }
        if (NavBusy) return;
        NavBusy = true;
        try
        {
            var status = await _connection.SendAsync("module.execute", new ModuleCommandRequest(company.Id, "receiving", "nav-save",
                JsonSerializer.SerializeToElement(new
                {
                    taxNumber = NavTaxNumber, userName = NavUserName, password, signKey, exchangeKey, testMode = NavTestMode
                })), _shutdown.Token);
            ShowNav(status);
            NavMessage = "A NAV-kapcsolat titkosítva mentve ehhez a céghez. A jelszó és a kulcsok nem jelennek meg újra.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { NavMessage = ex.Message; }
        finally { NavBusy = false; }
    }

    private void ShowNav(JsonElement status)
    {
        _navTax = status.GetProperty("taxNumber").GetString() ?? "";
        _navUser = status.GetProperty("userName").GetString() ?? "";
        _navTest = status.GetProperty("testMode").GetBoolean();
        NavConfigured = status.GetProperty("configured").GetBoolean();
        _navGeneration++;
        Changed(nameof(NavTaxNumber)); Changed(nameof(NavUserName)); Changed(nameof(NavTestMode)); Changed(nameof(NavGeneration));
    }

    private void ClearNav(string message)
    {
        _navTax = ""; _navUser = ""; _navTest = false; NavConfigured = false; _navGeneration++;
        Changed(nameof(NavTaxNumber)); Changed(nameof(NavUserName)); Changed(nameof(NavTestMode)); Changed(nameof(NavGeneration));
        NavMessage = message;
    }
}
