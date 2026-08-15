import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { ApiProblem, OccupancyKind, TodayPanel } from '../../core/http/api.types';
import { SessionService } from '../../core/auth/session.service';
import { Icon } from '../../shared/ui/icon';
import { TodayApi } from './today.api';

const MOTIVOS: Record<OccupancyKind, string> = {
  Ferias: 'férias',
  Folga: 'folga',
  Falta: 'falta',
  Afastamento: 'afastamento',
  Servico: 'serviço',
  Missao: 'missão',
  Outro: 'outro',
};

/**
 * O painel de hoje.
 *
 * A tese do produto e "o gestor nao deveria precisar perguntar o que esta acontecendo".
 * Esta e a tela onde isso ou e verdade ou nao e: abrir o sistema tem que responder quem
 * esta fora, o que precisa de gente, o que venceu e o que espera decisao — sem clicar.
 *
 * A ordem dos blocos e a ordem da urgencia, nao a do modelo de dados: primeiro o que
 * cobra uma acao HOJE (decidir, completar escala, cobrar prazo), depois o que so informa.
 * Um painel que abre com grafico bonito e esconde a solicitacao parada ha tres dias esta
 * otimizando para a captura de tela, nao para o trabalho.
 */
@Component({
  selector: 'mamao-dashboard',
  imports: [RouterLink, Icon],
  template: `
    <header class="head">
      <div>
        <h1>Hoje</h1>
        <p class="muted">{{ session.tenantName() }} · {{ dataPorExtenso() }}</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    @if (painel(); as p) {
      @if (tudoQuieto()) {
        <div class="card quieto">
          <mamao-icon name="confere" [size]="22" />
          <div>
            <strong>Nada pedindo atenção.</strong>
            <p class="muted">
              Ninguém fora, nada vencido, nada esperando decisão. Se isso parece errado,
              provavelmente falta cadastrar — comece por
              <a routerLink="/pessoas">Pessoas</a>.
            </p>
          </div>
        </div>
      }

      <!-- ── o que cobra decisão ─────────────────────────────────────────────── -->
      @if (p.approvals.length > 0) {
        <section class="card bloco bloco--acao">
          <h2><mamao-icon name="alerta" [size]="18" /> Esperando sua decisão</h2>
          <ul class="lista">
            @for (a of p.approvals; track a.id) {
              <li>
                <div>
                  <strong>{{ a.employeeName }}</strong>
                  <span class="muted"> · {{ MOTIVOS[a.kind] }} · {{ periodo(a.startsOn, a.endsOn) }}</span>
                </div>
                <span class="quando" [class.quando--perto]="a.diasAteComecar <= 3">
                  {{ comecaEm(a.diasAteComecar) }}
                </span>
              </li>
            }
          </ul>
          <a class="btn btn--primary" routerLink="/disponibilidade">Decidir</a>
        </section>
      }

      <!-- ── o que precisa de gente ──────────────────────────────────────────── -->
      @if (p.missions.length > 0) {
        <section class="card bloco" [class.bloco--acao]="faltaGente()">
          <h2><mamao-icon name="escala" [size]="18" /> Escala de hoje e amanhã</h2>
          <ul class="lista">
            @for (m of p.missions; track m.id) {
              <li>
                <div>
                  <strong>{{ m.name }}</strong>
                  <span class="muted">
                    · {{ diaCurto(m.on) }}{{ m.startsAt ? ' às ' + m.startsAt.slice(0, 5) : '' }}
                  </span>
                </div>
                @if (m.missing > 0) {
                  <span class="etiqueta etiqueta--perigo">faltam {{ m.missing }}</span>
                } @else if (!m.confirmed) {
                  <span class="etiqueta etiqueta--atencao">completa, não confirmada</span>
                } @else {
                  <span class="etiqueta etiqueta--ok">confirmada</span>
                }
              </li>
            }
          </ul>
          <a class="btn btn--ghost" routerLink="/escala">Abrir a escala</a>
        </section>
      }

      <!-- ── o que venceu ────────────────────────────────────────────────────── -->
      @if (p.work.length > 0) {
        <section class="card bloco" [class.bloco--acao]="temAtrasada()">
          <h2><mamao-icon name="demandas" [size]="18" /> Demandas vencendo</h2>
          <ul class="lista">
            @for (w of p.work; track w.id) {
              <li>
                <div>
                  <strong>{{ w.title }}</strong>
                  <span class="muted"> · {{ w.assigneeName ?? 'sem responsável' }}</span>
                </div>
                <span class="etiqueta" [class.etiqueta--perigo]="w.overdue">
                  {{ w.overdue ? 'venceu ' + diaCurto(w.dueOn!) : 'vence hoje' }}
                </span>
              </li>
            }
          </ul>
          <a class="btn btn--ghost" routerLink="/demandas">Abrir o quadro</a>
        </section>
      }

      <!-- ── quem está fora ──────────────────────────────────────────────────── -->
      <section class="card bloco">
        <h2><mamao-icon name="pessoas" [size]="18" /> Equipe</h2>

        <p class="numeros">
          <strong>{{ p.teamSize - p.out.length }}</strong> de {{ p.teamSize }} disponíveis
          @if (p.out.length > 0) {
            <span class="muted"> · {{ p.out.length }} fora</span>
          }
        </p>

        @if (p.out.length > 0) {
          <ul class="lista">
            @for (pessoa of p.out; track pessoa.employeeId) {
              <li>
                <div>
                  <strong>{{ pessoa.name }}</strong>
                  @if (pessoa.departmentName) {
                    <span class="muted"> · {{ pessoa.departmentName }}</span>
                  }
                </div>
                <span class="muted">{{ MOTIVOS[pessoa.kind] }} até {{ diaCurto(pessoa.until) }}</span>
              </li>
            }
          </ul>
        }

        @if (p.backTomorrow.length > 0) {
          <p class="volta muted">
            <mamao-icon name="relogio" [size]="15" />
            Volta amanhã: {{ nomes(p.backTomorrow) }}.
          </p>
        }

        <a class="btn btn--ghost" routerLink="/calendario">Ver o mês</a>
      </section>
    } @else if (!erro()) {
      <p class="empty-state">Carregando…</p>
    }
  `,
  styles: `
    .head { margin-bottom: var(--space-5); }
    .head h1 { margin: 0; }
    .head p { margin: var(--space-1) 0 0; }

    .quieto { align-items: flex-start; display: flex; gap: var(--space-3); max-width: 640px; }
    .quieto p { margin: 4px 0 0; }

    .bloco { margin-bottom: var(--space-4); max-width: 760px; }
    /* A borda amarela e o unico enfeite da tela, e so aparece no bloco que pede acao. */
    .bloco--acao { border-left: 3px solid var(--mamao-yellow-500); }
    .bloco h2 { align-items: center; display: flex; font-size: 15px; gap: 8px; margin: 0 0 var(--space-3); }

    .numeros { font-size: 15px; margin: 0 0 var(--space-3); }
    .numeros strong { font-size: 22px; }

    .lista { list-style: none; margin: 0 0 var(--space-3); padding: 0; }
    .lista li {
      align-items: center; border-top: 1px solid var(--border); display: flex;
      gap: var(--space-3); justify-content: space-between; padding: 8px 0;
    }
    .lista li:first-child { border-top: 0; }
    .lista li > div { min-width: 0; }

    .quando { color: var(--text-secondary); font-size: 13px; white-space: nowrap; }
    .quando--perto { color: var(--status-danger-fg); font-weight: 600; }

    .etiqueta { color: var(--text-secondary); font-size: 12px; white-space: nowrap; }
    .etiqueta--perigo { color: var(--status-danger-fg); font-weight: 600; }
    .etiqueta--atencao { color: var(--status-warning-fg, #8a6100); }
    .etiqueta--ok { color: var(--status-success-fg); }

    .volta { align-items: center; display: flex; font-size: 13px; gap: 6px; margin: 0 0 var(--space-3); }
  `,
})
export class DashboardPage implements OnInit {
  private readonly api = inject(TodayApi);

