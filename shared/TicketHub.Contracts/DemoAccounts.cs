namespace TicketHub.Contracts;

/// <summary>Fixed ids for the Development seeders so services and tools agree on who owns what.</summary>
public static class DemoAccounts
{
    public static readonly Guid Buyer = Guid.Parse("11111111-1111-4111-8111-000000000001");
    public static readonly Guid Organizer = Guid.Parse("11111111-1111-4111-8111-000000000002");
    public static readonly Guid Staff = Guid.Parse("11111111-1111-4111-8111-000000000003");
    public static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-000000000004");
    public static readonly Guid Admin = Guid.Parse("11111111-1111-4111-8111-000000000005");
    public static readonly Guid SecondOrganizer = Guid.Parse("11111111-1111-4111-8111-000000000006");
    public static readonly Guid SecondBuyer = Guid.Parse("11111111-1111-4111-8111-000000000007");
}
