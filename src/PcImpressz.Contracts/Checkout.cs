namespace PcImpressz.Contracts;
public sealed record BillingCustomer(Guid Id,string Name,string TaxId,string Country,string PostalCode,string City,string Address,bool IsActive=true,bool IsCustomer=true);
public sealed record CheckoutCustomerSave(Guid CompanyId,string Name,string TaxId,string Country,string PostalCode,string City,string Address);
public sealed record TaxpayerLookup(Guid CompanyId,string TaxNumber);

public sealed class RecordNotFoundException(string message):ArgumentException(message);

public static class PaymentRounding
{
    public static decimal Payable(decimal gross,string paymentMethod)=>paymentMethod=="cash"?decimal.Round(gross/5m,0,MidpointRounding.AwayFromZero)*5m:gross;
}