# Capítulo 5 — Signals: como o Angular sabe redesenhar

> **Objetivo:** entender o modelo de reatividade moderno do Angular, saber quando usar
> `signal`, `computed` e `effect`, e entender por que o Mamão é *zoneless*.

Este é o capítulo mais importante da Parte I. Se você entender signals, o resto do Angular
é vocabulário.

---

## 5.1 O problema

Você tem um número na memória e um texto na tela:

```typescript
let contador = 0;
```

```html
<p>Contagem: {{ contador }}</p>
```

Alguém clica e o número vira 1. **Como a tela fica sabendo?**

O JavaScript não avisa ninguém quando uma variável muda. Não existe evento de "variável
alterada". Alguém tem que descobrir.

Historicamente houve três respostas.

### Resposta 1: você avisa na mão (jQuery, 2010)

```javascript
contador++;
document.getElementById('contagem').textContent = contador;
```

Funciona e não escala: com trinta valores na tela, você esquece de atualizar um, e a tela
mostra uma coisa enquanto a memória tem outra.

### Resposta 2: verificar tudo, o tempo todo (Angular clássico, Zone.js)

O Angular tradicional usa uma biblioteca chamada **Zone.js**, que faz algo audacioso:
substitui as funções do navegador — `addEventListener`, `setTimeout`, `fetch` — por versões
que avisam o Angular.

O raciocínio é: *dados só mudam por causa de um evento, um timer ou uma resposta de rede*.
Interceptando os três, o Angular sabe quando **pode** ter mudado algo. E aí ele **verifica
a aplicação inteira**, comparando cada valor exibido com o anterior.

Funciona. É genial. E é caro: um clique num botão dispara a verificação de todos os
componentes vivos, inclusive os que não têm relação nenhuma com aquele botão.

### Resposta 3: o valor avisa (signals — o jeito atual)

Um **signal** é uma caixinha que guarda um valor e **sabe quem está olhando para ele**.
Quando o valor muda, ele avisa exatamente esses interessados. Ninguém mais é verificado.

```typescript
readonly contador = signal(0);
```

```html
<p>Contagem: {{ contador() }}</p>
```

Ao renderizar, o Angular registra: *"este trecho de tela depende do signal `contador`"*.
Quando você faz `contador.set(1)`, só esse trecho é remarcado.

> **Chimpanzé pergunta:** *"Por que `contador()` com parênteses? Não é uma função?"*
>
> É, sim — literalmente. `signal(0)` devolve uma função. Chamá-la faz duas coisas ao mesmo
> tempo: devolve o valor **e** registra que quem está chamando depende dela. É esse
> registro que faz a reatividade funcionar. Sem os parênteses você teria a caixa, não o
> conteúdo.

## 5.2 Criando e mudando

```typescript
import { signal } from '@angular/core';

const nome = signal('Ana');

nome();                              // ler       → 'Ana'
nome.set('Bia');                     // trocar
nome.update((atual) => atual + '!'); // trocar em função do valor atual
```

`set` quando você tem o valor novo. `update` quando ele depende do anterior:

```typescript
// src/app/features/employees/employees.store.ts
async toggleInactive(): Promise<void> {
  this.includeInactive.update((value) => !value);
  this.page.set(1);
  await this.load();
}
```

⚠️ **A armadilha número um dos signals:** signal detecta **troca de referência**, não
alteração interna.

```typescript
const lista = signal<string[]>([]);

lista().push('novo');            // ❌ a tela NÃO atualiza
lista.set([...lista(), 'novo']); // ✅ referência nova, a tela atualiza
```

O primeiro caso altera o array por dentro; a caixa continua apontando para o mesmo array,
então nada mudou do ponto de vista do signal. **Sempre crie um valor novo.**

O mesmo vale para objetos:

```typescript
// ❌
usuario().nome = 'Bia';
// ✅
usuario.update((u) => ({ ...u, nome: 'Bia' }));
```

## 5.3 `computed`: valor derivado

Quando um valor é **calculado a partir de outros**, ele não deve ser guardado — deve ser
derivado:

```typescript
readonly items = signal<EmployeeListItem[]>([]);
readonly loading = signal(false);
readonly total = signal(0);
readonly pageSize = signal(25);

readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);
readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
```

Isso é o `EmployeesStore` real. Três propriedades do `computed`:

