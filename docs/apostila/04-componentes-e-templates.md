# Capítulo 4 — Componentes e templates

> **Objetivo:** entender o átomo do Angular, ler qualquer template do Mamão e escrever o
> seu.

---

## 4.1 O que é um componente

Um componente é **um pedaço de tela com comportamento**. Três partes, sempre:

| Parte | O que é | Onde fica |
|---|---|---|
| **Template** | o HTML | `template:` |
| **Estilo** | o CSS | `styles:` |
| **Classe** | os dados e a lógica | `export class …` |

```typescript
import { Component, signal } from '@angular/core';

@Component({
  selector: 'mamao-contador',        // como se usa: <mamao-contador />
  template: `
    <p>Contagem: {{ valor() }}</p>
    <button (click)="somar()">+1</button>
  `,
  styles: `
    p { font-weight: 600; }
  `,
})
export class Contador {
  readonly valor = signal(0);

  somar(): void {
    this.valor.update((v) => v + 1);
  }
}
```

Aquele `@Component({...})` é um **decorator**: uma anotação que não muda o comportamento da
classe por si, mas registra informações que o Angular lê. É o equivalente aos *attributes*
do C# (`[ApiController]`, `[Fact]`).

> **Chimpanzé pergunta:** *"Por que o HTML fica dentro do arquivo `.ts`?"*
>
> Não precisa. Você pode usar `templateUrl: './contador.html'` e um arquivo separado. O
> Mamão prefere junto porque a maioria dos componentes é pequena e ficar pulando entre três
> arquivos para entender uma tela cansa mais do que rolar. Em telas grandes — como a da
> escala — separar seria melhor. É gosto, não regra.

## 4.2 O `selector` e as convenções

O `selector` é a etiqueta HTML que você inventa. No Mamão todos começam com `mamao-`:

```typescript
selector: 'mamao-employees'
```

O prefixo evita colisão com etiquetas HTML de verdade e com bibliotecas. Se um dia o HTML
ganhar um `<employees>` nativo, o seu `<mamao-employees>` continua funcionando.

## 4.3 Interpolação: mostrar valor

```html
<h1>Olá, {{ nome }}</h1>
<p>{{ 2 + 2 }}</p>
<p>{{ pessoa.fullName }}</p>
<p>{{ contarPessoas() }}</p>
```

Chaves duplas: *"avalie esta expressão e escreva o resultado como texto"*.

⚠️ **Cuidado:** o que vai dentro é uma **expressão**, não um comando. Não existe `if`, não
existe `for`, não existe atribuição. Se você precisa de lógica, ela vai para a classe.

🔬 **Sob o capô:** o Angular escapa o resultado automaticamente. Se `nome` for
`<script>alert(1)</script>`, aparece o texto literal na tela — não executa. Essa é a
proteção contra **XSS**, e é o motivo de você raramente precisar pensar nisso no Angular.

## 4.4 Property binding: mandar valor para um atributo

```html
<!-- texto literal: o atributo recebe a string "pessoa.id" -->
<img src="pessoa.id">

<!-- binding: o atributo recebe o VALOR de pessoa.id -->
<img [src]="pessoa.foto">

<button [disabled]="store.page() === 1">Anterior</button>
<span [class.badge--success]="pessoa.isActive">Ativo</span>
```

Os colchetes significam: *"o valor vem do TypeScript"*.

`[class.nome-da-classe]="condição"` adiciona ou remove uma classe CSS. Exemplo real:

```html
<span class="badge"
      [class.badge--success]="pessoa.isActive"
      [class.badge--neutral]="!pessoa.isActive">
  {{ pessoa.isActive ? 'Ativo' : 'Desligado' }}
</span>
```

## 4.5 Event binding: reagir ao usuário

```html
<button (click)="salvar()">Salvar</button>
<input (input)="buscar($event)">
<select (change)="filtrar($event)">
<form (submit)="enviar()">
```

Parênteses significam: *"quando este evento acontecer, execute isto"*.

`$event` é o evento do navegador. Para pegar o valor de um `<select>`:

```html
<select (change)="store.setDepartment($any($event.target).value || null)">
```

O `$any()` existe porque o TypeScript sabe apenas que `$event.target` é um `EventTarget`
genérico, que não tem `.value`. `$any()` silencia a checagem naquele ponto. É feio e é o
jeito prático; a alternativa é um método na classe que faz o cast direito.

## 4.6 Controle de fluxo: `@if`, `@for`, `@switch`

**Esta é a maior mudança recente do Angular.** Até a versão 16 era assim:

