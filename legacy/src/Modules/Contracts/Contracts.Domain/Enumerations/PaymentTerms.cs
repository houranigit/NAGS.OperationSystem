namespace Contracts.Domain.Enumerations;

public enum PaymentTerms
{
    DueOnReceipt = 0,
    Net15 = 15,
    Net30 = 30,
    Net45 = 45,
    Net60 = 60,
    Net90 = 90,
    Prepaid = 100
}
