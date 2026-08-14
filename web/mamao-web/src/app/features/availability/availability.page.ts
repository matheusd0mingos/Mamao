import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type {
  AbsenceRequestResponse,
  ApiProblem,
  AvailabilityResponse,
  EmployeeListItem,
  OccupancyKind,
} from '../../core/http/api.types';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { EmployeesApi } from '../employees/employees.api';
import { AvailabilityApi } from './availability.api';

/** Rotulo humano para o motivo. O enum vem como texto do servidor, nunca como numero. */
const MOTIVOS: Record<OccupancyKind, string> = {
  Ferias: 'férias',
  Folga: 'folga',
  Falta: 'falta',
  Afastamento: 'afastamento',
  Servico: 'serviço',
  Missao: 'missão',
  Outro: 'outro compromisso',
};

/**
 * A tela que responde "quem posso escalar amanha".
 *
 * Mostra TODO MUNDO — disponiveis e ocupados, com o motivo de cada ausencia — e nao so
 * a lista de livres. O gestor precisa ver quem ficou de fora e por que: e a diferenca
 * entre confiar no sistema e conferir tudo de novo na memoria, que e o que ele faz hoje.
 *
 * A caixa de solicitacoes vive aqui embaixo de proposito. E o mesmo ciclo: ver quem esta
 * fora, decidir quem mais pode sair. Separar em duas rotas obrigaria a ir e voltar para
 * tomar uma decisao so.
 */
