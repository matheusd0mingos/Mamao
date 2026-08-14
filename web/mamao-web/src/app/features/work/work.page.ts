import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type {
  ApiProblem,
  WorkAlertKind,
  WorkBoard,
  WorkItemPriority,
  WorkItemResponse,
  WorkItemStatus,
} from '../../core/http/api.types';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { EmployeesApi } from '../employees/employees.api';
import { OrganizationApi } from '../organization/organization.api';
import { Icon, type IconName } from '../../shared/ui/icon';
import { WorkApi } from './work.api';

interface Coluna {
  readonly status: WorkItemStatus;
  readonly rotulo: string;
}

const COLUNAS: readonly Coluna[] = [
  { status: 'Aberta', rotulo: 'A fazer' },
  { status: 'EmAndamento', rotulo: 'Em andamento' },
  { status: 'Concluida', rotulo: 'Concluída' },
];

const ICONE_DO_ALERTA: Record<WorkAlertKind, IconName> = {
  Atrasada: 'alerta',
  ResponsavelAusente: 'alerta',
  ResponsavelSai: 'relogio',
  SemResponsavel: 'ninguem',
};

const PRIORIDADES: ReadonlyArray<{ valor: WorkItemPriority; rotulo: string }> = [
  { valor: 'Alta', rotulo: 'Alta' },
  { valor: 'Normal', rotulo: 'Normal' },
  { valor: 'Baixa', rotulo: 'Baixa' },
];

/**
 * O quadro de demandas.
 *
 * A quarta visao dos mesmos objetos: as mesmas pessoas e a mesma disponibilidade que
 * aparecem no calendario e na escala, agora do ponto de vista do que precisa sair. O que
 * diferencia de um quadro generico e uma linha so — o alerta do cartao. "O Marcos entra
 * de ferias na quarta e o prazo e sexta" e a frase que o gestor precisa ler ANTES de
 * sexta, e nenhuma ferramenta de tarefas consegue dize-la, porque nao sabe quem esta de
 * ferias.
 *
 * Arrastar e o atalho, nao o mecanismo: o HTML5 drag-and-drop nao existe no celular. Cada
 * cartao tem os botoes de mover, e e por eles que a tela funciona em qualquer lugar.
 */
