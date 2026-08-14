import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import type { ApiProblem, TenantOption } from '../../core/http/api.types';

@Component({
  selector: 'mamao-login',
  imports: [ReactiveFormsModule],
  template: `
    <div class="wrap">
      <div class="card panel">
        <div class="brand">
          <span class="brand__mark">ã</span>
          <div>
            <div class="brand__name">mamão</div>
            <small class="muted">gestão sem complicação</small>
          </div>
        </div>

        @if (erro(); as problema) {
          <div class="alert alert--danger">{{ problema.detail }}</div>
        }

        @if (empresas().length > 0) {
          <p class="muted">Seu acesso vale para mais de uma empresa. Escolha uma:</p>
          @for (empresa of empresas(); track empresa.tenantId) {
            <button type="button" class="btn btn--ghost empresa" (click)="entrarNaEmpresa(empresa.tenantId)">
              {{ empresa.name }} <span class="muted">· {{ empresa.role }}</span>
            </button>
          }
        } @else {
          <form [formGroup]="form" (ngSubmit)="entrar()">
            <div class="field" [class.field--invalid]="invalido('email')">
              <label for="email">E-mail</label>
              <input id="email" type="email" formControlName="email" autocomplete="username" />
              @if (invalido('email')) {
                <span class="field__error">Informe um e-mail válido.</span>
              }
            </div>

            <div class="field" [class.field--invalid]="invalido('password')">
              <label for="password">Senha</label>
              <input id="password" type="password" formControlName="password" autocomplete="current-password" />
              @if (invalido('password')) {
                <span class="field__error">Informe sua senha.</span>
              }
            </div>

            <button type="submit" class="btn btn--primary full" [disabled]="enviando()">
              {{ enviando() ? 'Entrando…' : 'Entrar' }}
            </button>
          </form>

          <p class="muted rodape">
            Primeira vez? <a href="#" (click)="irParaCadastro($event)">Cadastre sua empresa</a>
          </p>
        }
      </div>
    </div>
  `,
  styles: `
    .wrap { align-items: center; display: flex; justify-content: center; min-height: 100vh; padding: var(--space-4); }
    .panel { padding: var(--space-6); width: 100%; max-width: 380px; }
    .brand { align-items: center; display: flex; gap: var(--space-3); margin-bottom: var(--space-5); }
    .brand__mark { color: var(--mamao-yellow-500); font-family: var(--font-display); font-size: 40px; line-height: 1; }
    .brand__name { font-family: var(--font-display); font-size: 26px; line-height: 1; }
    .full { width: 100%; }
    .empresa { margin-bottom: var(--space-2); width: 100%; }
    .rodape { font-size: 13px; margin-bottom: 0; margin-top: var(--space-4); text-align: center; }
  `,
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  readonly enviando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);
  readonly empresas = signal<TenantOption[]>([]);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  invalido(campo: 'email' | 'password'): boolean {
    const control = this.form.controls[campo];
    return control.invalid && (control.dirty || control.touched);
  }

  async entrar(tenantId?: string): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      const { email, password } = this.form.getRawValue();
      const opcoes = await this.session.login(email, password, tenantId);

      if (opcoes) {
        this.empresas.set(opcoes);
        return;
      }

      await this.router.navigate(['/inicio']);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.enviando.set(false);
    }
  }

  entrarNaEmpresa(tenantId: string): Promise<void> {
    return this.entrar(tenantId);
  }

  irParaCadastro(evento: Event): void {
    evento.preventDefault();
    void this.router.navigate(['/cadastrar-empresa']);
  }
}
