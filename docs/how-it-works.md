# How it works

This walks through the main journeys end to end. Each has a sequence diagram of the happy path and
at least one failure or compensation path. State diagrams for the main entities are at the end.

## 1. Signing in (both apps)

The browser never holds a JWT. It talks to its own BFF, which logs in against identity, keeps the
tokens in Redis and gives the browser an opaque, `HttpOnly` session cookie.

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant BFF as ticketing-bff
    participant ID as identity
    participant R as Redis

    B->>BFF: GET /bff/user
    BFF-->>B: { isAuthenticated: false } + XSRF-TOKEN-APP cookie
    B->>BFF: POST /bff/login (X-CSRF: 1, X-XSRF-TOKEN)
    BFF->>ID: POST /auth/login
    ID-->>BFF: access token (15 min) + refresh token (7 days)
    BFF->>R: SET bff:app:session:{sid} = tokens
    BFF-->>B: Set-Cookie __Host-tickethub-app (HttpOnly, Secure, SameSite=Strict) + user info
    Note over B,BFF: later calls carry only the cookie
    B->>BFF: GET /bff/me/tickets
    BFF->>R: GET session tokens
    alt access token about to expire
        BFF->>ID: POST /auth/refresh (rotates refresh token)
        BFF->>R: store new tokens
    end
    BFF->>BFF: attach Authorization: Bearer … to downstream calls
```

**Failure path:** if the refresh token was already rotated (someone replayed an old one), identity
revokes the whole token family and returns 401; the BFF drops the session and the next request gets 401.
A `Buyer` who logs in to the dashboard BFF gets 403 at login and no cookie is set.

## 2. Browsing and the live seat map

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant BFF as ticketing-bff
    participant EV as events
    participant SE as seating
    participant PR as pricing

    B->>BFF: GET /bff/events/{id}/page
    par fan-out
        BFF->>EV: GET /events/{id} and /events/{id}/layout
        BFF->>SE: gRPC GetAvailability(include_seats)
        BFF->>PR: GET /prices/{id}
    end
    BFF-->>B: event + sections/rows/seats with state + current tier prices
    B->>BFF: WebSocket /hubs/seating (proxied by YARP)
    BFF->>SE: WebSocket
    B->>SE: JoinEvent(eventId)
    Note over SE: Kafka seat-held / released / expired / seats-sold → hub group event:{id}
    SE-->>B: SeatsChanged { seatIds, state }
```

**Failure path:** if seating doesn't answer, the page still renders with `partial: true`; seat states
show as `unknown` and a banner warns that availability may be stale. If events is down the page fails
with 502, since there is nothing useful to show without the seat map.

## 3. Hold → pay → tickets (choreographed saga)

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant BFF as ticketing-bff
    participant SE as seating
    participant PR as pricing
    participant OR as orders
    participant K as Kafka
    participant NO as notifier

    B->>BFF: POST /api/seating/holds {eventId, seatIds}
    BFF->>SE: (rate limited per user) proxy
    SE->>SE: Lua: all seats free? SET seat keys PX 600000
    SE-->>B: 201 hold (expires in 10 min)
    SE-)K: seating.seat-held
    K-)PR: lock price for each seat at heldAt
    B->>BFF: GET /bff/checkout/{holdId}
    BFF-->>B: seats, locked prices, fees, countdown
    B->>BFF: POST /api/orders {holdId} + Idempotency-Key
    BFF->>OR: proxy with bearer token
    OR->>SE: gRPC GetHold
    OR->>PR: POST /prices/locks
    OR->>OR: create Pending order + PaymentIntent (Stripe test / mock)
    OR-)K: orders.order-created (outbox)
    B->>OR: pay (Stripe.js test card, or mock-pay)
    Note over OR: webhook verified by signature, deduplicated by provider event id
    OR->>SE: gRPC ConfirmHold (pins the seats)
    OR-)K: orders.order-paid (outbox)
    K-)SE: insert sold_seats (PK event+seat), mark sold in Redis
    SE-)K: seating.seats-sold
    K-)OR: issue one signed QR ticket per seat
    OR-)K: orders.tickets-issued
    K-)NO: confirmation email with inline QR codes (MailHog)
    SE-->>B: SeatsChanged(sold) to every viewer
```

### Compensations

```mermaid
sequenceDiagram
    autonumber
    participant SE as seating
    participant OR as orders
    participant PSP as Payment provider
    participant K as Kafka

    rect rgb(255, 243, 224)
    Note over SE,OR: Hold expires before payment
    SE->>SE: expiry worker claims hold (ZREM), releases seat keys
    SE-)K: seating.hold-expired
    K-)OR: Pending order → Expired
    OR-)K: orders.order-expired
    end

    rect rgb(255, 235, 238)
    Note over OR,PSP: Payment fails
    PSP-)OR: payment failed webhook
    OR->>SE: gRPC ReleaseHold(payment-failed)
    SE-)K: seating.seat-released
    OR-)K: orders.order-expired (Failed)
    end

    rect rgb(232, 234, 246)
    Note over OR,PSP: Payment succeeds after the hold already expired
    PSP-)OR: payment succeeded webhook
    OR->>SE: gRPC ConfirmHold → false (hold-expired)
    OR->>PSP: refund full amount (idempotency key late-{orderId})
    OR-)K: orders.order-refunded (reason hold-expired)
    end
