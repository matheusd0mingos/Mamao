import { DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
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
        <p class="muted">{{ store.total() }} cadastrada(s)</p>
      </div>

      <button *mamaoHasPermission="'people.write'" class="btn btn--primary" routerLink="/pessoas/nova">
        Cadastrar funcionário
      </button>
    </header>

    <div class="filtros">
      <input [formControl]="busca" placeholder="Buscar por nome, cargo ou matrícula" aria-label="Buscar" />
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
            <p>Cadastre a primeira pessoa para o Mamão começar a fazer sentido.</p>
            <button *mamaoHasPermission="'people.write'" class="btn btn--primary" routerLink="/pessoas/nova">
              Cadastrar funcionário
            </button>
          }
        </div>
      } @else {
        <div class="table-wrap">
          <table class="data">
            <thead>
              <tr>
                <th>Nome</th>
                <th>Cargo</th>
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
    .head { align-items: flex-start; display: flex; justify-content: space-between; margin-bottom: var(--space-5); }
    .head p { margin: var(--space-1) 0 0; }
    .filtros { align-items: center; display: flex; gap: var(--space-4); margin-bottom: var(--space-4); }
    .filtros input[type='text'], .filtros input:not([type]) {
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-sm);
      font: inherit; max-width: 380px; padding: var(--space-2) var(--space-3); width: 100%;
    }
    .inativos { align-items: center; color: var(--text-secondary); display: flex; font-size: 14px; gap: var(--space-2); white-space: nowrap; }
    .paginacao { align-items: center; display: flex; gap: var(--space-4); justify-content: center; padding: var(--space-4); }
    .empty-state .btn { margin-top: var(--space-4); }
  `,
})
export class EmployeesPage implements OnInit {
  readonly store = inject(EmployeesStore);
  readonly busca = new FormControl('', { nonNullable: true });

  constructor() {
    // RxJS onde e fluxo (busca com debounce); signal no resto.
    this.busca.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((termo) => void this.store.setSearch(termo));
  }

  ngOnInit(): void {
    void this.store.load();
  }
}
