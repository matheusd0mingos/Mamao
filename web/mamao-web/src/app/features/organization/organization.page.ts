import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type { ApiProblem, DepartmentNode, PositionResponse } from '../../core/http/api.types';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { OrganizationApi } from './organization.api';

/**
 * Setores e cargos numa tela so. Sao dois cadastros pequenos que o gestor mexe junto,
 * quase sempre uma vez — separar em duas rotas seria dar a cada um mais peso de navegacao
 * do que ele merece.
 *
 * A arvore de setores e desenhada por indentacao a partir de `depth`, que vem pronto do
 * servidor: montar a hierarquia no navegador seria refazer trabalho que a consulta ja fez.
 */
@Component({
  selector: 'mamao-organization',
  imports: [FormsModule, RouterLink, HasPermissionDirective],
  template: `
    <header class="head">
      <div>
        <h1>Estrutura</h1>
        <p class="muted">Setores e cargos da sua empresa.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <div class="colunas">
      <!-- SETORES ------------------------------------------------------------- -->
      <section class="card painel">
        <h2>Setores</h2>
        <p class="muted dica">
          Opcional. Crie quando servir para alguma coisa — filtrar a equipe, montar escala
          por turno, definir quem aprova.
        </p>

        @if (carregando()) {
          <p class="empty-state">Carregando…</p>
        } @else if (setores().length === 0) {
          <p class="empty-state">Nenhum setor ainda.</p>
        } @else {
          <ul class="arvore">
            @for (setor of setores(); track setor.id) {
              <li [style.padding-left.px]="setor.depth * 20">
                <span class="arvore__nome">{{ setor.name }}</span>
                <span class="muted arvore__contagem">
                  {{ rotuloDePessoas(setor) }}
                </span>
                <button
                  *mamaoHasPermission="'people.write'"
                  class="link-perigo"
                  type="button"
                  (click)="excluirSetor(setor)"
                >
                  Excluir
                </button>
              </li>
            }
          </ul>
        }

        <form *mamaoHasPermission="'people.write'" class="novo" (ngSubmit)="criarSetor()">
          <input
            [(ngModel)]="nomeDoSetor"
            name="nomeDoSetor"
            placeholder="Nome do setor"
            maxlength="120"
            aria-label="Nome do setor"
          />
          <select [(ngModel)]="paiDoSetor" name="paiDoSetor" aria-label="Dentro de">
            <option [ngValue]="null">Nível principal</option>
            @for (setor of setores(); track setor.id) {
              <option [ngValue]="setor.id">{{ '— '.repeat(setor.depth) }}{{ setor.name }}</option>
            }
          </select>
          <button class="btn btn--ghost" type="submit" [disabled]="salvando()">Adicionar</button>
        </form>
      </section>

      <!-- CARGOS -------------------------------------------------------------- -->
      <section class="card painel">
        <h2>Cargos</h2>
        <p class="muted dica">
          A importação de planilha cria os cargos que encontrar. Aqui você corrige e
          completa.
        </p>

        @if (carregando()) {
          <p class="empty-state">Carregando…</p>
        } @else if (cargos().length === 0) {
          <p class="empty-state">Nenhum cargo ainda.</p>
        } @else {
          <ul class="lista">
            @for (cargo of cargos(); track cargo.id) {
              <li>
                <span>{{ cargo.name }}</span>
                <span class="muted arvore__contagem">{{ rotuloDeOcupantes(cargo) }}</span>
                <button
                  *mamaoHasPermission="'people.write'"
                  class="link-perigo"
                  type="button"
                  (click)="excluirCargo(cargo)"
                >
                  Excluir
                </button>
              </li>
            }
          </ul>
        }

        <form *mamaoHasPermission="'people.write'" class="novo" (ngSubmit)="criarCargo()">
          <input
            [(ngModel)]="nomeDoCargo"
            name="nomeDoCargo"
            placeholder="Nome do cargo"
            maxlength="120"
            aria-label="Nome do cargo"
          />
          <button class="btn btn--ghost" type="submit" [disabled]="salvando()">Adicionar</button>
        </form>
      </section>
    </div>

    <p class="muted volta"><a routerLink="/pessoas">← Voltar para Pessoas</a></p>
  `,
  styles: `
    .head { margin-bottom: var(--space-5); }
    .head p { margin: var(--space-1) 0 0; }
    .colunas { display: grid; gap: var(--space-5); grid-template-columns: 1fr 1fr; }
    .painel { padding: var(--space-5); }
    .painel h2 { font-size: 16px; margin: 0 0 var(--space-2); }
    .dica { font-size: 13px; margin: 0 0 var(--space-4); }
    .arvore, .lista { list-style: none; margin: 0 0 var(--space-4); padding: 0; }
    .arvore li, .lista li {
      align-items: center; border-bottom: 1px solid var(--border); display: flex;
      gap: var(--space-3); padding: var(--space-2) 0;
    }
    .arvore__nome { font-weight: 500; }
    .arvore__contagem { font-size: 13px; margin-left: auto; white-space: nowrap; }
    .novo { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .novo input, .novo select {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      flex: 1 1 140px; font: inherit; min-width: 0; padding: var(--space-2) var(--space-3);
    }
    .volta { margin-top: var(--space-5); }
    .volta a { color: var(--text-secondary); text-decoration: none; }
    @media (max-width: 900px) { .colunas { grid-template-columns: 1fr; } }
  `,
})
export class OrganizationPage implements OnInit {
  private readonly api = inject(OrganizationApi);

