import { CurrencyPipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import { Kpi } from '../core/models';
import { sparkline } from './charts';

const LABELS: Record<Kpi['key'], string> = {
  grossRevenue: 'Gross revenue',
  netRevenue: 'Net revenue',
  ticketsSold: 'Tickets sold',
  avgTicketPrice: 'Avg ticket price',
  sellThrough: 'Sell-through',
  checkInRate: 'Check-in rate',
  refundRate: 'Refund rate',
};

// For these a rise is bad news, so the arrow colour flips.
const LOWER_IS_BETTER = new Set<Kpi['key']>(['refundRate']);

@Component({
  selector: 'app-kpi-card',
  imports: [MatCardModule, CurrencyPipe, PercentPipe, DecimalPipe, NgxEchartsDirective],
  template: `
    <mat-card class="kpi">
      <div class="label">{{ label() }}</div>
      <div class="value">
        @switch (kind()) {
          @case ('money') { {{ kpi().value | currency: 'GBP' : 'symbol' : '1.0-0' }} }
          @case ('percent') { {{ kpi().value | percent: '1.0-1' }} }
          @default { {{ kpi().value | number }} }
        }
      </div>
      <div class="change" [class.good]="good()" [class.bad]="bad()">
        @if (kpi().change === null) {
          <span>no previous data</span>
        } @else {
          <span>{{ (kpi().change ?? 0) >= 0 ? '▲' : '▼' }} {{ kpi().change | percent: '1.0-1' }} vs previous period</span>
        }
      </div>
      @if (trend().length > 1) {
        <div class="spark" echarts [options]="spark()"></div>
      }
    </mat-card>
  `,
  styles: `
    .kpi { padding: 16px; min-height: 140px; }
    .label { color: #666; font-size: 13px; }
    .value { font-size: 26px; font-weight: 600; margin: 4px 0; }
    .change { font-size: 12px; color: #666; }
    .good { color: #2e7d32; }
    .bad { color: #c62828; }
    .spark { height: 40px; margin-top: 8px; }
  `,
})
export class KpiCardComponent {
  readonly kpi = input.required<Kpi>();
  readonly trend = input<number[]>([]);

  readonly label = computed(() => LABELS[this.kpi().key]);
  readonly kind = computed(() => {
    const key = this.kpi().key;
    if (key === 'grossRevenue' || key === 'netRevenue' || key === 'avgTicketPrice') return 'money';
    if (key === 'ticketsSold') return 'count';
    return 'percent';
  });
  readonly good = computed(() => this.direction() > 0);
  readonly bad = computed(() => this.direction() < 0);
  readonly spark = computed(() => sparkline(this.trend()));

  private direction(): number {
    const change = this.kpi().change;
    if (!change) return 0;
    return LOWER_IS_BETTER.has(this.kpi().key) ? -Math.sign(change) : Math.sign(change);
  }
}
