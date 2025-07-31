import { CurrencyPipe, DatePipe, PercentPipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { NgxEchartsDirective } from 'ngx-echarts';
import { catchError, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { funnel } from '../shared/charts';

@Component({
  selector: 'app-funnel',
  imports: [MatCardModule, MatTableModule, MatButtonModule, NgxEchartsDirective, PercentPipe, CurrencyPipe, DatePipe],
  template: `
    <section class="row">
      <mat-card class="grow">
        <mat-card-header><mat-card-title>Conversion funnel (seats)</mat-card-title></mat-card-header>
        <div class="chart" echarts [options]="funnelChart()"></div>
      </mat-card>
      @if (funnelData(); as f) {
        <mat-card class="stats">
          <div class="label">Holds placed</div><div class="value">{{ f.holds }}</div>
          <div class="label">Hold → paid</div><div class="value">{{ f.holdConversionRate | percent: '1.0-1' }}</div>
          <div class="label">Hold expiry rate</div><div class="value">{{ f.holdExpiryRate | percent: '1.0-1' }}</div>
          <div class="label">Orders paid</div><div class="value">{{ f.paidOrders }} / {{ f.orders }}</div>
        </mat-card>
      }
    </section>

    @if (impact(); as i) {
      <mat-card>
        <mat-card-header>
          <mat-card-title>Pricing impact</mat-card-title>
          <span class="spacer"></span>
          <a mat-button [href]="api.exportUrl('pricing-impact', filters.params())">CSV</a>
        </mat-card-header>
        <mat-card-content>
          <p class="summary">
            {{ i.summary.increases }} increases (velocity after: {{ (i.summary.avgVelocityChangeAfterIncrease ?? 0) | percent: '1.0-0' }}),
            {{ i.summary.decreases }} decreases (velocity after: {{ (i.summary.avgVelocityChangeAfterDecrease ?? 0) | percent: '1.0-0' }})
          </p>
          <table mat-table [dataSource]="i.rows">
            <ng-container matColumnDef="changedAt"><th mat-header-cell *matHeaderCellDef>When</th><td mat-cell *matCellDef="let r">{{ r.changedAt | date: 'd MMM' }}</td></ng-container>
            <ng-container matColumnDef="event"><th mat-header-cell *matHeaderCellDef>Event</th><td mat-cell *matCellDef="let r">{{ r.event }}</td></ng-container>
            <ng-container matColumnDef="price"><th mat-header-cell *matHeaderCellDef>Price</th><td mat-cell *matCellDef="let r">{{ r.oldPrice | currency: 'GBP' }} → {{ r.newPrice | currency: 'GBP' }}</td></ng-container>
            <ng-container matColumnDef="velocity"><th mat-header-cell *matHeaderCellDef>Tickets/day before → after</th><td mat-cell *matCellDef="let r">{{ r.velocityBefore }} → {{ r.velocityAfter }}</td></ng-container>
            <ng-container matColumnDef="reason"><th mat-header-cell *matHeaderCellDef>Reason</th><td mat-cell *matCellDef="let r">{{ r.reason }}</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns"></tr>
          </table>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .row { display: flex; gap: 16px; margin-bottom: 16px; flex-wrap: wrap; }
    .grow { flex: 2; min-width: 320px; }
    .stats { flex: 1; min-width: 200px; padding: 16px; }
    .label { font-size: 12px; color: #666; margin-top: 8px; }
    .value { font-size: 22px; font-weight: 600; }
    .chart { height: 340px; }
    mat-card-header { align-items: center; }
    .spacer { flex: 1; }
    .summary { color: #555; }
    table { width: 100%; }
  `,
})
export class FunnelComponent {
  readonly api = inject(DashboardApiService);
  readonly filters = inject(FilterService);
  readonly columns = ['changedAt', 'event', 'price', 'velocity', 'reason'];
  private readonly params$ = toObservable(this.filters.params);

  readonly funnelData = toSignal(this.params$.pipe(switchMap((p) => this.api.funnel(p).pipe(catchError(() => of(null))))));
  readonly impact = toSignal(this.params$.pipe(switchMap((p) => this.api.pricingImpact(p).pipe(catchError(() => of(null))))));
  readonly funnelChart = computed(() => funnel(this.funnelData() ?? { stages: [], holds: 0, orders: 0, paidOrders: 0, holdExpiryRate: 0, holdConversionRate: 0 }));
}