```html
<!-- O JEITO ANTIGO — você vai ver muito isso no Google -->
<div *ngIf="carregando">Carregando…</div>
<li *ngFor="let p of pessoas; trackBy: trackById">{{ p.nome }}</li>
```

Da 17 em diante:

```html
<!-- O JEITO ATUAL -->
@if (carregando) {
  <div>Carregando…</div>
}

@for (p of pessoas; track p.id) {
  <li>{{ p.nome }}</li>
}
```

O novo não precisa de import, é mais rápido e lê melhor. Use ele.

### `@if` com `@else`

```html
@if (store.loading()) {
  <p class="empty-state">Carregando…</p>
} @else if (store.isEmpty()) {
  <div class="empty-state">Sua equipe ainda não está aqui.</div>
} @else {
  <table class="data">…</table>
}
```

### `@if` guardando o valor

```html
@if (store.error(); as problema) {
  <div class="alert alert--danger">{{ problema.detail }}</div>
}
```

O `as problema` faz duas coisas: testa se existe **e** guarda numa variável já com o tipo
certo, sem `null`. Padrão muito usado no Mamão.

### `@for` e o `track`

```html
@for (pessoa of store.items(); track pessoa.id) {
  <tr>
    <td>{{ pessoa.fullName }}</td>
  </tr>
}
```

**O `track` é obrigatório** — e não é burocracia.

Quando a lista muda, o Angular precisa decidir: recriar todas as linhas do zero, ou
aproveitar as que já existem? O `track` diz como identificar "a mesma pessoa". Com
`track pessoa.id`, trocar de página reaproveita o que dá e recria só o necessário. Sem
identificação estável, tudo é destruído e recriado — o que perde foco de campo, perde
posição de rolagem e pisca.

⚠️ **Nunca use `track $index` em lista que reordena ou filtra.** O índice 0 continua sendo
0 mesmo quando a pessoa naquela posição mudou; o Angular acha que nada mudou e mantém o
conteúdo velho.

`@for` também oferece o vazio:

```html
@for (item of lista; track item.id) {
  <li>{{ item.nome }}</li>
} @empty {
  <li>Nada aqui ainda.</li>
}
```

### `@switch`

```html
@switch (documento.status) {
  @case ('Vencido')  { <span class="badge badge--danger">Vencido</span> }
  @case ('Vencendo') { <span class="badge badge--warn">Vence em breve</span> }
  @default           { <span class="badge">Em dia</span> }
}
```

## 4.7 Pipes: formatar na hora de mostrar

Um **pipe** transforma o valor só na exibição:

```html
{{ pessoa.hiredOn | date: 'dd/MM/yyyy' }}
{{ produto.preco | currency: 'BRL' }}
{{ nome | uppercase }}
{{ objeto | json }}                      <!-- ótimo para depurar -->
```

Pipes precisam ser importados no componente:

```typescript
import { DatePipe } from '@angular/common';

@Component({
  imports: [DatePipe],
  template: `{{ pessoa.hiredOn | date: 'dd/MM/yyyy' }}`,
})
```

E para o formato brasileiro sair certo, o locale é registrado uma vez só:

```typescript
// src/app/app.config.ts
import { registerLocaleData } from '@angular/common';
import ptBr from '@angular/common/locales/pt';

registerLocaleData(ptBr);

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ],
};
```

O comentário no código do Mamão diz o motivo em cinco palavras: *"nada de formatar data a
mão"*. Toda vez que alguém escreve `data.split('-').reverse().join('/')`, nasce um bug de
fuso horário.

## 4.8 `imports`: o que mudou dos NgModules

Este é o segundo ponto onde os tutoriais antigos vão te confundir.

**Antes (Angular ≤ 14):** todo componente pertencia a um `NgModule`, e o módulo declarava
o que estava disponível. Era muito arquivo para pouco resultado.

**Agora:** componentes são *standalone*. Cada um declara o que usa:

```typescript
@Component({
  selector: 'mamao-employees',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, HasPermissionDirective],
  template: `…`,
})
export class EmployeesPage { }
```

Se você usa `routerLink` no template e esquece de importar `RouterLink`, o Angular avisa na
compilação. A lista de imports é a lista de coisas que aquele template pode usar.

O Mamão nunca teve NgModules — nasceu depois da mudança.

## 4.9 Passando dados entre componentes

### De pai para filho: `input`

```typescript
// filho
@Component({
  selector: 'mamao-badge',
  template: `<span class="badge">{{ texto() }}</span>`,
})
export class Badge {
  readonly texto = input.required<string>();
  readonly cor = input<string>('neutro');   // com padrão
}
```

```html
<!-- pai -->
<mamao-badge texto="Ativo" cor="verde" />
<mamao-badge [texto]="pessoa.situacao" />
```

