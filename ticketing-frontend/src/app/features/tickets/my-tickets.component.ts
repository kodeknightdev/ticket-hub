import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, input, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { MyTickets, TicketGroup } from '../../core/models';

@Component({
  selector: 'app-my-tickets',
  imports: [DatePipe, RouterLink, MatCardModule, MatChipsModule],
  template: `
    <h1>My tickets</h1>
    @if (justBought() && waiting()) {
      <p class="info">Payment received. Your tickets will appear here in a moment…</p>
    }
    @if (data(); as d) {
      @if (d.upcoming.length === 0 && d.past.length === 0) {
        <p>No tickets yet. <a routerLink="/">Browse events</a></p>
      }
      @for (group of d.upcoming; track $index) {
        <mat-card class="group">
          <mat-card-header>
            <mat-card-title>{{ group.event?.title ?? 'Event' }}</mat-card-title>
            <mat-card-subtitle>{{ group.event?.venueName }} · {{ group.event?.startsAt | date: 'EEE d MMM y, HH:mm' }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <mat-chip-set>
              @for (ticket of group.tickets; track ticket.id) {
                <mat-chip [routerLink]="['/tickets', ticket.id]" [class]="ticket.status">{{ ticket.label }} · {{ ticket.status }}</mat-chip>
              }
            </mat-chip-set>
          </mat-card-content>
        </mat-card>
      }
      @if (d.past.length > 0) {
        <h2>Past events</h2>
        @for (group of d.past; track $index) {
          <p>{{ group.event?.title }} · {{ group.tickets.length }} ticket(s)</p>
        }
      }
    }
  `,
  styles: `
    .group { margin-bottom: 16px; }
    .info { background: #e8f5e9; padding: 8px 12px; border-radius: 4px; }
    mat-chip { cursor: pointer; }
    mat-chip.Void { text-decoration: line-through; }
  `,
})
export class MyTicketsComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly justBought = input<string>();
  readonly data = signal<MyTickets | null>(null);
  readonly waiting = signal(true);

  ngOnInit(): void {
    this.refresh();
    if (this.justBought()) {
      // tickets are issued asynchronously after seats-sold, so poll briefly
      let tries = 0;
      this.timer = setInterval(() => {
        if (++tries > 10 || !this.waiting()) this.stop();
        else this.refresh();
      }, 1500);
    }
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private refresh(): void {
    this.api.myTickets().subscribe((d) => {
      this.data.set(d);
      const bought = this.justBought();
      if (bought && d.upcoming.some((g: TicketGroup) => g.tickets.some((t) => t.orderId === bought))) {
        this.waiting.set(false);
      }
    });
  }

  private stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
