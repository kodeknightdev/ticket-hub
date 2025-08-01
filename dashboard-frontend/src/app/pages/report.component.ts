import { CurrencyPipe, DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { catchError, forkJoin, of, switchMap } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';

/** Plain, print-friendly summary of the current filter selection. */
@Component({
  selector: 'app-report',
  imports: [MatButtonModule, CurrencyPipe, PercentPipe, DecimalPipe, DatePipe],
  template: `
    <div class="no-print actions"><button mat-flat-button color="primary" (click)="print()">Print / save as PDF</button></div>
    @if (report(); as r) {
      <article class="report">
        <h1>ticket-hub summary report</h1>
        <p>{{ filters.from() | date: 'd MMM y' }} – {{ filters.to() | date: 'd MMM y' }} · prepared for {{ auth.user()?.name }} on {{ today | date: 'd MMM y, HH:mm' }}</p>
        @if (r.overview) {
          <h2>Headline numbers</h2>
          <table>
            @for (k of r.overview.kpis; track k.key) {
              <tr>
                <th>{{ k.key }}</th>
                <td>{{ k.key.includes('Rate') || k.key === 'sellThrough' ? (k.value | percent: '1.0-1') : k.key === 'ticketsSold' ? (k.value | number) : (k.value | currency: 'GBP') }}</td>
                <td>{{ k.change === null ? '–' : (k.change | percent: '1.0-1') }} vs previous</td>
              </tr>
            }
          </table>
        }
        @if (r.top) {
          <h2>Top events by revenue</h2>
          <table>
            <tr><th>Event</th><th>Tickets</th><th>Revenue</th><th>Avg price</th></tr>
            @for (row of r.top.rows; track row.key) {
              <tr><td>{{ row.label }}</td><td>{{ row.tickets | number }}</td><td>{{ row.revenue | currency: 'GBP' }}</td><td>{{ row.avgPrice | currency: 'GBP' }}</td></tr>
            }
          </table>
        }
        @if (r.forecast) {
          <h2>Revenue outlook</h2>
          <p>Next 30 days: {{ r.forecast.totals['30'].predicted | currency: 'GBP' : 'symbol' : '1.0-0' }}, next 90 days: {{ r.forecast.totals['90'].predicted | currency: 'GBP' : 'symbol' : '1.0-0' }}
            @if (r.forecast.accuracy) { (backtest MAPE {{ r.forecast.accuracy.mape | number: '1.1-1' }}%) }</p>
        }
        <h2>Alerts</h2>
        <ul>
          @for (a of r.alerts; track a.id) { <li>{{ a.message }}</li> } @empty { <li>None</li> }
        </ul>
      </article>
    }
  `,
  styles: `
    .actions { margin-bottom: 16px; }
    .report { background: #fff; padding: 32px; max-width: 900px; }
    table { border-collapse: collapse; width: 100%; margin-bottom: 16px; }
    th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #ddd; }
    @media print { .no-print { display: none; } .report { padding: 0; } }
  `,
})
export class ReportComponent {
  readonly filters = inject(FilterService);
  readonly auth = inject(AuthService);
  private readonly api = inject(DashboardApiService);
  readonly today = new Date();

  readonly report = toSignal(
    toObservable(this.filters.params).pipe(
      switchMap((p) => forkJoin({
        overview: this.api.overview(p).pipe(catchError(() => of(null))),
        top: this.api.breakdown(p, 'event').pipe(catchError(() => of(null))),
        forecast: this.api.revenueForecast(p, 90).pipe(catchError(() => of(null))),
        alerts: this.api.alerts(p).pipe(catchError(() => of([]))),
      })),
    ),
  );

  print(): void {
    window.print();
  }
}
