import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import type { ApiProblem, AuditEntryResponse, AuditPage } from '../../core/http/api.types';
import { Icon } from '../../shared/ui/icon';

/** Rótulos das ações. O código é estável e vai para o banco; o texto é da tela. */
const ACOES: Record<string, string> = {
  'employee.created': 'cadastrou funcionário',
  'employee.updated': 'alterou funcionário',
  'employee.moved': 'moveu de setor',
  'employee.terminated': 'desligou',
  'employee.imported': 'importou planilha',
  'employee.contract.saved': 'salvou contrato',
  'department.created': 'criou setor',
  'department.renamed': 'alterou setor',
  'department.deleted': 'excluiu setor',
  'position.deleted': 'excluiu cargo',
  'availability.occupancy.created': 'lançou ausência',
  'availability.occupancy.deleted': 'removeu ausência',
  'availability.absence.approved': 'aprovou solicitação',
  'availability.absence.rejected': 'recusou solicitação',
  'mission.confirmed': 'confirmou escala',
  'mission.cancelled': 'cancelou missão',
  'rotation.policy.changed': 'mudou a política de rodízio',
  'restriction.created': 'criou restrição',
  'restriction.removed': 'removeu restrição',
  'work.created': 'criou demanda',
  'work.updated': 'alterou demanda',
  'work.moved': 'moveu demanda',
  'work.deleted': 'excluiu demanda',
  'invite.created': 'convidou',
  'invite.accepted': 'aceitou convite',
  'invite.revoked': 'revogou convite',
};

/**
 * Quem fez o quê, quando, de onde.
 *
 * A permissao `audit.read` existia desde o inicio e nao levava a lugar nenhum. Esta e a
 * tela que responde a pergunta que aparece quando algo deu errado — e o estado atual ja
 * nao conta a historia: "quem mudou isso?", "quando esse setor apareceu?", "quem aprovou?".
 *
 * O registro e somente-adicao no banco: nem a aplicacao consegue reescrever. Por isso esta
 * tela nao tem botao nenhum — nao ha o que editar aqui, de proposito.
 */
