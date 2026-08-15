# Capítulo 3 — TypeScript: o mínimo necessário

> **Objetivo:** ler e escrever o TypeScript que aparece no Mamão. Não é um curso completo
> da linguagem — é o subconjunto que você precisa, e nada além.

---

## 3.1 Por que existe

JavaScript aceita qualquer coisa:

```javascript
function saudacao(pessoa) {
  return "Olá, " + pessoa.nome;
}

saudacao({ nome: "Ana" });   // "Olá, Ana"
saudacao("Ana");             // "Olá, undefined"   ← e ninguém reclama
saudacao(42);                // "Olá, undefined"
saudacao();                  // 💥 estoura em produção
```

O erro só aparece quando um usuário real clica. TypeScript move essa descoberta para o
momento em que você digita:

```typescript
interface Pessoa { nome: string; }

function saudacao(pessoa: Pessoa): string {
  return `Olá, ${pessoa.nome}`;
}

saudacao("Ana");   // ❌ o editor sublinha em vermelho, antes de rodar
```

> **Chimpanzé pergunta:** *"Então TypeScript é mais seguro em produção?"*
>
> Não diretamente — e essa confusão é comum. O TypeScript **desaparece** na compilação: o
> navegador recebe JavaScript comum, sem nenhuma verificação de tipo. Se um JSON chegar do
> servidor com o formato errado, o TypeScript não percebe em tempo de execução. O ganho
> está em pegar erros **antes** de publicar, e em fazer o editor te ajudar enquanto você
> escreve.

## 3.2 Variáveis

```typescript
const nome = 'Ana';        // não pode ser reatribuída
let contador = 0;          // pode
contador = 1;              // ok
// nome = 'Bia';           // ❌ erro

var antiga = 'evite';      // do JavaScript velho. Nunca use.
```

Use `const` por padrão. Só troque para `let` quando o compilador reclamar. Isso não é
preciosismo: `const` comunica ao leitor que aquele valor não muda, e a maior parte dos
valores não muda.

⚠️ **Pegadinha clássica:** `const` impede **reatribuir**, não impede **alterar por dentro**.

```typescript
const lista = [1, 2, 3];
lista.push(4);        // ✅ permitido — a lista mudou
// lista = [9];       // ❌ proibido — isso seria reatribuir
```

## 3.3 Tipos básicos

```typescript
const nome: string = 'Ana';
const idade: number = 34;           // não existe int/float separados
const ativo: boolean = true;
const nomes: string[] = ['Ana', 'Bia'];
const nada: null = null;
const indefinido: undefined = undefined;
```

Na prática você quase nunca escreve o tipo: o compilador **infere**.

```typescript
const nome = 'Ana';        // TypeScript já sabe: string
const idade = 34;          // number
```

Escreva o tipo quando ele **não** for óbvio — em parâmetros de função, em retornos, e
quando um valor começa vazio:

```typescript
const items = signal<EmployeeListItem[]>([]);   // sem o <...> seria "array de quê?"
```

## 3.4 `interface` e `type`

Uma **interface** descreve o formato de um objeto:

```typescript
interface Funcionario {
  id: string;
  fullName: string;
  code: string | null;      // pode ser texto OU null
  isActive: boolean;
  departmentName?: string;  // a ? significa: pode não vir
}
```

Duas coisas importantes aí:

- `string | null` é uma **união**: o valor é uma coisa **ou** outra. Você é obrigado a
  tratar os dois casos.
- `?` marca a propriedade como **opcional**: ela pode simplesmente não existir no objeto.

> **Chimpanzé pergunta:** *"`null` e `undefined` não são a mesma coisa?"*
>
> Não, e a diferença importa quando o dado vem de uma API .NET:
> - `null` = **existe e está vazio**. O C# mandou `"code": null`.
> - `undefined` = **não existe**. A propriedade não veio no JSON.
>
> Um campo `string?` no C# vira `string | null` no JSON. Um campo que o backend às vezes
> nem inclui vira `?` no TypeScript. Na hora de testar, `if (x)` cobre os dois (e mais o
> zero e a string vazia — cuidado); `if (x != null)` cobre exatamente os dois.

`type` faz quase a mesma coisa, e o Mamão usa para dar apelido a tipos gerados:

```typescript
// src/app/core/http/api.types.ts
type Schemas = components['schemas'];

export type EmployeeResponse = Schemas['EmployeeResponse'];
export type CreateEmployeeRequest = Schemas['CreateEmployeeRequest'];
```

