# Capítulo 7 — Rotas, guardas e lazy loading

> **Objetivo:** entender como uma SPA troca de tela sem recarregar, como proteger telas por
> permissão, e como não baixar o sistema inteiro no primeiro acesso.

---

## 7.1 Navegação sem recarregar

Numa SPA o endereço muda mas a página não recarrega. Quem faz isso é o **Router**.

Em `app.ts`:

```typescript
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
export class App {}
```

`<router-outlet />` é o buraco onde a tela atual é encaixada. O roteador lê a URL, acha a
rota correspondente, cria o componente e o coloca ali.

E os links:

```html
<a routerLink="/pessoas">Pessoas</a>                      <!-- ✅ -->
<a [routerLink]="['/pessoas', pessoa.id]">{{ nome }}</a>  <!-- ✅ com parâmetro -->
<a href="/pessoas">Pessoas</a>                            <!-- ❌ recarrega tudo -->
```

⚠️ Usar `href` numa SPA descarta a aplicação e baixa tudo de novo. O `routerLink` intercepta
o clique e apenas troca o conteúdo do outlet.

## 7.2 O mapa de rotas

```typescript
export const routes: Routes = [
  { path: 'entrar', loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage) },
  { path: '', canMatch: [authGuard], loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: 'pessoas', canMatch: [permissionGuard('people.read')],
        loadComponent: () => import('./features/employees/employees.page').then((m) => m.EmployeesPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
```

Peças:

- **`path`** — o pedaço da URL. Sem barra no começo.
- **`loadComponent`** — carrega o componente **sob demanda** (seção 7.4).
- **`canMatch`** — guardas (seção 7.5).
- **`children`** — rotas aninhadas dentro de um layout.
- **`'**'`** — qualquer coisa que não bateu. Sempre por último.

## 7.3 Rotas aninhadas: o *shell*

O Mamão tem uma casca com menu lateral e cabeçalho, e o conteúdo muda dentro dela:

```
┌─────────────────────────────────────────┐
│  Mamão            [busca]      [perfil] │  ← Shell
├──────────┬──────────────────────────────┤
│ Início   │                              │
│ Pessoas  │   ← aqui entra a tela atual  │  ← <router-outlet> do Shell
│ Escala   │                              │
│ Demandas │                              │
└──────────┴──────────────────────────────┘
```

O `Shell` é o componente pai; todas as telas internas são `children`. Ele também tem um
`<router-outlet />` no template. Ao navegar de `/pessoas` para `/escala`, **só o miolo
troca** — o menu não é recriado, não pisca, não perde a rolagem.

## 7.4 Lazy loading

`loadComponent` recebe uma função que só executa quando a rota é acessada:

```typescript
loadComponent: () => import('./features/work/work.page').then((m) => m.WorkPage)
```

O compilador vê esse `import()` dinâmico e coloca aquele componente num **arquivo separado**.
Quem nunca abre "Demandas" nunca baixa o código de demandas.

Foi exatamente isso que apareceu no build do Mamão:

```
chunk-B0_azGTH.js   | dashboard-page        |  12.55 kB
chunk-UUdSdnQO.js   | employee-import-page  |  12.46 kB
chunk-B3ddEZ47.js   | organization-page     |  11.73 kB
chunk-DCblMCoR.js   | audit-page            |   9.09 kB
```

Um pedaço por tela. O bundle inicial fica com a casca e o essencial — **95 kB comprimidos**,
medidos somando os quatro arquivos que o `index.html` referencia.

> **Chimpanzé pergunta:** *"E a tela demora ao abrir pela primeira vez?"*
>
> Baixa 10 KB, o que em qualquer conexão decente é imperceptível. E o Angular pré-carrega
> em segundo plano quando você configura para isso. O ganho no primeiro acesso — o mais
> importante, porque é quando a pessoa decide se o sistema é lento — compensa muito.

## 7.5 Guardas

Uma **guarda** é uma função que decide se a rota pode ser ativada.

```typescript
export const authGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.isAuthenticated() ? true : inject(Router).createUrlTree(['/entrar']);
};
```

Devolver `true` libera; devolver uma `UrlTree` redireciona.

O Mamão tem uma segunda guarda, por permissão:

```typescript
/** Esconde a rota sem permissao. O endpoint correspondente tambem verifica — sempre. */
export const permissionGuard =
  (permission: string): CanMatchFn =>
  () => {
    const session = inject(SessionService);
    if (session.has(permission)) return true;

    return inject(Router).parseUrl(rotaInicial(session));
  };
```

Repare: é uma função que **devolve** uma guarda. Isso permite parametrizar:

