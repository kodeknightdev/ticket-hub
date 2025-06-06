import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Ticket } from '../../core/models';

@Component({
  selector: 'app-ticket-detail',
  imports: [DatePipe, RouterLink, MatCardModule],
  template: `
    <a routerLink="/tickets">← My tickets</a>
    @if (ticket(); as t) {
      <mat-card class="ticket">
        <mat-card-header>
          <mat-card-title>{{ t.label }}</mat-card-title>
          <mat-card-subtitle>Status: {{ t.status }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (t.qrPngBase64) {
            <img class="qr" [src]="'data:image/png;base64,' + t.qrPngBase64" alt="Ticket QR code" />
          } @else {
            <p>This ticket has been voided.</p>
          }
          <code>{{ t.code }}</code>
          <p>Issued {{ t.issuedAt | date: 'd MMM y, HH:mm' }}</p>
          @if (t.checkedInAt) {
            <p>Checked in {{ t.checkedInAt | date: 'd MMM y, HH:mm' }}</p>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .ticket { max-width: 380px; margin: 16px auto; text-align: center; }
    .qr { width: 260px; height: 260px; image-rendering: pixelated; }
    code { display: block; font-size: 11px; word-break: break-all; color: #555; margin: 8px 0; }
  `,
})
export class TicketDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly id = input.required<string>();
  readonly ticket = signal<Ticket | null>(null);

  ngOnInit(): void {
    this.api.ticket(this.id()).subscribe((t) => this.ticket.set(t));
  }
}
