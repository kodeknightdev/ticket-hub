import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { csrfInterceptor } from './csrf.interceptor';

describe('csrfInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([csrfInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  it('adds X-CSRF to state-changing BFF calls', () => {
    http.post('/api/seating/holds', {}).subscribe();
    expect(controller.expectOne('/api/seating/holds').request.headers.get('X-CSRF')).toBe('1');
  });

  it('leaves reads and third-party calls alone', () => {
    http.get('/bff/user').subscribe();
    http.post('https://api.stripe.com/x', {}).subscribe();
    expect(controller.expectOne('/bff/user').request.headers.has('X-CSRF')).toBeFalse();
    expect(controller.expectOne('https://api.stripe.com/x').request.headers.has('X-CSRF')).toBeFalse();
  });
});
