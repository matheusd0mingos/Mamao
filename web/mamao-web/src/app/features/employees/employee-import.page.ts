import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import type {
  ApiProblem,
  EmployeeImportFormat,
  EmployeeImportPreview,
  EmployeeImportRow,
  EmployeeImportRowStatus,
} from '../../core/http/api.types';
import { EmployeesApi } from './employees.api';
import { EmployeesStore } from './employees.store';

/**
 * Importar a planilha que a empresa JA TEM e o que decide o trial: ninguem digita 40
 * pessoas num formulario para avaliar um sistema. Ver docs/produto/mvp-e-posicionamento.md#p3.
 *
 * A tela tem tres momentos, e a ordem e o produto:
 *  1. o formato, explicado ANTES de escolher arquivo (com modelo para baixar);
 *  2. a previa, linha a linha, com o erro apontando o numero da linha do Excel;
 *  3. a gravacao, so depois de a pessoa ver o que vai acontecer.
 *
 * Nada e gravado no passo 2. O arquivo e reenviado na confirmacao: guardar o upload no
 * servidor entre os dois passos exigiria estado temporario, limpeza e um caminho novo de
 * vazamento entre tenants — caro demais para economizar um upload de 50 kB.
 */
@Component({
  selector: 'mamao-employee-import',
  imports: [RouterLink],
  template: `
    <nav class="volta"><a routerLink="/pessoas">← Pessoas</a></nav>

    <header class="head">
      <div>
        <h1>Importar funcionários</h1>
        <p class="muted">Traga a planilha que você já usa. Nada é salvo antes de você conferir.</p>
      </div>
    </header>

    @if (erro(); as problema) {
      <div class="alert alert--danger">{{ problema.detail }}</div>
    }

    <!-- 1. O FORMATO ------------------------------------------------------------- -->
    <section class="card bloco">
      <h2>1. Como o arquivo precisa estar</h2>
      <p class="muted">
        Um arquivo <strong>.csv</strong> com a primeira linha contendo os nomes das colunas.
        A ordem das colunas não importa, e reconhecemos vários nomes para a mesma coluna.
      </p>

      @if (formato(); as f) {
        <div class="table-wrap">
          <table class="data">
            <thead>
              <tr>
                <th>Coluna</th>
                <th>Obrigatória</th>
                <th>Nomes que reconhecemos</th>
              </tr>
            </thead>
            <tbody>
              @for (coluna of f.columns; track coluna.field) {
                <tr>
                  <td><strong>{{ coluna.label }}</strong></td>
                  <td>
                    @if (coluna.required) {
                      <span class="badge badge--neutral">Sim</span>
                    } @else {
                      <span class="muted">opcional</span>
                    }
                  </td>
                  <td class="nomes">
                    @for (nome of coluna.acceptedNames; track nome) {
                      <code>{{ nome }}</code>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <p class="muted nota">
          Datas em <code>dd/mm/aaaa</code> ou <code>aaaa-mm-dd</code>. Separado por ponto e
          vírgula ou por vírgula — os dois funcionam. Acentos são lidos mesmo se o arquivo
          não estiver em UTF-8. Até {{ f.maxRows.toLocaleString('pt-BR') }} linhas por arquivo.
        </p>

        <details class="exemplo">
          <summary>Ver um exemplo do conteúdo</summary>
          <pre>{{ f.example }}</pre>
        </details>

        <button class="btn btn--ghost" (click)="baixarModelo()" [disabled]="baixando()">
          {{ baixando() ? 'Preparando…' : 'Baixar modelo (.csv)' }}
        </button>
      } @else {
        <p class="muted">Carregando o formato…</p>
      }
    </section>

    <!-- 2. O ARQUIVO ------------------------------------------------------------- -->
    <section class="card bloco">
      <h2>2. Escolha o arquivo</h2>

      <div
        class="dropzone"
        [class.dropzone--ativa]="arrastando()"
        (dragover)="aoArrastar($event, true)"
        (dragleave)="aoArrastar($event, false)"
        (drop)="aoSoltar($event)"
      >
        <p>Arraste o arquivo aqui ou</p>
        <label class="btn btn--primary">
          Escolher arquivo
          <input type="file" accept=".csv,text/csv,text/plain" (change)="aoEscolher($event)" hidden />
        </label>
        @if (arquivo(); as a) {
          <p class="muted arquivo">{{ a.name }} — {{ tamanhoLegivel(a.size) }}</p>
        }
      </div>
    </section>

    <!-- 3. A PREVIA -------------------------------------------------------------- -->
    @if (lendo()) {
      <section class="card bloco"><p class="empty-state">Lendo o arquivo…</p></section>
    } @else if (previa(); as p) {
      <section class="card bloco">
        <h2>3. Confira antes de importar</h2>

        @for (aviso of p.warnings; track aviso) {
          <div class="alert alert--warning">{{ aviso }}</div>
        }

        @if (p.summary.total > 0) {
          <div class="resumo">
            <div class="resumo__item">
              <strong>{{ p.summary.ready }}</strong>
              <span>{{ p.summary.ready === 1 ? 'pronta para importar' : 'prontas para importar' }}</span>
            </div>
            @if (p.summary.invalid > 0) {
              <div class="resumo__item resumo__item--erro">
                <strong>{{ p.summary.invalid }}</strong>
                <span>com erro</span>
              </div>
            }
            @if (p.summary.duplicates > 0) {
              <div class="resumo__item resumo__item--aviso">
                <strong>{{ p.summary.duplicates }}</strong>
                <span>{{ p.summary.duplicates === 1 ? 'duplicada' : 'duplicadas' }}</span>
              </div>
            }
          </div>

          <!-- De onde veio cada campo. Sem isto, "Cargo" vazio parece bug do sistema. -->
          <p class="muted mapeamento">
            Colunas lidas:
            @for (coluna of p.columns; track coluna.field) {
              <span class="mapa">
                {{ coluna.label }}
                @if (coluna.header) {
                  ← <code>{{ coluna.header }}</code>
                } @else {
                  <span class="mapa__ausente">não encontrada</span>
                }
              </span>
            }
          </p>

          <div class="table-wrap">
            <table class="data">
              <thead>
                <tr>
                  <th>Linha</th>
                  <th>Nome</th>
                  <th>Cargo</th>
                  <th>Admissão</th>
                  <th>Matrícula</th>
                  <th>Situação</th>
                </tr>
              </thead>
              <tbody>
                @for (linha of p.rows; track linha.lineNumber) {
                  <tr [class.linha--problema]="linha.status !== 'Pronta'">
                    <td class="muted">{{ linha.lineNumber }}</td>
                    <td>{{ linha.fullName || '—' }}</td>
                    <td>{{ linha.positionName || '—' }}</td>
                    <td>{{ dataLegivel(linha) }}</td>
                    <td class="muted">{{ linha.code ?? '—' }}</td>
                    <td>
                      <span class="badge" [class]="classeDaSituacao(linha.status)">
                        {{ rotuloDaSituacao(linha.status) }}
                      </span>
                      @if (linha.errors.length) {
                        <span class="erros">{{ linha.errors.join(' ') }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <div class="acoes">
          <button
            class="btn btn--primary"
            [disabled]="p.summary.ready === 0 || importando()"
            (click)="confirmar()"
          >
            {{ importando() ? 'Importando…' : rotuloDoBotao() }}
          </button>
          <a class="btn btn--ghost" routerLink="/pessoas">Cancelar</a>
        </div>

        @if (p.summary.total > p.summary.ready) {
          <p class="muted nota">
            As linhas com erro não são importadas. Corrija na planilha e envie o arquivo de
            novo — as que já entraram não serão duplicadas se a matrícula estiver preenchida.
          </p>
        }
      </section>
    }
  `,
  styles: `
    .volta { margin-bottom: var(--space-3); }
    .volta a { color: var(--text-secondary); font-size: 14px; text-decoration: none; }
    .head { margin-bottom: var(--space-5); }
    .head p { margin: var(--space-1) 0 0; }
    .bloco { margin-bottom: var(--space-5); padding: var(--space-5); }
    .bloco h2 { font-size: 16px; margin: 0 0 var(--space-2); }
    .bloco > p { margin-top: 0; }
    .nomes code {
      background: var(--surface-sunken); border-radius: var(--radius-sm); font-size: 12px;
      margin-right: var(--space-2); padding: 1px var(--space-2); white-space: nowrap;
    }
    .nota { font-size: 13px; margin: var(--space-3) 0; }
    .nota code { font-size: 12px; }
    .exemplo { margin-bottom: var(--space-4); }
    .exemplo summary { color: var(--text-secondary); cursor: pointer; font-size: 14px; }
    .exemplo pre {
      background: var(--surface-sunken); border-radius: var(--radius-sm); font-size: 13px;
      margin-top: var(--space-3); overflow-x: auto; padding: var(--space-3) var(--space-4);
    }
    .dropzone {
      align-items: center; border: 1px dashed var(--border); border-radius: var(--radius-sm);
      display: flex; flex-direction: column; gap: var(--space-3); padding: var(--space-6);
      text-align: center;
    }
    .dropzone--ativa { background: var(--surface-sunken); border-color: var(--mamao-green-700); }
    .dropzone p { color: var(--text-secondary); margin: 0; }
    .dropzone .btn { cursor: pointer; }
    .arquivo { font-size: 13px; }
    .resumo { display: flex; flex-wrap: wrap; gap: var(--space-5); margin-bottom: var(--space-4); }
    .resumo__item { display: flex; flex-direction: column; }
    .resumo__item strong { font-size: 24px; line-height: 1.1; }
    .resumo__item span { color: var(--text-secondary); font-size: 13px; }
    .resumo__item--erro strong { color: var(--status-danger-fg); }
    .resumo__item--aviso strong { color: var(--status-warning-fg); }
    .mapeamento { font-size: 13px; margin-bottom: var(--space-4); }
    .mapa { display: inline-block; margin-right: var(--space-4); white-space: nowrap; }
    .mapa code { color: var(--text-primary); }
    .mapa__ausente { color: var(--status-danger-fg); }
    .linha--problema { background: var(--surface-sunken); }
    .erros { color: var(--status-danger-fg); display: block; font-size: 12px; margin-top: 2px; }
    .acoes { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-4); }
  `,
})
export class EmployeeImportPage implements OnInit {
  private readonly api = inject(EmployeesApi);
  private readonly store = inject(EmployeesStore);
  private readonly router = inject(Router);