Regra prática: `interface` para objetos que você escreve; `type` para apelidos, uniões e
combinações.

## 3.5 Funções

```typescript
// declaração comum
function somar(a: number, b: number): number {
  return a + b;
}

// arrow function — a forma curta, muito usada
const somar2 = (a: number, b: number): number => a + b;

// parâmetro com valor padrão
function listar(pagina: number = 1): void { }

// parâmetro opcional
function buscar(termo: string, setor?: string): void { }
```

`void` como retorno significa "não devolve nada útil".

Exemplo real do Mamão, com padrão e opcional juntos:

```typescript
// src/app/features/employees/employees.api.ts
list(
  search: string,
  includeInactive: boolean,
  page: number,
  pageSize: number,
  departmentId: string | null = null,     // padrão
): Promise<PagedEmployees> { … }
```

## 3.6 Assincronismo: `Promise`, `async`, `await`

Este é o conceito que mais trava iniciante, então vamos com calma.

Chamar o servidor **demora** — 50 ms, 2 segundos, às vezes nunca responde. JavaScript tem
uma **única** linha de execução: se ela ficasse parada esperando, a página inteira
congelaria. Nada de rolar, nada de clicar.

A solução é: a função devolve na hora uma **promessa** de que o valor chega depois.

```typescript
const promessa = http.get<Produto[]>('/api/v1/produtos');
// aqui `promessa` NÃO é a lista. É um comprovante.
```

`await` diz: *"pause esta função até a promessa se resolver, mas libere o navegador para
fazer outras coisas nesse meio-tempo"*.

```typescript
async function carregar(): Promise<void> {
  const produtos = await buscarProdutos();   // pausa aqui
  console.log(produtos.length);              // só roda quando chegou
}
```

Regras que resolvem 90% das dúvidas:

1. Para usar `await`, a função precisa ser `async`.
2. Uma função `async` **sempre** devolve uma `Promise`, mesmo que você retorne um número.
3. `await` numa função errada é o erro mais comum de todos:

```typescript
// ❌ ERRADO — devolve uma Promise, não a lista
function carregar() {
  const dados = buscarProdutos();
  return dados.length;      // 💥 Promise não tem .length
}

// ✅ CERTO
async function carregar() {
  const dados = await buscarProdutos();
  return dados.length;
}
```

### Tratando erro

```typescript
async load(): Promise<void> {
  this.loading.set(true);
  this.error.set(null);

  try {
    const result = await this.api.list(/* … */);
    this.items.set(result.items ?? []);
  } catch (problem) {
    this.error.set(problem as ApiProblem);
  } finally {
    this.loading.set(false);      // roda dando certo ou errado
  }
}
```

Isso é o `EmployeesStore` do Mamão, literal. Repare no `finally`: sem ele, um erro deixaria
`loading` como `true` para sempre e a tela ficaria em "Carregando…" eternamente.

### `void` antes da chamada

Você vai ver isso no Mamão:

```typescript
ngOnInit(): void {
  void this.store.load();
}
```

`ngOnInit` não pode ser `async`, então não dá para usar `await`. O `void` na frente diz ao
compilador: *"eu sei que isto devolve uma Promise e estou ignorando de propósito"*. Sem
ele, o linter avisa sobre promessa não tratada — o que é um aviso bom, porque promessa
ignorada por acidente engole erros.

## 3.7 Operadores que aparecem o tempo todo

### `?.` — acesso opcional

```typescript
const nome = pessoa?.endereco?.cidade;
// se `pessoa` ou `endereco` for null/undefined, devolve undefined
// em vez de estourar "Cannot read property of null"
```

### `??` — valor padrão

```typescript
const codigo = pessoa.code ?? '—';
// usa '—' só se code for null ou undefined
```

⚠️ Cuidado com a diferença para `||`:

```typescript
const a = 0 || 10;    // 10  ← zero é "falsy", então o || substitui
const b = 0 ?? 10;    // 0   ← ?? só substitui null/undefined
```

Para número e para string vazia, essa diferença já causou bug em muita gente. Use `??`.

### Template literal

```typescript
const msg = `${total} pessoas cadastradas`;
```

Crase, não aspas. Permite quebra de linha e interpolação — é o que o Mamão usa para montar
URLs:

```typescript
return firstValueFrom(this.http.get<EmployeeResponse>(`${this.base}/${id}`));
```

