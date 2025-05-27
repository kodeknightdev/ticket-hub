import { HttpInterceptorFn } from '@angular/common/http';

const SAFE = ['GET', 'HEAD', 'OPTIONS'];

/** The BFF rejects state-changing calls without this header; a cross-site form post can't add it. */
export const csrfInterceptor: HttpInterceptorFn = (req, next) => {
  if (SAFE.includes(req.method) || !(req.url.startsWith('/bff') || req.url.startsWith('/api'))) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { 'X-CSRF': '1' } }));
};
