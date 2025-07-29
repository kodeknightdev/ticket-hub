import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import { catchError, combineLatest, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { revenueForecast } from '../shared/charts';

@Component({
  selector: 'app-forecast',
  imports: [MatCardModule, MatButtonToggleModule, NgxEchartsDirective, CurrencyPipe, DecimalPipe],
  template: `
    @if (forecast(); as f) {
      <section class="totals">
        @for (h of horizons; track h) {
          <div class="total" [class.active]="h === horizon()">
            <div class="label">Next {{ h }} days</div>
            <div class="value">{{ f.totals[h].predicted | currency: 'GBP' : 'symbol' : '1.0-0' }}</div>
            <div class="range">{{ f.totals[h].lower | currency: 'GBP' : 'symbol' : '1.0-0' }} – {{ f.totals[h].upper | currency: 'GBP' : 'symbol' : '1.0-0' }}</div>
          </div>
        }
        <div class="total accuracy">
          <div class="label">Forecast accuracy (backtest)</div>
          @if (f.accuracy; as a) {
            <div class="value">{{ a.mape | number: '1.1-1' }}% MAPE</div>
            <div class="range">over {{ a.events }} completed events · lower is better</div>
          } @else {
            <div class="value">n/a</div>
          }
        </div>
      </section>
      <mat-card>
        <mat-card-header>
          <mat-card-title>Platform revenue forecast</mat-card-title>
          <span class="spacer"></span>
          <mat-button-toggle-group [value]="horizon()" (change)="horizon.set($event.value)" hideSingleSelectionIndicator>
            @for (h of horizons; track h) { <mat-button-toggle [value]="h">{{ h }} days</mat-button-toggle> }
          </mat-button-toggle-group>
        </mat-card-header>
        <div class="chart" echarts [options]="chart()"></div>
        <mat-card-footer class="note">Holt-Winters with weekly seasonality, refit hourly. The shaded band is an 80% interval.</mat-card-footer>
      </mat-card>
    }
  `,
  styles: `
    .totals { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 16px; margin-bottom: 16px; }
    .total { background: #fff; border-radius: 8px; padding: 16px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
    .total.active { outline: 2px solid #3949ab; }
    .label { font-size: 12px; color: #666; }
    .value { font-size: 24px; font-weight: 600; }
    .range { font-size: 12px; color: #888; }
    mat-card-header { align-items: center; }
    .spacer { flex: 1; }
    .chart { height: 400px; }
    .note { padding: 8px 16px; font-size: 12px; color: #777; }
  `,
})
export class ForecastComponent {
  private readonly api = inject(DashboardApiService);
  private readonly filters = inject(FilterService);
  readonly horizons = [30, 60, 90] as const;
  readonly horizon = signal<30 | 60 | 90>(30);

  readonly forecast = toSignal(
    combineLatest([toObservable(this.filters.params), toObservable(this.horizon)]).pipe(
      switchMap(([p, h]) => this.api.revenueForecast(p, h).pipe(catchError(() => of(null)))),
    ),
  );
  readonly chart = computed(() => (this.forecast() ? revenueForecast(this.forecast()!) : {}));
}
