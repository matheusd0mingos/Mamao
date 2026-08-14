import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApiProblem, EmployeeResponse } from '../../core/http/api.types';
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

        <div class="field" [class.field--invalid]="!!erroDoCampo('positionName')">
          <label for="positionName">Cargo</label>
          <input id="positionName" formControlName="positionName" />
          @if (erroDoCampo('positionName'); as msg) {
            <span class="field__error">{{ msg }}</span>
          }
        </div>

        @if (!id()) {
          <div class="linha">
            <div class="field" [class.field--invalid]="!!erroDoCampo('hiredOn')">
              <label for="hiredOn">Data de admissão</label>
              <input id="hiredOn" type="date" formControlName="hiredOn" />
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

        <div class="acoes">
          <button type="submit" class="btn btn--primary" [disabled]="salvando()">
            {{ salvando() ? 'Salvando…' : 'Salvar' }}
          </button>
          <a class="btn btn--ghost" routerLink="/pessoas">Cancelar</a>
        </div>
      </form>
    </div>
  `,
  styles: `
    .volta { margin-bottom: var(--space-3); }
    .volta a { color: var(--text-secondary); font-size: 14px; text-decoration: none; }
    .form { margin-top: var(--space-4); max-width: 560px; padding: var(--space-5); }
    .linha { display: grid; gap: var(--space-4); grid-template-columns: 1fr 1fr; }
    .acoes { display: flex; gap: var(--space-3); margin-top: var(--space-2); }
    @media (max-width: 640px) { .linha { grid-template-columns: 1fr; } }
  `,
})
export class EmployeeFormPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(EmployeesApi);
  private readonly store = inject(EmployeesStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly id = signal<string | null>(null);
  readonly salvando = signal(false);
  readonly erro = signal<ApiProblem | null>(null);

  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    positionName: ['', [Validators.required]],
    hiredOn: [new Date().toISOString().slice(0, 10), [Validators.required]],
    code: [''],
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) return;

    this.id.set(id);
    void this.carregar(id);
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
      const { fullName, positionName, hiredOn, code } = this.form.getRawValue();
      const id = this.id();

      if (id) {
        await this.api.update(id, { fullName, positionName });
      } else {
        await this.api.create({ fullName, positionName, hiredOn, code: code.trim() || null });
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
      this.form.patchValue({
        fullName: pessoa.fullName,
        positionName: pessoa.positionName,
        hiredOn: pessoa.hiredOn,
        code: pessoa.code ?? '',
      });
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    }
  }
}