1. **Descobre sozinho de quem depende.** Você não declara nada; ele observa quais signals
   foram lidos durante o cálculo.
2. **É preguiçoso.** Só calcula quando alguém pergunta.
3. **Guarda o resultado.** Perguntar dez vezes calcula uma vez, até que uma dependência
   mude.

> **Chimpanzé pergunta:** *"Por que não fazer um método normal `isEmpty()`?"*
>
> Um método normal recalcula **toda vez** que o template é avaliado. Em `@for` com 200
> linhas, isso é 200 execuções. Um `computed` executa uma vez e reaproveita. Além disso, o
> Angular sabe rastrear o `computed` como dependência — o método comum é opaco para ele.

Regra: **se dá para derivar, derive.** Guardar `isEmpty` num signal separado significa
lembrar de atualizá-lo em todo lugar que mexe em `items` — e um dia você esquece, e a tela
mostra "nenhuma pessoa" com a tabela cheia.

## 5.4 `effect`: reagir a mudanças

`effect` roda um trecho de código sempre que algum signal lido dentro dele mudar:

```typescript
constructor() {
  effect(() => {
    console.log('a busca virou:', this.search());
  });
}
```

Use para **efeitos colaterais**: gravar no `localStorage`, mexer no DOM diretamente, logar.

⚠️ **Não use `effect` para calcular valor.** Se você se pegar escrevendo:

```typescript
// ❌ errado
effect(() => {
  this.totalPaginas.set(Math.ceil(this.total() / this.pageSize()));
});
```

…isso é um `computed` disfarçado, com o dobro de trabalho e um risco de laço infinito.

O Mamão usa `effect` uma vez, e o uso é legítimo — mexer no DOM:

```typescript
// src/app/core/auth/has-permission.directive.ts
constructor() {
  effect(() => {
    const permitido = this.session.has(this.mamaoHasPermission());
    this.container.clear();

    if (permitido) {
      this.container.createEmbeddedView(this.template);
    }
  });
}
```

Se a permissão mudar (a pessoa fez logout e entrou com outra conta), o elemento entra ou
sai da tela sozinho.

## 5.5 Zoneless: o que muda

No `app.config.ts` do Mamão:

```typescript
providers: [
  provideZonelessChangeDetection(),
  // …
]
```

Isso desliga o Zone.js. Consequências:

**O bom:**
- Aproximadamente 100 KB a menos no bundle.
- Nenhuma verificação global. Só o que depende de um signal que mudou é redesenhado.
- Rastreamento de pilha limpo — sem dezenas de quadros de `zone.js` no meio dos seus erros.

**O que exige atenção:**
- Mudar uma propriedade comum **não** redesenha nada. Estado de tela precisa estar em
  signal.

```typescript
export class Errado {
  nome = 'Ana';                     // propriedade comum

  trocar() {
    this.nome = 'Bia';              // ❌ com zoneless, a tela não muda
  }
}

export class Certo {
  readonly nome = signal('Ana');

  trocar() {
    this.nome.set('Bia');           // ✅
  }
}
```

Isso deixa de ser sutileza e vira disciplina: **se aparece na tela, é signal.**

## 5.6 Signals e RxJS: quando usar cada um

O Angular carrega o **RxJS**, uma biblioteca de fluxos assíncronos, muito mais poderosa e
muito mais difícil. A dúvida "signal ou RxJS?" tem uma resposta prática.

O Mamão escreveu a regra num comentário:

```typescript
// src/app/features/employees/employees.page.ts
constructor() {
  // RxJS onde e fluxo (busca com debounce); signal no resto.
  this.busca.valueChanges
    .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
    .subscribe((termo) => void this.store.setSearch(termo));
}
```

| Situação | Use |
|---|---|
| Estado de tela (lista, carregando, erro, filtro) | **signal** |
| Valor derivado | **computed** |
| Uma requisição HTTP | **`firstValueFrom`** (vira Promise) |
| Fluxo com tempo: debounce, throttle, cancelar anterior | **RxJS** |

Aquele trecho faz três coisas que signal não faz bem:

- `debounceTime(300)` — espera a pessoa parar de digitar por 300 ms. Sem isso, "Carlos"
  dispara seis buscas.
