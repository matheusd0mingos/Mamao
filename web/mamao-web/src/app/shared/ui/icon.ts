import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';

/**
 * Os desenhos. Traco de 1.5, cantos e pontas arredondados, grade de 24 — o mesmo desenho
 * em todos, senao o conjunto parece recortado de tres lugares diferentes.
 *
 * Sao caminhos escritos aqui e nao uma biblioteca de icones: o app usa uma duzia deles, e
 * uma dependencia inteira para isso custaria mais bytes que a tela que ela decora. O dia
 * em que forem cinquenta, troca-se por um sprite — nada aqui impede.
 */
const DESENHOS: Record<string, string> = {
  // Visao geral: quatro paineis.
  inicio: '<rect x="3" y="3" width="7" height="8" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="11" width="7" height="10" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/>',

  // Pessoas: duas silhuetas.
  pessoas: '<path d="M16 19v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 4 17.5V19"/><circle cx="10" cy="7.5" r="3.5"/><path d="M20 19v-1.5a3.5 3.5 0 0 0-2.6-3.4"/><path d="M15.5 4.2a3.5 3.5 0 0 1 0 6.6"/>',

  // Disponibilidade: pessoa e relogio.
  disponibilidade: '<path d="M12 19H4v-1.5A3.5 3.5 0 0 1 7.5 14h3"/><circle cx="9" cy="7.5" r="3.5"/><circle cx="16.5" cy="16.5" r="4.5"/><path d="M16.5 14.5v2.2l1.5 1"/>',

  // Calendario.
  calendario: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/>',

  // Escala: pessoas em fila com marca de conferido.
  escala: '<path d="M4 6h10M4 12h7M4 18h5"/><path d="m14.5 16.5 2 2 4-4.5"/>',

  // Demandas: um quadro com tres colunas. Tres retangulos soltos, que era o desenho
  // obvio, viravam tres tracos ilegiveis a 18px — a moldura e o que da a leitura.
  demandas: '<rect x="3" y="4.5" width="18" height="15" rx="2"/><path d="M9 4.5v15M15 4.5v15"/>',

  // Estrutura: organograma.
  estrutura: '<rect x="9" y="3" width="6" height="4.5" rx="1"/><rect x="2.5" y="16.5" width="6" height="4.5" rx="1"/><rect x="15.5" y="16.5" width="6" height="4.5" rx="1"/><path d="M12 7.5v4.5M5.5 16.5V12h13v4.5"/>',

  // Acessos: chave.
  acessos: '<circle cx="8" cy="12" r="4"/><path d="M12 12h9M17.5 12v3.5M20.5 12v2.5"/>',

  // Alerta: o triangulo. E o unico icone que existe para ser notado.
  alerta: '<path d="M12 4.5 2.8 20h18.4L12 4.5Z"/><path d="M12 10v4"/><circle cx="12" cy="17" r=".6" fill="currentColor" stroke="none"/>',

  relogio: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',

  // Sem responsavel: silhueta com interrogacao implicita (traco cortado).
  ninguem: '<path d="M19 19v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 7 17.5V19"/><circle cx="13" cy="7.5" r="3.5"/><path d="m3 9 4 4M7 9l-4 4"/>',

  mais: '<path d="M12 5v14M5 12h14"/>',
  fechar: '<path d="m6 6 12 12M18 6 6 18"/>',
  sair: '<path d="M9 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H9"/><path d="M15 8.5 18.5 12 15 15.5M18.5 12H9"/>',
  anterior: '<path d="m14.5 5-7 7 7 7"/>',
  proximo: '<path d="m9.5 5 7 7-7 7"/>',
  lixeira: '<path d="M4 7h16M9.5 7V5.5A1.5 1.5 0 0 1 11 4h2a1.5 1.5 0 0 1 1.5 1.5V7"/><path d="M6.5 7 7.5 20h9l1-13"/>',
};

export type IconName = keyof typeof DESENHOS;

/**
 * Um icone.
 *
 * `aria-hidden` sempre: aqui o icone acompanha um rotulo escrito, nunca o substitui. Icone
 * sozinho como unico significado e a forma mais rapida de tornar uma tela inutilizavel para
 * quem usa leitor — e ambigua para todo mundo.
 */
@Component({
  selector: 'mamao-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
      [innerHTML]="desenho()"
    ></svg>
  `,
  styles: `
    :host { display: inline-flex; flex: none; }
  `,
})
export class Icon {
  private readonly sanitizer = inject(DomSanitizer);

  readonly name = input.required<IconName>();
  readonly size = input(20);

  /**
   * O caminho entra por innerHTML, e o sanitizador do Angular remove `path` e `circle` —
   * ele nao conhece SVG. O bypass e seguro AQUI e so aqui: `name` indexa um dicionario
   * fechado escrito neste arquivo, entao nao existe caminho por onde conteudo de fora chegue.
   * Aceitar string de caminho por input quebraria essa garantia — por isso o input e o nome,
   * nunca o desenho.
   */
  protected readonly desenho = computed<SafeHtml>(() =>
    this.sanitizer.bypassSecurityTrustHtml(DESENHOS[this.name()] ?? ''),
  );
}
