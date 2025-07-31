import { DecimalPipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import { catchError, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { demandHeatmap, leadTime } from '../shared/charts';

@Component({
  selector: 'app-demand',
  imports: [MatCardModule, MatButtonModule, NgxEchartsDirective, DecimalPipe],
  template: `
    <mat-card>
      <mat-card-header><mat-card-title>When people buy (weekday × hour, UTC)</mat-card-title></mat-card-header>
      <div class="chart" echarts [options]="heatmapChart()"></div>
    </mat-card>
    <mat-card>
      <mat-card-header>
        <mat-card-title>Lead time: days between purchase and event</mat-card-title>
        <span class="spacer"></span>
        <a mat-button [href]="api.exportUrl('lead-time', filters.params())">CSV</a>
      </mat-card-header>
      @if (lead(); as l) {
        <p class="stats">Median {{ l.median | number: '1.0-0' }} days · mean {{ l.mean | number: '1.0-1' }} days</p>
      }
      <div class="chart" echarts [options]="leadChart()"></div>
    </mat-card>
  `,
  styles: `
    mat-card { margin-bottom: 16px; }
    mat-card-header { align-items: center; }
    .spacer { flex: 1; }
    .chart { height: 340px; }
    .stats { padding: 0 16px; color: #555; }
  `,
})
export class DemandComponent {
  readonly api = inject(DashboardApiService);
  readonly filters = inject(FilterService);
  private readonly params$ = toObservable(this.filters.params);

  private readonly heatmap = toSignal(this.params$.pipe(switchMap((p) => this.api.demandHeatmap(p).pipe(catchError(() => of(null))))));
  readonly lead = toSignal(this.params$.pipe(switchMap((p) => this.api.leadTime(p).pipe(catchError(() => of(null))))));

  readonly heatmapChart = computed(() => demandHeatmap(this.heatmap() ?? { weekdays: [], hours: [], matrix: [] }));
  readonly leadChart = computed(() => leadTime(this.lead() ?? { buckets: [], median: 0, mean: 0 }));
}
