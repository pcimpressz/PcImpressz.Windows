using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
namespace PcImpressz.Maintenance;
public static class DemoDataSeeder
{
    public const string Marker=DemoDataset.Marker;
    public static void RequireDemo(CompanyStore companies,Guid company)
    {
        if(companies.List().SingleOrDefault(c=>c.Id==company) is not {IsDemo:true})throw new ArgumentException("Mintaadat csak kifejezetten demóként jelölt cégbe tölthető.");
    }
    public static DemoSeedReport Seed(CompanyStore companies,ICompanyDatabase database,Guid company,TimeProvider time,string actor)
    {
        RequireDemo(companies,company);
        return PcImpressz.Modules.Receiving.DemoDataSeeder.Seed(id=>companies.ModuleDataDirectory(id,"receiving"),database,company,time,actor);
    }
}