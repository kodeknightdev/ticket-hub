import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';
import { AuthService } from './core/auth.service';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('offers sign in when anonymous', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('ticket-hub');
    expect(text).toContain('Sign in');
    expect(text).not.toContain('Scan');
  });

  it('shows organiser and scanner links for an organizer', () => {
    TestBed.inject(AuthService).user.set({ id: '1', email: 'o@x', name: 'Riverside Live', role: 'Organizer' });
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Organise');
    expect(text).toContain('Scan');
    expect(text).toContain('Riverside Live');
  });
});