@Component({
  selector: 'mamao-availability',
  imports: [FormsModule, HasPermissionDirective],
  template: `
    <header class="head">
      <div>
        <h1>Disponibilidade</h1>
        <p class="muted">Quem está livre, quem está fora e por quê.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <section class="card filtros">
      <label>
        <span>Dia</span>
        <input type="date" [(ngModel)]="dia" name="dia" (change)="carregar()" />
      </label>

      <label>
        <span>Das</span>
        <input type="time" [(ngModel)]="de" name="de" (change)="carregar()" />
      </label>

      <label>
        <span>Até</span>
        <input type="time" [(ngModel)]="ate" name="ate" (change)="carregar()" />
      </label>

      <p class="muted dica">
        Sem horário, considera o dia inteiro. Com horário, quem tem compromisso em outro
        turno continua disponível.
      </p>
    </section>

    <!-- REGISTRAR ------------------------------------------------------------ -->
    <section class="card" *mamaoHasPermission="'timeoff.request'">
      <h2>Registrar ausência</h2>
      <p class="muted dica">
        Quem já está fora sai da lista de disponíveis na hora. O que depende de decisão
        entra como pendente até alguém aprovar.
      </p>

      @if (sucesso(); as mensagem) {
        <div class="alert alert--sucesso">{{ mensagem }}</div>
      }

      <form class="form" (ngSubmit)="registrar(false)">
        <label>
          <span>Pessoa</span>
          <select [(ngModel)]="pessoaId" name="pessoaId" required>
            <option value="">Selecione…</option>
            @for (pessoa of equipe(); track pessoa.id) {
              <option [value]="pessoa.id">{{ pessoa.fullName }}</option>
            }
          </select>
        </label>

        <label>
          <span>Motivo</span>
          <select [(ngModel)]="motivo" name="motivo">
            @for (item of motivosDisponiveis; track item) {
              <option [value]="item">{{ MOTIVOS[item] }}</option>
            }
          </select>
        </label>

        <label>
          <span>De</span>
          <input type="date" [(ngModel)]="inicio" name="inicio" required />
        </label>

        <label>
          <span>Até</span>
          <input type="date" [(ngModel)]="fim" name="fim" required />
        </label>

        <label>
          <span>Das (opcional)</span>
          <input type="time" [(ngModel)]="horaDe" name="horaDe" />
        </label>

        <label>
          <span>Até (opcional)</span>
          <input type="time" [(ngModel)]="horaAte" name="horaAte" />
        </label>

        <label class="form__larga">
          <span>Observação</span>
          <input
            [(ngModel)]="observacao"
            name="observacao"
            maxlength="500"
            placeholder="Opcional. Aparece para quem decide."
          />
        </label>

        <div class="form__acoes">
          <!--
            Dois caminhos explicitos em vez de um botao que muda de significado conforme
            quem esta logado: "registrar direto" e um fato ja decidido (falta, serviço),
            "enviar para aprovacao" e um pedido. Esconder essa diferenca faria o chefe
            aprovar sem perceber que aprovou.
          -->
          <button class="btn" type="submit" [disabled]="salvando()">
            Enviar para aprovação
          </button>
          <button
            *mamaoHasPermission="'timeoff.approve'"
            class="btn btn--ghost"
            type="button"
            [disabled]="salvando()"
            (click)="registrar(true)"
          >
            Registrar direto
          </button>
        </div>
      </form>
    </section>

    @if (carregando()) {
      <p class="empty-state">Carregando…</p>
    } @else {
      <div class="resumo">
        <div class="resumo__item">
          <strong>{{ disponiveis().length }}</strong>
          <span class="muted">disponíveis</span>
        </div>
        <div class="resumo__item">
          <strong>{{ ocupados().length }}</strong>
          <span class="muted">fora</span>
        </div>
        <div class="resumo__item">
          <strong>{{ pessoas().length }}</strong>
          <span class="muted">na equipe</span>
        </div>
      </div>

      <div class="colunas">
        <section class="card painel">
          <h2>Disponíveis</h2>
          @if (disponiveis().length === 0) {
            <p class="empty-state">Ninguém livre nesse horário.</p>
          } @else {
            <ul class="lista">
              @for (pessoa of disponiveis(); track pessoa.employeeId) {
                <li>
                  <span class="lista__nome">{{ pessoa.employeeName }}</span>
                  <span class="muted">{{ contexto(pessoa) }}</span>
                </li>
              }
            </ul>
          }
        </section>

        <section class="card painel">
          <h2>Fora</h2>
          @if (ocupados().length === 0) {
            <p class="empty-state">Equipe completa.</p>
          } @else {
            <ul class="lista">
              @for (pessoa of ocupados(); track pessoa.employeeId) {
                <li>
                  <span class="lista__nome">{{ pessoa.employeeName }}</span>
                  <span class="motivo">{{ motivos(pessoa) }}</span>
                </li>
              }
            </ul>
          }
        </section>
      </div>

      <!-- SOLICITACOES ------------------------------------------------------- -->
      <section class="card" *mamaoHasPermission="'timeoff.approve'">
        <h2>Solicitações pendentes</h2>

        @if (pendentes().length === 0) {
          <p class="empty-state">Nada esperando decisão.</p>
        } @else {
          <ul class="pedidos">
            @for (pedido of pendentes(); track pedido.id) {
              <li>
                <div>
                  <span class="lista__nome">{{ pedido.employeeName }}</span>
                  <span class="muted">
                    {{ MOTIVOS[pedido.kind] }} · {{ periodo(pedido) }} ·
                    {{ pedido.days }} {{ pedido.days === 1 ? 'dia' : 'dias' }}
                  </span>
                  @if (pedido.reason) {
                    <p class="muted pedido__motivo">"{{ pedido.reason }}"</p>
                  }
                  @if (pedido.conflicts.length > 0) {
                    <p class="conflito">
                      Também fora nesse período: {{ pedido.conflicts.join(', ') }}
                    </p>
                  }
                </div>

                <div class="pedido__acoes">
                  <button class="btn btn--primary" type="button" (click)="aprovar(pedido)">
                    Aprovar
                  </button>
                  <button class="link-perigo" type="button" (click)="recusar(pedido)">
                    Recusar
                  </button>
                </div>
              </li>
            }
          </ul>
        }
      </section>
    }
  `,
  styles: `
    .filtros { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: flex-end; }
    .filtros label { display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
    .filtros .dica { flex: 1 1 260px; margin: 0; font-size: 13px; }

    .resumo { display: flex; gap: var(--space-4); margin: var(--space-4) 0; flex-wrap: wrap; }
    .resumo__item { display: flex; flex-direction: column; }
    .resumo__item strong { font-size: 28px; line-height: 1.1; }

    .colunas { display: grid; gap: var(--space-4); grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); }
    .painel h2 { font-size: 18px; margin-bottom: var(--space-2); }

    .lista { list-style: none; margin: 0; padding: 0; }
    .lista li { display: flex; justify-content: space-between; gap: var(--space-2); padding: 8px 0; border-top: 1px solid var(--border); min-width: 0; }
    .lista li:first-child { border-top: 0; }
    .lista__nome { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .motivo { color: var(--status-danger-fg); font-size: 14px; white-space: nowrap; }

    .form { display: grid; gap: var(--space-3); grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); align-items: end; }
    .form label { display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
    .form__larga { grid-column: 1 / -1; }
    .form__acoes { display: flex; gap: var(--space-2); grid-column: 1 / -1; flex-wrap: wrap; }

    .pedidos { list-style: none; margin: 0; padding: 0; }
    .pedidos li { display: flex; justify-content: space-between; gap: var(--space-3); padding: 12px 0; border-top: 1px solid var(--border); flex-wrap: wrap; }
    .pedidos li:first-child { border-top: 0; }
    .pedido__motivo { margin: 4px 0 0; font-size: 14px; }
    .pedido__acoes { display: flex; gap: var(--space-2); align-items: center; }
    .conflito { color: #8a5a08; font-size: 13px; margin: 4px 0 0; }
  `,
})
export class AvailabilityPage implements OnInit {
  private readonly api = inject(AvailabilityApi);
  private readonly employees = inject(EmployeesApi);

  protected readonly MOTIVOS = MOTIVOS;

  protected dia = hoje();
  protected de = '';
  protected ate = '';