  readonly setores = signal<DepartmentNode[]>([]);
  readonly cargos = signal<PositionResponse[]>([]);
  readonly carregando = signal(true);
  readonly salvando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  nomeDoSetor = '';
  paiDoSetor: string | null = null;
  nomeDoCargo = '';

  ngOnInit(): void {
    void this.carregar();
  }

  /** O acumulado da subárvore só aparece quando difere: repetir "3 · 3" seria ruído. */
  rotuloDePessoas(setor: DepartmentNode): string {
    const direto = setor.employeeCount === 1 ? '1 pessoa' : `${setor.employeeCount} pessoas`;

    return setor.subtreeEmployeeCount === setor.employeeCount
      ? direto
      : `${direto} · ${setor.subtreeEmployeeCount} com os de baixo`;
  }

  rotuloDeOcupantes(cargo: PositionResponse): string {
    return cargo.employeeCount === 1 ? '1 pessoa' : `${cargo.employeeCount} pessoas`;
  }

  async criarSetor(): Promise<void> {
    if (!this.nomeDoSetor.trim()) return;

    await this.executar(async () => {
      await this.api.createDepartment({ name: this.nomeDoSetor.trim(), parentId: this.paiDoSetor });
      this.nomeDoSetor = '';
    });
  }

  async criarCargo(): Promise<void> {
    if (!this.nomeDoCargo.trim()) return;

    await this.executar(async () => {
      await this.api.createPosition({ name: this.nomeDoCargo.trim() });
      this.nomeDoCargo = '';
    });
  }

  async excluirSetor(setor: DepartmentNode): Promise<void> {
    await this.executar(() => this.api.deleteDepartment(setor.id));
  }

  async excluirCargo(cargo: PositionResponse): Promise<void> {
    await this.executar(() => this.api.deletePosition(cargo.id));
  }

  /**
   * O servidor e quem sabe se um setor tem gente dentro ou se o cargo esta em uso, e ele
   * devolve a frase pronta. A tela nao tenta adivinhar antes de perguntar.
   */
  private async executar(acao: () => Promise<unknown>): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);

    try {
      await acao();
      await this.carregar();
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.salvando.set(false);
    }
  }

  private async carregar(): Promise<void> {
    this.carregando.set(true);

    try {
      const [setores, cargos] = await Promise.all([
        this.api.listDepartments(),
        this.api.listPositions(),
      ]);

      this.setores.set(setores);
      this.cargos.set(cargos);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.carregando.set(false);
    }
  }
}
