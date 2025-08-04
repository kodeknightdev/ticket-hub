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

  it('knows owners from organizers', async () => {
    const loading = service.load();
    http.expectOne('/bff/user').flush({ isAuthenticated: true, user: { id: '1', email: 'o@x', name: 'Olivia', role: 'Owner' } });
    await loading;
    expect(service.isOwner()).toBeTrue();

    service.user.set({ id: '2', email: 'g@x', name: 'Gary', role: 'Organizer' });
    expect(service.isOwner()).toBeFalse();
  });

  it('surfaces a 403 when a buyer tries to sign in', async () => {
    const login = service.login('buyer@x', 'pw');
    http.expectOne('/bff/login').flush({ title: 'nope' }, { status: 403, statusText: 'Forbidden' });
    await expectAsync(login).toBeRejected();
    expect(service.user()).toBeNull();
  });
});