```

## 4. Door check-in

```mermaid
sequenceDiagram
    autonumber
    participant S as Staff scanner
    participant BFF as ticketing-bff
    participant OR as orders
    participant K as Kafka

    S->>BFF: POST /api/checkin {code}
    BFF->>OR: proxy (Staff/Organizer token)
    OR->>OR: verify HMAC signature in the code
    OR->>OR: UPDATE tickets SET CheckedIn WHERE id = @id AND status = Valid
    alt 1 row updated
        OR-)K: orders.ticket-checked-in (same transaction, outbox)
        OR-->>S: 200 admitted
    else 0 rows
        OR-->>S: 409 already-checked-in (with time) or voided
    end
```

Forged or mistyped codes fail signature verification and never touch the database.

## 5. Event cancellation (orchestrated saga)

```mermaid
sequenceDiagram
    autonumber
    participant ORG as Organizer
    participant EV as events
    participant K as Kafka
    participant OR as orders (orchestrator)
    participant PSP as Payment provider
    participant NO as notifier

    ORG->>EV: POST /events/{id}/cancel
    EV-)K: events.event-cancelled (outbox)
    K-)OR: create CancellationSaga + one step per paid order, Pending orders → Expired
    loop every 2 s until all steps are terminal
        OR->>PSP: refund (idempotency key cancel-{orderId})
        OR->>OR: step → Refunded (persisted)
        OR->>OR: void tickets, step → TicketsVoided (persisted)
        OR-)K: orders.order-refunded (reason event-cancelled)
        OR->>OR: step → Completed
    end
    K-)NO: "Event cancelled, you've been refunded" emails
```

**Failure path:** if the provider refuses a refund, the step records the error and stays at
`Pending`; the next sweep retries it. After five attempts the step is `Failed` and the saga ends as
`CompletedWithErrors`, visible on the dashboard's alerts page. Because every stage is written before
the next starts, a crash mid-saga resumes where it left off, and the provider-side idempotency key
means a retried refund can't pay out twice.

## 6. Dashboard request

```mermaid
sequenceDiagram
    autonumber
    participant D as Dashboard
    participant BFF as dashboard-bff
    participant C as HybridCache (memory + Redis)
    participant AN as analytics
    participant EV as events

    D->>BFF: GET /bff/overview?from&to&city
    BFF->>BFF: resolve scope (Owner = all, Organizer = own event ids)
    BFF->>C: get dash:overview:{scope}:{query}
    alt cache hit
        C-->>BFF: JSON
        BFF-->>D: 200 (X-Cache: HIT)
    else miss
        par
            BFF->>AN: GET /analytics/kpis?…&eventIds=…
            BFF->>EV: GET /events/stats?ids=…
        end
        BFF->>C: store for 60 s (skipped when partial)
        BFF-->>D: 200 (X-Cache: MISS)
    end
```

## State diagrams

### Order

```mermaid
stateDiagram-v2
    [*] --> Pending: POST /orders
    Pending --> Paid: payment succeeded + hold confirmed
    Pending --> Refunded: payment succeeded after hold expired
    Pending --> Failed: payment failed
    Pending --> Expired: hold expired / event cancelled
    Paid --> TicketsIssued: seats-sold received
    Paid --> Refunded: refund / cancellation saga
    TicketsIssued --> Refunded: refund / cancellation saga
    Refunded --> [*]
    Failed --> [*]
    Expired --> [*]
```

### Seat (as seen by seating)

```mermaid
stateDiagram-v2
    [*] --> Available
    Available --> Held: hold placed (Lua, TTL 10 min)
    Held --> Available: released / TTL expired
    Held --> Confirmed: payment succeeded (TTL removed)
    Confirmed --> Sold: order-paid consumed
    Sold --> [*]
```

### Event

```mermaid
stateDiagram-v2
    [*] --> Draft: organizer creates
    Draft --> Published: publish
    Draft --> Cancelled: cancel (no saga needed)
    Published --> Cancelled: cancel → refund saga
    Published --> [*]: event date passes
```

### Ticket

```mermaid
stateDiagram-v2
    [*] --> Valid: issued with signed QR
    Valid --> CheckedIn: first scan
    Valid --> Void: refund / cancellation
    CheckedIn --> [*]
    Void --> [*]
```

### Cancellation saga step

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Refunded: provider refund ok
    Pending --> Pending: refund failed (attempt < 5)
    Pending --> Failed: 5 failed attempts
    Refunded --> TicketsVoided
    TicketsVoided --> Completed: order-refunded published
```
