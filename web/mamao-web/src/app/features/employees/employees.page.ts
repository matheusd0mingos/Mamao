import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { EmployeesStore } from './employees.store';

@Component({
  selector: 'mamao-employees',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, HasPermissionDirective],
  template: `
    <header class="head">
      <div>
        <h1>Pessoas</h1>
        <p class="muted">{{ resumo() }}</p>
      </div>

      <div *mamaoHasPermission="'people.write'" class="head__acoes">
        <a class="btn btn--ghost" routerLink="/estrutura">Estrutura</a>
        <a class="btn btn--ghost" routerLink="/pessoas/importar">Importar planilha</a>
        <a class="btn btn--primary" routerLink="/pessoas/nova">Cadastrar funcionário</a>
      </div>
    </header>

    <div class="filtros">
      <input [formControl]="busca" placeholder="Buscar por nome, cargo ou matrícula" aria-label="Buscar" />
      @if (store.departments().length > 0) {
        <select
          [value]="store.departmentId() ?? ''"
          (change)="store.setDepartment($any($event.target).value || null)"
          aria-label="Filtrar por setor"
        >
          <option value="">Todos os setores</option>
          @for (setor of store.departments(); track setor.id) {
            <option [value]="setor.id">{{ '— '.repeat(setor.depth) }}{{ setor.name }}</option>
          }
        </select>
      }

      <label class="inativos">
        <input type="checkbox" [checked]="store.includeInactive()" (change)="store.toggleInactive()" />
        Mostrar desligados
      </label>
    </div>

    @if (store.error(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <div class="card">
      @if (store.loading()) {
        <p class="empty-state">Carregando…</p>
      } @else if (store.isEmpty()) {
        <!-- Vazio nunca e vazio: mostra o proximo passo concreto. -->
        <div class="empty-state">
          @if (store.search()) {
            <p>Nenhum funcionário encontrado para "{{ store.search() }}".</p>
          } @else {
            <p><strong>Sua equipe ainda não está aqui.</strong></p>
            <!--
              Importar vem primeiro de proposito: quem esta avaliando o Mamao ja tem a
              equipe numa planilha e nao vai digitar 40 pessoas para experimentar.
            -->
            <p>Traga a planilha que você já usa — leva menos de um minuto.</p>
            <div *mamaoHasPermission="'people.write'" class="empty-state__acoes">
              <a class="btn btn--primary" routerLink="/pessoas/importar">Importar planilha</a>
              <a class="btn btn--ghost" routerLink="/pessoas/nova">Cadastrar uma pessoa</a>
            </div>
          }
        </div>
      } @else {
        <div class="table-wrap">
          <table class="data">
            <thead>
              <tr>
                <th>Nome</th>
                <th>Cargo</th>
                <th>Setor</th>
                <th>Matrícula</th>
                <th>Admissão</th>
                <th>Situação</th>
              </tr>
            </thead>
            <tbody>
              @for (pessoa of store.items(); track pessoa.id) {
                <tr>
                  <td><a [routerLink]="['/pessoas', pessoa.id]">{{ pessoa.fullName }}</a></td>
                  <td>{{ pessoa.positionName }}</td>
                  <td class="muted">{{ pessoa.departmentName ?? '—' }}</td>
                  <td class="muted">{{ pessoa.code ?? '—' }}</td>
                  <td>{{ pessoa.hiredOn | date: 'dd/MM/yyyy' }}</td>
                  <td>
                    <span class="badge" [class.badge--success]="pessoa.isActive" [class.badge--neutral]="!pessoa.isActive">
                      {{ pessoa.isActive ? 'Ativo' : 'Desligado' }}
                    </span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (store.totalPages() > 1) {
          <div class="paginacao">
            <button class="btn btn--ghost" [disabled]="store.page() === 1" (click)="store.goToPage(store.page() - 1)">
              Anterior
            </button>
            <span class="muted">Página {{ store.page() }} de {{ store.totalPages() }}</span>
            <button
              class="btn btn--ghost"
              [disabled]="store.page() === store.totalPages()"
              (click)="store.goToPage(store.page() + 1)"
            >
              Próxima
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .head {
      align-items: flex-start; display: flex; flex-wrap: wrap; gap: var(--space-3);
      justify-content: space-between; margin-bottom: var(--space-5);
    }
    .head p { margin: var(--space-1) 0 0; }
    .filtros {
      align-items: center; display: flex; flex-wrap: wrap; gap: var(--space-3) var(--space-4);
      margin-bottom: var(--space-4);
    }
    .filtros select {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      font: inherit; padding: var(--space-2) var(--space-3);
    }
    .filtros input[type='text'], .filtros input:not([type]) {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      flex: 1 1 240px; font: inherit; max-width: 380px; min-width: 0;
      padding: var(--space-2) var(--space-3);
    }
    .inativos { align-items: center; color: var(--text-secondary); display: flex; font-size: 14px; gap: var(--space-2); white-space: nowrap; }
    .paginacao { align-items: center; display: flex; gap: var(--space-4); justify-content: center; padding: var(--space-4); }
    .head__acoes { display: flex; flex-wrap: wrap; gap: var(--space-3); }
    .empty-state__acoes {
      display: flex; flex-wrap: wrap; gap: var(--space-3); justify-content: center;
      margin-top: var(--space-4);
    }
  `,
})
export class EmployeesPage implements OnInit {
  readonly store = inject(EmployeesStore);

  /** "(s)" e preguica: o texto aparece em toda listagem e o usuario le todo dia. */
  readonly resumo = computed(() => {
    const total = this.store.total();
    if (total === 0) return 'Nenhuma pessoa cadastrada';
    return total === 1 ? '1 pessoa cadastrada' : `${total} pessoas cadastradas`;
  });
  readonly busca = new FormControl('', { nonNullable: true });

  constructor() {
    // RxJS onde e fluxo (busca com debounce); signal no resto.
    this.busca.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((termo) => void this.store.setSearch(termo));
  }

  ngOnInit(): void {
    void this.store.load();
    void this.store.loadDepartments();
  }
}
