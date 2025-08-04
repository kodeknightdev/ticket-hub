import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { EventsComponent } from './events.component';

const row = (title: string, revenue: number, status: string) => ({
  eventId: title, title, artist: 'x', category: 'Concert', city: 'Leeds', venue: 'Hall', startsAt: '2025-08-01T19:00:00Z',
  capacity: 100, sold: 50, liveSold: null, revenue, avgPrice: 20, daysToEvent: 10, paceStatus: status,
  projectedSellThrough: 0.8, predictedSelloutDate: null, model: 'holt-winters',
});

describe('EventsComponent', () => {
  it('loads performance rows, filters them and shows pace chips', () => {
    TestBed.configureTestingModule({
      imports: [EventsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(EventsComponent);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController)
      .expectOne((r) => r.url === '/bff/events/performance')
      .flush({ rows: [row('Neon Harbour', 5000, 'On track'), row('Quiet Night', 800, 'At risk')], partial: false });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.source.data.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('At risk');

    component.applyFilter('quiet');
    expect(component.source.filteredData.map((r) => r.title)).toEqual(['Quiet Night']);
  });
});
