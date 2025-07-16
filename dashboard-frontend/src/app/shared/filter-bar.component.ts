import { Component, OnInit, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { AuthService } from '../core/auth.service';
import { DashboardApiService } from '../core/dashboard-api.service';
import { FilterService, Granularity } from '../core/filter.service';
import { FilterOptions } from '../core/models';

@Component({
  selector: 'app-filter-bar',
  providers: [provideNativeDateAdapter()],
  imports: [FormsModule, MatFormFieldModule, MatDatepickerModule, MatSelectModule, MatButtonModule, MatButtonToggleModule],
  template: `
    <div class="bar">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Date range</mat-label>
        <mat-date-range-input [rangePicker]="picker">
          <input matStartDate [ngModel]="filters.from()" (dateChange)="$event.value && filters.from.set($event.value)" placeholder="From" />
          <input matEndDate [ngModel]="filters.to()" (dateChange)="$event.value && filters.to.set($event.value)" placeholder="To" />
        </mat-date-range-input>
        <mat-datepicker-toggle matIconSuffix [for]="picker" />
        <mat-date-range-picker #picker />
      </mat-form-field>
      <div class="presets">
        <button mat-button (click)="filters.preset(30)">30d</button>
        <button mat-button (click)="filters.preset(90)">90d</button>
        <button mat-button (click)="filters.preset(365)">12m</button>
      </div>
      <mat-button-toggle-group [value]="filters.granularity()" (change)="filters.granularity.set($event.value)" hideSingleSelectionIndicator>
        @for (g of granularities; track g) {
          <mat-button-toggle [value]="g">{{ g }}</mat-button-toggle>
        }
      </mat-button-toggle-group>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>City</mat-label>
        <mat-select [value]="filters.city()" (selectionChange)="filters.city.set($event.value)">
          <mat-option value="">All</mat-option>
          @for (c of options().cities; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Category</mat-label>
        <mat-select [value]="filters.category()" (selectionChange)="filters.category.set($event.value)">
          <mat-option value="">All</mat-option>
          @for (c of options().categories; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Venue</mat-label>
        <mat-select [value]="filters.venue()" (selectionChange)="filters.venue.set($event.value)">
          <mat-option value="">All</mat-option>
          @for (v of options().venues; track v.id) { <mat-option [value]="v.id">{{ v.name }}</mat-option> }
        </mat-select>
      </mat-form-field>
      @if (auth.isOwner()) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Organizer</mat-label>
          <mat-select [value]="filters.organizerId()" (selectionChange)="filters.organizerId.set($event.value)">
            <mat-option value="">All</mat-option>
            @for (o of options().organizers; track o) { <mat-option [value]="o">{{ o.slice(0, 8) }}…</mat-option> }
          </mat-select>
        </mat-form-field>
      }
      <button mat-stroked-button (click)="filters.reset()">Reset</button>
    </div>
  `,
  styles: `
    .bar { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 16px; }
    .presets button { min-width: 0; }
  `,
})
export class FilterBarComponent implements OnInit {
  readonly filters = inject(FilterService);
  readonly auth = inject(AuthService);
  private readonly api = inject(DashboardApiService);

  readonly granularities: Granularity[] = ['day', 'week', 'month'];
  readonly options = signal<FilterOptions>({ cities: [], categories: [], venues: [], organizers: [] });

  constructor() {
    effect(() => {
      if (!this.auth.isAuthenticated()) this.options.set({ cities: [], categories: [], venues: [], organizers: [] });
    });
  }

  ngOnInit(): void {
    this.api.filters(this.filters.params()).subscribe((o) => this.options.set(o));
  }
}