```typescript
canMatch: [permissionGuard('people.read')]
```

E o comentário repete o princípio do Capítulo 1: **a guarda esconde; o endpoint protege.**

### `canMatch` vs. `canActivate`

- `canActivate` — a rota bate, o componente ia ser criado, a guarda barra.
- `canMatch` — a rota **nem é considerada**; o roteador segue procurando.

`canMatch` é melhor com lazy loading: se a guarda recusa, o arquivo daquela tela **nem é
baixado**. Com `canActivate`, o download acontece e depois é descartado.

## 7.6 Um bug real de rota

Este é bom, porque ele parece impossível até você ver a causa.

```typescript
/**
 * A primeira tela DEPENDE do papel.
 *
 * O gerente de TI nao ve disponibilidade, entao o painel "Hoje" nao e a casa dele — a tela
 * de acessos e. Mandar todo mundo para /inicio faria a pessoa cair numa tela que o guarda
 * recusa e voltar para /inicio: laco de redirecionamento na primeira vez que ela entra.
 */
export function rotaInicial(session: SessionService): string {
  if (session.has('availability.read')) return '/inicio';
  if (session.has('users.invite')) return '/acessos';
  if (session.has('people.read')) return '/pessoas';
  return '/entrar';
}
```

O que acontecia: a raiz `''` redirecionava todo mundo para `/inicio`. O gerente de TI não
tem `availability.read`, então a guarda de `/inicio` o mandava de volta para `''`, que o
mandava para `/inicio`… Laço infinito, e a tela nunca abria.

A correção não foi dar a permissão ao gerente de TI — seria alargar o acesso para consertar
uma tela. Foi tornar o destino da raiz **dependente do papel**.

**Lição:** todo redirecionamento padrão precisa levar a um lugar onde a pessoa
**realmente** pode entrar.

## 7.7 Ordem das rotas importa

```typescript
{
  // Antes de 'pessoas/:id': senao "importar" seria lido como um id de funcionario.
  path: 'pessoas/importar',
  loadComponent: () => import('./features/employees/employee-import.page')…
},
{
  path: 'pessoas/:id',
  loadComponent: () => import('./features/employees/employee-profile.page')…
},
```

`:id` é um curinga: casa com **qualquer** coisa, inclusive a palavra `importar`. O roteador
usa a **primeira** rota que casar. Invertendo a ordem, `/pessoas/importar` abriria o perfil
do funcionário de id `"importar"`, que não existe.

**Regra:** rota específica antes de rota com parâmetro.

## 7.8 Lendo parâmetros

Com `withComponentInputBinding()` ligado no `app.config.ts`, parâmetros de rota chegam como
`input`:

```typescript
export class EmployeeProfilePage {
  readonly id = input.required<string>();     // vem de 'pessoas/:id'
}
```

Sem isso, seria preciso injetar `ActivatedRoute` e ler manualmente. O jeito com `input` é
mais direto e testável.

Para query string (`/redefinir-senha?token=abc`), o Mamão lê direto:

```typescript
this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
```

---

## Para fixar

1. **Por que `canMatch` combina melhor com lazy loading do que `canActivate`?**
   <details><summary>Resposta</summary>
   Porque `canMatch` roda antes de a rota ser escolhida: se recusar, o arquivo daquela tela
   nem é baixado. `canActivate` roda depois do carregamento.
   </details>

2. **O que acontece se `pessoas/:id` vier antes de `pessoas/importar`?**
   <details><summary>Resposta</summary>
   `/pessoas/importar` casa com `:id` (o parâmetro vira a string "importar") e abre a tela
   de perfil pedindo um funcionário inexistente.
   </details>

3. **Por que `href` é errado numa SPA?**
   <details><summary>Resposta</summary>
   Porque provoca navegação real do navegador: descarta a aplicação, baixa tudo de novo e
   perde todo o estado em memória.
   </details>

## Laboratório

1. Monte três rotas com lazy loading. Abra o DevTools → Network, filtre por JS, e navegue.
   Veja um arquivo novo chegando a cada primeira visita — e nenhum na segunda.
2. Escreva uma guarda que só permite entrar se `localStorage.getItem('admin') === 'sim'`.
   Teste barrando e liberando pelo console.
3. **Reproduza o laço:** faça a rota `''` redirecionar para `/secreta`, e a guarda de
   `/secreta` redirecionar para `''`. Abra e veja o navegador travar. Depois conserte com a
   estratégia do `rotaInicial`.

---

**Anterior:** [Capítulo 6](06-injecao-de-dependencia.md) ·
**Próximo:** [Capítulo 8 — Formulários](08-formularios.md)
