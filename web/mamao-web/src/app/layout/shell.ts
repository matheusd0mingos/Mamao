import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { GlobalSearch } from './global-search';
import { Icon } from '../shared/ui/icon';
import { rotuloDoPapel } from '../core/auth/role-label';
import { SessionService } from '../core/auth/session.service';

/**
 * Casca da aplicacao: sidebar + conteudo. Densidade adulta — sistema usado 8h por dia
 * precisa de mais linhas por tela que uma landing page.
 */
@Component({
  selector: 'mamao-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, GlobalSearch],
  template: `
    <div class="shell">
      <aside class="sidebar">
        <div class="brand">
          <span class="brand__mark">ã</span>
          <span class="brand__text">
            mamão
            <small>pessoas, atividades e disponibilidade</small>
          </span>
        </div>

        <mamao-global-search class="busca" />

        <!-- O icone acompanha o rotulo, nunca o substitui: navegacao so com desenho
             obriga o usuario a decorar o que cada um significa. -->
        <nav>
          <a routerLink="/inicio" routerLinkActive="active">
            <mamao-icon name="inicio" [size]="18" /> Visão geral
          </a>
          <a routerLink="/pessoas" routerLinkActive="active">
            <mamao-icon name="pessoas" [size]="18" /> Pessoas
          </a>
          <a routerLink="/disponibilidade" routerLinkActive="active">
            <mamao-icon name="disponibilidade" [size]="18" /> Disponibilidade
          </a>
          <a routerLink="/calendario" routerLinkActive="active">
            <mamao-icon name="calendario" [size]="18" /> Calendário
          </a>
          <a routerLink="/demandas" routerLinkActive="active">
            <mamao-icon name="demandas" [size]="18" /> Demandas
          </a>
          <a routerLink="/escala" routerLinkActive="active">
            <mamao-icon name="escala" [size]="18" /> Escala
          </a>
        </nav>

        <div class="sidebar__foot">
          <div class="tenant">{{ session.tenantName() }}</div>
          <div class="role muted">{{ rotuloDoPapel(session.role()) }}</div>
          <button type="button" class="btn btn--ghost" (click)="session.logout()">
          <mamao-icon name="sair" [size]="16" /> Sair
        </button>
        </div>
      </aside>

      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    /* min-width: 0 nas duas colunas: sem isso um filho largo (tabela) se recusa a
       encolher e empurra a pagina inteira para a direita. */
    .shell { display: grid; grid-template-columns: 248px minmax(0, 1fr); min-height: 100vh; }
    .shell > * { min-width: 0; }

    .sidebar {
      background: var(--mamao-green-900);
      color: var(--text-on-dark);
      display: flex;
      flex-direction: column;
      padding: var(--space-5) var(--space-4);
    }

    .brand { align-items: center; display: flex; gap: var(--space-3); margin-bottom: var(--space-6); }
    .brand__mark { color: var(--mamao-yellow-500); font-family: var(--font-display); font-size: 34px; line-height: 1; }
    .brand__text { display: flex; flex-direction: column; font-family: var(--font-display); font-size: 22px; line-height: 1.1; }
    .brand__text small { font-family: var(--font-ui); font-size: 10px; letter-spacing: 0.08em; opacity: 0.7; text-transform: uppercase; }

    .busca { display: block; margin-bottom: var(--space-4); }

    nav { display: flex; flex-direction: column; gap: var(--space-1); flex: 1; }
    nav a {
      align-items: center;
      border-radius: var(--radius-sm);
      color: var(--text-on-dark);
      display: flex;
      gap: 10px;
      opacity: 0.82;
      padding: var(--space-2) var(--space-3);
      text-decoration: none;
    }
    nav a:hover { background: rgb(255 255 255 / 8%); opacity: 1; }
    nav a.active { background: var(--mamao-yellow-500); color: var(--mamao-green-900); font-weight: 500; opacity: 1; }

    .sidebar__foot { border-top: 1px solid rgb(255 255 255 / 12%); display: flex; flex-direction: column; gap: var(--space-2); padding-top: var(--space-4); }
    .tenant { font-weight: 500; }
    .role { color: rgb(247 243 234 / 65%); font-size: 13px; }
    .sidebar__foot .btn { align-items: center; border-color: rgb(255 255 255 / 25%); color: var(--text-on-dark); display: flex; gap: 8px; justify-content: center; }

    .content { padding: var(--space-6); min-width: 0; }

    @media (max-width: 860px) {
      .shell { grid-template-columns: minmax(0, 1fr); }

      .sidebar {
        align-items: center;
        display: grid;
        gap: var(--space-3);
        grid-template-columns: auto 1fr auto;
        padding: var(--space-3) var(--space-4);
      }

      .brand { margin-bottom: 0; }
      /* Segunda linha inteira, em vez de sumir: no celular a busca vale MAIS, porque
         rolar uma lista de cem pessoas com o polegar e pior que no desktop. */
      .busca { grid-column: 1 / -1; margin-bottom: 0; }
      .brand__text { font-size: 18px; }
      .brand__text small { display: none; }

      /* A navegacao rola em si mesma quando nao couber, em vez de esticar o cabecalho. */
      nav { flex-direction: row; overflow-x: auto; scrollbar-width: none; }
      nav::-webkit-scrollbar { display: none; }
      nav a { white-space: nowrap; }

      .sidebar__foot { border: 0; flex-direction: row; align-items: center; padding: 0; }
      .tenant, .role { display: none; }   /* o nome da empresa volta no cabecalho da pagina */

      .content { padding: var(--space-4); }
    }
  `,
})
export class Shell {
  readonly session = inject(SessionService);
  readonly rotuloDoPapel = rotuloDoPapel;
}
