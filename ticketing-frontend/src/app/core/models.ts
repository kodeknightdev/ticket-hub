export type Role = 'Buyer' | 'Organizer' | 'Staff' | 'Owner' | 'Admin';

export interface User {
  id: string;
  email: string;
  name: string;
  role: Role;
}

export interface SessionInfo {
  isAuthenticated: boolean;
  user: User | null;
}

export interface EventSummary {
  id: string;
  organizerId: string;
  title: string;
  artist: string;
  category: string;
  city: string;
  venueName: string;
  startsAt: string;
  coverImageUrl: string | null;
  status: string;
  capacity: number;
  fromPrice: number;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export type SeatState = 'available' | 'held' | 'sold' | 'unknown';

export interface PageSeat {
  id: string;
  number: number;
  tierId: string;
  state: SeatState;
}

export interface PageSection {
  id: string;
  name: string;
  rows: { row: string; seats: PageSeat[] }[];
}

export interface PageTier {
  id: string;
  name: string;
  price: number;
  basePrice: number;
}

export interface EventPage {
  event: {
    id: string;
    title: string;
    artist: string;
    category: string;
    city: string;
    venueName: string;
    startsAt: string;
    coverImageUrl: string | null;
    description: string | null;
    status: string;
  };
  tiers: PageTier[];
  sections: PageSection[];
  availability: { capacity: number; available: number; held: number; sold: number } | null;
  partial: boolean;
  failures: string[];
}

export interface Hold {
  holdId: string;
  eventId: string;
  expiresAt: string;
  secondsLeft: number;
}

export interface Order {
  id: string;
  eventId: string;
  holdId: string;
  status: 'Pending' | 'Paid' | 'TicketsIssued' | 'Expired' | 'Failed' | 'Refunded';
  statusReason: string | null;
  subtotal: number;
  fees: number;
  total: number;
  currency: string;
  payment: { provider: 'mock' | 'stripe'; paymentRef: string | null; clientSecret: string | null; publishableKey: string | null } | null;
}

export interface CheckoutPage {
  holdId: string;
  eventId: string;
  eventTitle: string | null;
  startsAt: string | null;
  expiresAt: string;
  secondsLeft: number;
  lines: { seatId: string; tierId: string; label: string; price: number }[];
  subtotal: number | null;
  fees: number | null;
  total: number | null;
  currency: string;
  existingOrder: Order | null;
  partial: boolean;
}

export interface Ticket {
  id: string;
  orderId: string;
  eventId: string;
  seatId: string;
  label: string;
  code: string;
  status: 'Valid' | 'CheckedIn' | 'Void';
  issuedAt: string;
  checkedInAt: string | null;
  qrPngBase64?: string | null;
}

export interface TicketGroup {
  event: { id: string; title: string; artist: string; city: string; venueName: string; startsAt: string; coverImageUrl: string | null; status: string } | null;
  tickets: Ticket[];
}

export interface MyTickets {
  upcoming: TicketGroup[];
  past: TicketGroup[];
  partial: boolean;
}

export interface CheckInResult {
  outcome: 'admitted' | 'already-checked-in' | 'voided' | 'wrong-event' | 'invalid';
  ticket?: Ticket;
  checkedInAt?: string;
}

export interface TierSpec {
  name: string;
  basePrice: number;
  minPrice: number;
  maxPrice: number;
}

export interface SectionSpec {
  name: string;
  rows: number;
  seatsPerRow: number;
  tier: string;
}

export interface CreateEventRequest {
  title: string;
  artist: string;
  category: string;
  city: string;
  venueName: string;
  startsAt: string;
  coverImageUrl: string | null;
  description: string | null;
  tiers: TierSpec[];
  sections: SectionSpec[];
}