@Component({
  selector: 'mamao-audit',
  imports: [FormsModule, Icon],
  template: `
    <header class="head">
      <div>
        <h1>Auditoria</h1>
        <p class="muted">
          Quem fez o quê, quando e de qual endereço. O registro é somente-adição: nem o
          sistema consegue apagar uma linha daqui.
        </p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <section class="card filtros">
      <label>
        <span>Ação</span>
        <select [(ngModel)]="acao" name="acao" (ngModelChange)="buscar()">
          <option value="">Todas</option>
          @for (a of pagina()?.actions ?? []; track a) {
            <option [value]="a">{{ ACOES[a] ?? a }}</option>
          }
        </select>
      </label>

      <label>
        <span>Pessoa ou alvo</span>
        <input [(ngModel)]="busca" name="busca" placeholder="nome" (ngModelChange)="buscarComAtraso()" />
      </label>

      <label>
        <span>De</span>
        <input type="date" [(ngModel)]="de" name="de" (ngModelChange)="buscar()" />
      </label>

      <label>
        <span>Até</span>
        <input type="date" [(ngModel)]="ate" name="ate" (ngModelChange)="buscar()" />
      </label>

      @if (temFiltro()) {
        <button class="btn btn--ghost" type="button" (click)="limpar()">Limpar</button>
      }
    </section>

    @if (pagina(); as p) {
      <section class="card">
        <p class="muted contagem">
          {{ p.total }} {{ p.total === 1 ? 'registro' : 'registros' }}
          @if (p.total > p.items.length) {
            · mostrando {{ p.items.length }}
          }
        </p>

        @if (p.items.length === 0) {
          <p class="empty-state">Nada com esse filtro.</p>
        } @else {
          <table class="tabela">
            <thead>
              <tr>
                <th>Quando</th>
                <th>Quem</th>
                <th>O quê</th>
                <th>Sobre</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (e of p.items; track e.id) {
                <tr>
                  <td class="quando">{{ quando(e.occurredAt) }}</td>
                  <td>
                    {{ e.actorName }}
                    @if (e.actorIp) {
                      <div class="muted ip">{{ e.actorIp }}</div>
                    }
                  </td>
                  <td>{{ ACOES[e.action] ?? e.action }}</td>
                  <td>{{ e.subjectLabel }}</td>
                  <td>
                    @if (e.metadata) {
                      <button type="button" class="detalhe" (click)="alternar(e.id)">
                        {{ aberto() === e.id ? 'ocultar' : 'detalhe' }}
                      </button>
                    }
                  </td>
                </tr>
                @if (aberto() === e.id && e.metadata) {
                  <tr class="linha-detalhe">
                    <td colspan="5">
                      <pre>{{ formatar(e.metadata) }}</pre>
                      @if (e.correlationId) {
                        <p class="muted rastreio">rastreio: {{ e.correlationId }}</p>
                      }
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>

          @if (p.total > p.page * p.pageSize) {
            <button class="btn btn--ghost mais" type="button" (click)="maisResultados()">
              <mamao-icon name="mais" [size]="16" /> Carregar mais
            </button>
          }
        }
      </section>
    } @else if (!erro()) {
      <p class="empty-state">Carregando…</p>
    }
  `,
  styles: `
    .head { margin-bottom: var(--space-4); max-width: 720px; }
    .head h1 { margin: 0; }
    .head p { margin: var(--space-1) 0 0; }

    .filtros { align-items: end; display: flex; flex-wrap: wrap; gap: var(--space-4); margin-bottom: var(--space-4); }
    .filtros label { display: flex; flex-direction: column; font-size: 14px; gap: 4px; }

    .contagem { font-size: 13px; margin: 0 0 var(--space-3); }

    .tabela { border-collapse: collapse; font-size: 14px; width: 100%; }
    .tabela th { color: var(--text-secondary); font-weight: 500; padding: 8px; text-align: left; }
    .tabela td { border-top: 1px solid var(--border); padding: 8px; vertical-align: top; }
    .quando { white-space: nowrap; }
    .ip { font-size: 12px; }

    .detalhe {
      background: none; border: 0; color: var(--text-secondary); cursor: pointer;
      font: inherit; font-size: 12px; padding: 0; text-decoration: underline;
    }
    .linha-detalhe td { background: var(--surface-muted, #f1ede4); }
    .linha-detalhe pre { font-size: 12px; margin: 0; white-space: pre-wrap; word-break: break-word; }
    .rastreio { font-size: 11px; margin: 6px 0 0; }

    .mais { align-items: center; display: flex; gap: 6px; margin-top: var(--space-3); }
  `,
})
export class AuditPage_ implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly ACOES = ACOES;

  protected readonly pagina = signal<AuditPage | null>(null);
  protected readonly erro = signal<ApiProblem | null>(null);
  protected readonly aberto = signal<string | null>(null);

  protected acao = '';
  protected busca = '';
  protected de = '';
  protected ate = '';

  private tamanho = 50;
  private atrasado?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    void this.carregar();
  }

  protected temFiltro(): boolean {
    return !!(this.acao || this.busca || this.de || this.ate);
  }

  protected limpar(): void {
    this.acao = '';
    this.busca = '';
    this.de = '';
    this.ate = '';
    void this.buscar();
  }

  protected buscar(): void {
    this.tamanho = 50;
    void this.carregar();
  }

  /** Digitar nome não pode disparar uma requisição por tecla. */
  protected buscarComAtraso(): void {
    clearTimeout(this.atrasado);
    this.atrasado = setTimeout(() => this.buscar(), 250);
  }

  protected maisResultados(): void {
    this.tamanho += 50;
    void this.carregar();
  }

  private async carregar(): Promise<void> {
    let params = new HttpParams().set('pageSize', Math.min(this.tamanho, 100));

    if (this.acao) params = params.set('action', this.acao);
    if (this.busca.trim()) params = params.set('search', this.busca.trim());
    if (this.de) params = params.set('from', this.de);
    if (this.ate) params = params.set('to', this.ate);

    try {
      this.pagina.set(await firstValueFrom(this.http.get<AuditPage>('/api/v1/audit', { params })));
      this.erro.set(null);
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected alternar(id: string): void {
    this.aberto.set(this.aberto() === id ? null : id);
  }

  protected quando(iso: string): string {
    const d = new Date(iso);
    const dois = (n: number) => String(n).padStart(2, '0');
    return `${dois(d.getDate())}/${dois(d.getMonth() + 1)} ${dois(d.getHours())}:${dois(d.getMinutes())}`;
  }

  /** O metadata é JSON livre; formatar indentado é o que o torna legível numa investigação. */
  protected formatar(metadata: string): string {
    try {
      return JSON.stringify(JSON.parse(metadata), null, 2);
    } catch {
      return metadata;
    }
  }

  protected entradas(p: AuditPage): AuditEntryResponse[] {
    return p.items;
  }
}
