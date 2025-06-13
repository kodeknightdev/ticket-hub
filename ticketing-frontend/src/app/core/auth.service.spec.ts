import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the session from the BFF', async () => {
    const loading = service.load();
    http.expectOne('/bff/user').flush({ isAuthenticated: true, user: { id: '1', email: 'b@x', name: 'Bea', role: 'Buyer' } });
    await loading;
    expect(service.isAuthenticated()).toBeTrue();
    expect(service.user()?.role).toBe('Buyer');
    expect(service.loaded()).toBeTrue();
  });

  it('treats a failed session call as signed out', async () => {
    const loading = service.load();
    http.expectOne('/bff/user').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await loading;
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('logs in and out through the BFF without ever seeing a token', async () => {
    const login = service.login('b@x', 'pw');
    const req = http.expectOne('/bff/login');
    expect(req.request.body).toEqual({ email: 'b@x', password: 'pw' });
    req.flush({ id: '1', email: 'b@x', name: 'Bea', role: 'Buyer' });
    await login;
    expect(service.user()?.name).toBe('Bea');

    const logout = service.logout();
    http.expectOne('/bff/logout').flush(null);
    await logout;
    expect(service.user()).toBeNull();
  });

  it('lets admins through every role check', () => {
    service.user.set({ id: '1', email: 'a@x', name: 'Ada', role: 'Admin' });
    expect(service.hasRole('Staff')).toBeTrue();
  });
});
