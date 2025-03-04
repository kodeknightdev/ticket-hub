namespace TicketHub.Contracts;

public static class Topics
{
    public const string SeatHeld = "seating.seat-held";
    public const string SeatReleased = "seating.seat-released";
    public const string HoldExpired = "seating.hold-expired";
    public const string HoldExpiring = "seating.hold-expiring";
    public const string SeatsSold = "seating.seats-sold";

    public const string OrderCreated = "orders.order-created";
    public const string OrderPaid = "orders.order-paid";
    public const string OrderExpired = "orders.order-expired";
    public const string OrderRefunded = "orders.order-refunded";
    public const string TicketsIssued = "orders.tickets-issued";
    public const string TicketCheckedIn = "orders.ticket-checked-in";

    public const string EventPublished = "events.event-published";
    public const string EventUpdated = "events.event-updated";
    public const string EventCancelled = "events.event-cancelled";

    public const string PriceUpdated = "pricing.price-updated";

    public static string RetryFor(string consumerGroup) => $"{consumerGroup}.retry";
    public static string DeadLetterFor(string consumerGroup) => $"{consumerGroup}.dlq";

    public static readonly string[] All =
    [
        SeatHeld, SeatReleased, HoldExpired, HoldExpiring, SeatsSold,
        OrderCreated, OrderPaid, OrderExpired, OrderRefunded, TicketsIssued, TicketCheckedIn,
        EventPublished, EventUpdated, EventCancelled, PriceUpdated
    ];
}