@Component({
  selector: 'mamao-work',
  imports: [FormsModule, HasPermissionDirective, Icon],
  template: `
    <header class="head">
      <div>
        <h1>Demandas</h1>
        <p class="muted">O que precisa sair, e quem está disponível para fazer.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    @if (quadro(); as q) {
      @if (q.atrasadas > 0 || q.semResponsavel > 0 || q.comRiscoDeAusencia > 0) {
        <section class="resumo">
          @if (q.atrasadas > 0) {
            <span class="resumo__item resumo__item--grave">
              <mamao-icon name="alerta" [size]="18" />
              {{ q.atrasadas }} {{ q.atrasadas === 1 ? 'atrasada' : 'atrasadas' }}
            </span>
          }
          @if (q.comRiscoDeAusencia > 0) {
            <span class="resumo__item resumo__item--atencao">
              <mamao-icon name="relogio" [size]="18" />
              {{ q.comRiscoDeAusencia }} com responsável fora antes do prazo
            </span>
          }
          @if (q.semResponsavel > 0) {
            <span class="resumo__item">
              <mamao-icon name="ninguem" [size]="18" />
              {{ q.semResponsavel }} sem responsável
            </span>
          }
        </section>
      }
    }

    <section class="barra card">
      <label>
        <span>Seção</span>
        <select [(ngModel)]="filtroSetor" name="filtroSetor" (ngModelChange)="carregar()">
          <option value="">Todas</option>
          @for (s of setores(); track s.id) {
            <option [value]="s.id">{{ s.name }}</option>
          }
        </select>
      </label>

      <label>
        <span>Responsável</span>
        <select [(ngModel)]="filtroPessoa" name="filtroPessoa" (ngModelChange)="carregar()">
          <option value="">Todos</option>
          @for (p of pessoas(); track p.id) {
            <option [value]="p.id">{{ p.fullName }}</option>
          }
        </select>
      </label>

      <label class="checkbox">
        <input
          type="checkbox"
          [(ngModel)]="incluirConcluidas"
          name="incluirConcluidas"
          (ngModelChange)="carregar()"
        />
        <span>Mostrar concluídas antigas</span>
      </label>

      <button
        *mamaoHasPermission="'work.assign'"
        class="btn btn--primary barra__nova"
        type="button"
        (click)="abrirNova()"
      >
        <mamao-icon name="mais" [size]="16" /> Nova demanda
      </button>
    </section>

    @if (formularioAberto()) {
    <section class="card" *mamaoHasPermission="'work.assign'">
      <h2>{{ editando() ? 'Editar demanda' : 'Nova demanda' }}</h2>
      <form class="form" (ngSubmit)="salvar()">
        <label class="form__larga">
          <span>O que precisa ser feito</span>
          <input
            [(ngModel)]="titulo"
            name="titulo"
            placeholder="Fechar o relatório do mês"
            maxlength="200"
            required
          />
        </label>

        <label>
          <span>Responsável</span>
          <select [(ngModel)]="responsavel" name="responsavel">
            <option value="">Ninguém ainda</option>
            @for (p of pessoas(); track p.id) {
              <option [value]="p.id">{{ p.fullName }}</option>
            }
          </select>
        </label>

        <label>
          <span>Prazo</span>
          <input type="date" [(ngModel)]="prazo" name="prazo" />
        </label>

        <label>
          <span>Prioridade</span>
          <select [(ngModel)]="prioridade" name="prioridade">
            @for (p of PRIORIDADES; track p.valor) {
              <option [value]="p.valor">{{ p.rotulo }}</option>
            }
          </select>
        </label>

        <label>
          <span>Seção</span>
          <select [(ngModel)]="setor" name="setor">
            <option value="">Nenhuma</option>
            @for (s of setores(); track s.id) {
              <option [value]="s.id">{{ s.name }}</option>
            }
          </select>
        </label>

        <div class="form__acoes">
          <button class="btn btn--primary" type="submit" [disabled]="salvando()">
            {{ editando() ? 'Salvar' : 'Criar' }}
          </button>
          <button class="btn btn--ghost" type="button" (click)="cancelarEdicao()">Cancelar</button>
          @if (editando()) {
            <button class="btn btn--ghost link-perigo" type="button" (click)="excluir(editando()!)">
              Excluir demanda
            </button>
          }
        </div>
      </form>
    </section>
    }

    <div class="quadro">
      @for (coluna of COLUNAS; track coluna.status) {
        <section
          class="coluna"
          [class.coluna--alvo]="alvo() === coluna.status"
          (dragover)="sobre($event, coluna.status)"
          (dragleave)="sair()"
          (drop)="soltar($event, coluna.status)"
        >
          <header class="coluna__topo">
            <h2>{{ coluna.rotulo }}</h2>
            <span class="contagem">{{ daColuna(coluna.status).length }}</span>
          </header>

          @for (item of daColuna(coluna.status); track item.id) {
            <article
              class="cartao"
              [class.cartao--alta]="item.priority === 'Alta'"
              [attr.draggable]="podeMover() ? 'true' : null"
              (dragstart)="arrastar(item)"
              (dragend)="sair()"
            >
              <h3>{{ item.title }}</h3>

              <p class="cartao__meta">
                @if (item.assigneeName) {
                  <span>{{ item.assigneeName }}</span>
                } @else {
                  <span class="sem-dono">sem responsável</span>
                }
                @if (item.dueOn) {
                  <span class="muted">· {{ data(item.dueOn) }}</span>
                }
                @if (item.priority === 'Alta') {
                  <span class="etiqueta-alta">alta</span>
                }
              </p>

              @if (item.alert; as a) {
                <p class="cartao__alerta" [attr.data-tipo]="a.kind">
                  <mamao-icon [name]="ICONE_DO_ALERTA[a.kind]" [size]="16" />
                  {{ a.message }}
                </p>
              }

              <div class="cartao__acoes" *mamaoHasPermission="'work.assign'">
                @for (destino of COLUNAS; track destino.status) {
                  @if (destino.status !== item.status) {
                    <button type="button" class="mover" (click)="mover(item, destino.status)">
                      {{ destino.rotulo }}
                    </button>
                  }
                }
                <button type="button" class="mover" (click)="editar(item)">Editar</button>
              </div>
            </article>
          } @empty {
            <p class="vazia">Nada aqui.</p>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .resumo { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-bottom: var(--space-4); }
    .resumo__item {
      align-items: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      display: flex;
      font-size: 14px;
      gap: 8px;
      padding: 8px var(--space-3);
    }
    .resumo__item--grave { border-color: var(--status-danger-fg); color: var(--status-danger-fg); }
    .resumo__item--atencao { color: var(--status-warning-fg, #8a6100); }

    .form { display: grid; gap: var(--space-3); grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); align-items: end; }
    .form label { display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
    .form__larga { grid-column: 1 / -1; }
    .form__acoes { display: flex; gap: var(--space-2); grid-column: 1 / -1; }

    .barra { align-items: end; display: flex; flex-wrap: wrap; gap: var(--space-4); }
    .barra label { display: flex; flex-direction: column; font-size: 14px; gap: 4px; }
    .barra .checkbox { align-items: center; flex-direction: row; gap: 8px; }
    .barra__nova { align-items: center; display: flex; gap: 6px; margin-left: auto; }

    /* Tres colunas de largura igual, com rolagem propria quando nao couberem. */
    .quadro {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(3, minmax(240px, 1fr));
      margin-top: var(--space-4);
    }

    .coluna {
      background: var(--surface-muted, #f1ede4);
      border: 1px solid transparent;
      border-radius: var(--radius-md);
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      min-height: 160px;
      padding: var(--space-3);
    }
    .coluna--alvo { border-color: var(--mamao-yellow-500); }
    .coluna__topo { align-items: baseline; display: flex; justify-content: space-between; }
    .coluna__topo h2 { font-size: 15px; margin: 0; }
    .contagem { color: var(--text-secondary); font-size: 13px; }

    .cartao {
      background: var(--surface);
      border: 1px solid var(--border);
      border-left: 3px solid var(--border);
      border-radius: var(--radius-sm);
      padding: var(--space-3);
    }
    .cartao--alta { border-left-color: var(--status-danger-fg); }
    .cartao h3 { font-family: var(--font-ui); font-size: 15px; font-weight: 600; margin: 0 0 6px; }

    .cartao__meta { align-items: center; display: flex; flex-wrap: wrap; font-size: 13px; gap: 6px; margin: 0; }
    .cartao__meta .sem-dono { color: var(--text-secondary); font-style: italic; }
    .etiqueta-alta {
      background: var(--status-danger-bg);
      border-radius: 999px;
      color: var(--status-danger-fg);
      font-size: 11px;
      padding: 1px 8px;
      text-transform: uppercase;
    }

    .cartao__alerta {
      align-items: center;
      display: flex;
      font-size: 13px;
      gap: 6px;
      margin: 8px 0 0;
    }
    .cartao__alerta[data-tipo='Atrasada'],
    .cartao__alerta[data-tipo='ResponsavelAusente'] { color: var(--status-danger-fg); }
    .cartao__alerta[data-tipo='ResponsavelSai'] { color: var(--status-warning-fg, #8a6100); }
    .cartao__alerta[data-tipo='SemResponsavel'] { color: var(--text-secondary); }

    .cartao__acoes { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
    .mover {
      background: none;
      border: 1px solid var(--border);
      border-radius: 999px;
      color: var(--text-secondary);
      cursor: pointer;
      font: inherit;
      font-size: 12px;
      padding: 2px 10px;
    }
    .mover:hover { background: var(--mamao-green-900); border-color: var(--mamao-green-900); color: var(--text-on-dark); }
    .mover--perigo:hover { background: var(--status-danger-fg); border-color: var(--status-danger-fg); }

    .vazia { color: var(--text-secondary); font-size: 13px; margin: 0; padding: var(--space-2) 0; }

    @media (max-width: 860px) {
      .quadro { grid-template-columns: minmax(0, 1fr); }
    }
  `,
})
export class WorkPage implements OnInit {
  private readonly api = inject(WorkApi);
  private readonly employees = inject(EmployeesApi);
  private readonly organization = inject(OrganizationApi);

