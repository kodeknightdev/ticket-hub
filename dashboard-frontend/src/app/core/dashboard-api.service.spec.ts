import { HttpParams, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DashboardApiService } from './dashboard-api.service';

describe('DashboardApiService', () => {
  let api: DashboardApiService;
  let http: HttpTestingController;
  const params = new HttpParams().set('from', '2025-06-01').set('to', '2025-06-30');

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(DashboardApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('passes filters and the breakdown dimension to the BFF', () => {
    api.breakdown(params, 'city').subscribe();
    const req = http.expectOne((r) => r.url === '/bff/breakdowns');
    expect(req.request.params.get('by')).toBe('city');
    expect(req.request.params.get('from')).toBe('2025-06-01');
    req.flush({ by: 'city', rows: [] });
  });

  it('asks for the right forecast horizon', () => {
    api.revenueForecast(params, 60).subscribe();
    const req = http.expectOne((r) => r.url === '/bff/forecast/revenue');
    expect(req.request.params.get('horizon')).toBe('60');
    req.flush({});
  });

  it('builds export links with the current filters', () => {
    expect(api.exportUrl('performance', params)).toBe('/bff/export/performance.csv?from=2025-06-01&to=2025-06-30');
  });
});
