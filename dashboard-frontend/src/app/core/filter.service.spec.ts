import { TestBed } from '@angular/core/testing';
import { FilterService } from './filter.service';

describe('FilterService', () => {
  let filters: FilterService;

  beforeEach(() => {
    filters = TestBed.inject(FilterService);
  });

  it('always sends a date range and granularity', () => {
    filters.setRange(new Date(2025, 5, 1), new Date(2025, 5, 30));
    const params = filters.params();
    expect(params.get('from')).toBe('2025-06-01');
    expect(params.get('to')).toBe('2025-06-30');
    expect(params.get('granularity')).toBe('week');
    expect(params.has('city')).toBeFalse();
  });

  it('only includes the filters that are set', () => {
    filters.city.set('Leeds');
    filters.category.set('Comedy');
    expect(filters.params().get('city')).toBe('Leeds');
    expect(filters.queryString()).toContain('category=Comedy');
    filters.reset();
    expect(filters.params().has('city')).toBeFalse();
  });

  it('picks a sensible granularity for presets', () => {
    filters.preset(30);
    expect(filters.granularity()).toBe('day');
    filters.preset(365);
    expect(filters.granularity()).toBe('month');
  });
});
