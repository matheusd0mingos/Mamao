import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
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

      const body = (error.error ?? {}) as Partial<ApiProblem>;

      const problem: ApiProblem = {
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

      return throwError(() => problem);
    }),
  );
