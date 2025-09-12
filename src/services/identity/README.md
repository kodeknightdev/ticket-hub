# identity

Users, roles and tokens. Issues short-lived HS256 JWT access tokens (15 min) and rotating refresh
tokens (7 days, stored hashed). Replaying an already-rotated refresh token revokes the whole family.

Roles: `Buyer`, `Organizer`, `Staff`, `Owner`, `Admin`. Claims: `sub`, `email`, `name`, `role`.

## Endpoints

| Method | Path | |
|---|---|---|
| POST | `/auth/login` | email + password → tokens + user |
| POST | `/auth/refresh` | rotate |
| POST | `/auth/revoke` | |
| POST | `/auth/register` | new Buyer |
| GET | `/users/me` | |
| GET | `/users`, `/users/{id}` | Owner/Admin |

## Events

Publishes and consumes nothing; other services trust the JWT it signs.

## Running alone

```bash
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Identity;User Id=identity_svc;Password=...;TrustServerCertificate=True"
dotnet run --project src/services/identity/TicketHub.Identity   # http://localhost:5201/scalar
```

In Development it migrates its database and seeds the demo accounts using `SEED_DEMO_PASSWORD`.
