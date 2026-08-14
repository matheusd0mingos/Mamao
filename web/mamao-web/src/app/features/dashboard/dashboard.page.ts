import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';

/**
 * Esqueleto da visao geral. O dashboard de verdade — pendencias acionaveis no topo,
 * equipe hoje, trabalho — chega no Marco 7, quando ha dado para mostrar.
 * Ver docs/produto/ux-telas-criticas.md.
 */
@Component({
  selector: 'mamao-dashboard',
  imports: [RouterLink],
  template: `
    <h1>Bom dia.</h1>
    <p class="muted">{{ session.tenantName() }}</p>

    <div class="card proximos">
      <h2>Primeiros passos</h2>
      <ol>
        <li>Cadastre sua equipe em <a routerLink="/pessoas">Pessoas</a>.</li>
        <li>Importe por CSV quando o cadastro passar de uma dezena (Marco 1).</li>
        <li>Suba os documentos com validade e deixe o Mamão avisar antes de vencer (Marco 2).</li>
      </ol>
      <p class="muted">
        Esta tela vira o painel de pendências no Marco 7 — pendência acima de gráfico.
      </p>
    </div>
  `,
  styles: `
    .proximos { margin-top: var(--space-5); padding: var(--space-5); max-width: 620px; }
    .proximos h2 { margin-bottom: var(--space-3); }
    ol { margin: 0 0 var(--space-4); padding-left: var(--space-5); }
    li { margin-bottom: var(--space-2); }
  `,
})
export class DashboardPage {
  readonly session = inject(SessionService);
}
