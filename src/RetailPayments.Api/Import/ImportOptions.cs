using System.ComponentModel.DataAnnotations;

namespace RetailPayments.Api.Import;

public class ImportOptions
{
    public const string Section = "Import";

    // hours after TransactionTime until a transaction is finalized; before that it can be revoked or corrected
    [Range(1, 720)]
    public int FinalizeAfterHours { get; set; } = 24;

    // how long we wait for one source before treating it as failed
    [Range(1, 600)]
    public int SourceTimeoutSeconds { get; set; } = 30;
}
