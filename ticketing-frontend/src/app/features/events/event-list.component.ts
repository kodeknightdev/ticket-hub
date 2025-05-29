import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { ApiService, EventSearch } from '../../core/api.service';
import { EventSummary } from '../../core/models';

@Component({
  selector: 'app-event-list',
  imports: [FormsModule, RouterLink, DatePipe, CurrencyPipe, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatPaginatorModule],
  template: `
    <section class="filters">
      <mat-form-field appearance="outline">
        <mat-label>Search artist or title</mat-label>
        <input matInput [(ngModel)]="query" (keyup.enter)="search(1)" name="q" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>City</mat-label>
        <mat-select [(ngModel)]="city" (selectionChange)="search(1)" name="city">
          <mat-option value="">Any city</mat-option>
          @for (c of cities(); track c) {
            <mat-option [value]="c">{{ c }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Category</mat-label>
        <mat-select [(ngModel)]="category" (selectionChange)="search(1)" name="category">
          <mat-option value="">Any category</mat-option>
          @for (c of categories(); track c) {
            <mat-option [value]="c">{{ c }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>From</mat-label>
        <input matInput type="date" [(ngModel)]="from" (change)="search(1)" name="from" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>To</mat-label>
        <input matInput type="date" [(ngModel)]="to" (change)="search(1)" name="to" />
      </mat-form-field>
    </section>

    @if (loading()) {
      <p>Loading events…</p>
    } @else if (events().length === 0) {
      <p class="empty">No events match those filters.</p>
    }

    <section class="grid">
      @for (event of events(); track event.id) {
        <mat-card class="event" [routerLink]="['/events', event.id]">
          @if (event.coverImageUrl) {
            <img mat-card-image [src]="event.coverImageUrl" [alt]="event.title" loading="lazy" />
          }
          <mat-card-header>
            <mat-card-title>{{ event.title }}</mat-card-title>
            <mat-card-subtitle>{{ event.venueName }}, {{ event.city }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <p>{{ event.startsAt | date: 'EEE d MMM, HH:mm' }}</p>
            <p class="price">from {{ event.fromPrice | currency: 'GBP' }}</p>
          </mat-card-content>
        </mat-card>
      }
    </section>

    <mat-paginator [length]="total()" [pageIndex]="page() - 1" [pageSize]="pageSize" [pageSizeOptions]="[12, 24]" (page)="onPage($event)" />
  `,
  styles: `
    .filters { display: flex; flex-wrap: wrap; gap: 12px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 16px; margin: 8px 0 16px; }
    .event { cursor: pointer; }
    .event img { height: 140px; object-fit: cover; }
    .price { font-weight: 600; }
    .empty { color: #666; }
  `,
})
export class EventListComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly events = signal<EventSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly categories = signal<string[]>([]);
  readonly cities = signal<string[]>([]);
  pageSize = 12;
  query = '';
  city = '';
  category = '';
  from = '';
  to = '';

  ngOnInit(): void {
    this.api.categories().subscribe((c) => this.categories.set(c));
    this.api.venues().subscribe((v) => this.cities.set([...new Set(v.map((x) => x.city))].sort()));
    this.search(1);
  }

  search(page: number): void {
    const search: EventSearch = {
      q: this.query.trim(),
      city: this.city,
      category: this.category,
      from: this.from ? new Date(this.from).toISOString() : undefined,
      to: this.to ? new Date(`${this.to}T23:59:59`).toISOString() : undefined,
      page,
      pageSize: this.pageSize,
    };
    this.loading.set(true);
    this.api.searchEvents(search).subscribe({
      next: (result) => {
        this.events.set(result.items);
        this.total.set(result.total);
        this.page.set(result.page);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onPage(event: PageEvent): void {
    this.pageSize = event.pageSize;
    this.search(event.pageIndex + 1);
  }
}
