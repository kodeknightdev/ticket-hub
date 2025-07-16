import { HttpParams } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { format, subDays } from 'date-fns';

export type Granularity = 'day' | 'week' | 'month';

/** Dashboard-wide filters. Every page reads params() so a change re-queries everything consistently. */
@Injectable({ providedIn: 'root' })
export class FilterService {
  readonly from = signal<Date>(subDays(new Date(), 89));
  readonly to = signal<Date>(new Date());
  readonly granularity = signal<Granularity>('week');
  readonly city = signal('');
  readonly category = signal('');
  readonly venue = signal('');
  readonly organizerId = signal('');

  readonly params = computed(() => {
    let params = new HttpParams()
      .set('from', format(this.from(), 'yyyy-MM-dd'))
      .set('to', format(this.to(), 'yyyy-MM-dd'))
      .set('granularity', this.granularity());
    for (const [key, value] of [['city', this.city()], ['category', this.category()], ['venue', this.venue()], ['organizerId', this.organizerId()]]) {
      if (value) params = params.set(key, value);
    }
    return params;
  });

  readonly queryString = computed(() => this.params().toString());

  setRange(from: Date, to: Date): void {
    this.from.set(from);
    this.to.set(to);
  }

  preset(days: number): void {
    this.setRange(subDays(new Date(), days - 1), new Date());
    this.granularity.set(days <= 31 ? 'day' : days <= 180 ? 'week' : 'month');
  }

  reset(): void {
    this.preset(90);
    this.city.set('');
    this.category.set('');
    this.venue.set('');
    this.organizerId.set('');
  }
}