  readonly formato = signal<EmployeeImportFormat | null>(null);
  readonly arquivo = signal<File | null>(null);
  readonly previa = signal<EmployeeImportPreview | null>(null);
  readonly erro = signal<ApiProblem | null>(null);
  readonly lendo = signal(false);
  readonly importando = signal(false);
  readonly baixando = signal(false);
  readonly arrastando = signal(false);

  /** O botao diz o numero: "Importar 38" e uma promessa verificavel, "Importar" nao e. */
  readonly rotuloDoBotao = computed(() => {
    const prontas = this.previa()?.summary.ready ?? 0;
    if (prontas === 0) return 'Nada para importar';
    return prontas === 1 ? 'Importar 1 pessoa' : `Importar ${prontas} pessoas`;
  });

  ngOnInit(): void {
    void this.carregarFormato();
  }

  aoEscolher(evento: Event): void {
    const escolhido = (evento.target as HTMLInputElement).files?.[0];
    if (escolhido) void this.analisar(escolhido);
  }

  aoArrastar(evento: DragEvent, ativo: boolean): void {
    evento.preventDefault();
    this.arrastando.set(ativo);
  }

  aoSoltar(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(false);

    const solto = evento.dataTransfer?.files?.[0];
    if (solto) void this.analisar(solto);
  }