  protected readonly COLUNAS = COLUNAS;
  protected readonly PRIORIDADES = PRIORIDADES;
  protected readonly ICONE_DO_ALERTA = ICONE_DO_ALERTA;

  protected readonly quadro = signal<WorkBoard | null>(null);
  protected readonly pessoas = signal<Array<{ id: string; fullName: string }>>([]);
  protected readonly setores = signal<Array<{ id: string; name: string }>>([]);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<ApiProblem | null>(null);

  protected readonly editando = signal<WorkItemResponse | null>(null);
  protected readonly formularioAberto = signal(false);
  protected readonly arrastado = signal<WorkItemResponse | null>(null);
  protected readonly alvo = signal<WorkItemStatus | null>(null);

  protected titulo = '';
  protected responsavel = '';
  protected prazo = '';
  protected prioridade: WorkItemPriority = 'Normal';
  protected setor = '';

  protected filtroSetor = '';
  protected filtroPessoa = '';
  protected incluirConcluidas = false;

  protected readonly itens = computed(() => this.quadro()?.items ?? []);

  ngOnInit(): void {
    void this.carregar();
    void this.carregarApoio();
  }

  protected daColuna(status: WorkItemStatus): WorkItemResponse[] {
    return this.itens().filter((i) => i.status === status);
  }

