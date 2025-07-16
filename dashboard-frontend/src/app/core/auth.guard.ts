import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const signedIn: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  if (!auth.loaded()) {
    await auth.load();
  }
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
