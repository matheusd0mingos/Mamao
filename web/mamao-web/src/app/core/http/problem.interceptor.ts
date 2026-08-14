import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, from, switchMap, throwError } from 'rxjs';
import type { ApiProblem } from './api.types';

/**
 * Traduz ProblemDetails para uma forma unica. O formulario consome `fieldErrors`
 * diretamente — validacao de servidor aparecendo no campo certo, sem codigo por tela.
 */
export const problemInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      // Em `responseType: 'blob'` (download de modelo, documento, PDF de escala) o corpo do
      // ERRO tambem chega como Blob — o ProblemDetails vem dentro dele. Sem desembrulhar,
      // toda falha de download viraria "Erro inesperado" e esconderia o motivo real.
      if (error.error instanceof Blob) {
        return from(error.error.text()).pipe(
          switchMap((texto) => throwError(() => traduzir(error, parseOuVazio(texto)))),
        );
      }

      return throwError(() => traduzir(error, (error.error ?? {}) as Partial<ApiProblem>));
    }),
  );

function parseOuVazio(texto: string): Partial<ApiProblem> {
  try {
    return JSON.parse(texto) as Partial<ApiProblem>;
  } catch {
    return {};
  }
}

function traduzir(error: HttpErrorResponse, body: Partial<ApiProblem>): ApiProblem {
  return {
    status: error.status,
    title: body.title ?? (error.status === 0 ? 'Sem conexao' : 'Erro inesperado'),
    detail:
      body.detail ??
      (error.status === 0
        ? 'Nao foi possivel falar com o servidor. Verifique sua conexao.'
        : 'Tente novamente. Se persistir, informe o codigo de rastreio.'),
    code: body.code,
    traceId: body.traceId,
    fieldErrors: body.fieldErrors,
  };
}
