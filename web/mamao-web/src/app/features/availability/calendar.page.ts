import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ApiProblem, OccupancyKind, OccupancyResponse } from '../../core/http/api.types';
import { AvailabilityApi } from './availability.api';
import {
  type Balde,
  type Zoom,
  ZOOMS,
  baldes,
  fimDoPeriodo,
  inicioDoPeriodo,
  moverPeriodo,
  rotuloDoPeriodo,
} from './calendar.zoom';

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

/** O que cai numa celula: o motivo dominante e quantos dias ele cobre naquele balde. */
interface Celula {
  readonly kind: OccupancyKind;
  readonly dias: number;
}

interface Linha {
  readonly employeeId: string;
  readonly nome: string;
  readonly celulas: ReadonlyArray<Celula | null>;
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
        <p class="muted">Quem está fora, e quando, no período que você escolher.</p>
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

        <span class="escalas">
          @for (z of ZOOMS; track z.valor) {
            <button
              class="escala"
              type="button"
              [class.escala--ativa]="zoom() === z.valor"
              (click)="trocarZoom(z.valor)"
            >
              {{ z.rotulo }}
            </button>
          }
        </span>

        <span class="legenda">
          @for (item of legenda; track item.tipo) {
            <span class="chip" [attr.data-tipo]="item.tipo">{{ item.sigla }} {{ item.rotulo }}</span>
          }
        </span>
      </div>

      <div class="filtros">
        <label>
          <span>Seção</span>
          <select [(ngModel)]="secao" name="secao">
            <option value="">Todas</option>
            @for (s of secoes(); track s) { <option [value]="s">{{ s }}</option> }
          </select>
        </label>

        <label>
          <span>Função</span>
          <select [(ngModel)]="funcao" name="funcao">
            <option value="">Todas</option>
            @for (f of funcoes(); track f) { <option [value]="f">{{ f }}</option> }
          </select>
        </label>

        <label>
          <span>Motivo</span>
          <select [(ngModel)]="tipo" name="tipo">
            <option value="">Todos</option>
            @for (t of tipos(); track t) { <option [value]="t">{{ MOTIVOS[t] }}</option> }
          </select>
        </label>

        <label class="alternador">
          <input type="checkbox" [(ngModel)]="soCoincidencias" name="soCoincidencias" />
          <span>Só quem coincide com alguém</span>
        </label>
      </div>

      @if (coincidencias().length > 0) {
        <p class="aviso">
          <strong>{{ coincidencias().length }}</strong>
          {{ rotuloDeSobreposicao() }}:
          {{ coincidencias().join(', ') }}
          @if (secao() === '') { <span class="muted"> — filtre por seção para ver o que realmente conflita.</span> }
        </p>
      }

