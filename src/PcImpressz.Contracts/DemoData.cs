namespace PcImpressz.Contracts;
public static class DemoDataset
{
    public const string Marker="dataset-v2";
    public const string LegacyMarker="dataset-v1";
}
public sealed record CompanyDemoSeedRequest(Guid CompanyId);
public sealed record DemoSeedReport(int Partners,int Products,int Receipts,int Sales,int Movements,bool AlreadySeeded=false);