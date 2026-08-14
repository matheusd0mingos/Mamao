import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ApiProblem, OccupancyKind, OccupancyResponse } from '../../core/http/api.types';
import { AvailabilityApi } from './availability.api';

const MOTIVOS: Record<OccupancyKind, string> = {
  Ferias: 'férias',
  Folga: 'folga',
  Falta: 'falta',
  Afastamento: 'afastamento',
  Servico: 'serviço',
  Missao: 'missão',
  Outro: 'outro',
};

/** Uma letra por motivo. Cabe numa celula de dia sem precisar de tooltip para o basico. */
const SIGLAS: Record<OccupancyKind, string> = {
  Ferias: 'F',
  Folga: 'D',
  Falta: 'X',
  Afastamento: 'A',
  Servico: 'S',
  Missao: 'M',
  Outro: '•',
};

interface Linha {
  readonly employeeId: string;
  readonly nome: string;
  readonly dias: ReadonlyArray<OccupancyKind | null>;
}

/**
 * O mes inteiro numa grade: pessoas nas linhas, dias nas colunas.
 *
 * A tela de disponibilidade responde "quem pode AMANHA". Esta responde a pergunta que
 * vem antes: "como esta o mes?" — e é a unica forma de ver de longe que tres pessoas da
 * mesma equipe sairam na mesma semana. Ninguem enxerga isso numa lista.
 *
 * So quem tem alguma ausencia no mes aparece. Listar a equipe inteira encheria a tela de
 * linhas vazias e esconderia justamente o que interessa.
 */
@Component({
  selector: 'mamao-calendar',
  imports: [FormsModule],
  template: `
    <header class="head">
      <div>
        <h1>Calendário</h1>
        <p class="muted">As ausências do mês inteiro, de relance.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <section class="card">
      <div class="controles">
        <button class="btn btn--ghost" type="button" (click)="mover(-1)">← Anterior</button>
        <strong class="mes">{{ rotuloDoMes() }}</strong>
        <button class="btn btn--ghost" type="button" (click)="mover(1)">Próximo →</button>

        <span class="legenda">
          @for (item of legenda; track item.tipo) {
            <span class="chip" [attr.data-tipo]="item.tipo">{{ item.sigla }} {{ item.rotulo }}</span>
          }
        </span>
      </div>

      @if (carregando()) {
        <p class="empty-state">Carregando…</p>
      } @else if (linhas().length === 0) {
        <p class="empty-state">Ninguém de folga, férias ou serviço neste mês.</p>
      } @else {
        <div class="rolagem">
          <table class="grade">
            <thead>
              <tr>
                <th class="pessoa">Pessoa</th>
                @for (dia of dias(); track dia) {
                  <th [class.fds]="fimDeSemana(dia)">{{ dia }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (linha of linhas(); track linha.employeeId) {
                <tr>
                  <td class="pessoa">{{ linha.nome }}</td>
                  @for (tipo of linha.dias; track $index) {
                    <td [class.fds]="fimDeSemana($index + 1)">
                      @if (tipo) {
                        <span class="marca" [attr.data-tipo]="tipo" [title]="MOTIVOS[tipo]">
                          {{ SIGLAS[tipo] }}
                        </span>
                      }
                    </td>
                  }
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td class="pessoa">Fora no dia</td>
                @for (total of totais(); track $index) {
                  <td [class.fds]="fimDeSemana($index + 1)" [class.pico]="total >= 3">
                    {{ total || '' }}
                  </td>
                }
              </tr>
            </tfoot>
          </table>
        </div>

        <p class="muted nota">
          A última linha conta quantas pessoas estão fora em cada dia. Três ou mais no
          mesmo dia aparece destacado — é onde a operação costuma apertar.
        </p>
      }
    </section>
  `,
  styles: `
    .controles { align-items: center; display: flex; flex-wrap: wrap; gap: var(--space-3); margin-bottom: var(--space-3); }
    .mes { font-family: var(--font-serif, inherit); font-size: 18px; min-width: 12ch; text-align: center; }
    .legenda { display: flex; flex-wrap: wrap; gap: 6px; margin-left: auto; }

    .chip { border-radius: 999px; font-size: 12px; padding: 2px 10px; }
    .chip[data-tipo='Ferias'], .marca[data-tipo='Ferias'] { background: #fdf1d8; color: #8a5a08; }
    .chip[data-tipo='Folga'], .marca[data-tipo='Folga'] { background: #e8f0ec; color: #1c5245; }
    .chip[data-tipo='Falta'], .marca[data-tipo='Falta'] { background: #fbe3e3; color: #a32020; }
    .chip[data-tipo='Afastamento'], .marca[data-tipo='Afastamento'] { background: #ece4f5; color: #5b3f8a; }
    .chip[data-tipo='Servico'], .marca[data-tipo='Servico'] { background: #dfe9f5; color: #1f4d82; }
    .chip[data-tipo='Missao'], .marca[data-tipo='Missao'] { background: #11362d; color: #f7f3ea; }

    /* A grade nao encolhe: num mes de 31 dias ela rola dentro do cartao em vez de
       espremer as colunas ate a marca virar um risco. */
    .rolagem { overflow-x: auto; }
    .grade { border-collapse: collapse; font-size: 12px; width: 100%; }
    .grade th, .grade td { border: 1px solid var(--border); padding: 3px; text-align: center; min-width: 24px; }
    .grade th { color: var(--text-secondary); font-weight: 500; }
    .grade .pessoa { min-width: 150px; text-align: left; white-space: nowrap; font-size: 13px; padding: 4px 8px; }
    .grade .fds { background: var(--surface-sunken); }
    .grade tfoot td { color: var(--text-secondary); font-weight: 600; }
    .grade tfoot .pico { background: #fbe3e3; color: #a32020; }

    .marca { border-radius: 4px; display: inline-block; font-weight: 700; line-height: 18px; width: 18px; }
    .nota { font-size: 13px; margin-top: var(--space-3); }
  `,
})
export class CalendarPage implements OnInit {
  private readonly api = inject(AvailabilityApi);