  readonly session = inject(SessionService);
  protected readonly MOTIVOS = MOTIVOS;

  protected readonly painel = signal<TodayPanel | null>(null);
  protected readonly erro = signal<ApiProblem | null>(null);

  protected readonly tudoQuieto = computed(() => {
    const p = this.painel();
    if (!p) return false;
    return (
      p.approvals.length === 0 &&
      p.missions.length === 0 &&
      p.work.length === 0 &&
      p.out.length === 0
    );
  });

  protected readonly faltaGente = computed(() =>
    (this.painel()?.missions ?? []).some((m) => m.missing > 0),
  );

  protected readonly temAtrasada = computed(() =>
    (this.painel()?.work ?? []).some((w) => w.overdue),
  );

  ngOnInit(): void {
    void this.carregar();
  }

  private async carregar(): Promise<void> {
    try {
      this.painel.set(await this.api.today());
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected dataPorExtenso(): string {
    const iso = this.painel()?.today;
    if (!iso) return '';

    const [a, m, d] = iso.split('-').map(Number);
    const data = new Date(a, m - 1, d);
    const semana = ['domingo', 'segunda', 'terça', 'quarta', 'quinta', 'sexta', 'sábado'];
    const meses = [
      'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
      'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
    ];

    return `${semana[data.getDay()]}, ${d} de ${meses[m - 1]}`;
  }

  protected diaCurto(iso: string): string {
    const [, m, d] = iso.split('-');
    return `${d}/${m}`;
  }

  protected periodo(de: string, ate: string): string {
    return de === ate ? this.diaCurto(de) : `${this.diaCurto(de)} a ${this.diaCurto(ate)}`;
  }

  /** "começa hoje" diz mais que "em 0 dias" — e é o que decide se dá para adiar a decisão. */
  protected comecaEm(dias: number): string {
    if (dias < 0) return 'já começou';
    if (dias === 0) return 'começa hoje';
    if (dias === 1) return 'começa amanhã';
    return `em ${dias} dias`;
  }

  protected nomes(pessoas: ReadonlyArray<{ name: string }>): string {
    return pessoas.map((p) => p.name.split(' ')[0]).join(', ');
  }
}
