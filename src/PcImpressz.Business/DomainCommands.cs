using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Business;
public static class DomainCommands
{
    public static void Register(IModuleContext context,string name,string permission,CommandAccess access,Func<ICompanyTransaction,JsonElement,string,JsonElement> execute) =>
        context.RegisterCommand(new Handler(context,name,permission,access,execute));
    public static JsonElement Json<T>(T data)=>JsonSerializer.SerializeToElement(data,CompanyDocuments.Json);
    public static T Read<T>(JsonElement input)=>input.Deserialize<T>(CompanyDocuments.Json) ?? throw new ArgumentException("Hiányzó kérésadat.");
    public static string Search(JsonElement input,string name="search")
    {
        if(!input.TryGetProperty(name,out var value))return "";
        if(value.ValueKind!=JsonValueKind.String || (value.GetString()?.Length??0)>200)throw new ArgumentException("A keresés legfeljebb 200 karakter lehet.");
        return value.GetString()?.Trim()??"";
    }
    public static bool Matches(string text,string search)=>text.Contains(search,StringComparison.CurrentCultureIgnoreCase);
    public static string Csv(IEnumerable<string[]> rows) => string.Join("\r\n",rows.Select(row=>string.Join(';',row.Select(Cell))));
    private static string Cell(string text)
    {
        if(text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t')||text.StartsWith('\r'))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    private sealed class Handler(IModuleContext context,string name,string permission,CommandAccess access,
        Func<ICompanyTransaction,JsonElement,string,JsonElement> execute):IContextualCommandHandler
    {
        public CommandDescriptor Descriptor {get;}=new(name,permission,access);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement input,CommandCallContext caller,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var company=input.GetProperty("companyId").GetGuid();
            var database=context.Database ?? throw new InvalidOperationException("A tranzakciós céges adattár nem elérhető.");
            return ValueTask.FromResult(access==CommandAccess.RecoveryReadOnly ? database.Read(company,tx=>execute(tx,input,caller.ActorId)) : database.Execute(company,tx=>execute(tx,input,caller.ActorId)));
        }
    }
}
