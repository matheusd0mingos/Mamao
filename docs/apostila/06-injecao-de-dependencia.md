# Capítulo 6 — Injeção de dependência

> **Objetivo:** entender `inject()`, serviços e escopos — e por que isso não é burocracia.

Se você vem do .NET, boa notícia: é o mesmo conceito de `IServiceCollection`, com nomes
diferentes.

---

## 6.1 O problema

Sua tela precisa falar com a API. A forma ingênua:

```typescript
export class EmployeesPage {
  private readonly api = new EmployeesApi();   // ❌
}
```

Três problemas nascem aí:

1. `EmployeesApi` precisa de `HttpClient`, que precisa de configuração, que precisa dos
   interceptors… você teria que montar a corrente inteira à mão.
2. Cada tela cria a sua instância. Dez telas, dez cópias, dez caches separados.
3. Em teste, você não consegue trocar por uma versão falsa.

**Injeção de dependência** inverte: o componente **declara** o que precisa, e alguém entrega.

```typescript
export class EmployeesPage {
  readonly store = inject(EmployeesStore);     // ✅
}
```

## 6.2 Serviço

Um **serviço** é uma classe sem tela — lógica, estado, acesso a dados:

```typescript
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/employees';

  get(id: string): Promise<EmployeeResponse> {
    return firstValueFrom(this.http.get<EmployeeResponse>(`${this.base}/${id}`));
  }
}
```

`@Injectable({ providedIn: 'root' })` significa: *"esta classe pode ser injetada, e existe
uma única instância para a aplicação inteira"*.

Equivalência com .NET:

| Angular | .NET |
|---|---|
| `providedIn: 'root'` | `services.AddSingleton<T>()` |
| `providers: []` no componente | escopo por instância do componente |
| `inject(X)` | parâmetro do construtor |

> **Chimpanzé pergunta:** *"Singleton não é ruim? Aprendi que é anti-padrão."*
>
> O anti-padrão é o singleton **global e estático**, que ninguém consegue substituir. Aqui
> a instância é gerenciada por um container: em teste você registra outra, e o código que
> depende dela nem percebe. É o oposto do problema.

## 6.3 `inject()` vs. construtor

Duas formas, ambas válidas:

```typescript
// forma moderna — a do Mamão
export class EmployeesPage {
  readonly store = inject(EmployeesStore);
}

// forma clássica
export class EmployeesPage {
  constructor(private readonly store: EmployeesStore) {}
}
```

`inject()` é preferido hoje porque funciona em lugares onde não há construtor — funções de
guarda e interceptors, por exemplo:

```typescript
export const authGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.isAuthenticated() ? true : inject(Router).createUrlTree(['/entrar']);
};
```

⚠️ **Regra rígida:** `inject()` só pode ser chamado em **contexto de injeção** — na
inicialização de campo, dentro do construtor, ou dentro de uma função de fábrica. Chamar
dentro de um método comum dá o erro `inject() must be called from an injection context`.

```typescript
export class Errado {
  carregar() {
    const api = inject(EmployeesApi);   // 💥
  }
}
```

## 6.4 Escopos

**Root** — uma instância para o app inteiro. É o padrão e cobre quase tudo:

```typescript
@Injectable({ providedIn: 'root' })
export class SessionService { }
```

Faz sentido: a sessão do usuário é uma só. Se cada tela tivesse a sua, cada uma teria um
token diferente.

**Por componente** — instância nova a cada instância do componente:

```typescript
@Component({
  providers: [FiltroLocal],
})
export class MinhaTela { }
```

Use quando o estado é daquela tela e deve morrer com ela.

> **Chimpanzé pergunta:** *"O `EmployeesStore` é root. Se eu sair da tela de pessoas e
> voltar, os dados antigos ainda estão lá?"*
>
> Sim — e isso é intencional. O filtro e a página continuam onde estavam, então voltar não
> perde o contexto. O `ngOnInit` chama `load()` de novo para atualizar. Se você quisesse
> tela sempre limpa, o store seria provido no componente.

## 6.5 Providers na configuração

O `app.config.ts` é onde se registram as coisas da aplicação:

```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    // A ordem importa: problemInterceptor traduz o erro que o authInterceptor deixar passar.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, problemInterceptor])),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ],
};
```

As funções `provideX()` são o jeito moderno (equivalem ao antigo `imports: [XModule]`).

A última linha mostra o formato cru: `{ provide: TOKEN, useValue: valor }`. Serve para
injetar coisas que não são classes — configuração, constantes, uma URL.

**O comentário sobre ordem não é decorativo.** Interceptors formam uma corrente: a
requisição passa por eles na ordem declarada, e a resposta volta na ordem inversa. O
`authInterceptor` precisa ver o 401 primeiro para tentar renovar o token; o
`problemInterceptor` traduz o que sobrar. Invertendo, o erro já viria traduzido e o
mecanismo de refresh nunca dispararia.

## 6.6 Trocando em teste

O ganho prático:

```typescript
TestBed.configureTestingModule({
  providers: [
    { provide: EmployeesApi, useValue: apiFalsa },
  ],
});
```

O componente continua fazendo `inject(EmployeesApi)` e recebe o dublê. Nenhuma linha do
código de produção muda.

---

## Para fixar

1. **Por que `inject()` não funciona dentro de um método comum?**
   <details><summary>Resposta</summary>
   Porque o Angular mantém um "contexto de injeção" ativo apenas durante a construção do
   objeto. Fora dele, `inject()` não tem como saber a qual injetor pertence.
   </details>

2. **`SessionService` como singleton — problema ou acerto?**
   <details><summary>Resposta</summary>
   Acerto. A sessão do usuário é única por aplicação. Múltiplas instâncias significariam
   tokens divergentes e telas discordando sobre quem está logado.
   </details>

3. **Por que a ordem dos interceptors importa?**
   <details><summary>Resposta</summary>
   Porque eles formam uma corrente. O `authInterceptor` precisa capturar o 401 bruto para
   tentar o refresh; se o `problemInterceptor` viesse antes, o erro já teria sido
   convertido e o refresh nunca aconteceria.
   </details>

## Laboratório

1. Crie `ContadorService` com `providedIn: 'root'` e um signal interno.
2. Injete em **dois** componentes diferentes na mesma tela. Incremente por um; observe o
   outro mudando junto — é a mesma instância.
3. Agora mova para `providers: [ContadorService]` num dos componentes. Repita. Agora são
   contagens independentes.
4. Tente chamar `inject()` dentro de um método. Leia a mensagem de erro — você vai
   reencontrá-la.

---

**Anterior:** [Capítulo 5](05-signals.md) ·
**Próximo:** [Capítulo 7 — Rotas, guardas e lazy loading](07-rotas-e-guardas.md)
