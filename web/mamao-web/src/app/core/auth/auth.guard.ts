import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { SessionService } from './session.service';

export const authGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.isAuthenticated() ? true : inject(Router).createUrlTree(['/entrar']);
};

/** Esconde a rota sem permissao. O endpoint correspondente tambem verifica — sempre. */
export const permissionGuard =
  (permission: string): CanMatchFn =>
  () => {
    const session = inject(SessionService);
    return session.has(permission) ? true : inject(Router).createUrlTree(['/']);
  };
