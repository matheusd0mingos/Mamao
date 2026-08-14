import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type {
  ApiProblem,
  ContractResponse,
  EmployeeResponse,
  InviteResponse,
} from '../../core/http/api.types';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { SessionService } from '../../core/auth/session.service';
import { InvitesApi } from '../access/invites.api';
import { EmployeesApi } from './employees.api';

/**
 * Perfil do funcionário: tudo que se sabe da pessoa numa página só.
 *
 * Antes, clicar num nome caía direto no formulário de edição — o que é errado por dois
 * motivos: ver é a operação comum e editar é a rara, e um formulário aberto por engano é
 * um formulário que alguém salva por engano.
 *
 * É também a página onde os marcos seguintes penduram o que produzirem: documentos
 * (Marco 2), ausências (Marco 3), escala (Marco 4) e férias (Marco 5) são todos "desta
 * pessoa". Por isso ela nasce agora, mesmo mostrando pouco.
 */
@Component({
  selector: 'mamao-employee-profile',
  imports: [DatePipe, FormsModule, RouterLink, HasPermissionDirective],
  template: `
    <nav class="volta"><a routerLink="/pessoas">← Pessoas</a></nav>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    @if (pessoa(); as p) {
      <header class="head">
        <div>
          <h1>{{ p.fullName }}</h1>
          <p class="muted">
            {{ p.positionName }}@if (p.departmentName) { · {{ p.departmentName }} }
          </p>
        </div>

        <div class="head__acoes">
          <span class="badge" [class.badge--success]="p.isActive" [class.badge--neutral]="!p.isActive">
            {{ p.isActive ? 'Ativo' : 'Desligado em ' + (p.terminatedOn | date: 'dd/MM/yyyy') }}
          </span>
          <a *mamaoHasPermission="'people.write'" class="btn btn--ghost" [routerLink]="['/pessoas', p.id, 'editar']">
            Editar
          </a>
        </div>
      </header>

      <div class="colunas">
        <section class="card bloco">
          <h2>Dados</h2>
          <dl>
            <dt>Matrícula</dt>
            <dd>{{ p.code ?? '—' }}</dd>

            <dt>Admissão</dt>
            <dd>{{ p.hiredOn | date: 'dd/MM/yyyy' }}</dd>

            <dt>Cargo</dt>
            <dd>{{ p.positionName }}</dd>

            <dt>Setor</dt>
            <dd>{{ p.departmentName ?? '—' }}</dd>

            <dt>Gestor</dt>
            <dd>{{ p.managerName ?? '—' }}</dd>

            <dt>E-mail</dt>
            <dd>{{ p.email ?? '—' }}</dd>
          </dl>
        </section>

        <section class="card bloco">
          <h2>Contrato</h2>

          @if (contrato(); as c) {
            @for (alerta of c.warnings; track alerta.code) {
              <div class="alert alert--warning">{{ alerta.message }}</div>
            }

            <dl>
              <dt>Regime</dt>
              <dd>{{ rotuloDoRegime(c.regime) }}</dd>

              <dt>Jornada</dt>
              <dd>{{ rotuloDaJornada(c.scheduleType) }}</dd>

              <dt>Carga semanal</dt>
              <dd>{{ c.weeklyHours }}h</dd>

              @if (c.compensationAgreementOn) {
                <dt>Acordo de compensação</dt>
                <dd>{{ c.compensationAgreementOn | date: 'dd/MM/yyyy' }}</dd>
              }

              @if (c.notes) {
                <dt>Observações</dt>
                <dd>{{ c.notes }}</dd>
              }
            </dl>
          } @else {
            <p class="muted">
              Contrato ainda não preenchido. Sem ele, o Mamão não sabe qual regra de jornada e
              de férias vale para esta pessoa.
            </p>
            <a *mamaoHasPermission="'people.write'" class="btn btn--ghost" [routerLink]="['/pessoas', p.id, 'editar']">
              Preencher contrato
            </a>
          }
        </section>

        <section class="card bloco">
          <h2>Acesso ao sistema</h2>

          @if (p.hasLogin) {
            <p><span class="badge badge--success">Tem acesso</span></p>
          } @else if (convite(); as c) {
            <p>
              <span class="badge badge--neutral">Convidado</span>
              <span class="muted"> · {{ c.email }}</span>
            </p>
          } @else {
            <p class="muted">Sem acesso. A pessoa está cadastrada, mas não entra no sistema.</p>
            <a *mamaoHasPermission="'users.invite'" class="btn btn--ghost" routerLink="/acessos">Convidar</a>
          }
        </section>

        @if (p.isActive) {
          <section class="card bloco" *mamaoHasPermission="'people.write'">
            <h2>Desligamento</h2>

            @if (!desligando()) {
              <p class="muted">
                Desligar preserva o histórico da pessoa — ela sai das listas e da escala, mas
                nada é apagado.
              </p>
              <button class="btn btn--ghost" type="button" (click)="desligando.set(true)">
                Desligar funcionário
              </button>
            } @else {
              <p class="muted">Em que data?</p>
              <div class="desligar">
                <input type="date" [(ngModel)]="dataDeDesligamento" name="dataDeDesligamento" aria-label="Data do desligamento" />
                <button class="btn btn--perigo" type="button" [disabled]="salvando()" (click)="desligar()">
                  {{ salvando() ? 'Desligando…' : 'Confirmar desligamento' }}
                </button>
                <button class="btn btn--ghost" type="button" (click)="desligando.set(false)">Cancelar</button>
              </div>
            }
          </section>
        }
      </div>
    } @else if (!erro()) {
      <p class="empty-state">Carregando…</p>
    }
  `,
  styles: `
    .volta { margin-bottom: var(--space-3); }
    .volta a { color: var(--text-secondary); font-size: 14px; text-decoration: none; }
    .head {
      align-items: flex-start; display: flex; flex-wrap: wrap; gap: var(--space-3);
      justify-content: space-between; margin-bottom: var(--space-5);
    }
    .head h1 { margin: 0; }
    .head p { margin: var(--space-1) 0 0; }
    .head__acoes { align-items: center; display: flex; gap: var(--space-3); }
    .colunas { align-items: start; display: grid; gap: var(--space-5); grid-template-columns: 1fr 1fr; }
    .bloco { padding: var(--space-5); }
    .bloco h2 { font-size: 16px; margin: 0 0 var(--space-4); }
    dl { display: grid; gap: var(--space-2) var(--space-4); grid-template-columns: auto 1fr; margin: 0; }
    dt { color: var(--text-secondary); font-size: 14px; }
    dd { margin: 0; }
    .desligar { align-items: center; display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .desligar input {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      font: inherit; padding: var(--space-2) var(--space-3);
    }
    .btn--perigo { background: var(--status-danger-fg); color: #fff; }
    @media (max-width: 900px) { .colunas { grid-template-columns: 1fr; } }
  `,
})
export class EmployeeProfilePage implements OnInit {
  private readonly api = inject(EmployeesApi);
  private readonly invites = inject(InvitesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);