  dataLegivel(linha: EmployeeImportRow): string {
    if (!linha.hiredOn) return '—';

    const [ano, mes, dia] = linha.hiredOn.split('-');
    return `${dia}/${mes}/${ano}`;
  }

  rotuloDaSituacao(status: EmployeeImportRowStatus): string {
    switch (status) {
      case 'Pronta':
        return 'Pronta';
      case 'Invalida':
        return 'Erro';
      case 'DuplicadaNoArquivo':
        return 'Repetida no arquivo';
      case 'DuplicadaNoCadastro':
        return 'Já cadastrada';
    }
  }

  classeDaSituacao(status: EmployeeImportRowStatus): string {
    if (status === 'Pronta') return 'badge--success';
    return status === 'Invalida' ? 'badge--danger' : 'badge--warning';
  }

  tamanhoLegivel(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} kB`
      : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  async baixarModelo(): Promise<void> {
    this.baixando.set(true);
    this.erro.set(null);

    try {
      const conteudo = await this.api.downloadTemplate();
      const url = URL.createObjectURL(conteudo);
      const link = document.createElement('a');

      link.href = url;
      link.download = 'modelo-funcionarios-mamao.csv';
      link.click();

      // Sem revoke, o blob fica na memoria da aba ate o reload.
      URL.revokeObjectURL(url);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.baixando.set(false);
    }
  }

  async confirmar(): Promise<void> {
    const escolhido = this.arquivo();
    if (!escolhido) return;

    this.importando.set(true);
    this.erro.set(null);

    try {
      await this.api.import(escolhido);
      await this.store.load();
      await this.router.navigate(['/pessoas']);
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.importando.set(false);
    }
  }

  private async carregarFormato(): Promise<void> {
    try {
      this.formato.set(await this.api.importFormat());
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    }
  }

  private async analisar(escolhido: File): Promise<void> {
    this.arquivo.set(escolhido);
    this.previa.set(null);
    this.erro.set(null);
    this.lendo.set(true);

    try {
      this.previa.set(await this.api.previewImport(escolhido));
    } catch (problema) {
      this.erro.set(problema as ApiProblem);
    } finally {
      this.lendo.set(false);
    }
  }
}
