export type Role = 'Owner' | 'Organizer' | 'Admin';

export interface User {
  id: string;
  email: string;
  name: string;
  role: Role;
}

export interface Kpi {
  key: 'grossRevenue' | 'netRevenue' | 'ticketsSold' | 'avgTicketPrice' | 'sellThrough' | 'checkInRate' | 'refundRate';
  value: number;
  previous: number;
  change: number | null;
}

export interface Overview {
  from: string;
  to: string;
  kpis: Kpi[];
  trend: { dates: string[]; grossRevenue: number[]; netRevenue: number[]; ticketsSold: number[]; avgTicketPrice: number[] };
  events: { upcoming: number; next30Days: number; drafts: number; cancelled: number } | null;
  partial: boolean;
}

export interface TimePoint {
  date: string;
  revenue: number;
  tickets: number;
  refunds: number;
  netRevenue: number;
}

export interface BreakdownRow {
  key: string;
  label: string;
  revenue: number;
  tickets: number;
  avgPrice: number;
  events: number;
}

export type PaceStatus = 'On track' | 'At risk' | 'Sold out' | 'Completed' | 'Cancelled';

export interface PerformanceRow {
  eventId: string;
  title: string;
  artist: string;
  category: string;
  city: string;
  venue: string;
  startsAt: string;
  capacity: number;
  sold: number;
  liveSold: number | null;
  revenue: number;
  avgPrice: number;
  daysToEvent: number;
  paceStatus: PaceStatus;
  projectedSellThrough: number;
  predictedSelloutDate: string | null;
  model: string | null;
}

export interface Insights {
  insights: {
    event: { eventId: string; title: string; artist: string; venue: string; city: string; capacity: number; startsAt: string; publishedAt: string; status: string };
    actual: { date: string; tickets: number; cumulative: number }[];
    velocity: { date: string; value: number }[];
    forecast: { date: string; predicted: number; lower: number; upper: number; cumulative: number; cumulativeLower: number; cumulativeUpper: number }[];
    summary: { model: string; soldToDate: number; projectedFinal: number; projectedSellThrough: number; paceStatus: PaceStatus; predictedSelloutDate: string | null } | null;
    priceChanges: { tierId: string; oldPrice: number; newPrice: number; reason: string; changedAt: string }[];
    sections: { sectionId: string; name: string; capacity: number; sold: number; fill: number }[];
  };
  priceHistory: { tierId: string; tierName: string; oldPrice: number; newPrice: number; reason: string; changedAt: string }[] | null;
  partial: boolean;
}

export interface RevenueForecast {
  horizon: number;
  history: { date: string; revenue: number }[];
  forecast: { date: string; predicted: number; lower: number; upper: number }[];
  totals: Record<'30' | '60' | '90', { predicted: number; lower: number; upper: number }>;
  accuracy: { model: string; mape: number; events: number; generatedAt: string } | null;
}

export interface Heatmap {
  weekdays: string[];
  hours: number[];
  matrix: number[][];
}

export interface SectionHeatmap {
  events: string[];
  sections: string[];
  cells: { eventId: string; event: string; section: string; sold: number; capacity: number; fill: number }[];
}

export interface LeadTime {
  buckets: { label: string; tickets: number }[];
  median: number;
  mean: number;
}

export interface Funnel {
  stages: { stage: string; value: number }[];
  holds: number;
  orders: number;
  paidOrders: number;
  holdExpiryRate: number;
  holdConversionRate: number;
}

export interface PricingImpact {
  rows: { eventId: string; event: string; changedAt: string; reason: string; oldPrice: number; newPrice: number; priceChange: number; velocityBefore: number; velocityAfter: number; velocityChange: number | null }[];
  summary: { increases: number; decreases: number; avgVelocityChangeAfterIncrease: number | null; avgVelocityChangeAfterDecrease: number | null };
}

export interface Alert {
  id: number;
  rule: string;
  severity: 'high' | 'medium' | 'info';
  eventId: string | null;
  message: string;
  createdAt: string;
}

export interface CancellationSaga {
  id: string;
  eventId: string;
  eventTitle: string;
  reason: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  total: number;
  completed: number;
  failed: number;
  inProgress: number;
}

export interface FilterOptions {
  cities: string[];
  categories: string[];
  venues: { id: string; name: string }[];
  organizers: string[];
}
