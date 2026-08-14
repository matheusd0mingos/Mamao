import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { ApiProblem } from '../../core/http/api.types';

@Component({
  selector: 'mamao-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="wrap">
      <div class="card panel">
        <h1>Nova senha</h1>

        @if (concluido()) {
          <p>Senha alterada. Você já pode entrar com ela.</p>
          <a class="btn btn--primary full" routerLink="/entrar">Ir para o login</a>
        } @else if (!temLink()) {
          <div class="alert alert--danger">
            Este link está incompleto. Peça um novo em "Esqueci minha senha".
          </div>
          <a class="btn btn--ghost full" routerLink="/esqueci-minha-senha">Pedir novo link</a>
        } @else {
          <p class="muted">Escolha uma senha nova para <strong>{{ email() }}</strong>.</p>

          @if (erro(); as problema) {
            <div class="alert alert--danger">{{ problema.detail }}</div>
          }

          <form [formGroup]="form" (ngSubmit)="redefinir()">
            <div class="field" [class.field--invalid]="!!erroDoCampo('newPassword')">
              <label for="newPassword">Nova senha</label>
              <input id="newPassword" type="password" formControlName="newPassword" autocomplete="new-password" />
              <small class="muted">Ao menos 10 caracteres.</small>
              @if (erroDoCampo('newPassword'); as msg) {
                <span class="field__error">{{ msg }}</span>
              }
            </div>

            <button type="submit" class="btn btn--primary full" [disabled]="enviando()">
              {{ enviando() ? 'Salvando…' : 'Salvar nova senha' }}
            </button>
          </form>
        }
      </div>
    </div>
  `,
  styles: `
    .wrap { align-items: center; display: flex; justify-content: center; min-height: 100vh; padding: var(--space-4); }
    .panel { padding: var(--space-6); width: 100%; max-width: 400px; }
    .panel h1 { margin-bottom: var(--space-2); }
    .full { width: 100%; }
  `,
})
export class ResetPasswordPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly email = signal('');
  private readonly token = signal('');
  readonly enviando = signal(false);
  readonly concluido = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(10)]],
  });

  temLink(): boolean {
    return this.email() !== '' && this.token() !== '';
  }

  ngOnInit(): void {
    const parametros = this.route.snapshot.queryParamMap;
    this.email.set(parametros.get('email') ?? '');
    this.token.set(parametros.get('token') ?? '');
  }

  erroDoCampo(campo: string): string | null {
    const doServidor = this.erro()?.fieldErrors?.[campo]?.[0];
    if (doServidor) return doServidor;

    const control = this.form.get(campo);
    return control?.invalid && (control.dirty || control.touched)
      ? 'Use ao menos 10 caracteres.'
      : null;
  }

  async redefinir(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      await firstValueFrom(
        this.http.post('/api/v1/auth/reset-password', {
          email: this.email(),
          token: this.token(),
          newPassword: this.form.controls.newPassword.value,
        }),
      );
      this.concluido.set(true);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.enviando.set(false);
    }
  }

  irParaLogin(): void {
    void this.router.navigate(['/entrar']);
  }
}
