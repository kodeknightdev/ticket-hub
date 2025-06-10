import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { EventSummary } from '../../core/models';

@Component({
  selector: 'app-organizer-events',
  imports: [DatePipe, RouterLink, MatTableModule, MatButtonModule],
  template: `
    <div class="head">
      <h1>My events</h1>
      <a mat-flat-button color="primary" routerLink="/organizer/new">New event</a>
    </div>
    <table mat-table [dataSource]="events()">
      <ng-container matColumnDef="title">
        <th mat-header-cell *matHeaderCellDef>Event</th>
        <td mat-cell *matCellDef="let e"><a [routerLink]="['/events', e.id]">{{ e.title }}</a></td>
      </ng-container>
      <ng-container matColumnDef="when">
        <th mat-header-cell *matHeaderCellDef>Date</th>
        <td mat-cell *matCellDef="let e">{{ e.startsAt | date: 'd MMM y, HH:mm' }}</td>
      </ng-container>
      <ng-container matColumnDef="venue">
        <th mat-header-cell *matHeaderCellDef>Venue</th>
        <td mat-cell *matCellDef="let e">{{ e.venueName }}, {{ e.city }}</td>
      </ng-container>
      <ng-container matColumnDef="capacity">
        <th mat-header-cell *matHeaderCellDef>Seats</th>
        <td mat-cell *matCellDef="let e">{{ e.capacity }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let e">{{ e.status }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let e">
          @if (e.status === 'Draft') {
            <button mat-button color="primary" (click)="publish(e)">Publish</button>
          }
          @if (e.status !== 'Cancelled') {
            <button mat-button color="warn" (click)="cancel(e)">Cancel</button>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `.head { display: flex; justify-content: space-between; align-items: center; } table { width: 100%; }`,
})
export class OrganizerEventsComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly events = signal<EventSummary[]>([]);
  readonly columns = ['title', 'when', 'venue', 'capacity', 'status', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.myEvents().subscribe((events) => this.events.set(events));
  }

  publish(event: EventSummary): void {
    this.api.publish(event.id).subscribe(() => this.load());
  }

  cancel(event: EventSummary): void {
    const reason = prompt(`Cancel "${event.title}"? Every paid order will be refunded. Reason:`, 'Cancelled by organiser');
    if (reason !== null) this.api.cancel(event.id, reason).subscribe(() => this.load());
  }
}
