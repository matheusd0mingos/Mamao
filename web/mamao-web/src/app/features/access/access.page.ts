import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type {
  ApiProblem,
  EmployeeListItem,
  InviteResponse,
} from '../../core/http/api.types';
import { EmployeesApi } from '../employees/employees.api';
import { InvitesApi } from './invites.api';

/** Uma linha da tela: a pessoa e a situação de acesso dela, já cruzadas. */
interface LinhaDeAcesso {
  readonly employeeId: string;
  readonly nome: string;
  readonly cargo: string;
  readonly email: string | null;
  readonly convite: InviteResponse | null;
}

/**
 * Quem tem acesso ao Mamão.
 *
 * A tela lista PESSOAS, não convites — o RH pensa "preciso que a Ana entre", não "preciso
 * criar um convite". O cruzamento entre funcionário (módulo People) e convite (módulo
 * Identity) é feito aqui, no cliente, e de propósito: juntar no servidor exigiria uma
 * consulta cruzando os dois schemas, que é justamente o atalho que a separação por módulo
 * existe para impedir. São dezenas de linhas; o custo é zero.
 *
 * Convidar 3 pessoas-chave é ativação; convidar o efetivo inteiro é dependência — e é o que
 * P1 protege. Ver docs/produto/mvp-e-posicionamento.md#p1.
 */
@Component({
  selector: 'mamao-access',
  imports: [FormsModule, RouterLink],
  template: `
    <header class="head">
      <div>
        <h1>Acessos</h1>
        <p class="muted">
          Quem entra no Mamão. Comece pelas pessoas-chave — coordenador, supervisor, RH.
          O sistema funciona por inteiro com você sozinho aqui dentro.
        </p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    @if (aviso(); as texto) {
      <div class="alert alert--sucesso">{{ texto }}</div>
    }

    <div class="card">
      @if (carregando()) {
        <p class="empty-state">Carregando…</p>
      } @else if (linhas().length === 0) {
        <div class="empty-state">
          <p><strong>Ninguém cadastrado ainda.</strong></p>
          <p>Traga a equipe primeiro; o convite vem depois.</p>
          <a class="btn btn--primary" routerLink="/pessoas/importar">Importar planilha</a>
        </div>
      } @else {
        <div class="table-wrap">
          <table class="data">
            <thead>
              <tr>
                <th>Pessoa</th>
                <th>E-mail</th>
                <th>Acesso</th>
                <th class="acao">&nbsp;</th>
              </tr>
            </thead>
            <tbody>
              @for (linha of linhas(); track linha.employeeId) {
                <tr>
                  <td>
                    <strong>{{ linha.nome }}</strong>
                    <span class="muted cargo">{{ linha.cargo }}</span>
                  </td>

                  <td class="muted">
                    @if (linha.email) {
                      {{ linha.email }}
                    } @else if (editando() === linha.employeeId) {
                      <input
                        [(ngModel)]="emailDigitado"
                        name="emailDigitado"
                        type="email"
                        placeholder="e-mail para o convite"
                        aria-label="E-mail para o convite"
                      />
                    } @else {
                      <span class="sem-email">sem e-mail</span>
                    }
                  </td>

                  <td>
                    @if (linha.convite; as convite) {
                      <span class="badge" [class]="classeDaSituacao(convite.status)">
                        {{ rotuloDaSituacao(convite.status) }}
                      </span>
                      @if (convite.status === 'Pendente') {
                        <span class="muted expira">expira {{ prazo(convite.expiresAt) }}</span>
                      }
                    } @else {
                      <span class="muted">—</span>
                    }
                  </td>

                  <td class="acao">
                    @if (linha.convite?.status === 'Aceito') {
                      <span class="muted">nada a fazer</span>
                    } @else if (linha.convite?.status === 'Pendente') {
                      <button class="link" type="button" (click)="reenviar(linha.convite!)">Reenviar</button>
                      <button class="link link--perigo" type="button" (click)="cancelar(linha.convite!)">
                        Cancelar
                      </button>
                    } @else if (editando() === linha.employeeId) {
                      <select [(ngModel)]="papel" name="papel" aria-label="Papel">
                        @for (opcao of papeis; track opcao.valor) {
                          <option [value]="opcao.valor">{{ opcao.rotulo }}</option>
                        }
                      </select>
                      <button
                        class="btn btn--primary"
                        type="button"
                        [disabled]="ocupado() || !enderecoUtilizavel(linha)"
                        (click)="convidar(linha)"
                      >
                        Enviar
                      </button>
                      <button class="link" type="button" (click)="fecharEdicao()">Cancelar</button>
                    } @else {
                      <button class="link" type="button" (click)="abrirEdicao(linha)">Convidar</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    <p class="muted volta"><a routerLink="/pessoas">← Voltar para Pessoas</a></p>
  `,
  styles: `
    .head { margin-bottom: var(--space-5); max-width: 640px; }
    .head p { margin: var(--space-1) 0 0; }
    .cargo { display: block; font-size: 13px; }
    .sem-email { font-style: italic; }
    .expira { display: block; font-size: 12px; margin-top: 2px; }
    .acao { text-align: right; white-space: nowrap; }
    .acao select, td input {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      font: inherit; margin-right: var(--space-2); padding: var(--space-1) var(--space-2);
    }
    td input { min-width: 220px; }
    .link {
      background: none; border: 0; color: var(--mamao-green-700); cursor: pointer; font: inherit;
      font-size: 14px; margin-left: var(--space-3); padding: 0; text-decoration: underline;
    }
    .link--perigo { color: var(--status-danger-fg); }
    .volta { margin-top: var(--space-5); }
    .volta a { color: var(--text-secondary); text-decoration: none; }
  `,
})
export class AccessPage implements OnInit {
  private readonly employees = inject(EmployeesApi);
  private readonly invites = inject(InvitesApi);

