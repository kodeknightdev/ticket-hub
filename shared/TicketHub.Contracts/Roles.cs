namespace TicketHub.Contracts;

public static class Roles
{
    public const string Buyer = "Buyer";
    public const string Organizer = "Organizer";
    public const string Staff = "Staff";
    public const string Owner = "Owner";
    public const string Admin = "Admin";

    public static readonly string[] All = [Buyer, Organizer, Staff, Owner, Admin];
}
