import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type {
  ApiProblem,
  MissionResponse,
  MissionSuggestion,
  RotationPolicyResponse,
  RotationTiebreak,
} from '../../core/http/api.types';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { MissionsApi } from './missions.api';

/** Mesmo rotulo humano da tela de disponibilidade — o enum vem como texto do servidor. */
/**
 * Os desempates, com o nome que a pessoa da secao usa. O texto de apoio importa tanto
 * quanto a opcao: quem configura isso decide quem vai pegar servico no sabado.
 */
const DESEMPATES: ReadonlyArray<{ valor: RotationTiebreak; rotulo: string; ajuda: string }> = [
  {
    valor: 'Antiguidade',
    rotulo: 'Antiguidade',
    ajuda: 'Mais antigo primeiro: a ordem do cargo e, dentro dela, o tempo de casa.',
  },
  {
    valor: 'Modernidade',
    rotulo: 'Modernidade',
    ajuda: 'Mais moderno primeiro. É o costume da escala de serviço em unidade militar.',
  },
  {
    valor: 'Sorteio',
    rotulo: 'Sorteio',
    ajuda: 'Sorteia uma vez por missão. Não muda se você recarregar a tela.',
  },
  {
    valor: 'Alfabetica',
    rotulo: 'Ordem alfabética',
    ajuda: 'Previsível — e por isso injusta a longo prazo com quem tem nome no começo.',
  },
];

const MOTIVOS: Record<string, string> = {
  Ferias: 'férias',
  Folga: 'folga',
  Falta: 'falta',
  Afastamento: 'afastamento',
  Servico: 'serviço',
  Missao: 'missão',
  Outro: 'outro compromisso',
};

/**
 * "Preciso de 20 pessoas amanha."
 *
 * A tela mostra o funil inteiro — considerados, impedidos, elegiveis — e a ordem do
 * rodizio COM o motivo de cada posicao. O motivo nao e enfeite: e o que o gestor mostra
 * para quem ficou de fora. Sem ele, "o sistema escolheu" vira desconfianca na segunda
 * escala e abandono na terceira.
 *
 * O sistema sugere; quem decide e a pessoa. Por isso a selecao vem marcada mas continua
 * editavel ate confirmar.
 */
