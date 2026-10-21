using PcImpressz.Core.Security;

namespace PcImpressz.Core.Licensing;

public sealed record LicenseTokens(string Entitlement, string Lease);

public sealed class LicenseEvaluator : ILicenseEvaluator
{
    private readonly object _gate = new();
    private readonly SignedDocumentVerifier _verifier;
    private readonly LicensePolicy _policy;
    private readonly TimeProvider _time;
    private readonly ILicenseCheckpointStore _store;
    private readonly Func<LicenseTokens?> _tokens;

    public LicenseEvaluator(SignedDocumentVerifier verifier, LicensePolicy policy,
        TimeProvider time, ILicenseCheckpointStore store, Func<LicenseTokens?> tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.Issuer);
        Identifiers.Require(policy.ProductId, "product ID");
        Identifiers.Require(policy.InstallationId, "installation ID");
        if (policy.MaximumLeaseDuration <= TimeSpan.Zero || policy.MaximumLeaseDuration > TimeSpan.FromDays(7)
            || policy.FutureIssueTolerance < TimeSpan.Zero || policy.ClockRollbackTolerance < TimeSpan.Zero)
            throw new ArgumentException("Invalid license timing policy.");
        (_verifier, _policy, _time, _store, _tokens) = (verifier, policy, time, store, tokens);
    }

    public LicenseDecision Inspect() => EvaluateSafely(null, null);

    public LicenseDecision EvaluateModule(string moduleId) => EvaluateSafely(moduleId, null);

    public LicenseDecision Evaluate(string moduleId, string capability) => EvaluateSafely(moduleId, capability);

    private LicenseDecision EvaluateSafely(string? moduleId, string? capability)
    {
        lock (_gate)
        {
            try { return EvaluateCore(moduleId, capability); }
            catch (DocumentValidationException)
            { return Deny(LicenseStatus.Invalid, "A licenc vagy a működési engedély érvénytelen."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException)
            { return Deny(LicenseStatus.StateUnavailable, "A licencállapot nem olvasható vagy nem menthető."); }
        }
    }

    private LicenseDecision EvaluateCore(string? moduleId, string? capability)
    {
        if (moduleId is not null && (!Identifiers.IsValid(moduleId)
            || capability is not null && !Identifiers.IsValid(capability)))
            return Deny(LicenseStatus.MissingCapability, "Ismeretlen képesség.");
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var checkpoint = _store.Read();
        if (now + _policy.ClockRollbackTolerance.TotalSeconds < checkpoint.LastObservedUnixTime)
            return Deny(LicenseStatus.RollbackDetected, "Az óra visszaállítása észlelhető; időellenőrzés szükséges.");
        if (now > checkpoint.LastObservedUnixTime)
        {
            checkpoint = checkpoint with { LastObservedUnixTime = now };
            _store.Write(checkpoint);
        }
        var pair = _tokens();
        if (pair is null)
            return Deny(LicenseStatus.Missing, "Nincs telepített licenc és működési engedély.");
        var license = _verifier.Verify<EntitlementDocument>(pair.Entitlement, LicenseDocumentTypes.Entitlement);
        var lease = _verifier.Verify<LeaseDocument>(pair.Lease, LicenseDocumentTypes.Lease);
        Validate(license, lease);
        if (license.Iss != _policy.Issuer || lease.Iss != _policy.Issuer
            || license.Aud != _policy.ProductId || lease.Aud != _policy.ProductId
            || string.IsNullOrWhiteSpace(_policy.OrganizationId)
            || license.OrganizationId != _policy.OrganizationId || lease.OrganizationId != _policy.OrganizationId)
            return Deny(LicenseStatus.Invalid, "A licenc kibocsátója, terméke vagy szervezete eltér.");
        if (lease.InstallationId != _policy.InstallationId)
            return Deny(LicenseStatus.WrongInstallation, "A működési engedély más telepítéshez tartozik.");
        if (license.LicenseId != lease.LicenseId || license.Revision != lease.Revision
            || lease.Iat < license.Iat || lease.Exp > license.Exp
            || lease.Exp - lease.Iat > (IsDevelopmentMaster(license, lease)
                ? TimeSpan.FromDays(36500).TotalSeconds : _policy.MaximumLeaseDuration.TotalSeconds))
            return Deny(LicenseStatus.Invalid, "A licenc és a működési engedély nem illeszkedik.");
        if (license.Iat > now + _policy.FutureIssueTolerance.TotalSeconds
            || lease.Iat > now + _policy.FutureIssueTolerance.TotalSeconds
            || license.Nbf > now || lease.Nbf > now)
            return Deny(LicenseStatus.NotYetValid, "Az engedély érvényessége még nem kezdődött el.");
        if (license.Exp <= now || lease.Exp <= now)
            return Deny(LicenseStatus.Expired, "A licenc vagy a működési engedély lejárt.");
        var previousRevision = checkpoint.HighestRevisions.GetValueOrDefault(license.LicenseId);
        if (license.Revision < previousRevision)
            return Deny(LicenseStatus.RollbackDetected, "Régebbi jogosultságverzió nem használható.");
        if (license.Revision > previousRevision)
        {
            checkpoint.HighestRevisions[license.LicenseId] = license.Revision;
            _store.Write(checkpoint);
        }
        if (moduleId is null)
            return new(LicenseStatus.Active, "Érvényes licenc és működési engedély; a moduljogok külön ellenőrződnek.",
                DateTimeOffset.FromUnixTimeSeconds(Math.Min(license.Exp, lease.Exp)));
        var grant = license.Modules.SingleOrDefault(m => m.ModuleId == moduleId);
        if (grant is null || capability is not null && !grant.Capabilities.Contains(capability, StringComparer.Ordinal))
            return Deny(LicenseStatus.MissingCapability, "Ez a modul vagy képesség nincs a licencben.");
        var until = Math.Min(grant.ExpiresAt, Math.Min(license.Exp, lease.Exp));
        if (until <= now)
            return Deny(LicenseStatus.Expired, "A modul előfizetése lejárt.");
        return new(LicenseStatus.Active, "Érvényes jogosultság.", DateTimeOffset.FromUnixTimeSeconds(until));
    }

    // A long-lived lease is permitted only for the separately signed development issuer.
    // Production issuers retain the strict seven-day maximum. No unsigned bypass exists.
    private bool IsDevelopmentMaster(EntitlementDocument license, LeaseDocument lease) =>
        _policy.Issuer == "pcimpressz-development"
        && _policy.OrganizationId == "development"
        && license.Iss == "pcimpressz-development"
        && lease.Iss == "pcimpressz-development"
        && license.OrganizationId == "development"
        && lease.OrganizationId == "development"
        && license.LicenseId.StartsWith("development-", StringComparison.Ordinal)
        && lease.LicenseId == license.LicenseId;

    private static LicenseDecision Deny(LicenseStatus status, string reason) => new(status, reason);

    private static void Validate(EntitlementDocument license, LeaseDocument lease)
    {
        static bool Window(long issued, long before, long expires) => issued >= 0 && before >= 0
            && expires <= 253402300799 && expires > issued && expires > before;
        if (license.SchemaVersion != 1 || lease.SchemaVersion != 1
            || !Identifiers.IsValid(license.LicenseId) || !Identifiers.IsValid(lease.LicenseId)
            || !Identifiers.IsValid(license.OrganizationId) || !Identifiers.IsValid(lease.OrganizationId)
            || !Identifiers.IsValid(lease.InstallationId)
            || license.Revision < 1 || lease.Revision < 1
            || !Window(license.Iat, license.Nbf, license.Exp) || !Window(lease.Iat, lease.Nbf, lease.Exp)
            || license.Modules is null || license.Modules.Length > 256)
            throw new DocumentValidationException("Invalid license fields.");
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in license.Modules)
        {
            if (grant is null || !Identifiers.IsValid(grant.ModuleId) || !moduleIds.Add(grant.ModuleId)
                || grant.ExpiresAt < 0 || grant.ExpiresAt > license.Exp || grant.Capabilities is null
                || grant.Capabilities.Length > 256 || grant.Capabilities.Any(c => !Identifiers.IsValid(c))
                || grant.Capabilities.Distinct(StringComparer.Ordinal).Count() != grant.Capabilities.Length)
                throw new DocumentValidationException("Invalid module grant.");
        }
    }
}