      @if (carregando()) {
        <p class="empty-state">Carregando…</p>
      } @else if (linhas().length === 0) {
        <p class="empty-state">
          @if (blocos().length === 0) {
            Ninguém de folga, férias ou serviço neste mês.
          } @else {
            Nenhuma ausência com esses filtros.
          }
        </p>
      } @else {
        <div class="rolagem">
          <table class="grade">
            <thead>
              <tr>
                <th class="pessoa">Pessoa</th>
                @for (coluna of colunas(); track coluna.rotulo) {
                  <th [class.fds]="coluna.fimDeSemana">
                    @if (coluna.sub) {
                      <span class="semana">{{ coluna.sub }}</span>
                    }
                    {{ coluna.rotulo }}
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (linha of linhas(); track linha.employeeId) {
                <tr>
                  <td class="pessoa">{{ linha.nome }}</td>
                  @for (celula of linha.celulas; track $index) {
                    <td [class.fds]="colunas()[$index].fimDeSemana">
                      @if (celula) {
                        <!--
                          No mes cabe uma letra por dia. Fora dele a celula vira um balde de
                          varios dias, e o numero de dias diz mais que a sigla: "8" num mes
                          responde "quanto tempo", que e a pergunta de quem olha de longe.
                        -->
                        <span
                          class="marca"
                          [attr.data-tipo]="celula.kind"
                          [title]="MOTIVOS[celula.kind] + ' · ' + celula.dias + (celula.dias === 1 ? ' dia' : ' dias')"
                        >
                          {{ colunas()[$index].unico ? SIGLAS[celula.kind] : celula.dias }}
                        </span>
                      }
                    </td>
                  }
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td class="pessoa">Pessoas fora</td>
                @for (total of totais(); track $index) {
                  <td [class.fds]="colunas()[$index].fimDeSemana" [class.pico]="total >= 3">
                    {{ total || '' }}
                  </td>
                }
              </tr>
            </tfoot>
          </table>
        </div>

        <p class="muted nota">
          A última linha conta quantas pessoas estão fora em cada coluna. Três ou mais
          aparece destacado — é onde a operação costuma apertar.
          @if (zoom() !== 'mes') {
            Fora do mês, cada célula mostra <strong>quantos dias</strong> a pessoa ficou
            fora naquele período.
          }
        </p>
      }
    </section>
  `,
  styles: `
    .controles { align-items: center; display: flex; flex-wrap: wrap; gap: var(--space-3); margin-bottom: var(--space-3); }
    .mes { font-family: var(--font-serif, inherit); font-size: 18px; min-width: 12ch; text-align: center; }
    .escalas { display: inline-flex; border: 1px solid var(--border); border-radius: var(--radius-sm); overflow: hidden; }
    .escala { background: var(--surface); border: 0; border-left: 1px solid var(--border); cursor: pointer; font: inherit; font-size: 13px; padding: 6px 12px; }
    .escala:first-child { border-left: 0; }
    .escala--ativa { background: var(--mamao-green-900); color: var(--text-inverse, #f7f3ea); font-weight: 600; }

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
    .semana { display: block; font-size: 10px; letter-spacing: .04em; opacity: .7; }
    .grade .pessoa { min-width: 150px; text-align: left; white-space: nowrap; font-size: 13px; padding: 4px 8px; }
    .grade .fds { background: var(--surface-sunken); }
    .grade tfoot td { color: var(--text-secondary); font-weight: 600; }
    .grade tfoot .pico { background: #fbe3e3; color: #a32020; }

    .marca { border-radius: 4px; display: inline-block; font-weight: 700; line-height: 18px; width: 18px; }
    .nota { font-size: 13px; margin-top: var(--space-3); }

    .filtros { align-items: end; display: flex; flex-wrap: wrap; gap: var(--space-3); margin-bottom: var(--space-3); }
    .filtros label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; min-width: 150px; }
    .filtros .alternador { align-items: center; flex-direction: row; gap: 8px; min-width: 0; }

    .aviso { background: #fdf1d8; border-radius: var(--radius-sm); color: #8a5a08; font-size: 14px; margin-bottom: var(--space-3); padding: var(--space-2) var(--space-3); }
  `,
})
export class CalendarPage implements OnInit {
  private readonly api = inject(AvailabilityApi);

  protected readonly MOTIVOS = MOTIVOS;
  protected readonly SIGLAS = SIGLAS;
  protected readonly legenda = (Object.keys(SIGLAS) as OccupancyKind[])
    .filter((t) => t !== 'Outro')
    .map((tipo) => ({ tipo, sigla: SIGLAS[tipo], rotulo: MOTIVOS[tipo] }));

  protected readonly ZOOMS = ZOOMS;
  protected readonly zoom = signal<Zoom>('mes');
  private readonly referencia = signal(inicioDoPeriodo(new Date(), 'mes'));

  // Filtros como signal (e nao campo simples) porque tudo abaixo deriva deles: mudar a
  // secao recalcula linhas, totais e coincidencias sem nenhuma chamada ao servidor.
  protected readonly secao = signal('');
  protected readonly funcao = signal('');
  protected readonly tipo = signal('');
  protected readonly soCoincidencias = signal(false);
  protected readonly blocos = signal<OccupancyResponse[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<ApiProblem | null>(null);

  /** Opcoes tiradas do proprio mes: filtro que oferece o que nao existe so atrapalha. */
  protected readonly secoes = computed(() => distintos(this.blocos().map((b) => b.departmentName)));
  protected readonly funcoes = computed(() => distintos(this.blocos().map((b) => b.positionName)));
  protected readonly tipos = computed(
    () => distintos(this.blocos().map((b) => b.kind)) as OccupancyKind[],
  );

  /** O que sobra depois dos filtros. Tudo daqui para baixo enxerga so isto. */
  private readonly filtrados = computed(() =>
    this.blocos().filter(
      (b) =>
        (this.secao() === '' || b.departmentName === this.secao()) &&
        (this.funcao() === '' || b.positionName === this.funcao()) &&
        (this.tipo() === '' || b.kind === this.tipo()),
    ),
  );

  protected readonly colunas = computed<Balde[]>(() => baldes(this.referencia(), this.zoom()));

  protected readonly rotuloDoMes = computed(() => rotuloDoPeriodo(this.referencia(), this.zoom()));

  /**
   * Uma linha por pessoa com ausencia, e cada dia recebe o motivo que o cobre. Quando dois
   * blocos caem no mesmo dia, o primeiro fica: a celula tem espaco para um, e a tela de
   * disponibilidade e quem mostra a lista completa daquele dia.
   */
  protected readonly linhas = computed<Linha[]>(() => {
    const colunas = this.colunas();
    const porPessoa = new Map<string, { nome: string; celulas: (Celula | null)[] }>();

    for (const bloco of this.filtrados()) {
      let linha = porPessoa.get(bloco.employeeId);
      if (!linha) {
        linha = { nome: bloco.employeeName, celulas: Array(colunas.length).fill(null) };
        porPessoa.set(bloco.employeeId, linha);
      }

      const inicio = data(bloco.startsOn);
      const fim = data(bloco.endsOn);

      colunas.forEach((balde, i) => {
        // Quantos dias DESTE bloco caem DENTRO deste balde. Num mes o resultado e 0 ou 1;
        // num balde de semana ou mes, e o tamanho da sobreposicao — que e exatamente o
        // numero que a celula mostra quando nao cabe uma letra por dia.
        const de = inicio > balde.inicio ? inicio : balde.inicio;
        const ate = fim < balde.fim ? fim : balde.fim;
        const dias = Math.floor((ate.getTime() - de.getTime()) / 86_400_000) + 1;
        if (dias <= 0) return;

        const atual = linha!.celulas[i];

        // Motivo dominante: quem cobre mais dias fica com a celula. Empate mantem o
        // primeiro, que e estavel porque a consulta vem ordenada por data.
        if (atual === null || dias > atual.dias)
          linha!.celulas[i] = { kind: bloco.kind, dias: dias + (atual?.dias ?? 0) };
        else
          linha!.celulas[i] = { kind: atual.kind, dias: atual.dias + dias };
      });
    }

    let linhas: Linha[] = [...porPessoa.entries()]
      .map(([employeeId, l]) => ({ employeeId, nome: l.nome, celulas: l.celulas }))
      .sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'));

    // "So quem coincide": esconde quem esta fora sozinho. E o corte que responde
    // "as ferias de quem batem com as de quem" sem obrigar a varrer a grade com o olho.
    if (this.soCoincidencias()) {
      const contagem = colunas.map((_, i) => linhas.filter((l) => l.celulas[i] !== null).length);
      linhas = linhas.filter((l) => l.celulas.some((c, i) => c !== null && contagem[i] > 1));
    }

    return linhas;
  });

  /** Dias em que duas ou mais pessoas do recorte atual estao fora ao mesmo tempo. */
  protected readonly coincidencias = computed(() =>
    this.colunas()
      .filter((_, i) => this.linhas().filter((l) => l.celulas[i] !== null).length > 1)
      .map((c) => c.rotulo),
  );

  protected readonly totais = computed(() =>
    this.colunas().map((_, i) => this.linhas().filter((l) => l.celulas[i] !== null).length),
  );

  ngOnInit(): void {
    void this.carregar();
  }

  /** "dias" so vale quando a coluna e um dia. Fora do mes, a coluna e semana ou mes. */
  protected rotuloDeSobreposicao(): string {
    const n = this.coincidencias().length;
    const unidade = this.zoom() === 'mes' ? 'dia' : this.zoom() === 'ano' ? 'mês' : 'semana';
    const plural = unidade === 'mês' ? 'meses' : `${unidade}s`;
    return `${n === 1 ? unidade : plural} com sobreposição`;
  }

  protected mover(passos: number): void {
    this.referencia.set(moverPeriodo(this.referencia(), this.zoom(), passos));
    void this.carregar();
  }

  /** Trocar de escala mantem a data que voce estava vendo, ancorada no periodo novo. */
  protected trocarZoom(valor: Zoom): void {
    this.zoom.set(valor);
    this.referencia.set(inicioDoPeriodo(this.referencia(), valor));
    void this.carregar();
  }

  private async carregar(): Promise<void> {
    this.carregando.set(true);
    this.erro.set(null);

    const inicio = this.referencia();
    const fim = fimDoPeriodo(inicio, this.zoom());

    try {
      this.blocos.set(await this.api.occupancies(iso(inicio), iso(fim)));
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }


}

/** Valores distintos, sem vazios, em ordem — para popular um filtro. */
function distintos(valores: readonly (string | null | undefined)[]): string[] {
  return [...new Set(valores.filter((v): v is string => !!v))].sort((a, b) => a.localeCompare(b, 'pt-BR'));
}

/** Data do servidor (yyyy-MM-dd) como data LOCAL, sem passar por UTC. */
function data(iso: string): Date {
  return new Date(iso + 'T00:00:00');
}

/** Data local em ISO. `toISOString` converteria para UTC e no Brasil voltaria um dia. */
function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