### Espalhamento e desestruturação

```typescript
const copia = { ...original, isActive: false };   // copia e sobrescreve um campo
const [primeiro, segundo] = lista;                 // tira itens da lista
const { fullName, code } = funcionario;            // tira campos do objeto
```

## 3.8 Genéricos

Um genérico é um tipo que recebe outro tipo como parâmetro. Parece abstrato; o uso é
concreto:

```typescript
this.http.get<PagedEmployees>('/api/v1/employees')
```

O `<PagedEmployees>` diz ao `HttpClient`: *"o JSON que vai voltar tem este formato"*. A
partir daí, o editor completa os campos e o compilador acusa erro de digitação.

⚠️ **Cuidado importante:** isso é uma **promessa sua**, não uma verificação. Se o servidor
devolver outra coisa, o TypeScript não percebe — ele não valida nada em tempo de execução.
É exatamente por isso que o Capítulo 9 existe: o tipo é **gerado** do C#, e não escrito à
mão, para que a promessa não possa ficar desatualizada.

## 3.9 `import` e `export`

```typescript
// arquivo que oferece
export class EmployeesApi { }
export type Funcionario = { };

// arquivo que consome
import { EmployeesApi } from './employees.api';
import type { Funcionario } from '../../core/http/api.types';
```

`import type` importa **só o tipo**, que some na compilação. É levemente mais eficiente e
deixa claro que aquilo não existe em tempo de execução. O Mamão usa em todos os DTOs.

Caminhos:
- `./` — mesma pasta
- `../` — pasta acima
- `@angular/core` — pacote do `node_modules`

## 3.10 O que é `strict`

O `tsconfig.json` do Mamão liga o modo estrito. Na prática, três coisas mudam:

1. **Não dá para usar `null` sem tratar.**
   ```typescript
   function f(nome: string) { }
   f(null);   // ❌ erro
   ```
2. **Todo parâmetro precisa de tipo** (implícito `any` é proibido).
3. **O compilador cobra os casos que faltam.**

É desconfortável nas primeiras semanas e paga depois. Não desligue.

---

## Para fixar

1. **Qual a diferença entre `x?.y` e `x!.y`?**
   <details><summary>Resposta</summary>
   `x?.y` devolve `undefined` com segurança se `x` for nulo. `x!.y` é você afirmando ao
   compilador "confie em mim, não é nulo" — e se você estiver errado, estoura em execução.
   Use `!` o mínimo possível; ele desliga justamente a proteção pela qual você adotou
   TypeScript.
   </details>

2. **Por que este código não compila?**
   ```typescript
   function total(): number {
     const dados = await api.list();
     return dados.total;
   }
   ```
   <details><summary>Resposta</summary>
   `await` só é permitido dentro de função `async`. E ao marcar como `async`, o retorno
   passa a ser `Promise<number>`, não `number`.
   </details>

3. **`items.set(result.items ?? [])` — por que o `?? []`?**
   <details><summary>Resposta</summary>
   Porque o tipo gerado do OpenAPI declara `items` como possivelmente ausente. Sem o
   padrão, `items` poderia virar `undefined`, e o `@for` do template quebraria. É o
   contrato sendo honesto sobre o que pode faltar.
   </details>

## Laboratório

1. Crie `exercicio.ts` e digite:
   ```typescript
   interface Produto { id: number; nome: string; preco: number; estoque?: number; }

   function resumo(p: Produto): string {
     return `${p.nome}: R$ ${p.preco} (${p.estoque ?? 0} em estoque)`;
   }
   ```
   Chame com e sem `estoque`. Depois chame com `{ id: 1 }` e leia a mensagem de erro
   inteira — mensagens de erro do TypeScript são longas mas dizem exatamente o que falta.

2. Escreva uma função `async` que espera um segundo e devolve `'pronto'`:
   ```typescript
   const dormir = (ms: number) => new Promise((r) => setTimeout(r, ms));
   ```

3. Teste a diferença na prática:
   ```typescript
   console.log(0 || 'padrão');   // ?
   console.log(0 ?? 'padrão');   // ?
   console.log('' || 'padrão');  // ?
   console.log('' ?? 'padrão');  // ?
   ```
   Preveja antes de rodar.

---

**Anterior:** [Capítulo 2](02-solucao-angular-e-dotnet.md) ·
**Próximo:** [Capítulo 4 — Componentes e templates](04-componentes-e-templates.md)
