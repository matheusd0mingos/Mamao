import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { ApiProblem } from '../../core/http/api.types';

@Component({
  selector: 'mamao-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="wrap">
      <div class="card panel">
        <h1>Recuperar acesso</h1>

        @if (enviado()) {
          <p>
            Se existir uma conta com <strong>{{ form.controls.email.value }}</strong>,
            enviamos um link para você escolher uma nova senha.
          </p>
          <p class="muted">
            O link vale por algumas horas. Confira também a caixa de spam.
          </p>
          <a class="btn btn--ghost full" routerLink="/entrar">Voltar para o login</a>
        } @else {
          <p class="muted">
            Informe seu e-mail e enviaremos um link para redefinir a senha.
          </p>

          @if (erro(); as problema) {
            <div class="alert alert--danger">{{ problema.detail }}</div>
          }

          <form [formGroup]="form" (ngSubmit)="enviar()">
            <div class="field">
              <label for="email">E-mail</label>
              <input id="email" type="email" formControlName="email" autocomplete="username" />
            </div>

            <button type="submit" class="btn btn--primary full" [disabled]="enviando()">
              {{ enviando() ? 'Enviando…' : 'Enviar link' }}
            </button>
          </form>

          <p class="muted rodape"><a routerLink="/entrar">Voltar para o login</a></p>
        }
      </div>
    </div>
  `,
  styles: `
    .wrap { align-items: center; display: flex; justify-content: center; min-height: 100vh; padding: var(--space-4); }
    .panel { padding: var(--space-6); width: 100%; max-width: 400px; }
    .panel h1 { margin-bottom: var(--space-2); }
    .full { width: 100%; }
    .rodape { font-size: 13px; margin-bottom: 0; margin-top: var(--space-4); text-align: center; }
  `,
})
export class ForgotPasswordPage {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);

  readonly enviando = signal(false);
  readonly enviado = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  async enviar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      await firstValueFrom(
        this.http.post('/api/v1/auth/forgot-password', this.form.getRawValue()),
      );
      // O servidor responde igual existindo ou nao a conta, e a tela faz o mesmo:
      // dizer "e-mail nao encontrado" entregaria quem tem cadastro.
      this.enviado.set(true);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.enviando.set(false);
    }
  }
}
