import { CurrencyPipe, DatePipe, PercentPipe } from '@angular/common';
import { AfterViewInit, Component, effect, inject, signal, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginator, MatPaginatorModule } from '@angular/material/paginator';
import { MatSort, MatSortModule } from '@angular/material/sort';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';
import { PerformanceRow } from '../core/models';
import { PaceChipComponent } from '../shared/pace-chip.component';

@Component({
  selector: 'app-events',
  imports: [MatTableModule, MatSortModule, MatPaginatorModule, MatButtonModule, MatFormFieldModule, MatInputModule, RouterLink, DatePipe, CurrencyPipe, PercentPipe, PaceChipComponent],
  template: `
    <div class="head">
      <h2>Event performance</h2>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Filter</mat-label>
        <input matInput (keyup)="applyFilter($any($event.target).value)" placeholder="Title, city, venue…" />
      </mat-form-field>
      <a mat-stroked-button [href]="api.exportUrl('performance', filters.params())">Export CSV</a>
    </div>
    @if (partial()) {
      <p class="warning">Live seat counts are unavailable right now; sold figures come from analytics and may lag slightly.</p>
    }
    <table mat-table [dataSource]="source" matSort matSortActive="startsAt" matSortDirection="asc">
      <ng-container matColumnDef="title">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Event</th>
        <td mat-cell *matCellDef="let r"><a [routerLink]="['/events', r.eventId]">{{ r.title }}</a><div class="sub">{{ r.venue }}, {{ r.city }}</div></td>
      </ng-container>
      <ng-container matColumnDef="startsAt">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Date</th>
        <td mat-cell *matCellDef="let r">{{ r.startsAt | date: 'd MMM y' }}</td>
      </ng-container>
      <ng-container matColumnDef="sold">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Sold / capacity</th>
        <td mat-cell *matCellDef="let r">{{ r.sold }} / {{ r.capacity }}</td>
      </ng-container>
      <ng-container matColumnDef="revenue">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Revenue</th>
        <td mat-cell *matCellDef="let r">{{ r.revenue | currency: 'GBP' : 'symbol' : '1.0-0' }}</td>
      </ng-container>
      <ng-container matColumnDef="avgPrice">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Avg price</th>
        <td mat-cell *matCellDef="let r">{{ r.avgPrice | currency: 'GBP' }}</td>
      </ng-container>
      <ng-container matColumnDef="daysToEvent">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Days to go</th>
        <td mat-cell *matCellDef="let r">{{ r.daysToEvent >= 0 ? r.daysToEvent : '–' }}</td>
      </ng-container>
      <ng-container matColumnDef="projectedSellThrough">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Projected</th>
        <td mat-cell *matCellDef="let r">{{ r.projectedSellThrough | percent }}</td>
      </ng-container>
      <ng-container matColumnDef="paceStatus">
        <th mat-header-cell *matHeaderCellDef mat-sort-header>Pace vs forecast</th>
        <td mat-cell *matCellDef="let r"><app-pace-chip [status]="r.paceStatus" /></td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
    <mat-paginator [pageSizeOptions]="[10, 25, 50]" [pageSize]="10" showFirstLastButtons />
  `,
  styles: `
    .head { display: flex; align-items: center; gap: 16px; }
    .head h2 { flex: 1; }
    table { width: 100%; }
    .sub { font-size: 12px; color: #777; }
    .warning { background: #fff3e0; padding: 8px 12px; border-radius: 4px; }
  `,
})
export class EventsComponent implements AfterViewInit {
  readonly api = inject(DashboardApiService);
  readonly filters = inject(FilterService);
  readonly columns = ['title', 'startsAt', 'sold', 'revenue', 'avgPrice', 'daysToEvent', 'projectedSellThrough', 'paceStatus'];
  readonly source = new MatTableDataSource<PerformanceRow>([]);
  readonly partial = signal(false);
  private readonly sort = viewChild(MatSort);
  private readonly paginator = viewChild(MatPaginator);

  private readonly data = toSignal(
    toObservable(this.filters.params).pipe(switchMap((p) => this.api.performance(p).pipe(catchError(() => of({ rows: [], partial: true }))))),
  );

  constructor() {
    this.source.filterPredicate = (row, filter) => `${row.title} ${row.city} ${row.venue} ${row.artist}`.toLowerCase().includes(filter);
    effect(() => {
      const data = this.data();
      this.source.data = data?.rows ?? [];
      this.partial.set(!!data?.partial);
    });
  }

  ngAfterViewInit(): void {
    this.source.sort = this.sort() ?? null;
    this.source.paginator = this.paginator() ?? null;
  }

  applyFilter(value: string): void {
    this.source.filter = value.trim().toLowerCase();
    this.source.paginator?.firstPage();
  }
}
