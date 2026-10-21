namespace RetailPayments.Api.Domain;

// Column sizes, shared by the database mapping and the import validation so they can't drift apart.
public static class FieldLimits
{
    // spec says strings are < 20 characters
    public const int Text = 19;

    // the spec's own sample product "USB-C Docking Station" is 21 characters, so this one is wider
    public const int ProductName = 50;
}