  protected async carregar(): Promise<void> {
    this.erro.set(null);
    try {
      this.quadro.set(
        await this.api.board({
          departmentId: this.filtroSetor || null,
          assigneeId: this.filtroPessoa || null,
          includeDone: this.incluirConcluidas,
        }),
      );
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  /**
   * Pessoas e secoes so para os seletores. Falhar aqui nao pode derrubar o quadro: sem a
   * lista o gestor ainda ve o que esta atrasado, que e o motivo de ele ter aberto a tela.
   */
  private async carregarApoio(): Promise<void> {
    try {
      const pagina = await this.employees.list('', false, 1, 200);
      this.pessoas.set(pagina.items.map((p) => ({ id: p.id as string, fullName: p.fullName })));
    } catch {
      this.pessoas.set([]);
    }

    try {
      const arvore = await this.organization.listDepartments();
      this.setores.set(achatar(arvore));
    } catch {
      this.setores.set([]);
    }
  }

  protected podeMover(): boolean {
    return true;
  }

  // ── formulario ───────────────────────────────────────────────────────────────

  protected abrirNova(): void {
    this.editando.set(null);
    this.limpar();
    this.formularioAberto.set(true);
  }

  protected editar(item: WorkItemResponse): void {
    this.editando.set(item);
    this.formularioAberto.set(true);
    this.titulo = item.title;
    this.responsavel = (item.assigneeId as string | null) ?? '';
    this.prazo = item.dueOn ?? '';
    this.prioridade = item.priority;
    this.setor = item.departmentId ?? '';
    globalThis.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected cancelarEdicao(): void {
    this.editando.set(null);
    this.formularioAberto.set(false);
    this.limpar();
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);

    const corpo = {
      title: this.titulo,
      details: null,
      priority: this.prioridade,
      assigneeId: this.responsavel || null,
      dueOn: this.prazo || null,
      departmentId: this.setor || null,
    };

    try {
      const emEdicao = this.editando();
      if (emEdicao) {
        await this.api.update(emEdicao.id as string, corpo);
        this.editando.set(null);
      } else {
        await this.api.create(corpo);
      }
      this.formularioAberto.set(false);
      this.limpar();
      await this.carregar();
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  private limpar(): void {
    this.titulo = '';
    this.responsavel = '';
    this.prazo = '';
    this.prioridade = 'Normal';
    this.setor = '';
  }

  // ── mover ────────────────────────────────────────────────────────────────────

  protected async mover(item: WorkItemResponse, status: WorkItemStatus): Promise<void> {
    this.erro.set(null);
    try {
      await this.api.move(item.id as string, status);
      await this.carregar();
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected async excluir(item: WorkItemResponse): Promise<void> {
    if (!globalThis.confirm(`Excluir "${item.title}"?`)) return;

    this.erro.set(null);
    try {
      await this.api.remove(item.id as string);
      this.editando.set(null);
      this.formularioAberto.set(false);
      this.limpar();
      await this.carregar();
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected arrastar(item: WorkItemResponse): void {
    this.arrastado.set(item);
  }

  protected sobre(evento: DragEvent, status: WorkItemStatus): void {
    if (!this.arrastado()) return;
    evento.preventDefault(); // sem isto o navegador recusa o drop
    this.alvo.set(status);
  }

  protected sair(): void {
    this.alvo.set(null);
  }

  protected soltar(evento: DragEvent, status: WorkItemStatus): void {
    evento.preventDefault();
    const item = this.arrastado();
    this.arrastado.set(null);
    this.alvo.set(null);

    if (item && item.status !== status) void this.mover(item, status);
  }

  protected data(iso: string): string {
    const [a, m, d] = iso.split('-');
    return `${d}/${m}/${a}`;
  }
}

/** A arvore de secoes vira lista plana com o caminho no nome, para caber num select. */
function achatar(
  nos: ReadonlyArray<{ id: string; name: string; children?: readonly unknown[] }>,
  prefixo = '',
): Array<{ id: string; name: string }> {
  const saida: Array<{ id: string; name: string }> = [];

  for (const no of nos) {
    const nome = prefixo ? `${prefixo} › ${no.name}` : no.name;
    saida.push({ id: no.id, name: nome });

    const filhos = (no.children ?? []) as ReadonlyArray<{
      id: string;
      name: string;
      children?: readonly unknown[];
    }>;
    saida.push(...achatar(filhos, nome));
  }

  return saida;
}
