import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { SessionService } from './session.service';

export const authGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.isAuthenticated() ? true : inject(Router).createUrlTree(['/entrar']);
};

/**
 * A primeira tela DEPENDE do papel.
 *
 * O gerente de TI nao ve disponibilidade, entao o painel "Hoje" nao e a casa dele — a tela
 * de acessos e. Mandar todo mundo para /inicio faria a pessoa cair numa tela que o guarda
 * recusa e voltar para /inicio: laco de redirecionamento na primeira vez que ela entra.
 */
export function rotaInicial(session: SessionService): string {
  if (session.has('availability.read')) return '/inicio';
  if (session.has('users.invite')) return '/acessos';
  if (session.has('people.read')) return '/pessoas';
  return '/entrar';
}

/** Esconde a rota sem permissao. O endpoint correspondente tambem verifica — sempre. */
export const permissionGuard =
  (permission: string): CanMatchFn =>
  () => {
    const session = inject(SessionService);
    if (session.has(permission)) return true;

    return inject(Router).parseUrl(rotaInicial(session));
  };