  readonly pessoa = signal<EmployeeResponse | null>(null);
  readonly contrato = signal<ContractResponse | null>(null);
  readonly convite = signal<InviteResponse | null>(null);
  readonly erro = signal<ApiProblem | null>(null);
  readonly desligando = signal(false);
  readonly salvando = signal(false);

  dataDeDesligamento = new Date().toISOString().slice(0, 10);

  private id = '';

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    void this.carregar();
  }

  rotuloDoRegime(regime: ContractResponse['regime']): string {
    switch (regime) {
      case 'Clt':
        return 'CLT';
      case 'EstatutarioFederal':
        return 'Estatutário federal';
      case 'EstatutarioLocal':
        return 'Estatutário estadual/municipal';
      case 'Militar':
        return 'Militar';
      default:
        return 'Outro';
    }
  }

  rotuloDaJornada(jornada: ContractResponse['scheduleType']): string {
    switch (jornada) {
      case 'Administrativo':
        return 'Administrativo (seg–sex)';
      case 'CincoDois':
        return '5×2';
      case 'SeisUm':
        return '6×1';
      case 'DozePorTrintaESeis':
        return '12×36';
      case 'Rodizio':
        return 'Rodízio (escala de serviço)';
      default:
        return 'Outro';
    }
  }

  async desligar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);

    try {
      this.pessoa.set(await this.api.terminate(this.id, { terminatedOn: this.dataDeDesligamento }));
      this.desligando.set(false);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  private async carregar(): Promise<void> {
    try {
      this.pessoa.set(await this.api.get(this.id));
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
      return;
    }

    // Contrato e convite são complementos: falhar em qualquer um dos dois não pode
    // esconder o perfil, que é a informação principal da página.
    void this.api.getContract(this.id).then(
      (c) => this.contrato.set(c),
      () => this.contrato.set(null),
    );

    if (this.session.has('users.invite')) {
      const email = this.pessoa()?.email?.toLowerCase();

      void this.invites.list().then(
        (lista) => this.convite.set(lista.find((c) => c.email.toLowerCase() === email) ?? null),
        () => this.convite.set(null),
      );
    }
  }
}
