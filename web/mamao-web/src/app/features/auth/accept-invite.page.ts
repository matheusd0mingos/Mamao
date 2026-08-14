import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApiProblem, InvitePreview } from '../../core/http/api.types';
import { SessionService } from '../../core/auth/session.service';
import { InvitesApi } from '../../features/access/invites.api';

/**
 * Aceite de convite. Pública: quem chega aqui ainda não tem conta.
 *
 * Mostra a empresa e o papel ANTES de pedir a senha. Um link pedindo senha, aberto do
 * e-mail, é indistinguível de phishing — e a pessoa tem o direito de ver onde está
 * entrando antes de digitar qualquer coisa.
 */
@Component({
  selector: 'mamao-accept-invite',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="wrap">
      <div class="card cartao">
        <div class="marca">
          <span class="til">ã</span>
          <div>
            <strong>mamão</strong>
            <small class="muted">gestão sem complicação</small>
          </div>
        </div>

        @if (carregando()) {
          <p class="muted">Verificando seu convite…</p>
        } @else if (convite(); as c) {
          <p class="chamada">
            Olá, <strong>{{ c.fullName }}</strong>. Você foi convidado para acessar o Mamão pela
            empresa <strong>{{ c.companyName }}</strong>, como {{ rotuloDoPapel(c.role) }}.
          </p>
          <p class="muted email">{{ c.email }}</p>

          @if (erro(); as problema) {
            <div class="alert alert--danger">{{ problema.detail }}</div>
          }

          <form [formGroup]="form" (ngSubmit)="aceitar()">
            <div class="field" [class.field--invalid]="invalido('password')">
              <label for="password">Crie sua senha</label>
              <input id="password" type="password" formControlName="password" autocomplete="new-password" />
              <span class="muted dica">Pelo menos 10 caracteres.</span>
              @if (invalido('password')) {
                <span class="field__error">A senha precisa de pelo menos 10 caracteres.</span>
              }
            </div>

            <div class="field" [class.field--invalid]="senhasDiferentes()">
              <label for="confirmacao">Repita a senha</label>
              <input id="confirmacao" type="password" formControlName="confirmacao" autocomplete="new-password" />
              @if (senhasDiferentes()) {
                <span class="field__error">As senhas não são iguais.</span>
              }
            </div>

            <button type="submit" class="btn btn--primary full" [disabled]="enviando()">
              {{ enviando() ? 'Entrando…' : 'Criar senha e entrar' }}
            </button>
          </form>
        } @else {
          <!-- Convite inválido não é beco sem saída: diz o que fazer. -->
          <div class="alert alert--danger">
            {{ erro()?.detail ?? 'Convite inválido.' }}
          </div>
          <p class="muted">
            Peça um novo convite ao responsável pela sua empresa. Links de convite valem por
            alguns dias e só podem ser usados uma vez.
          </p>
          <p class="muted rodape"><a routerLink="/entrar">Já tenho acesso</a></p>
        }
      </div>
    </div>
  `,
  styles: `
    .wrap { align-items: center; display: flex; justify-content: center; min-height: 100vh; padding: var(--space-4); }
    .cartao { max-width: 420px; padding: var(--space-6); width: 100%; }
    .marca { align-items: center; display: flex; gap: var(--space-3); margin-bottom: var(--space-5); }
    .til { color: var(--mamao-yellow-500); font-size: 32px; line-height: 1; }
    .marca strong { display: block; font-size: 20px; }
    .marca small { display: block; font-size: 12px; }
    .chamada { margin: 0 0 var(--space-1); }
    .email { font-size: 13px; margin: 0 0 var(--space-5); }
    .dica { font-size: 13px; }
    .full { width: 100%; }
    .rodape { margin-top: var(--space-4); text-align: center; }
  `,
})
export class AcceptInvitePage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(InvitesApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly convite = signal<InvitePreview | null>(null);
  readonly carregando = signal(true);
  readonly enviando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  private token = '';

  readonly form = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(10)]],
    confirmacao: ['', [Validators.required]],
  });

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    void this.carregar();
  }

  invalido(campo: string): boolean {
    const control = this.form.get(campo);
    return !!control?.invalid && (control.dirty || control.touched);
  }

  senhasDiferentes(): boolean {
    const { password, confirmacao } = this.form.getRawValue();
    return confirmacao.length > 0 && password !== confirmacao;
  }

  rotuloDoPapel(papel: string): string {
    switch (papel) {
      case 'Owner':
        return 'proprietário';
      case 'Hr':
        return 'RH';
      case 'Manager':
        return 'gestor';
      default:
        return 'funcionário';
    }
  }

  async aceitar(): Promise<void> {
    if (this.form.invalid || this.senhasDiferentes()) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      const auth = await this.api.accept(this.token, this.form.getRawValue().password);

      // Entra direto: mandar para o login logo depois de escolher a senha seria pedir a
      // mesma senha duas vezes em dez segundos.
      this.session.adopt(auth);
      await this.router.navigate(['/inicio']);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.enviando.set(false);
    }
  }

  private async carregar(): Promise<void> {
    if (!this.token) {
      this.erro.set({ status: 400, title: 'Convite inválido', detail: 'Link de convite sem token.' });
      this.carregando.set(false);
      return;
    }

    try {
      this.convite.set(await this.api.preview(this.token));
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }
}
