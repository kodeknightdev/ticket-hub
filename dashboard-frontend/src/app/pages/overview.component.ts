import { Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import { catchError, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { Kpi } from '../core/models';
import { salesOverTime } from '../shared/charts';
import { KpiCardComponent } from '../shared/kpi-card.component';

@Component({
  selector: 'app-overview',
  imports: [KpiCardComponent, MatCardModule, NgxEchartsDirective, RouterLink],
  template: `
    @if (overview(); as o) {
      @if (o.partial) {
        <p class="warning">Some numbers are missing because a service didn't answer; showing what we have.</p>
      }
      <section class="kpis">
        @for (kpi of o.kpis; track kpi.key) {
          <app-kpi-card [kpi]="kpi" [trend]="trendFor(kpi)" />
        }
        @if (o.events; as e) {
          <mat-card class="events">
            <div class="label">Upcoming events</div>
            <div class="value">{{ e.upcoming }}</div>
            <div class="sub">{{ e.next30Days }} in the next 30 days · {{ e.drafts }} drafts</div>
          </mat-card>
        }
      </section>
      <mat-card>
        <mat-card-header><mat-card-title>Sales</mat-card-title></mat-card-header>
        <div class="chart" echarts [options]="chart()"></div>
      </mat-card>
      @if (alerts().length > 0) {
        <mat-card class="alerts">
          <mat-card-header><mat-card-title>Needs attention</mat-card-title></mat-card-header>
          <mat-card-content>
            @for (a of alerts().slice(0, 4); track a.id) {
              <p [class]="a.severity">{{ a.message }}</p>
            }
            <a routerLink="/alerts">All alerts</a>
          </mat-card-content>
        </mat-card>
      }
    } @else {
      <p>Loading…</p>
    }
  `,
  styles: `
    .kpis { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 16px; margin-bottom: 16px; }
    .events { padding: 16px; }
    .events .label { color: #666; font-size: 13px; }
    .events .value { font-size: 26px; font-weight: 600; }
    .events .sub { font-size: 12px; color: #666; }
    .chart { height: 360px; }
    .alerts { margin-top: 16px; }
    .high { color: #c62828; }
    .medium { color: #ef6c00; }
    .warning { background: #fff3e0; padding: 8px 12px; border-radius: 4px; }
  `,
})
export class OverviewComponent {
  private readonly api = inject(DashboardApiService);
  private readonly filters = inject(FilterService);
  private readonly params$ = toObservable(this.filters.params);

  readonly overview = toSignal(this.params$.pipe(switchMap((p) => this.api.overview(p).pipe(catchError(() => of(null))))));
  readonly timeseries = toSignal(this.params$.pipe(switchMap((p) => this.api.timeseries(p).pipe(catchError(() => of(null))))));
  readonly alerts = toSignal(this.params$.pipe(switchMap((p) => this.api.alerts(p).pipe(catchError(() => of([]))))), { initialValue: [] });
  readonly chart = computed(() => salesOverTime(this.timeseries()?.points ?? []));

  trendFor(kpi: Kpi): number[] {
    const trend = this.overview()?.trend;
    if (!trend) return [];
    switch (kpi.key) {
      case 'grossRevenue': return trend.grossRevenue;
      case 'netRevenue': return trend.netRevenue;
      case 'ticketsSold': return trend.ticketsSold;
      case 'avgTicketPrice': return trend.avgTicketPrice;
      default: return [];
    }
  }
}