  protected readonly pessoas = signal<AvailabilityResponse[]>([]);
  protected readonly pendentes = signal<AbsenceRequestResponse[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<ApiProblem | null>(null);

  // ── formulário ─────────────────────────────────────────────────────────────
  protected readonly equipe = signal<EmployeeListItem[]>([]);
  protected readonly salvando = signal(false);
  protected readonly sucesso = signal<string | null>(null);

  protected pessoaId = '';
  protected motivo: OccupancyKind = 'Ferias';
  protected inicio = hoje();
  protected fim = hoje();
  protected horaDe = '';
  protected horaAte = '';
  protected observacao = '';

  /** Ordem pensada no uso: férias e folga são o que mais se pede; "outro" fica por último. */
  protected readonly motivosDisponiveis: OccupancyKind[] = [
    'Ferias',
    'Folga',
    'Falta',
    'Afastamento',
    'Servico',
    'Outro',
  ];

  protected readonly disponiveis = computed(() => this.pessoas().filter((p) => p.available));
  protected readonly ocupados = computed(() => this.pessoas().filter((p) => !p.available));

  ngOnInit(): void {
    void this.carregar();
    void this.carregarPendentes();
    void this.carregarEquipe();
  }

  private async carregarEquipe(): Promise<void> {
    // pageSize alto de proposito: o alvo do produto sao equipes de 5 a 60 pessoas, entao
    // uma pagina cobre a lista inteira e o select nao precisa paginar.
    const pagina = await this.employees.list('', false, 1, 200);
    this.equipe.set(pagina.items);
  }

  /**
   * Um formulário, dois destinos. `direto` grava a ausência como fato consumado; sem ele,
   * vira solicitação pendente. A diferença é de processo, não de dado — o período e o
   * motivo são os mesmos —, e é por isso que não são duas telas.
   */
  protected async registrar(direto: boolean): Promise<void> {
    if (!this.pessoaId) {
      this.erro.set({ detail: 'Escolha a pessoa.' } as ApiProblem);
      return;
    }

    // O servidor recusa um horário sozinho; melhor não deixar o pedido sair daqui errado.
    const temHorario = this.horaDe !== '' && this.horaAte !== '';

    const comum = {
      employeeId: this.pessoaId,
      kind: this.motivo,
      startsOn: this.inicio,
      endsOn: this.fim,
      startsAt: temHorario ? this.horaDe : null,
      endsAt: temHorario ? this.horaAte : null,
    };

    this.salvando.set(true);
    this.erro.set(null);
    this.sucesso.set(null);

    try {
      if (direto) {
        await this.api.createOccupancy({ ...comum, note: this.observacao || null });
        this.sucesso.set('Ausência registrada.');
      } else {
        await this.api.createRequest({ ...comum, reason: this.observacao || null });
        this.sucesso.set('Solicitação enviada. Aparece abaixo até alguém decidir.');
      }

      this.observacao = '';
      await Promise.all([this.carregar(), this.carregarPendentes()]);
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  protected async carregar(): Promise<void> {
    this.carregando.set(true);
    this.erro.set(null);

    try {
      // Horario so vale em par: um lado sozinho nao define recorte, e o servidor recusaria.
      const temHorario = this.de !== '' && this.ate !== '';
      this.pessoas.set(
        await this.api.on(this.dia, temHorario ? this.de : undefined, temHorario ? this.ate : undefined),
      );
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }

  private async carregarPendentes(): Promise<void> {
    try {
      this.pendentes.set(await this.api.requests('Pendente'));
    } catch {
      // A caixa de solicitacoes e secundaria nesta tela: se ela falhar, a resposta de
      // disponibilidade — que e o motivo de a pessoa ter aberto — continua na tela.
      this.pendentes.set([]);
    }
  }

  protected async aprovar(pedido: AbsenceRequestResponse): Promise<void> {
    await this.decidir(() => this.api.approve(pedido.id));
  }

  protected async recusar(pedido: AbsenceRequestResponse): Promise<void> {
    const motivo = prompt('Por que está recusando? (a pessoa vê esta resposta)');
    if (motivo === null) return;

    await this.decidir(() => this.api.reject(pedido.id, motivo));
  }

  private async decidir(acao: () => Promise<unknown>): Promise<void> {
    this.erro.set(null);
    try {
      await acao();
      // Aprovar muda a disponibilidade do dia. Recarregar as duas listas e o que faz a
      // tela contar a verdade logo depois da decisao.
      await Promise.all([this.carregar(), this.carregarPendentes()]);
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected motivos(pessoa: AvailabilityResponse): string {
    return pessoa.busy.map((k) => MOTIVOS[k]).join(' · ');
  }

  protected contexto(pessoa: AvailabilityResponse): string {
    return [pessoa.positionName, pessoa.departmentName].filter(Boolean).join(' · ');
  }

  protected periodo(pedido: AbsenceRequestResponse): string {
    const de = formatar(pedido.startsOn);
    return pedido.startsOn === pedido.endsOn ? de : `${de} a ${formatar(pedido.endsOn)}`;
  }
}

function hoje(): string {
  return new Date().toISOString().slice(0, 10);
}

function formatar(iso: string): string {
  const [ano, mes, dia] = iso.split('-');
  return `${dia}/${mes}/${ano}`;
}
