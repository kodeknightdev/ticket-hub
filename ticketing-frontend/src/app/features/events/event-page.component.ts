import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { EventPage, SeatState } from '../../core/models';
import { SeatingHubService } from '../../core/seating-hub.service';
import { MAX_SEATS, SeatMapComponent } from './seat-map.component';

@Component({
  selector: 'app-event-page',
  imports: [DatePipe, CurrencyPipe, MatButtonModule, MatProgressBarModule, SeatMapComponent],
  template: `
    @if (page(); as p) {
      <header class="hero">
        <div>
          <h1>{{ p.event.title }}</h1>
          <p>{{ p.event.artist }} · {{ p.event.venueName }}, {{ p.event.city }}</p>
          <p>{{ p.event.startsAt | date: 'EEEE d MMMM y, HH:mm' }}</p>
        </div>
        @if (p.availability; as a) {
          <div class="availability">
            <strong>{{ a.available }}</strong> of {{ a.capacity }} left
            <mat-progress-bar mode="determinate" [value]="(100 * (a.sold + a.held)) / a.capacity" />
          </div>
        }
      </header>

      @if (p.partial) {
        <p class="warning">Live seat availability is temporarily unavailable; seats may already be taken.</p>
      }

      <section class="tiers">
        @for (tier of p.tiers; track tier.id) {
          <span class="tier">{{ tier.name }}: <strong>{{ tier.price | currency: 'GBP' }}</strong>
            @if (tier.price !== tier.basePrice) { <small>(was {{ tier.basePrice | currency: 'GBP' }})</small> }
          </span>
        }
      </section>

      <app-seat-map [sections]="p.sections" [tiers]="p.tiers" [selected]="selected()" (selectedChange)="selected.set($event)" />

      <footer class="basket">
        <span>{{ selected().length }} / {{ maxSeats }} seats · {{ selectedTotal() | currency: 'GBP' }}</span>
        @if (error()) {
          <span class="error">{{ error() }}</span>
        }
        <button mat-flat-button color="primary" [disabled]="selected().length === 0 || busy()" (click)="hold()">Hold seats for 10 minutes</button>
      </footer>
    } @else if (notFound()) {
      <p>That event doesn't exist or isn't on sale.</p>
    } @else {
      <mat-progress-bar mode="indeterminate" />
    }
  `,
  styles: `
    .hero { display: flex; justify-content: space-between; align-items: flex-end; gap: 24px; margin-bottom: 16px; }
    .hero h1 { margin: 0 0 4px; }
    .availability { min-width: 200px; }
    .tiers { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 16px; }
    .tier small { color: #777; }
    .warning { background: #fff3e0; padding: 8px 12px; border-radius: 4px; }
    .basket { position: sticky; bottom: 0; background: #fff; border-top: 1px solid #ddd; padding: 12px 0; display: flex; gap: 16px; align-items: center; justify-content: flex-end; }
    .error { color: #c62828; }
  `,
})
export class EventPageComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly hub = inject(SeatingHubService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private subscriptions: Subscription[] = [];

  readonly id = input.required<string>();
  readonly page = signal<EventPage | null>(null);
  readonly notFound = signal(false);
  readonly selected = signal<string[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly maxSeats = MAX_SEATS;

  readonly selectedTotal = computed(() => {
    const p = this.page();
    if (!p) return 0;
    const prices = Object.fromEntries(p.tiers.map((t) => [t.id, t.price]));
    const seats = p.sections.flatMap((s) => s.rows.flatMap((r) => r.seats));
    return this.selected().reduce((sum, id) => sum + (prices[seats.find((s) => s.id === id)?.tierId ?? ''] ?? 0), 0);
  });

  ngOnInit(): void {
    this.api.eventPage(this.id()).subscribe({
      next: (page) => this.page.set(page),
      error: () => this.notFound.set(true),
    });

    this.subscriptions.push(
      this.hub.seats$.subscribe((change) => {
        if (change.eventId === this.id()) this.applySeats(change.seatIds, change.state);
      }),
      this.hub.prices$.subscribe((change) => {
        if (change.eventId !== this.id()) return;
        this.page.update((p) => p && { ...p, tiers: p.tiers.map((t) => (t.id === change.tierId ? { ...t, price: change.price } : t)) });
      }),
    );
    this.hub.join(this.id()).catch(() => this.page.update((p) => p && { ...p, partial: true }));
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach((s) => s.unsubscribe());
    void this.hub.leave(this.id());
  }

  applySeats(seatIds: string[], state: SeatState): void {
    const ids = new Set(seatIds);
    this.page.update((p) => p && {
      ...p,
      sections: p.sections.map((s) => ({
        ...s,
        rows: s.rows.map((r) => ({ ...r, seats: r.seats.map((seat) => (ids.has(seat.id) ? { ...seat, state } : seat)) })),
      })),
    });
    if (state !== 'available') {
      this.selected.update((sel) => sel.filter((id) => !ids.has(id)));
    }
  }

  hold(): void {
    if (!this.auth.isAuthenticated()) {
      void this.router.navigate(['/login'], { queryParams: { returnUrl: `/events/${this.id()}` } });
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.hold(this.id(), this.selected()).subscribe({
      next: (hold) => void this.router.navigate(['/checkout', hold.holdId]),
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(
          e.status === 409 ? 'Someone just grabbed one of those seats, pick again.'
            : e.status === 429 ? 'Too many attempts, wait a few seconds.'
              : e.error?.title ?? 'Could not hold seats',
        );
        if (e.status === 409 && Array.isArray(e.error?.conflicts)) {
          this.applySeats(e.error.conflicts, 'held');
        }
      },
    });
  }
}
