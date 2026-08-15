# Capítulo 14 — Exercícios

> Onze exercícios, do fácil ao difícil, com gabarito. Os cinco últimos são no código real
> do Mamão.

Faça na ordem. Cada um usa o anterior.

---

## Nível 1 — Fundamentos

### Exercício 1 · Componente com estado

Crie `mamao-termometro`: um número entre 0 e 40, dois botões (−1 e +1), e o texto muda de
cor conforme a faixa — azul abaixo de 15, verde entre 15 e 28, vermelho acima.

**Requisitos:** o número em `signal`; a cor em `computed`; sem `if` no template além do
necessário.

<details><summary>Gabarito</summary>

```typescript
@Component({
  selector: 'mamao-termometro',
  template: `
    <p [style.color]="cor()">{{ graus() }}°C</p>
    <button (click)="mudar(-1)">−</button>
    <button (click)="mudar(1)">+</button>
  `,
})
export class Termometro {
  readonly graus = signal(20);

  readonly cor = computed(() => {
    const g = this.graus();
    if (g < 15) return '#1d4ed8';
    if (g <= 28) return '#11362d';
    return '#b91c1c';
  });

  mudar(delta: number): void {
    this.graus.update((g) => Math.min(40, Math.max(0, g + delta)));
  }
}
```

A faixa é **derivada** da temperatura, então é `computed`. Guardar a cor num segundo signal
significaria lembrar de atualizá-la em todo lugar que mexe no número.
</details>

---

### Exercício 2 · A armadilha da referência

Este código não funciona. Descubra por quê e conserte.

```typescript
readonly tarefas = signal<string[]>([]);

adicionar(texto: string): void {
  this.tarefas().push(texto);
}

remover(indice: number): void {
  this.tarefas().splice(indice, 1);
}
```

<details><summary>Gabarito</summary>

Ambos alteram o array **por dentro**. A referência não muda, então o signal não notifica
ninguém e a tela fica parada.

```typescript
adicionar(texto: string): void {
  this.tarefas.update((atual) => [...atual, texto]);
}

remover(indice: number): void {
  this.tarefas.update((atual) => atual.filter((_, i) => i !== indice));
}
```
</details>

---

### Exercício 3 · Lista com filtro

Lista de nomes fixa, um `<input>` de busca e a lista filtrada em tempo real. Sem chamar
API.

**Requisito extra:** a busca deve ignorar maiúsculas e acentos ("joao" acha "João").

<details><summary>Gabarito</summary>

```typescript
export class Lista {
  readonly nomes = signal(['João Silva', 'Ana Souza', 'Maria José']);
  readonly termo = signal('');

  readonly filtrados = computed(() => {
    const t = normalizar(this.termo());
    if (!t) return this.nomes();
    return this.nomes().filter((n) => normalizar(n).includes(t));
  });
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')                    // separa a letra do acento
    .replace(/[̀-ͯ]/g, '')     // remove os acentos
    .toLowerCase();
}
```

`NFD` decompõe "ã" em "a" + til; o `replace` tira os tis. É o equivalente em JavaScript ao
que o Mamão faz no Postgres com a extensão `unaccent`.
</details>

---

## Nível 2 — Integração

### Exercício 4 · Os três estados

Escreva um store que carrega de uma API e trata os **três** estados corretamente: enquanto
carrega, se der erro, e o resultado — incluindo o resultado vazio.

<details><summary>Gabarito</summary>

```typescript
@Injectable({ providedIn: 'root' })
export class ProdutosStore {
  private readonly api = inject(ProdutosApi);

  readonly items = signal<Produto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<ApiProblem | null>(null);

  readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.items.set(await this.api.list());
    } catch (problem) {
      this.error.set(problem as ApiProblem);
    } finally {
      this.loading.set(false);
    }
  }
}
```

O `finally` é o que separa o gabarito da resposta comum: sem ele, um erro deixa `loading`
em `true` para sempre e a tela mostra "Carregando…" eternamente.
</details>

---

### Exercício 5 · Interceptor de log

Escreva um interceptor que mede a duração de cada requisição e registra no console
`GET /api/v1/employees — 143 ms`.

<details><summary>Gabarito</summary>

```typescript
export const logInterceptor: HttpInterceptorFn = (request, next) => {
  const inicio = performance.now();

  return next(request).pipe(
    tap({
      next: (evento) => {
        if (evento.type === HttpEventType.Response) {
          const ms = Math.round(performance.now() - inicio);
          console.log(`${request.method} ${request.url} — ${ms} ms`);
        }
      },
      error: () => {
        const ms = Math.round(performance.now() - inicio);
        console.warn(`${request.method} ${request.url} — falhou em ${ms} ms`);
      },
    }),
  );
};
```

Registre **primeiro** na lista para medir a corrente inteira.
</details>

---

### Exercício 6 · Busca com debounce

Ligue um `FormControl` a uma busca no servidor, esperando a pessoa parar de digitar, sem
repetir termo igual e sem vazar a inscrição.

<details><summary>Gabarito</summary>

```typescript
readonly busca = new FormControl('', { nonNullable: true });

constructor() {
  this.busca.valueChanges
    .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
    .subscribe((termo) => void this.store.setSearch(termo));
}
```

Os três operadores resolvem três problemas distintos: excesso de requisições, requisição
redundante e vazamento de memória. Faltando o terceiro, a inscrição sobrevive ao componente.
</details>

---

## Nível 3 — No Mamão de verdade

