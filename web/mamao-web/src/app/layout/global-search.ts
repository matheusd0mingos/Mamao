import { Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import type { SearchResult, SearchResultKind } from '../core/http/api.types';
import { TodayApi } from '../features/dashboard/today.api';
import { Icon, type IconName } from '../shared/ui/icon';

const ICONE: Record<SearchResultKind, IconName> = {
  Pessoa: 'pessoas',
  Missao: 'escala',
  Demanda: 'demandas',
  Setor: 'estrutura',
  Cargo: 'estrutura',
};

const ROTULO: Record<SearchResultKind, string> = {
  Pessoa: 'pessoa',
  Missao: 'missão',
  Demanda: 'demanda',
  Setor: 'seção',
  Cargo: 'cargo',
};

/**
 * Busca global.
 *
 * Passada a primeira semana ninguem navega por menu: a pessoa sabe o nome do que procura
 * e quer chegar la. Sem isto, achar "Ana" custa Pessoas -> filtro -> rolar.
 *
 * A rota de cada resultado e montada AQUI e nao vem do servidor: a API nao deveria
 * conhecer as rotas do frontend, senao renomear uma rota viraria deploy dos dois lados.
 */
@Component({
  selector: 'mamao-global-search',
  imports: [FormsModule, Icon],
  template: `
    <div class="busca">
      <label class="campo">
        <mamao-icon name="busca" [size]="16" />
        <input
          #entrada
          type="search"
          [(ngModel)]="termo"
          (ngModelChange)="procurar()"
          (focus)="aberto.set(true)"
          placeholder="Buscar  /"
          aria-label="Buscar pessoas, missões e demandas"
          autocomplete="off"
        />
      </label>

      @if (aberto() && termo.trim().length >= 2) {
        <div class="resultados">
          @if (buscando()) {
            <p class="vazio">Buscando…</p>
          } @else if (resultados().length === 0) {
            <p class="vazio">Nada encontrado para “{{ termo.trim() }}”.</p>
          } @else {
            @for (r of resultados(); track r.kind + r.id) {
              <button type="button" class="resultado" (click)="abrir(r)">
                <mamao-icon [name]="ICONE[r.kind]" [size]="16" />
                <span class="texto">
                  <strong>{{ r.title }}</strong>
                  <small>{{ ROTULO[r.kind] }}{{ r.subtitle ? ' · ' + r.subtitle : '' }}</small>
                </span>
              </button>
            }
            @if (truncado()) {
              <p class="vazio">Há mais resultados. Refine a busca.</p>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .busca { position: relative; }

    .campo { align-items: center; display: flex; gap: 8px; position: relative; }
    .campo mamao-icon { color: rgb(247 243 234 / 55%); left: 10px; position: absolute; }
    .campo input {
      background: rgb(255 255 255 / 8%);
      border: 1px solid rgb(255 255 255 / 14%);
      border-radius: var(--radius-sm);
      color: var(--text-on-dark);
      font: inherit;
      font-size: 14px;
      padding: 7px 10px 7px 32px;
      width: 100%;
    }
    .campo input::placeholder { color: rgb(247 243 234 / 45%); }
    /* O botao de limpar do Chrome vem azul e quadrado sobre o fundo escuro. */
    .campo input::-webkit-search-cancel-button { -webkit-appearance: none; appearance: none; }
    .campo input:focus-visible {
      background: rgb(255 255 255 / 12%);
      border-color: var(--mamao-yellow-500);
      box-shadow: none;
      outline: none;
    }

    .resultados {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      box-shadow: 0 12px 32px rgb(0 0 0 / 22%);
      left: 0;
      max-height: 60vh;
      /* Mais larga que a barra lateral: o subtitulo e o que distingue dois resultados de
         mesmo nome, e quebrado em tres linhas ele deixa de distinguir. */
      min-width: 340px;
      overflow-y: auto;
      position: absolute;
      top: calc(100% + 6px);
      z-index: 30;
    }

    .resultado {
      align-items: center;
      background: none;
      border: 0;
      border-top: 1px solid var(--border);
      cursor: pointer;
      display: flex;
      font: inherit;
      gap: 10px;
      padding: 9px 12px;
      text-align: left;
      width: 100%;
    }
    .resultado:first-child { border-top: 0; }
    .resultado:hover { background: var(--surface-muted, #f1ede4); }
    .resultado mamao-icon { color: var(--text-secondary); }
    .texto { display: flex; flex-direction: column; min-width: 0; }
    .texto strong { font-size: 14px; font-weight: 600; }
    .texto small { color: var(--text-secondary); font-size: 12px; }

    .vazio { color: var(--text-secondary); font-size: 13px; margin: 0; padding: 10px 12px; }
  `,
})
export class GlobalSearch {
  private readonly api = inject(TodayApi);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  private readonly entrada = viewChild<ElementRef<HTMLInputElement>>('entrada');

  protected readonly ICONE = ICONE;
  protected readonly ROTULO = ROTULO;

  protected readonly resultados = signal<SearchResult[]>([]);
  protected readonly truncado = signal(false);
  protected readonly buscando = signal(false);
  protected readonly aberto = signal(false);

  protected termo = '';

  /** Cresce a cada tecla e so a ULTIMA resposta vale: sem isto, uma busca lenta de tres
   *  letras chegaria depois da de cinco e sobrescreveria a lista certa. */
  private geracao = 0;
  private agendado?: ReturnType<typeof setTimeout>;

  @HostListener('document:keydown', ['$event'])
  protected atalho(evento: KeyboardEvent): void {
    if (evento.key === 'Escape') {
      this.aberto.set(false);
      return;
    }

    // "/" abre a busca — mas não quando a pessoa está digitando em outro campo, senão
    // seria impossível escrever uma data ou um nome com barra.
    const alvo = evento.target as HTMLElement | null;
    const digitando = alvo?.tagName === 'INPUT' || alvo?.tagName === 'TEXTAREA' || alvo?.isContentEditable;

    if (evento.key === '/' && !digitando) {
      evento.preventDefault();
      this.entrada()?.nativeElement.focus();
    }
  }

  @HostListener('document:click', ['$event'])
  protected foraDaCaixa(evento: MouseEvent): void {
    if (!this.host.nativeElement.contains(evento.target as Node)) this.aberto.set(false);
  }

  protected procurar(): void {
    const termo = this.termo.trim();

    clearTimeout(this.agendado);
    this.aberto.set(true);

    if (termo.length < 2) {
      this.resultados.set([]);
      this.buscando.set(false);
      return;
    }

    // 180ms: uma requisição por palavra digitada, não por tecla.
    this.agendado = setTimeout(() => void this.buscar(termo), 180);
  }

  private async buscar(termo: string): Promise<void> {
    const minha = ++this.geracao;
    this.buscando.set(true);

    try {
      const resposta = await this.api.search(termo);
      if (minha !== this.geracao) return;

      this.resultados.set(resposta.results);
      this.truncado.set(resposta.truncated);
    } catch {
      if (minha !== this.geracao) return;
      this.resultados.set([]);
    } finally {
      if (minha === this.geracao) this.buscando.set(false);
    }
  }

  protected abrir(r: SearchResult): void {
    this.aberto.set(false);
    this.termo = '';
    this.resultados.set([]);

    switch (r.kind) {
      case 'Pessoa':
        void this.router.navigate(['/pessoas', r.id]);
        break;
      case 'Missao':
        void this.router.navigate(['/escala']);
        break;
      case 'Demanda':
        void this.router.navigate(['/demandas']);
        break;
      default:
        void this.router.navigate(['/estrutura']);
        break;
    }
  }
}
