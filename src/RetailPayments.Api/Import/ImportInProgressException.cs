namespace RetailPayments.Api.Import;

// thrown if an import is started while another is running
public class ImportInProgressException() : InvalidOperationException("An import is already running.");
