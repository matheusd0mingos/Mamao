import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionService } from '../core/auth/session.service';

/**
 * Casca da aplicacao: sidebar + conteudo. Densidade adulta — sistema usado 8h por dia
 * precisa de mais linhas por tela que uma landing page.
 */
@Component({
  selector: 'mamao-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="sidebar">
        <div class="brand">
          <span class="brand__mark">ã</span>
          <span class="brand__text">
            mamão
            <small>gestão sem complicação</small>
          </span>
        </div>

        <nav>
          <a routerLink="/inicio" routerLinkActive="active">Visão geral</a>
          <a routerLink="/pessoas" routerLinkActive="active">Pessoas</a>
        </nav>

        <div class="sidebar__foot">
          <div class="tenant">{{ session.tenantName() }}</div>
          <div class="role muted">{{ session.role() }}</div>
          <button type="button" class="btn btn--ghost" (click)="session.logout()">Sair</button>
        </div>
      </aside>

      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .shell { display: grid; grid-template-columns: 248px 1fr; min-height: 100vh; }

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

    nav { display: flex; flex-direction: column; gap: var(--space-1); flex: 1; }
    nav a {
      border-radius: var(--radius-sm);
      color: var(--text-on-dark);
      opacity: 0.82;
      padding: var(--space-2) var(--space-3);
      text-decoration: none;
    }
    nav a:hover { background: rgb(255 255 255 / 8%); opacity: 1; }
    nav a.active { background: var(--mamao-yellow-500); color: var(--mamao-green-900); font-weight: 500; opacity: 1; }

    .sidebar__foot { border-top: 1px solid rgb(255 255 255 / 12%); display: flex; flex-direction: column; gap: var(--space-2); padding-top: var(--space-4); }
    .tenant { font-weight: 500; }
    .role { color: rgb(247 243 234 / 65%); font-size: 13px; }
    .sidebar__foot .btn { border-color: rgb(255 255 255 / 25%); color: var(--text-on-dark); }

    .content { padding: var(--space-6); }

    @media (max-width: 860px) {
      .shell { grid-template-columns: 1fr; }
      .sidebar { flex-direction: row; align-items: center; gap: var(--space-4); padding: var(--space-3); }
      .sidebar__foot { border: 0; flex-direction: row; padding: 0; }
      nav { flex-direction: row; }
      .content { padding: var(--space-4); }
    }
  `,
})
export class Shell {
  readonly session = inject(SessionService);
}
