import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const path = request.url.split('?')[0];
  if (!path.startsWith('/api/') || path === '/api/auth/login' || path === '/api/auth/register') {
    return next(request);
  }

  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.getAccessToken();
  const authorizedRequest = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorizedRequest).pipe(
    catchError(error => {
      if (error instanceof HttpErrorResponse && error.status === 401 && token === auth.getAccessToken()) {
        auth.clearSession();
        void router.navigate(['/login']);
      }
      return throwError(() => error);
    })
  );
};
