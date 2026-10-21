namespace RetailPayments.Api.Sources;

// a transaction as the source system sends it
public record SourceTransaction(
    string SourceSystem,
    int TransactionId,
    string? CardNumber,
    string? LocationCode,
    string? ProductName,
    decimal Amount,
    DateTime TransactionTime);