  readonly carregando = signal(true);
  readonly ocupado = signal(false);
  readonly erro = signal<ApiProblem | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly editando = signal<string | null>(null);

  private readonly pessoas = signal<EmployeeListItem[]>([]);
  private readonly convites = signal<InviteResponse[]>([]);

  emailDigitado = '';
  papel = 'Employee';

  readonly papeis = [
    { valor: 'Employee', rotulo: 'Funcionário' },
    { valor: 'Manager', rotulo: 'Gestor' },
    { valor: 'Hr', rotulo: 'RH' },
    { valor: 'ItManager', rotulo: 'Gerente de TI' },
    { valor: 'Owner', rotulo: 'Proprietário' },
  ];

  /**
   * O cruzamento é por e-mail porque é a única chave que os dois módulos compartilham —
   * e ela é única nos dois lados, então não há ambiguidade.
   */
  readonly linhas = computed<LinhaDeAcesso[]>(() => {
    const porEmail = new Map(this.convites().map((c) => [c.email.toLowerCase(), c]));

    return this.pessoas().map((pessoa) => ({
      employeeId: pessoa.id,
      nome: pessoa.fullName,
      cargo: pessoa.positionName,
      email: pessoa.email,
      convite: pessoa.email ? (porEmail.get(pessoa.email.toLowerCase()) ?? null) : null,
    }));
  });

  ngOnInit(): void {
    void this.carregar();
  }

  rotuloDaSituacao(status: InviteResponse['status']): string {
    switch (status) {
      case 'Pendente':
        return 'Convidado';
      case 'Aceito':
        return 'Tem acesso';
      case 'Expirado':
        return 'Convite expirado';
      case 'Cancelado':
        return 'Convite cancelado';
    }
  }

  classeDaSituacao(status: InviteResponse['status']): string {
    if (status === 'Aceito') return 'badge--success';
    if (status === 'Pendente') return 'badge--neutral';
    return 'badge--warning';
  }

  /** "em 6 dias" diz mais que uma data que a pessoa precisa comparar com hoje. */
  prazo(iso: string): string {
    const dias = Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000);
    if (dias <= 0) return 'hoje';
    return dias === 1 ? 'amanhã' : `em ${dias} dias`;
  }

  abrirEdicao(linha: LinhaDeAcesso): void {
    this.editando.set(linha.employeeId);
    this.emailDigitado = linha.email ?? '';
    this.papel = 'Employee';
  }

  fecharEdicao(): void {
    this.editando.set(null);
    this.emailDigitado = '';
  }

  /**
   * Botão desabilitado enquanto não há endereço utilizável. A versão anterior deixava
   * clicar e não fazia nada — o pior tipo de resposta, porque a pessoa clica de novo.
   */
  enderecoUtilizavel(linha: LinhaDeAcesso): boolean {
    const endereco = (linha.email ?? this.emailDigitado).trim();
    return endereco.length > 4 && endereco.includes('@');
  }

  async convidar(linha: LinhaDeAcesso): Promise<void> {
    const endereco = (linha.email ?? this.emailDigitado).trim();
    if (!this.enderecoUtilizavel(linha)) return;

    await this.executar(async () => {
      // Sem e-mail no cadastro: o convite também preenche a ficha. Senão a pessoa
      // digitaria o mesmo endereço de novo no próximo convite.
      if (!linha.email) {
        const pessoa = await this.employees.get(linha.employeeId);
        await this.employees.update(linha.employeeId, {
          fullName: pessoa.fullName,
          positionId: pessoa.positionId,
          departmentId: pessoa.departmentId,
          managerId: pessoa.managerId,
          email: endereco,
        });
      }

      await this.invites.create({
        email: endereco,
        fullName: linha.nome,
        role: this.papel,
        employeeId: linha.employeeId,
      });

      this.aviso.set(`Convite enviado para ${endereco}.`);
      this.fecharEdicao();
    });
  }

  async reenviar(convite: InviteResponse): Promise<void> {
    await this.executar(async () => {
      await this.invites.resend(convite.id);
      this.aviso.set(`Convite reenviado para ${convite.email}. O link anterior deixou de valer.`);
    });
  }

  async cancelar(convite: InviteResponse): Promise<void> {
    await this.executar(async () => {
      await this.invites.revoke(convite.id);
      this.aviso.set(`Convite de ${convite.email} cancelado.`);
    });
  }

  private async executar(acao: () => Promise<unknown>): Promise<void> {
    this.ocupado.set(true);
    this.erro.set(null);
    this.aviso.set(null);

    try {
      await acao();
      await this.carregar();
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.ocupado.set(false);
    }
  }

  private async carregar(): Promise<void> {
    this.carregando.set(true);

    try {
      const [pagina, convites] = await Promise.all([
        this.employees.list('', false, 1, 200, null),
        this.invites.list(),
      ]);

      this.pessoas.set(pagina.items ?? []);
      this.convites.set(convites);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }
}
