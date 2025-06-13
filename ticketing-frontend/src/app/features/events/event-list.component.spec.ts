import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { EventListComponent } from './event-list.component';

describe('EventListComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventListComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('loads the first page and renders cards', () => {
    const fixture = TestBed.createComponent(EventListComponent);
    fixture.detectChanges();

    http.expectOne('/api/categories').flush(['Concert']);
    http.expectOne('/api/venues').flush([{ id: 'v', name: 'Riverside Arena', city: 'Manchester' }]);
    const search = http.expectOne((r) => r.url === '/api/events');
    expect(search.request.params.get('page')).toBe('1');
    search.flush({
      items: [{ id: 'e1', title: 'Neon Harbour Live', venueName: 'Riverside Arena', city: 'Manchester', startsAt: '2025-07-01T19:00:00Z', fromPrice: 35, coverImageUrl: null }],
      page: 1, pageSize: 12, total: 1,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Neon Harbour Live');
    expect(fixture.componentInstance.cities()).toEqual(['Manchester']);
  });

  it('sends filters to the API', () => {
    const fixture = TestBed.createComponent(EventListComponent);
    const component = fixture.componentInstance;
    component.city = 'Leeds';
    component.category = 'Comedy';
    component.search(2);

    const req = http.expectOne((r) => r.url === '/api/events');
    expect(req.request.params.get('city')).toBe('Leeds');
    expect(req.request.params.get('category')).toBe('Comedy');
    expect(req.request.params.get('page')).toBe('2');
  });
});