@Component({
  selector: 'mamao-missions',
  imports: [FormsModule, HasPermissionDirective],
  template: `
    <header class="head">
      <div>
        <h1>Escala</h1>
        <p class="muted">Diga quantos e quando. O Mamão diz quem pode.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    @if (politica(); as p) {
      <section class="card">
        <h2>Como o Mamão desempata</h2>
        <p class="muted dica">
          A ordem principal é sempre quem participou menos. Isto aqui decide o que acontece
          <strong>no empate</strong> — e é a regra que você explica para quem ficou de fora.
        </p>

        <form class="politica" (ngSubmit)="salvarPolitica()">
          <label>
            <span>No empate, entra primeiro</span>
            <select [(ngModel)]="desempate" name="desempate">
              @for (d of DESEMPATES; track d.valor) {
                <option [value]="d.valor">{{ d.rotulo }}</option>
              }
            </select>
            <small class="muted">{{ ajudaDoDesempate() }}</small>
          </label>

          <label>
            <span>Descanso depois do serviço</span>
            <input type="number" [(ngModel)]="descanso" name="descanso" min="0" max="60" />
            <small class="muted">Em dias. Zero desliga a regra.</small>
          </label>

          <label>
            <span>Quando não descansou</span>
            <select [(ngModel)]="descansoImpede" name="descansoImpede" [disabled]="descanso === 0">
              <option [ngValue]="false">Evitar — vai para o fim da fila</option>
              <option [ngValue]="true">Impedir — sai da lista</option>
            </select>
          </label>

          <label>
            <span>Janela do histórico</span>
            <input type="number" [(ngModel)]="janela" name="janela" min="7" max="730" />
            <small class="muted">Em dias. Quantas participações contam para o rodízio.</small>
          </label>

          <div class="politica__acoes" *mamaoHasPermission="'settings.write'">
            <button class="btn btn--primary" type="submit" [disabled]="salvando()">
              Salvar política
            </button>
            @if (politicaSalva()) {
              <span class="salvo">Salvo.</span>
            }
          </div>
        </form>
      </section>
    }

    <section class="card" *mamaoHasPermission="'schedule.write'">
      <h2>Nova missão</h2>
      <form class="form" (ngSubmit)="criar()">
        <label class="form__larga">
          <span>O que é</span>
          <input [(ngModel)]="nome" name="nome" placeholder="Formatura" maxlength="160" required />
        </label>
        <label>
          <span>Quando</span>
          <input type="date" [(ngModel)]="quando" name="quando" required />
        </label>
        <label>
          <span>Das</span>
          <input type="time" [(ngModel)]="horaDe" name="horaDe" />
        </label>
        <label>
          <span>Até</span>
          <input type="time" [(ngModel)]="horaAte" name="horaAte" />
        </label>
        <label>
          <span>Quantas pessoas</span>
          <input type="number" [(ngModel)]="quantas" name="quantas" min="1" max="500" required />
        </label>
        <div class="form__acoes">
          <button class="btn btn--primary" type="submit" [disabled]="salvando()">Criar</button>
        </div>
      </form>
    </section>

    @if (missoes().length === 0) {
      <p class="empty-state">Nenhuma missão pela frente.</p>
    } @else {
      <section class="card">
        <h2>Próximas</h2>
        <ul class="lista">
          @for (missao of missoes(); track missao.id) {
            <li>
              <button class="link-missao" type="button" (click)="montar(missao)">
                <strong>{{ missao.name }}</strong>
                <span class="muted">
                  {{ data(missao.on) }}{{ missao.startsAt ? ' · ' + hora(missao.startsAt) : '' }} ·
                  {{ missao.assignedCount }}/{{ missao.requiredPeople }} escaladas
                </span>
              </button>
              <span class="etiqueta" [class.etiqueta--ok]="missao.status === 'Confirmada'">
                {{ missao.status }}
              </span>
            </li>
          }
        </ul>
      </section>
    }

    @if (selecionada(); as missao) {
      <section class="card">
        <h2>{{ missao.name }} — {{ data(missao.on) }}</h2>

        @if (sugestao(); as s) {
          <div class="funil">
            <div><strong>{{ s.totalConsidered }}</strong><span class="muted">considerados</span></div>
            <div><strong class="fora">{{ s.blocked.length }}</strong><span class="muted">indisponíveis</span></div>
            <div><strong>{{ s.eligible.length }}</strong><span class="muted">elegíveis</span></div>
            <div><strong>{{ escolhidos().size }}</strong><span class="muted">de {{ s.requiredPeople }} vagas</span></div>
          </div>

          <table class="tabela">
            <thead>
              <tr>
                <th></th>
                <th>Pessoa</th>
                <th class="num">Vezes</th>
                <th>Situação</th>
              </tr>
            </thead>
            <tbody>
              @for (pessoa of s.eligible; track pessoa.employeeId) {
                <tr [class.dentro]="escolhidos().has(pessoa.employeeId)">
                  <td>
                    <input
                      type="checkbox"
                      [checked]="escolhidos().has(pessoa.employeeId)"
                      (change)="alternar(pessoa.employeeId)"
                    />
                  </td>
                  <td>{{ pessoa.name }}</td>
                  <td class="num">{{ pessoa.participations }}</td>
                  <td class="muted">{{ pessoa.reason }}</td>
                </tr>
              }
              @for (bloqueado of s.blocked; track bloqueado.employeeId) {
                <tr class="bloqueado">
                  <td>—</td>
                  <td>{{ bloqueado.name }}</td>
                  <td class="num"></td>
                  <td class="motivo">
                    {{ bloqueado.restriction ?? MOTIVOS[bloqueado.reason ?? ''] ?? bloqueado.reason }}
                  </td>
                </tr>
              }
            </tbody>
          </table>

          <div class="acoes" *mamaoHasPermission="'schedule.write'">
            <button class="btn btn--primary" type="button" [disabled]="salvando()" (click)="confirmar(missao)">
              Confirmar escala
            </button>
            <span class="muted">
              {{ escolhidos().size }} de {{ s.requiredPeople }}. Confirmar bloqueia a agenda de
              quem foi escalado.
            </span>
          </div>
        } @else {
          <p class="empty-state">Montando…</p>
        }
      </section>
    }
  `,
  styles: `
    .form { display: grid; gap: var(--space-3); grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); align-items: end; }
    .form label { display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
    .form__larga { grid-column: 1 / -1; }
    .form__acoes { grid-column: 1 / -1; }

    .lista { list-style: none; margin: 0; padding: 0; }
    .lista li { display: flex; align-items: center; gap: var(--space-3); padding: 10px 0; border-top: 1px solid var(--border); min-width: 0; }
    .lista li:first-child { border-top: 0; }
    .link-missao { min-width: 0; background: none; border: 0; cursor: pointer; display: flex; flex-direction: column; font: inherit; gap: 2px; padding: 0; text-align: left; flex: 1; }
    .etiqueta { font-size: 12px; color: var(--text-secondary); white-space: nowrap; }
    .etiqueta--ok { color: var(--status-success-fg); font-weight: 600; }

    .funil { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: var(--space-3); margin: var(--space-3) 0; }
    .funil div { display: flex; flex-direction: column; }
    .funil strong { font-size: 26px; line-height: 1.1; }
    .funil .fora { color: var(--status-danger-fg); }

    .tabela { border-collapse: collapse; width: 100%; font-size: 14px; }
    .tabela th { color: var(--text-secondary); font-weight: 500; padding: 8px; text-align: left; }
    .tabela td { border-top: 1px solid var(--border); padding: 8px; }
    .tabela .num { text-align: right; }
    .tabela tr.dentro { background: var(--status-success-bg); }
    .tabela tr.bloqueado { color: var(--text-secondary); }
    .tabela .motivo { color: var(--status-danger-fg); }

    .acoes { align-items: center; display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-3); }

    .dica { margin-bottom: var(--space-3); }
    .politica { display: grid; gap: var(--space-3); grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); align-items: start; }
    .politica label { display: flex; flex-direction: column; font-size: 14px; gap: 4px; }
    .politica small { line-height: 1.35; }
    .politica__acoes { align-items: center; display: flex; gap: var(--space-3); grid-column: 1 / -1; }
    .salvo { color: var(--status-success-fg); font-size: 13px; }
  `,
})
export class MissionsPage implements OnInit {
  private readonly api = inject(MissionsApi);

