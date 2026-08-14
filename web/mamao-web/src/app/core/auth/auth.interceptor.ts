import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { SessionService } from './session.service';

const PUBLIC_PATHS = ['/api/v1/auth/login', '/api/v1/auth/register-company', '/api/v1/auth/refresh'];

/**
 * Anexa o bearer e, num 401, tenta rotacionar o refresh token uma vez antes de derrubar a
 * sessao. Access token e curto de proposito (revogacao de acesso precisa valer rapido),
 * entao renovar em silencio e o que torna isso usavel.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);

  if (PUBLIC_PATHS.some((path) => request.url.startsWith(path))) {
    return next(request);
  }

  const withToken = (token: string | null) =>
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(withToken(session.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !session.refreshToken) {
        return throwError(() => error);
      }

      return from(session.refresh()).pipe(
        switchMap((renovou) =>
          renovou ? next(withToken(session.accessToken)) : throwError(() => error),
        ),
      );
    }),
  );
};
