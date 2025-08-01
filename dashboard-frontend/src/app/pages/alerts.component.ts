import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService } from '../core/filter.service';

@Component({
  selector: 'app-alerts',
  imports: [MatCardModule, MatTableModule, MatButtonModule, RouterLink, DatePipe],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>Alerts</mat-card-title>
        <span class="spacer"></span>
        <a mat-button [href]="api.exportUrl('alerts', filters.params())">CSV</a>
      </mat-card-header>
      <mat-card-content>
        @for (alert of alerts(); track alert.id) {
          <div class="alert" [class]="'alert ' + alert.severity">
            <span class="severity">{{ alert.severity }}</span>
            <span class="message">{{ alert.message }}</span>
            @if (alert.eventId) { <a [routerLink]="['/events', alert.eventId]">open</a> }
          </div>
        } @empty {
          <p>Nothing needs attention right now.</p>
        }
      </mat-card-content>
    </mat-card>

    <mat-card>
      <mat-card-header><mat-card-title>Event cancellations (refund saga)</mat-card-title></mat-card-header>
      <mat-card-content>
        @if (sagas().length === 0) {
          <p>No cancelled events.</p>
        } @else {
          <table mat-table [dataSource]="sagas()">
            <ng-container matColumnDef="event"><th mat-header-cell *matHeaderCellDef>Event</th><td mat-cell *matCellDef="let s">{{ s.eventTitle }}</td></ng-container>
            <ng-container matColumnDef="started"><th mat-header-cell *matHeaderCellDef>Started</th><td mat-cell *matCellDef="let s">{{ s.startedAt | date: 'd MMM HH:mm' }}</td></ng-container>
            <ng-container matColumnDef="progress"><th mat-header-cell *matHeaderCellDef>Refunded</th><td mat-cell *matCellDef="let s">{{ s.completed }} / {{ s.total }}@if (s.failed) { · {{ s.failed }} failed }</td></ng-container>
            <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let s">{{ s.status }}</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns"></tr>
          </table>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card { margin-bottom: 16px; }
    mat-card-header { align-items: center; }
    .spacer { flex: 1; }
    .alert { display: flex; gap: 12px; align-items: center; padding: 10px; border-left: 4px solid; margin-bottom: 8px; background: #fff; }
    .alert.high { border-color: #c62828; }
    .alert.medium { border-color: #ef6c00; }
    .alert.info { border-color: #1e88e5; }
    .severity { text-transform: uppercase; font-size: 11px; font-weight: 700; width: 60px; }
    .message { flex: 1; }
    table { width: 100%; }
  `,
})
export class AlertsComponent {
  readonly api = inject(DashboardApiService);
  readonly filters = inject(FilterService);
  readonly columns = ['event', 'started', 'progress', 'status'];

  readonly alerts = toSignal(toObservable(this.filters.params).pipe(switchMap((p) => this.api.alerts(p).pipe(catchError(() => of([]))))), { initialValue: [] });
  readonly sagas = toSignal(this.api.cancellations().pipe(catchError(() => of([]))), { initialValue: [] });
}
