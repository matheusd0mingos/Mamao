import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import ptBr from '@angular/common/locales/pt';
import {
  ApplicationConfig,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { problemInterceptor } from './core/http/problem.interceptor';

// Locale registrado uma vez; nada de formatar data a mao.
registerLocaleData(ptBr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    // A ordem importa: problemInterceptor traduz o erro que o authInterceptor deixar passar.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, problemInterceptor])),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ],
};
