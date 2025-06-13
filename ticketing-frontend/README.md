# ticketing-frontend

Angular 19 app for buyers, organizers and door staff. Standalone components, signals and the new
control flow, Angular Material for the UI. It only ever talks to `ticketing-bff` on its own origin
(`/bff`, `/api`, `/hubs`), so there is no token handling in the browser at all.

| Route | Who | What |
|---|---|---|
| `/` | anyone | search events by text, city, category and dates |
| `/events/:id` | anyone (hold needs sign-in) | live seat map via SignalR, pick up to 8 seats, hold for 10 minutes |
| `/checkout/:holdId` | signed in | countdown, locked prices and fees, pay with the mock provider or Stripe test mode |
| `/tickets`, `/tickets/:id` | signed in | tickets grouped by event, QR code detail |
| `/organizer`, `/organizer/new` | Organizer | own events, create with tiers and sections, publish, cancel |
| `/scan` | Staff, Organizer | check in by typed code or camera (`@zxing/browser`) |

```bash
npm ci
npm start                                                   # http://localhost:4200, proxies to the BFF on :5000
npx ng test --watch=false --browsers=ChromeHeadless
npx ng build
```

The production image builds with Node and serves `dist/` from unprivileged nginx, which also proxies
the BFF paths (`BFF_UPSTREAM`).
