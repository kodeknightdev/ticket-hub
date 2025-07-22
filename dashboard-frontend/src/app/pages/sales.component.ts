import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import { catchError, combineLatest, of, switchMap } from 'rxjs';
import { BreakdownBy, DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { horizontalBars, salesOverTime, sectionHeatmap } from '../shared/charts';

@Component({
  selector: 'app-sales',
  imports: [MatCardModule, MatButtonToggleModule, MatButtonModule, NgxEchartsDirective],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>Sales over time</mat-card-title>
        <span class="spacer"></span>
        <a mat-button [href]="api.exportUrl('sales', filters.params())">Export CSV</a>
      </mat-card-header>
      <div class="chart" echarts [options]="timeChart()"></div>
    </mat-card>

    <mat-card>
      <mat-card-header>
        <mat-card-title>Top by revenue</mat-card-title>
        <span class="spacer"></span>
        <mat-button-toggle-group [value]="by()" (change)="by.set($event.value)" hideSingleSelectionIndicator>
          @for (option of options; track option) { <mat-button-toggle [value]="option">{{ option }}</mat-button-toggle> }
        </mat-button-toggle-group>
        <a mat-button [href]="api.exportUrl('breakdown-' + by(), filters.params())">CSV</a>
      </mat-card-header>
      <div class="chart" echarts [options]="breakdownChart()"></div>
    </mat-card>

    <mat-card>
      <mat-card-header><mat-card-title>Sell-through by venue section</mat-card-title></mat-card-header>
      <div class="chart tall" echarts [options]="heatmapChart()"></div>
    </mat-card>
  `,
  styles: `
    mat-card { margin-bottom: 16px; }
    mat-card-header { align-items: center; }
    .spacer { flex: 1; }
    .chart { height: 360px; }
    .chart.tall { height: 460px; }
  `,
})
export class SalesComponent {
  readonly api = inject(DashboardApiService);
  readonly filters = inject(FilterService);
  readonly options: BreakdownBy[] = ['event', 'artist', 'category', 'city', 'venue'];
  readonly by = signal<BreakdownBy>('event');
  private readonly params$ = toObservable(this.filters.params);

  private readonly timeseries = toSignal(this.params$.pipe(switchMap((p) => this.api.timeseries(p).pipe(catchError(() => of(null))))));
  private readonly breakdown = toSignal(
    combineLatest([this.params$, toObservable(this.by)]).pipe(switchMap(([p, by]) => this.api.breakdown(p, by).pipe(catchError(() => of(null))))),
  );
  private readonly heatmap = toSignal(this.params$.pipe(switchMap((p) => this.api.sectionHeatmap(p).pipe(catchError(() => of(null))))));

  readonly timeChart = computed(() => salesOverTime(this.timeseries()?.points ?? []));
  readonly breakdownChart = computed(() => {
    const rows = this.breakdown()?.rows ?? [];
    return horizontalBars(rows.map((r) => r.label), rows.map((r) => r.revenue), 'Revenue');
  });
  readonly heatmapChart = computed(() => sectionHeatmap(this.heatmap() ?? { events: [], sections: [], cells: [] }));
}
