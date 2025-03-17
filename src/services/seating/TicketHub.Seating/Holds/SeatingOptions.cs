namespace TicketHub.Seating.Holds;

public sealed class SeatingOptions
{
    public const string Section = "Seating";

    public int HoldSeconds { get; set; } = 600;
    public int MaxSeatsPerHold { get; set; } = 8;
    public int WarnSecondsBeforeExpiry { get; set; } = 120;
}