  protected readonly MOTIVOS = MOTIVOS;
  protected readonly SIGLAS = SIGLAS;
  protected readonly legenda = (Object.keys(SIGLAS) as OccupancyKind[])
    .filter((t) => t !== 'Outro')
    .map((tipo) => ({ tipo, sigla: SIGLAS[tipo], rotulo: MOTIVOS[tipo] }));

  private readonly referencia = signal(primeiroDiaDoMes(new Date()));
  protected readonly blocos = signal<OccupancyResponse[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<ApiProblem | null>(null);

  protected readonly dias = computed(() => {
    const total = diasNoMes(this.referencia());
    return Array.from({ length: total }, (_, i) => i + 1);
  });

  protected readonly rotuloDoMes = computed(() =>
    this.referencia().toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' }),
  );

  /**
   * Uma linha por pessoa com ausencia, e cada dia recebe o motivo que o cobre. Quando dois
   * blocos caem no mesmo dia, o primeiro fica: a celula tem espaco para um, e a tela de
   * disponibilidade e quem mostra a lista completa daquele dia.
   */
  protected readonly linhas = computed<Linha[]>(() => {
    const total = this.dias().length;
    const ano = this.referencia().getFullYear();
    const mes = this.referencia().getMonth();
    const porPessoa = new Map<string, Linha>();

    for (const bloco of this.blocos()) {
      let linha = porPessoa.get(bloco.employeeId);
      if (!linha) {
        linha = { employeeId: bloco.employeeId, nome: bloco.employeeName, dias: Array(total).fill(null) };
        porPessoa.set(bloco.employeeId, linha);
      }

      const inicio = new Date(bloco.startsOn + 'T00:00:00');
      const fim = new Date(bloco.endsOn + 'T00:00:00');

      for (let d = 1; d <= total; d++) {
        const dia = new Date(ano, mes, d);
        if (dia >= inicio && dia <= fim && linha.dias[d - 1] === null) {
          (linha.dias as (OccupancyKind | null)[])[d - 1] = bloco.kind;
        }
      }
    }

    return [...porPessoa.values()].sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'));
  });

  protected readonly totais = computed(() =>
    this.dias().map((_, i) => this.linhas().filter((l) => l.dias[i] !== null).length),
  );

  ngOnInit(): void {
    void this.carregar();
  }

  protected mover(meses: number): void {
    const atual = this.referencia();
    this.referencia.set(new Date(atual.getFullYear(), atual.getMonth() + meses, 1));
    void this.carregar();
  }

  private async carregar(): Promise<void> {
    this.carregando.set(true);
    this.erro.set(null);

    const inicio = this.referencia();
    const fim = new Date(inicio.getFullYear(), inicio.getMonth() + 1, 0);

    try {
      this.blocos.set(await this.api.occupancies(iso(inicio), iso(fim)));
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }

  protected fimDeSemana(dia: number): boolean {
    const d = new Date(this.referencia().getFullYear(), this.referencia().getMonth(), dia).getDay();
    return d === 0 || d === 6;
  }
}

function primeiroDiaDoMes(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

function diasNoMes(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
}

/** Data local em ISO. `toISOString` converteria para UTC e no Brasil voltaria um dia. */
function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
