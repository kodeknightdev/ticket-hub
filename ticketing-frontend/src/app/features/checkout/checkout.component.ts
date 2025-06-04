import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, OnInit, inject, input, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { Router, RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { idempotencyKeyFor } from '../../core/idempotency';
import { CheckoutPage, Order } from '../../core/models';
import { CountdownPipe } from './countdown.pipe';
import { StripeLike, loadStripe } from './stripe';

@Component({
  selector: 'app-checkout',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatCardModule, CountdownPipe],
  template: `
    @if (page(); as p) {
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ p.eventTitle ?? 'Your seats' }}</mat-card-title>
          @if (p.startsAt) {
            <mat-card-subtitle>{{ p.startsAt | date: 'EEE d MMM y, HH:mm' }}</mat-card-subtitle>
          }
        </mat-card-header>
        <mat-card-content>
          <p class="timer" [class.urgent]="secondsLeft() < 120">Seats held for {{ secondsLeft() | countdown }}</p>
          <table>
            @for (line of p.lines; track line.seatId) {
              <tr><td>{{ line.label }}</td><td class="num">{{ line.price | currency: p.currency }}</td></tr>
            }
            <tr class="muted"><td>Booking fees</td><td class="num">{{ p.fees | currency: p.currency }}</td></tr>
            <tr class="total"><td>Total</td><td class="num">{{ p.total | currency: p.currency }}</td></tr>
          </table>
          @if (p.partial) {
            <p class="warning">Some price details are still loading; the final total is confirmed when you pay.</p>
          }
          <div #card class="card-element" [hidden]="!stripeMode()"></div>
          @if (message()) {
            <p [class.error]="failed()">{{ message() }}</p>
          }
        </mat-card-content>
        <mat-card-actions align="end">
          <button mat-button (click)="release()" [disabled]="busy()">Release seats</button>
          <button mat-flat-button color="primary" (click)="pay()" [disabled]="busy() || secondsLeft() === 0">
            Pay {{ p.total | currency: p.currency }}
          </button>
        </mat-card-actions>
      </mat-card>
    } @else if (expired()) {
      <p>This hold has expired and the seats are back on sale. <a routerLink="/">Find another event</a></p>
    }
  `,
  styles: `
    mat-card { max-width: 560px; margin: 0 auto; }
    .timer { font-size: 18px; font-weight: 600; }
    .timer.urgent { color: #c62828; }
    table { width: 100%; border-collapse: collapse; margin: 12px 0; }
    td { padding: 6px 0; border-bottom: 1px solid #eee; }
    .num { text-align: right; }
    .muted td { color: #666; }
    .total td { font-weight: 700; border-bottom: none; }
    .warning { background: #fff3e0; padding: 8px; }
    .error { color: #c62828; }
    .card-element { padding: 12px; border: 1px solid #ccc; border-radius: 4px; margin: 12px 0; }
  `,
})
export class CheckoutComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cardHost = viewChild<ElementRef<HTMLDivElement>>('card');

  readonly holdId = input.required<string>();
  readonly page = signal<CheckoutPage | null>(null);
  readonly expired = signal(false);
  readonly secondsLeft = signal(0);
  readonly busy = signal(false);
  readonly message = signal<string | null>(null);
  readonly failed = signal(false);
  readonly stripeMode = signal(false);
  private stripeSession: { stripe: StripeLike; card: unknown; order: Order } | null = null;

  ngOnInit(): void {
    this.api.checkout(this.holdId()).subscribe({
      next: (page) => {
        this.page.set(page);
        this.secondsLeft.set(page.secondsLeft);
      },
      error: () => this.expired.set(true),
    });
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.secondsLeft.update((s) => Math.max(0, s - 1));
        if (this.page() && this.secondsLeft() === 0) {
          this.page.set(null);
          this.expired.set(true);
        }
      });
  }

  async pay(): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('Creating your order…');
    try {
      if (this.stripeSession) {
        await this.confirmStripe();
        return;
      }
      const order = await new Promise<Order>((resolve, reject) =>
        this.api.createOrder(this.holdId(), idempotencyKeyFor(this.holdId())).subscribe({ next: resolve, error: reject }),
      );
      if (order.payment?.provider === 'stripe' && order.payment.clientSecret && order.payment.publishableKey) {
        await this.payWithStripe(order);
      } else {
        await this.payWithMock(order);
      }
    } catch (e) {
      this.failed.set(true);
      this.message.set(e instanceof HttpErrorResponse && e.status === 410 ? 'Your hold expired before payment.' : (e as Error).message ?? 'Payment failed');
      this.busy.set(false);
    }
  }

  private async payWithMock(order: Order): Promise<void> {
    this.message.set('Processing payment (test mode)…');
    const paid = await new Promise<Order>((resolve, reject) => this.api.mockPay(order.id).subscribe({ next: resolve, error: reject }));
    if (paid.status === 'Paid' || paid.status === 'TicketsIssued') {
      sessionStorage.removeItem(`idem:${this.holdId()}`);
      await this.router.navigate(['/tickets'], { queryParams: { justBought: order.id } });
    } else {
      throw new Error(paid.statusReason === 'hold-expired' ? 'Payment arrived after your hold expired, so it was refunded.' : 'Payment was declined');
    }
  }

  private async payWithStripe(order: Order): Promise<void> {
    this.stripeMode.set(true);
    const stripe = await loadStripe(order.payment!.publishableKey!);
    const card = stripe.elements().create('card');
    card.mount(this.cardHost()!.nativeElement);
    this.stripeSession = { stripe, card, order };
    this.message.set('Enter a Stripe test card (4242 4242 4242 4242) and press Pay again.');
    this.busy.set(false);
  }

  private async confirmStripe(): Promise<void> {
    const { stripe, card, order } = this.stripeSession!;
    this.message.set('Confirming with Stripe (test mode)…');
    const result = await stripe.confirmCardPayment(order.payment!.clientSecret!, { payment_method: { card } });
    if (result.error) throw new Error(result.error.message);
    // the webhook flips the order to Paid; tickets follow once seating confirms the sale
    sessionStorage.removeItem(`idem:${this.holdId()}`);
    await this.router.navigate(['/tickets'], { queryParams: { justBought: order.id } });
  }

  release(): void {
    this.busy.set(true);
    this.api.releaseHold(this.holdId()).subscribe({
      next: () => void this.router.navigate(['/events', this.page()?.eventId]),
      error: () => this.busy.set(false),
    });
  }
}
