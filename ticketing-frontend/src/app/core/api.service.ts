import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CheckInResult, CheckoutPage, CreateEventRequest, EventPage, EventSummary, Hold, MyTickets, Order, Paged, Ticket,
} from './models';

export interface EventSearch {
  q?: string;
  city?: string;
  category?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  searchEvents(search: EventSearch): Observable<Paged<EventSummary>> {
    let params = new HttpParams().set('page', search.page).set('pageSize', search.pageSize);
    for (const key of ['q', 'city', 'category', 'from', 'to'] as const) {
      const value = search[key];
      if (value) params = params.set(key, value);
    }
    return this.http.get<Paged<EventSummary>>('/api/events', { params });
  }

  categories(): Observable<string[]> {
    return this.http.get<string[]>('/api/categories');
  }

  venues(): Observable<{ id: string; name: string; city: string }[]> {
    return this.http.get<{ id: string; name: string; city: string }[]>('/api/venues');
  }

  eventPage(id: string): Observable<EventPage> {
    return this.http.get<EventPage>(`/bff/events/${id}/page`);
  }

  hold(eventId: string, seatIds: string[]): Observable<Hold> {
    return this.http.post<Hold>('/api/seating/holds', { eventId, seatIds });
  }

  releaseHold(holdId: string): Observable<void> {
    return this.http.delete<void>(`/api/seating/holds/${holdId}`);
  }

  checkout(holdId: string): Observable<CheckoutPage> {
    return this.http.get<CheckoutPage>(`/bff/checkout/${holdId}`);
  }

  createOrder(holdId: string, idempotencyKey: string): Observable<Order> {
    return this.http.post<Order>('/api/orders', { holdId }, { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) });
  }

  order(id: string): Observable<Order> {
    return this.http.get<Order>(`/api/orders/${id}`);
  }

  mockPay(orderId: string, succeed = true): Observable<Order> {
    return this.http.post<Order>(`/api/orders/${orderId}/mock-pay`, { succeed });
  }

  myTickets(): Observable<MyTickets> {
    return this.http.get<MyTickets>('/bff/me/tickets');
  }

  ticket(id: string): Observable<Ticket> {
    return this.http.get<Ticket>(`/api/tickets/${id}`);
  }

  checkIn(code: string, eventId?: string): Observable<CheckInResult> {
    return this.http.post<CheckInResult>('/api/checkin', { code, eventId: eventId || null });
  }

  myEvents(): Observable<EventSummary[]> {
    return this.http.get<EventSummary[]>('/api/events/mine');
  }

  createEvent(request: CreateEventRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/events', request);
  }

  publish(id: string): Observable<unknown> {
    return this.http.post(`/api/events/${id}/publish`, {});
  }

  cancel(id: string, reason: string): Observable<unknown> {
    return this.http.post(`/api/events/${id}/cancel`, { reason });
  }
}