Para os próximos, clone o repositório e suba o ambiente
([README](../../README.md#rodando-localmente-debian-13-trixie)).

### Exercício 7 · Um campo novo ponta a ponta

Mostre o **e-mail** do funcionário na listagem de pessoas.

<details><summary>Gabarito</summary>

1. `EmployeeListItem` (C#): adicione `string? Email`.
2. No `Select` do `EmployeeService`, projete `e.Email`.
3. `dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"`
4. `cd web/mamao-web && npm run generate:api`
5. No template: `<td class="muted">{{ pessoa.email ?? '—' }}</td>`
6. `npm run build`
7. Commite os quatro arquivos juntos.

Se pular 3 ou 4, o passo 6 falha dizendo que `email` não existe no tipo. É o contrato
fazendo o trabalho dele.
</details>

---

### Exercício 8 · Filtro novo

Adicione "somente admitidos nos últimos 12 meses" à listagem.

<details><summary>Dicas e pontos de atenção</summary>

- Signal no store + parâmetro no `EmployeesApi` + `Where` no serviço.
- **Não esqueça `this.page.set(1)`** ao mudar o filtro — senão você pode ficar numa página
  que não existe mais no resultado filtrado (Capítulo 12).
- Calcule a data de corte **no servidor**. O relógio do navegador é do usuário, e ele pode
  estar errado ou em outro fuso.
</details>

---

### Exercício 9 · Ache o bug do estado vazio

Na tela de pessoas, quando a busca não retorna nada, a mensagem some no primeiro instante e
depois aparece. Por quê? (Dica: pense em `loading` e `isEmpty` durante a transição.)

<details><summary>Gabarito</summary>

```typescript
readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);
```

Entre `loading.set(true)` e a resposta, `items` ainda contém o **resultado anterior**.
Quando a resposta chega vazia, há um instante em que `loading` já é `false` e `items` acabou
de ser esvaziado — e a transição fica visível como um "pisca".

Duas soluções: limpar `items` ao iniciar o carregamento, ou manter a lista anterior visível
esmaecida enquanto carrega (melhor para a percepção de velocidade). A segunda é o padrão
"stale-while-revalidate".
</details>

---

### Exercício 10 · Escreva o teste que faltava

O Caso 6 do Capítulo 13 aconteceu porque o `MamaoApiFactory` não migrava o `AuditDbContext`.
Escreva um teste que **falharia** se alguém adicionasse um quarto contexto e esquecesse de
migrá-lo.

<details><summary>Caminho da solução</summary>

Descubra por reflexão todos os tipos que herdam de `DbContext` nos assemblies carregados e
verifique que cada um está registrado no container **e** teve `Migrate` chamado.

Um teste de arquitetura é mais direto e igualmente eficaz: garanta que a lista dentro de
`MigrarTudoAsync` tem o mesmo tamanho que a quantidade de `DbContext` do produto. Se alguém
criar um novo e não incluir, o teste quebra com uma mensagem que diz exatamente o que fazer.

O Mamão já usa essa técnica: há um teste de arquitetura que quebra o build se um índice não
começar por `tenant_id`.
</details>

---

### Exercício 11 · O difícil — cancelamento de requisição

Na busca de pessoas, se a pessoa digitar rápido, duas requisições podem estar em voo. Se a
**primeira** demorar mais que a segunda, ela chega depois e sobrescreve o resultado certo
com o antigo.

Isso se chama **condição de corrida**. Como resolver?

<details><summary>Gabarito</summary>

**Solução 1 — RxJS `switchMap`** (a idiomática):

```typescript
this.busca.valueChanges.pipe(
  debounceTime(300),
  distinctUntilChanged(),
  switchMap((termo) => this.api.listObservable(termo)),   // cancela a anterior
  takeUntilDestroyed(),
).subscribe((r) => this.store.items.set(r.items ?? []));
```

`switchMap` **cancela** a inscrição anterior quando um valor novo chega. Como o `HttpClient`
usa `AbortController` por baixo, a requisição HTTP é abortada de verdade.

**Solução 2 — carimbo de sequência** (funciona com Promise):

```typescript
private sequencia = 0;

async setSearch(termo: string): Promise<void> {
  const minha = ++this.sequencia;
  const resultado = await this.api.list(termo, /* … */);

  if (minha !== this.sequencia) return;   // chegou tarde: descarta

  this.items.set(resultado.items ?? []);
}
```

Não cancela a requisição, mas ignora a resposta obsoleta. Simples e suficiente.

**Por que o Mamão não sofre com isso hoje:** o `debounceTime(300)` torna a sobreposição
rara, e a busca é rápida. É uma dívida conhecida, não um descuido — e saber onde a solução
simples para de servir vale mais do que a solução complexa aplicada cedo demais.
</details>

---

## Projeto final

Implemente um módulo completo de **Avisos**: o gestor publica um aviso com título, texto e
data de expiração; todo mundo vê os ativos no painel "Hoje".

Precisa ter:

- [ ] Entidade com `tenant_id` e RLS ligada na migration
- [ ] `AviseService` com criar / listar ativos / arquivar
- [ ] Endpoints com `RequireAuthorization` e uma permissão nova em `Permissions`
- [ ] Auditoria em criação e arquivamento, na **mesma transação**
- [ ] Contrato regenerado e commitado
- [ ] Store com os três estados
- [ ] Tela com estado vazio útil (o que fazer, não só "nada aqui")
- [ ] Botão escondido por `*mamaoHasPermission`
- [ ] Testes unitários da regra de expiração
- [ ] Um teste de integração provando que a empresa A não vê aviso da empresa B

O último item é o mais importante e o mais esquecido.

---

**Anterior:** [Capítulo 13](13-bugs-reais.md) · **Apoio:** [Glossário](99-glossario.md)