  protected readonly MOTIVOS = MOTIVOS;
  protected readonly DESEMPATES = DESEMPATES;

  protected readonly missoes = signal<MissionResponse[]>([]);
  protected readonly selecionada = signal<MissionResponse | null>(null);
  protected readonly sugestao = signal<MissionSuggestion | null>(null);
  protected readonly escolhidos = signal<Set<string>>(new Set());
  protected readonly salvando = signal(false);
  protected readonly erro = signal<ApiProblem | null>(null);
  protected readonly politica = signal<RotationPolicyResponse | null>(null);
  protected readonly politicaSalva = signal(false);

  protected desempate: RotationTiebreak = 'Antiguidade';
  protected descanso = 0;
  protected descansoImpede = false;
  protected janela = 90;

  protected nome = '';
  protected quando = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
  protected horaDe = '07:00';
  protected horaAte = '09:00';
  protected quantas = 20;

  ngOnInit(): void {
    void this.carregar();
    void this.carregarPolitica();
  }

  private async carregarPolitica(): Promise<void> {
    try {
      const p = await this.api.policy();
      this.politica.set(p);
      this.desempate = p.tiebreak;
      this.descanso = p.minRestDays;
      this.descansoImpede = p.restBlocks;
      this.janela = p.windowDays;
    } catch {
      // Sem a política a tela continua servindo: a sugestão usa o padrão do servidor.
      this.politica.set(null);
    }
  }

  protected ajudaDoDesempate(): string {
    return DESEMPATES.find((d) => d.valor === this.desempate)?.ajuda ?? '';
  }

  protected async salvarPolitica(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    this.politicaSalva.set(false);

    try {
      this.politica.set(
        await this.api.savePolicy({
          tiebreak: this.desempate,
          minRestDays: Number(this.descanso),
          restBlocks: this.descansoImpede,
          windowDays: Number(this.janela),
        }),
      );
      this.politicaSalva.set(true);

      // Se havia uma missão aberta, a sugestão dela acabou de mudar de ordem — remontar é
      // o que torna o efeito da configuração visível na hora.
      const aberta = this.selecionada();
      if (aberta) await this.montar(aberta);
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  private async carregar(): Promise<void> {
    try {
      this.missoes.set(await this.api.list());
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected async criar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const criada = await this.api.create({
        name: this.nome,
        on: this.quando,
        requiredPeople: this.quantas,
        startsAt: this.horaDe || null,
        endsAt: this.horaAte || null,
        departmentId: null,
        notes: null,
      });
      this.nome = '';
      await this.carregar();
      await this.montar(criada);
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  /**
   * Abre a missao e ja traz a sugestao com os primeiros marcados. Vir marcado e o ponto:
   * o caminho curto e confirmar, e mexer e a excecao — mas mexer continua possivel ate o
   * ultimo segundo, porque quem responde pela escala e o gestor.
   */
  protected async montar(missao: MissionResponse): Promise<void> {
    this.selecionada.set(missao);
    this.sugestao.set(null);
    this.erro.set(null);

    try {
      const s = await this.api.suggestion(missao.id);
      this.sugestao.set(s);

      // Ja escalado manda; so quando ninguem foi escalado ainda a sugestao decide.
      const jaEscalados = (missao.assignedIds ?? []) as string[];
      const sugeridos = s.eligible.filter((p) => p.suggested).map((p) => p.employeeId as string);
      this.escolhidos.set(new Set<string>(jaEscalados.length > 0 ? jaEscalados : sugeridos));
    } catch (e) {
      this.erro.set(e as ApiProblem);
    }
  }

  protected alternar(id: string): void {
    const proximo = new Set(this.escolhidos());
    proximo.has(id) ? proximo.delete(id) : proximo.add(id);
    this.escolhidos.set(proximo);
  }

  protected async confirmar(missao: MissionResponse): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      await this.api.assign(missao.id, [...this.escolhidos()]);
      await this.api.confirm(missao.id);
      this.selecionada.set(null);
      this.sugestao.set(null);
      await this.carregar();
    } catch (e) {
      this.erro.set(e as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  protected data(iso: string): string {
    const [a, m, d] = iso.split('-');
    return `${d}/${m}/${a}`;
  }

  protected hora(valor: string): string {
    return valor.slice(0, 5);
  }
}
