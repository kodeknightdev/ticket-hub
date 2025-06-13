import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { EventPage } from '../../core/models';
import { SeatingHubService, SeatsChanged } from '../../core/seating-hub.service';
import { EventPageComponent } from './event-page.component';

const page: EventPage = {
  event: { id: 'e1', title: 'Gig', artist: 'Band', category: 'Concert', city: 'Leeds', venueName: 'Hall', startsAt: '2025-08-01T19:00:00Z', coverImageUrl: null, description: null, status: 'Published' },
  tiers: [{ id: 't1', name: 'Standard', price: 50, basePrice: 50 }],
  sections: [{ id: 's1', name: 'Floor', rows: [{ row: 'A', seats: [{ id: 'a1', number: 1, tierId: 't1', state: 'available' }, { id: 'a2', number: 2, tierId: 't1', state: 'available' }] }] }],
  availability: { capacity: 2, available: 2, held: 0, sold: 0 },
  partial: false,
  failures: [],
};

describe('EventPageComponent', () => {
  const seats$ = new Subject<SeatsChanged>();

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: SeatingHubService, useValue: { seats$, prices$: new Subject(), join: () => Promise.resolve(), leave: () => Promise.resolve() } },
      ],
    });
  });

  it('applies live seat updates and drops seats someone else just took', () => {
    const fixture = TestBed.createComponent(EventPageComponent);
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/bff/events/e1/page').flush(page);

    const component = fixture.componentInstance;
    component.selected.set(['a1', 'a2']);
    expect(component.selectedTotal()).toBe(100);

    seats$.next({ eventId: 'e1', seatIds: ['a2'], state: 'held' });
    expect(component.selected()).toEqual(['a1']);
    expect(component.page()!.sections[0].rows[0].seats[1].state).toBe('held');

    seats$.next({ eventId: 'other', seatIds: ['a1'], state: 'sold' });
    expect(component.page()!.sections[0].rows[0].seats[0].state).toBe('available');
  });
});
