# load-sim

Simulates an on-sale rush: registers N throwaway buyers, then has them all try to hold overlapping
seats for one event at the same time. Winners create an order and pay with the mock provider.

At the end it checks two things:

- no seat was won by more than one hold (Redis Lua holds are atomic)
- every paid seat is marked sold on the seat map (Kafka `order-paid` reached seating)

```bash
dotnet run --project tools/load-sim/TicketHub.LoadSim -- --buyers=200 --seats=2 --concurrency=80
```

Options (flags or env vars): `--identity`, `--events`, `--seating`, `--orders` (service base URLs),
`--event` (defaults to the first published event), `--buyers`, `--seats`, `--concurrency`,
`--pay` (share of winners that pay), `--settle` (seconds to wait before checking the seat map).

It talks to the services directly instead of going through `ticketing-bff`, because the BFF's
per-user hold rate limit would otherwise be the thing under test. Exit code is 1 if a double sale is detected.
