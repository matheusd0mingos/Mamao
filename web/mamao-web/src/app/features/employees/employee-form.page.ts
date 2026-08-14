import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type {
  ApiProblem,
  ContractResponse,
  DepartmentNode,
  EmployeeResponse,
  PositionResponse,
} from '../../core/http/api.types';
import { OrganizationApi } from '../organization/organization.api';
import { EmployeesApi } from './employees.api';
import { EmployeesStore } from './employees.store';

/**
 * Cadastro e edicao de funcionario. Pede o minimo de proposito — formacao, competencias e
 * certificacoes sao preenchimento progressivo, nos marcos seguintes.
 * Ver docs/produto/mvp-e-posicionamento.md#p3.
 */
@Component({
  selector: 'mamao-employee-form',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <nav class="volta"><a routerLink="/pessoas">← Pessoas</a></nav>

    <h1>{{ id() ? 'Editar funcionário' : 'Cadastrar funcionário' }}</h1>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <div class="card form">
      <form [formGroup]="form" (ngSubmit)="salvar()">
        <div class="field" [class.field--invalid]="!!erroDoCampo('fullName')">
          <label for="fullName">Nome completo</label>
          <input id="fullName" formControlName="fullName" />
          @if (erroDoCampo('fullName'); as msg) {
            <span class="field__error">{{ msg }}</span>
          }
        </div>

        <div class="field" [class.field--invalid]="!!erroDoCampo('positionId')">
          <label for="positionId">Cargo</label>

          <!--
            Lista e nao texto livre: o Marco 4 pergunta "quantos vigilantes cobrem este
            turno?", e essa conta nao existe em cima de texto onde "Vigilante" e
            "vigilante " sao dois cargos. Criar na hora fica logo ao lado para que a
            escolha nao vire um beco quando o cargo ainda nao existe.
          -->
          <div class="cargo">
            <select id="positionId" formControlName="positionId">
              <option value="">Selecione…</option>
              @for (cargo of cargos(); track cargo.id) {
                <option [value]="cargo.id">{{ cargo.name }}</option>
              }
            </select>
            <button type="button" class="btn btn--ghost" (click)="alternarNovoCargo()">
              {{ criandoCargo() ? 'Cancelar' : 'Novo cargo' }}
            </button>
          </div>

          @if (criandoCargo()) {
            <div class="cargo">
              <input
                [value]="nomeDoNovoCargo()"
                (input)="nomeDoNovoCargo.set($any($event.target).value)"
                placeholder="Ex.: Vigilante noturno"
                aria-label="Nome do novo cargo"
              />
              <button type="button" class="btn btn--primary" (click)="salvarNovoCargo()">
                Criar e usar
              </button>
            </div>
          }

          @if (erroDoCampo('positionId'); as msg) {
            <span class="field__error">{{ msg }}</span>
          }
        </div>

        <div class="field">
          <label for="departmentId">Setor <span class="muted">(opcional)</span></label>
          <select id="departmentId" formControlName="departmentId">
            <option value="">Sem setor</option>
            @for (setor of setores(); track setor.id) {
              <option [value]="setor.id">{{ '— '.repeat(setor.depth) }}{{ setor.name }}</option>
            }
          </select>
        </div>

        @if (!id()) {
          <div class="linha">
            <div class="field" [class.field--invalid]="!!erroDoCampo('hiredOn')">
              <label for="hiredOn">Data de admissão</label>
              <input id="hiredOn" type="date" formControlName="hiredOn" />

              <!--
                O <input type="date"> nativo renderiza no locale do NAVEGADOR, que nao
                controlamos: um usuario brasileiro com Chrome em ingles ve MM/DD e digita
                errado. O eco abaixo mostra a data interpretada, sempre em pt-BR, para
                nao restar ambiguidade. O DatePicker proprio do design system resolve isso
                de vez quando o TimeGrid chegar (Marco 4).
              -->
              @if (dataInterpretada(); as legivel) {
                <span class="muted eco">{{ legivel }}</span>
              }

              @if (erroDoCampo('hiredOn'); as msg) {
                <span class="field__error">{{ msg }}</span>
              }
            </div>

            <div class="field" [class.field--invalid]="!!erroDoCampo('code')">
              <label for="code">Matrícula <span class="muted">(opcional)</span></label>
              <input id="code" formControlName="code" />
              @if (erroDoCampo('code'); as msg) {
                <span class="field__error">{{ msg }}</span>
              }
            </div>
          </div>
        }

        <div class="field" [class.field--invalid]="!!erroDoCampo('email')">
          <label for="email">E-mail <span class="muted">(opcional)</span></label>
          <input id="email" type="email" formControlName="email" autocomplete="off" />
          <!--
            Diz para que serve, senao vira campo que ninguem preenche: e por ele que sai
            aviso de escala e documento vencendo, e e o endereco do convite de login (V1.5).
          -->
          <span class="muted eco">
            Usado para avisar a pessoa e, mais adiante, para convidá-la a acessar o Mamão.
          </span>
          @if (erroDoCampo('email'); as msg) {
            <span class="field__error">{{ msg }}</span>
          }
        </div>

        <div class="acoes">
          <button type="submit" class="btn btn--primary" [disabled]="salvando()">
            {{ salvando() ? 'Salvando…' : 'Salvar' }}
          </button>
          <a class="btn btn--ghost" routerLink="/pessoas">Cancelar</a>
        </div>
      </form>
    </div>

    @if (id()) {
      <div class="card form">
        <h2>Contrato</h2>
        <p class="muted dica">
          O regime decide qual lei se aplica a férias e jornada desta pessoa. Numa mesma
          empresa pode haver regimes diferentes.
        </p>

        <!--
          Alertas, nunca bloqueios: a operação real tem exceção e nós não conhecemos a
          convenção coletiva do cliente. Ver docs/adr/0015 e 0017.
        -->
        @for (alerta of alertas(); track alerta.code) {
          <div class="alert alert--warning">{{ alerta.message }}</div>
        }

        <form [formGroup]="contrato" (ngSubmit)="salvarContrato()">
          <div class="linha">
            <div class="field">
              <label for="regime">Regime de vínculo</label>
              <select id="regime" formControlName="regime">
                @for (opcao of regimes; track opcao.valor) {
                  <option [value]="opcao.valor">{{ opcao.rotulo }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="scheduleType">Jornada</label>
              <select id="scheduleType" formControlName="scheduleType">
                @for (opcao of jornadas; track opcao.valor) {
                  <option [value]="opcao.valor">{{ opcao.rotulo }}</option>
                }
              </select>
            </div>
          </div>

          <div class="linha">
            <div class="field">
              <label for="weeklyHours">Carga semanal (horas)</label>
              <input id="weeklyHours" type="number" step="0.5" min="1" max="84" formControlName="weeklyHours" />
            </div>

            @if (pedeAcordo()) {
              <div class="field">
                <label for="compensationAgreementOn">Data do acordo de compensação</label>
                <input id="compensationAgreementOn" type="date" formControlName="compensationAgreementOn" />
                <span class="muted eco">Acordo individual escrito ou norma coletiva.</span>
              </div>
            }
          </div>

          <div class="field">
            <label for="notes">Observações <span class="muted">(opcional)</span></label>
            <input id="notes" formControlName="notes" maxlength="500" />
          </div>

          <div class="acoes">
            <button type="submit" class="btn btn--primary" [disabled]="salvandoContrato()">
              {{ salvandoContrato() ? 'Salvando…' : 'Salvar contrato' }}
            </button>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    .volta { margin-bottom: var(--space-3); }
    .volta a { color: var(--text-secondary); font-size: 14px; text-decoration: none; }
    .form { margin-top: var(--space-4); max-width: 560px; padding: var(--space-5); }
    .linha { display: grid; gap: var(--space-4); grid-template-columns: 1fr 1fr; }
    .acoes { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-2); }
    .eco { font-size: 13px; }
    .form h2 { font-size: 16px; margin: 0 0 var(--space-2); }
    .dica { font-size: 13px; margin: 0 0 var(--space-4); }
    .form select, .form input[type='number'] {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      font: inherit; padding: var(--space-2) var(--space-3); width: 100%;
    }
    .cargo { display: flex; gap: var(--space-2); }
    .cargo select, .cargo input { flex: 1 1 auto; min-width: 0; }
    .cargo .btn { flex: 0 0 auto; white-space: nowrap; }
    @media (max-width: 640px) { .linha { grid-template-columns: 1fr; } }
  `,
})
export class EmployeeFormPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(EmployeesApi);
  private readonly organizacao = inject(OrganizationApi);
  private readonly store = inject(EmployeesStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly id = signal<string | null>(null);

  /** Preservado na edicao: o gestor e definido em outra tela, e o PUT nao pode zera-lo. */
  private readonly managerId = signal<string | null>(null);
  private readonly admissao = signal(new Date().toISOString().slice(0, 10));
  readonly salvando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);
  readonly cargos = signal<PositionResponse[]>([]);
  readonly setores = signal<DepartmentNode[]>([]);
  readonly criandoCargo = signal(false);
  readonly nomeDoNovoCargo = signal('');
  readonly salvandoContrato = signal(false);
  readonly alertas = signal<ContractResponse['warnings']>([]);

  readonly regimes = [
    { valor: 'Clt', rotulo: 'CLT' },
    { valor: 'EstatutarioFederal', rotulo: 'Estatutário federal (Lei 8.112)' },
    { valor: 'EstatutarioLocal', rotulo: 'Estatutário estadual/municipal' },
    { valor: 'Militar', rotulo: 'Militar' },
    { valor: 'Outro', rotulo: 'Outro (PJ, estágio, voluntário)' },
  ];

  readonly jornadas = [
    { valor: 'Administrativo', rotulo: 'Administrativo (seg–sex)' },
    { valor: 'CincoDois', rotulo: '5×2' },
    { valor: 'SeisUm', rotulo: '6×1' },
    { valor: 'DozePorTrintaESeis', rotulo: '12×36' },
    { valor: 'Rodizio', rotulo: 'Rodízio (escala de serviço)' },
    { valor: 'Outro', rotulo: 'Outro' },
  ];

  readonly contrato = this.fb.nonNullable.group({
    regime: ['Clt'],
    scheduleType: ['CincoDois'],
    weeklyHours: [44],
    compensationAgreementOn: [''],
    notes: [''],
  });

  /** O acordo só faz sentido em 12×36 sob CLT — pedir em militar seria ruído. */
  readonly pedeAcordo = computed(() => this.jornadaAtual() === 'DozePorTrintaESeis' && this.regimeAtual() === 'Clt');
  private readonly jornadaAtual = signal('CincoDois');
  private readonly regimeAtual = signal('Clt');

  /** Data que o formulario realmente vai enviar, escrita por extenso em pt-BR. */
  readonly dataInterpretada = computed(() => {
    const iso = this.admissao();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(iso)) return null;

    const [ano, mes, dia] = iso.split('-').map(Number);
    const data = new Date(ano, mes - 1, dia);
    if (Number.isNaN(data.getTime())) return null;

    return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'full' }).format(data);
  });

  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    positionId: ['', [Validators.required]],
    departmentId: [''],
    hiredOn: [new Date().toISOString().slice(0, 10), [Validators.required]],
    code: [''],
    email: [''],
  });

  constructor() {
    // O valor do controle vira signal para o eco reagir sem subscribe manual.
    this.form.controls.hiredOn.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((valor) => this.admissao.set(valor));

    this.contrato.controls.scheduleType.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((valor) => this.jornadaAtual.set(valor));

    this.contrato.controls.regime.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((valor) => this.regimeAtual.set(valor));
  }

  ngOnInit(): void {
    void this.carregarOpcoes();

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) return;

    this.id.set(id);
    void this.carregar(id);
    void this.carregarContrato(id);
  }

  async salvarContrato(): Promise<void> {
    const id = this.id();
    if (!id) return;

    this.salvandoContrato.set(true);
    this.erro.set(null);

    try {
      const valores = this.contrato.getRawValue();
      const salvo = await this.api.saveContract(id, {
        regime: valores.regime as ContractResponse['regime'],
        scheduleType: valores.scheduleType as ContractResponse['scheduleType'],
        weeklyHours: Number(valores.weeklyHours),
        // Vazio vira a data de admissão, resolvido no servidor: perguntar a mesma data
        // duas vezes no cadastro é atrito sem retorno.
        startsOn: '0001-01-01',
        compensationAgreementOn: valores.compensationAgreementOn || null,
        notes: valores.notes.trim() || null,
      });

      this.alertas.set(salvo.warnings);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.salvandoContrato.set(false);
    }
  }

  private async carregarContrato(id: string): Promise<void> {
    try {
      const atual = await this.api.getContract(id);

      this.contrato.patchValue({
        regime: atual.regime,
        scheduleType: atual.scheduleType,
        weeklyHours: atual.weeklyHours,
        compensationAgreementOn: atual.compensationAgreementOn ?? '',
        notes: atual.notes ?? '',
      });

      this.jornadaAtual.set(atual.scheduleType);
      this.regimeAtual.set(atual.regime);
      this.alertas.set(atual.warnings);
    } catch {
      // Sem contrato ainda é o caso normal de quem acabou de ser cadastrado ou importado:
      // o formulário abre nos padrões e a pessoa preenche.
    }
  }

  alternarNovoCargo(): void {
    this.criandoCargo.update((aberto) => !aberto);
    this.nomeDoNovoCargo.set('');
  }

  /** Cria o cargo e ja o deixa selecionado — senao a pessoa cria e tem que procurar na lista. */
  async salvarNovoCargo(): Promise<void> {
    const nome = this.nomeDoNovoCargo().trim();
    if (nome.length < 2) return;

    try {
      const criado = await this.organizacao.createPosition({ name: nome });
      this.cargos.update((atuais) => [...atuais, criado].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR')));
      this.form.controls.positionId.setValue(criado.id);
      this.criandoCargo.set(false);
      this.nomeDoNovoCargo.set('');
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    }
  }

  private async carregarOpcoes(): Promise<void> {
    try {
      const [cargos, setores] = await Promise.all([
        this.organizacao.listPositions(),
        this.organizacao.listDepartments(),
      ]);

      this.cargos.set(cargos);
      this.setores.set(setores);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    }
  }

  erroDoCampo(campo: string): string | null {
    const doServidor = this.erro()?.fieldErrors?.[campo]?.[0];
    if (doServidor) return doServidor;

    const control = this.form.get(campo);
    if (control?.invalid && (control.dirty || control.touched)) {
      return 'Campo obrigatório.';
    }

    return null;
  }

  async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.salvando.set(true);
    this.erro.set(null);

    try {
      const { fullName, positionId, departmentId, hiredOn, code, email } = this.form.getRawValue();
      const id = this.id();
      const setor = departmentId || null;
      const endereco = email.trim() || null;

      if (id) {
        await this.api.update(id, {
          fullName,
          positionId,
          departmentId: setor,
          managerId: this.managerId(),
          email: endereco,
        });
      } else {
        await this.api.create({
          fullName,
          positionId,
          hiredOn,
          code: code.trim() || null,
          departmentId: setor,
          managerId: null,
          email: endereco,
        });
      }

      await this.store.load();
      await this.router.navigate(['/pessoas']);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  private async carregar(id: string): Promise<void> {
    try {
      const pessoa: EmployeeResponse = await this.api.get(id);
      this.managerId.set(pessoa.managerId);
      this.form.patchValue({
        fullName: pessoa.fullName,
        positionId: pessoa.positionId,
        departmentId: pessoa.departmentId ?? '',
        hiredOn: pessoa.hiredOn,
        code: pessoa.code ?? '',
        email: pessoa.email ?? '',
      });
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    }
  }
}
