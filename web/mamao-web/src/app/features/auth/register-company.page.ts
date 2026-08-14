import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import type { ApiProblem } from '../../core/http/api.types';

/**
 * Cadastro self-service da empresa. Pede o minimo: o caminho ate o primeiro dado real
 * precisa caber em 15 minutos, e formulario longo na primeira tela e abandono.
 */
@Component({
  selector: 'mamao-register-company',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="wrap">
      <div class="card panel">
        <h1>Criar sua empresa</h1>
        <p class="muted">Leva menos de um minuto. Você já entra com tudo pronto.</p>

        @if (erro(); as problema) {
          <div class="alert alert--danger">{{ problema.detail }}</div>
        }

        <form [formGroup]="form" (ngSubmit)="cadastrar()">
          <div class="field">
            <label for="companyName">Nome da empresa</label>
            <input id="companyName" formControlName="companyName" />
            @if (erroDoCampo('companyName'); as msg) {
              <span class="field__error">{{ msg }}</span>
            }
          </div>

          <div class="field">
            <label for="fullName">Seu nome</label>
            <input id="fullName" formControlName="fullName" />
          </div>

          <div class="field">
            <label for="email">Seu e-mail</label>
            <input id="email" type="email" formControlName="email" autocomplete="username" />
            @if (erroDoCampo('email'); as msg) {
              <span class="field__error">{{ msg }}</span>
            }
          </div>

          <div class="field">
            <label for="password">Senha</label>
            <input id="password" type="password" formControlName="password" autocomplete="new-password" />
            <small class="muted">Ao menos 10 caracteres.</small>
            @if (erroDoCampo('password'); as msg) {
              <span class="field__error">{{ msg }}</span>
            }
          </div>

          <button type="submit" class="btn btn--primary full" [disabled]="enviando()">
            {{ enviando() ? 'Criando…' : 'Criar empresa' }}
          </button>
        </form>

        <p class="muted rodape">Já tem conta? <a routerLink="/entrar">Entrar</a></p>
      </div>
    </div>
  `,
  styles: `
    .wrap { align-items: center; display: flex; justify-content: center; min-height: 100vh; padding: var(--space-4); }
    .panel { padding: var(--space-6); width: 100%; max-width: 420px; }
    .panel h1 { margin-bottom: var(--space-1); }
    .full { width: 100%; }
    .rodape { font-size: 13px; margin-bottom: 0; margin-top: var(--space-4); text-align: center; }
  `,
})
export class RegisterCompanyPage {
  private readonly fb = inject(FormBuilder);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  readonly enviando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  readonly form = this.fb.nonNullable.group({
    companyName: ['', [Validators.required]],
    fullName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  /** Erro de servidor por campo vem em `fieldErrors` e cai direto no input certo. */
  erroDoCampo(campo: string): string | null {
    return this.erro()?.fieldErrors?.[campo]?.[0] ?? null;
  }

  async cadastrar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      const { companyName, fullName, email, password } = this.form.getRawValue();
      await this.session.registerCompany(companyName, fullName, email, password);
      await this.router.navigate(['/inicio']);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.enviando.set(false);
    }
  }
}
