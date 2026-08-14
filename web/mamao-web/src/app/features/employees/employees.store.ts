import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiProblem, DepartmentNode, EmployeeListItem } from '../../core/http/api.types';
import { OrganizationApi } from '../organization/organization.api';
import { EmployeesApi } from './employees.api';

/**
 * Store por feature com signals. Cobre a complexidade real do Mamao sem actions,
 * reducers, effects e selectors. Ver docs/adr/0008-frontend-angular.md.
 */
@Injectable({ providedIn: 'root' })
export class EmployeesStore {
  private readonly api = inject(EmployeesApi);
  private readonly organizacao = inject(OrganizationApi);

  readonly items = signal<EmployeeListItem[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly includeInactive = signal(false);
  readonly departmentId = signal<string | null>(null);

  /**
   * Setores ficam aqui e nao na pagina porque o filtro precisa deles antes da primeira
   * pintura. Carregam uma vez: e um cadastro de dezenas de linhas que quase nunca muda.
   */
  readonly departments = signal<DepartmentNode[]>([]);
  readonly loading = signal(false);
  readonly error = signal<ApiProblem | null>(null);

  readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const result = await this.api.list(
        this.search(),
        this.includeInactive(),
        this.page(),
        this.pageSize(),
        this.departmentId(),
      );

      this.items.set(result.items ?? []);
      this.total.set(result.total);
    } catch (problem) {
      this.error.set(problem as ApiProblem);
    } finally {
      this.loading.set(false);
    }
  }

  async setDepartment(id: string | null): Promise<void> {
    this.departmentId.set(id);
    this.page.set(1);
    await this.load();
  }

  /** Chamado uma vez pela tela; falha aqui nao pode derrubar a listagem. */
  async loadDepartments(): Promise<void> {
    try {
      this.departments.set(await this.organizacao.listDepartments());
    } catch {
      this.departments.set([]);
    }
  }

  async setSearch(term: string): Promise<void> {
    this.search.set(term);
    this.page.set(1);
    await this.load();
  }

  async toggleInactive(): Promise<void> {
    this.includeInactive.update((value) => !value);
    this.page.set(1);
    await this.load();
  }

  async goToPage(page: number): Promise<void> {
    this.page.set(Math.min(Math.max(page, 1), this.totalPages()));
    await this.load();
  }
}
