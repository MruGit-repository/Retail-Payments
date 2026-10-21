namespace RetailPayments.Api.Domain;

// source record that failed validation; kept here instead of in Transactions
public class RejectedRecord
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public ImportBatch ImportBatch { get; set; } = null!;

    public string SourceSystem { get; set; } = "";
    public int TransactionId { get; set; }
    public string Reason { get; set; } = "";
    // the record as received, as JSON
    public string Payload { get; set; } = "";
    public DateTime RejectedAt { get; set; }
}
