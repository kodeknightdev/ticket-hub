import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { Role } from './models';

export const requireRole = (...roles: Role[]): CanActivateFn => async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.loaded()) {
    await auth.load();
  }
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  return roles.length === 0 || auth.hasRole(...roles) ? true : router.createUrlTree(['/']);
};