`input()` devolve um **signal** (Capítulo 5), por isso o `texto()` com parênteses.

Exemplo real do Mamão, na diretiva de permissão:

```typescript
readonly mamaoHasPermission = input.required<string>();
```

### De filho para pai: `output`

```typescript
// filho
export class Busca {
  readonly buscou = output<string>();

  digitou(termo: string): void {
    this.buscou.emit(termo);
  }
}
```

```html
<!-- pai -->
<mamao-busca (buscou)="filtrar($event)" />
```

## 4.10 Diretivas

Diretiva é comportamento sem template próprio — ela modifica um elemento existente. O
Mamão tem uma:

```typescript
/**
 * `<button *mamaoHasPermission="'people.write'">` — esconde o que a pessoa nao pode fazer.
 * Isto e conforto, nao seguranca: quem protege e a policy no endpoint.
 */
@Directive({ selector: '[mamaoHasPermission]' })
export class HasPermissionDirective {
  private readonly session = inject(SessionService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly mamaoHasPermission = input.required<string>();

  constructor() {
    effect(() => {
      const permitido = this.session.has(this.mamaoHasPermission());
      this.container.clear();

      if (permitido) {
        this.container.createEmbeddedView(this.template);
      }
    });
  }
}
```

Uso:

```html
<div *mamaoHasPermission="'people.write'" class="head__acoes">
  <a class="btn btn--primary" routerLink="/pessoas/nova">Cadastrar funcionário</a>
</div>
```

O `*` na frente é açúcar sintático para "envolva este elemento num template e deixe a
diretiva decidir se ele entra na tela". `TemplateRef` é o molde; `ViewContainerRef` é o
lugar onde ele pode ser instanciado.

## 4.11 Estilo: encapsulamento

O CSS que você escreve em `styles:` **só vale para aquele componente**:

```typescript
@Component({
  template: `<p>Olá</p>`,
  styles: `p { color: red; }`,
})
```

Nenhum outro `<p>` da aplicação fica vermelho.

🔬 **Sob o capô:** o Angular adiciona um atributo único a cada elemento do componente
(`_ngcontent-abc123`) e reescreve o seu seletor para `p[_ngcontent-abc123]`. Não é mágica,
é especificidade CSS combinada com um atributo gerado.

Estilos que valem para tudo vão em `src/styles.css`. O Mamão tem lá o design system:
variáveis de cor, espaçamento, tipografia, e o estilo padrão de `input`, `button`, `.card`.

⚠️ **Armadilha real do Mamão** (contada por inteiro no Capítulo 13): um campo numérico
estreito insistia em ocupar a largura toda. A causa era especificidade: a regra global
`input:not([type=checkbox]):not([type=radio])` tem peso maior que a classe local `.ordem`.
Estilo local **não** ganha automaticamente de estilo global — quem ganha é o seletor mais
específico.

---

## Para fixar

1. **Qual a diferença entre `src="foto"` e `[src]="foto"`?**
   <details><summary>Resposta</summary>
   O primeiro define o atributo com a string literal `"foto"`. O segundo avalia `foto`
   como expressão TypeScript e usa o valor.
   </details>

2. **Por que `track` é obrigatório no `@for`?**
   <details><summary>Resposta</summary>
   Porque o Angular precisa saber identificar cada item para reaproveitar elementos quando
   a lista muda. Sem isso, toda alteração destruiria e recriaria a lista inteira, perdendo
   foco, rolagem e estado dos elementos.
   </details>

3. **O que está errado aqui?**
   ```html
   @for (p of pessoas; track $index) {
     <input [value]="p.nome">
   }
   ```
   <details><summary>Resposta</summary>
   `track $index` em lista que pode reordenar ou filtrar. Se a ordem mudar, o Angular
   acredita que o item na posição 0 continua o mesmo e mantém o `<input>` com o valor da
   pessoa antiga. Use `track p.id`.
   </details>

## Laboratório

1. Crie um componente `mamao-pessoa-card` que recebe um objeto via `input.required` e
   mostra nome, cargo e um badge verde/cinza conforme `isActive`.
2. Use `@for` para renderizar uma lista deles a partir de um array fixo.
3. Adicione `@empty` e teste com a lista vazia.
4. Adicione um `<input>` que filtra a lista por nome. Dica: guarde o termo num `signal` e
   derive a lista filtrada com `computed` — que é exatamente o assunto do próximo capítulo.

---

**Anterior:** [Capítulo 3](03-typescript-minimo.md) ·
**Próximo:** [Capítulo 5 — Signals](05-signals.md)
