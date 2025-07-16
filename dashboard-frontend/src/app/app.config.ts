import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideEchartsCore } from 'ngx-echarts';
import { routes } from './app.routes';
import { AuthService } from './core/auth.service';
import { csrfInterceptor } from './core/csrf.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withInterceptors([csrfInterceptor]),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN-DASH', headerName: 'X-XSRF-TOKEN' }),
    ),
    provideAnimationsAsync(),
    provideEchartsCore({ echarts: () => import('echarts') }),
    provideAppInitializer(() => inject(AuthService).load()),
  ],
};
