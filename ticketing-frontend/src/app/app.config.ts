import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { AuthService } from './core/auth.service';
import { csrfInterceptor } from './core/csrf.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withInterceptors([csrfInterceptor]),
      // Cookie name is per app: both frontends live on localhost and cookies ignore ports.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN-APP', headerName: 'X-XSRF-TOKEN' }),
    ),
    provideAnimationsAsync(),
    provideAppInitializer(() => inject(AuthService).load()),
  ],
};