- `distinctUntilChanged()` — se o termo é igual ao anterior, não repete.
- `takeUntilDestroyed()` — cancela a inscrição quando o componente sai da tela. **Sem isso
  há vazamento de memória:** o componente morre, a inscrição continua viva, referenciando
  o componente morto.

## 5.7 O padrão *store* do Mamão

Uma pergunta que sempre aparece: onde mora o estado?

Muitos projetos Angular usam **NgRx** — actions, reducers, effects, selectors. É poderoso e
custa muito código. A [ADR-0008](../adr/0008-frontend-angular.md) decidiu contra:

> *Store por feature com signals. Cobre a complexidade real do Mamão sem actions, reducers,
> effects e selectors.*

Um store é um serviço com signals dentro:

```typescript
@Injectable({ providedIn: 'root' })
export class EmployeesStore {
  private readonly api = inject(EmployeesApi);

  // ── estado ────────────────────────────────
  readonly items = signal<EmployeeListItem[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly error = signal<ApiProblem | null>(null);

  // ── derivado ──────────────────────────────
  readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  // ── ações ─────────────────────────────────
  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.api.list(/* … */);
      this.items.set(result.items ?? []);
      this.total.set(result.total);
    } catch (problem) {
      this.error.set(problem as ApiProblem);
    } finally {
      this.loading.set(false);
    }
  }
}
```

E o componente vira quase só template:

```typescript
export class EmployeesPage implements OnInit {
  readonly store = inject(EmployeesStore);

  ngOnInit(): void {
    void this.store.load();
    void this.store.loadDepartments();
  }
}
```

Três signals que aparecem juntos em quase toda tela — **`loading`, `error`, `items`** — são
os três estados que qualquer carregamento tem. Esquecer o `error` é o erro mais comum de
iniciante: a tela fica em "Carregando…" para sempre quando a requisição falha, e o usuário
não sabe se espera ou desiste.

---

## Para fixar

1. **Por que `lista().push(x)` não atualiza a tela?**
   <details><summary>Resposta</summary>
   Porque o signal compara referências. `push` altera o array existente sem trocar a
   referência, então o signal não vê mudança. É preciso `lista.set([...lista(), x])`.
   </details>

2. **`computed` ou `effect` para "quando a lista mudar, recalcular o total"?**
   <details><summary>Resposta</summary>
   `computed`. Total é um valor derivado. `effect` é para efeito colateral — gravar,
   registrar, mexer no DOM.
   </details>

3. **Com zoneless, este código funciona?**
   ```typescript
   export class Tela {
     mensagem = '';
     async salvar() {
       await this.api.salvar();
       this.mensagem = 'Salvo!';
     }
   }
   ```
   <details><summary>Resposta</summary>
   Não. `mensagem` é propriedade comum: nada notifica o Angular. Precisa ser
   `readonly mensagem = signal('')` e `this.mensagem.set('Salvo!')`.
   </details>

4. **O que acontece sem `takeUntilDestroyed()` numa inscrição de `valueChanges`?**
   <details><summary>Resposta</summary>
   Vazamento de memória. A inscrição continua ativa depois que o componente é destruído,
   mantendo-o vivo na memória e possivelmente executando callbacks que mexem em algo que
   não está mais na tela.
   </details>

## Laboratório

1. Componente com `nome = signal('')` e um `computed` `saudacao` que devolve
   `'Olá, ' + nome()` ou `'Digite seu nome'` se estiver vazio.
2. Adicione `effect(() => console.log('mudou:', this.nome()))`. Digite e observe o console:
   ele dispara a cada letra.
3. Agora replique o padrão do Mamão: `FormControl` + `debounceTime(500)`, e registre no
   console só depois da pausa. Compare a quantidade de logs.
4. **Experimento decisivo:** crie `lista = signal<string[]>([])` e dois botões — um que faz
   `lista().push('x')` e outro que faz `lista.set([...lista(), 'x'])`. Renderize com `@for`.
   Clique dez vezes no primeiro (nada acontece), depois uma vez no segundo. Todos os itens
   aparecem de uma vez. Isso mostra que os `push` funcionaram no array, mas ninguém foi
   avisado.

---

**Anterior:** [Capítulo 4](04-componentes-e-templates.md) ·
**Próximo:** [Capítulo 6 — Injeção de dependência](06-injecao-de-dependencia.md)
