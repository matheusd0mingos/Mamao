# Capítulo 2 — A solução completa: Angular + .NET juntos

> **Objetivo:** montar do nada um projeto que tem os dois lados, entender por que as pastas
> ficam onde ficam, e ver os dois programas conversando pela primeira vez.

Este é o capítulo que quase nenhum tutorial escreve. Tutoriais ensinam Angular sozinho ou
.NET sozinho. O trabalho de verdade é ligar os dois — e as decisões que você toma no
primeiro dia te acompanham por anos.

---

## 2.1 A primeira decisão: um repositório ou dois?

Você tem dois programas. Eles moram juntos ou separados?

| | **Monorepo** (um repositório) | **Polyrepo** (dois) |
|---|---|---|
| Mudar API e tela no mesmo commit | Sim | Não — dois PRs, ordem importa |
| Ver se o front quebrou ao mudar o C# | O CI vê na hora | Só descobre depois |
| Times independentes | Atrapalha um pouco | Melhor |
| Deploy separado | Dá, com esforço | Natural |

**O Mamão usa monorepo**, e a razão é específica: o contrato entre os dois lados é gerado
automaticamente (Capítulo 9). Se eu mudo um campo no C#, o TypeScript precisa ser
regenerado no **mesmo commit**, senão o repositório fica num estado que não compila. Com
dois repositórios isso é impossível de garantir.

> **Chimpanzé pergunta:** *"Monorepo não fica gigante?"*
>
> O Mamão inteiro — backend, frontend, landing page, scripts de deploy, documentação — tem
> 301 arquivos e 8 MB de histórico. "Gigante" é problema do Google, não seu.

## 2.2 A estrutura de pastas do Mamão

```
Mamao/
├── Mamao.slnx                     ← a "solução": lista os projetos .NET
├── src/                           ← BACKEND
│   ├── Mamao.Api/                 ← o servidor HTTP (endpoints)
│   ├── Mamao.Worker/              ← tarefas de fundo (migrations, e-mails)
│   ├── Mamao.AppHost/             ← orquestração local (sobe tudo junto)
│   ├── Mamao.Identity/            ← login, usuários, tokens
│   ├── Mamao.SharedKernel/        ← código comum a todos os módulos
│   └── Modules/People/            ← um módulo de negócio
├── tests/                         ← testes do backend
├── web/                           ← FRONTEND
│   ├── openapi.json               ← ★ o contrato entre os dois lados
│   ├── normalize-openapi.mjs      ← script que limpa o contrato
│   ├── landing/                   ← site público (HTML puro, sem framework)
│   └── mamao-web/                 ← a aplicação Angular
├── deploy/                        ← Docker, Caddy, backup, restore
└── docs/                          ← ADRs, arquitetura, esta apostila
```

Três decisões visíveis aí:

**1. `web/openapi.json` fica *fora* da pasta do Angular.** Ele não pertence ao frontend —
é o contrato *entre* os dois. Quem o produz é o C#; quem o consome é o TypeScript. Colocá-lo
dentro de `mamao-web/` sugeriria que é um arquivo do front, e alguém acabaria editando à
mão.

**2. A landing page é HTML puro, sem Angular.** Uma página de marketing precisa carregar em
menos de um segundo e aparecer no Google. Baixar um framework inteiro para mostrar cinco
parágrafos seria autossabotagem. Nem todo problema precisa da mesma ferramenta.

**3. O backend é um *modular monolith*.** Um programa só, com fronteiras internas fortes
(um schema de banco por módulo, um `DbContext` por módulo). Não são microserviços — isso
seria pagar o custo de rede e de operação sem ter o problema que microserviços resolvem.
A decisão está registrada na [ADR-0001](../adr/0001-modular-monolith.md).

> **Chimpanzé pergunta:** *"O que é uma ADR?"*
>
> *Architecture Decision Record*. Um documento curto que diz: **o que** foi decidido, **por
> que**, e **o que foi descartado**. Serve para você, daqui a oito meses, não desfazer uma
> decisão sem saber que ela tinha motivo. O Mamão tem 20 delas em [`docs/adr/`](../adr/).

## 2.3 Criando a coisa toda do zero

Vamos criar um projeto novo, com os dois lados. Faça junto.

### Passo 1 — a pasta e o Git

```bash
mkdir loja && cd loja
git init
```

### Passo 2 — o backend

```bash
mkdir -p src
dotnet new webapi -n Loja.Api -o src/Loja.Api
dotnet new sln -n Loja
dotnet sln add src/Loja.Api/Loja.Api.csproj
```

- `dotnet new webapi` cria uma API HTTP.
- `dotnet new sln` cria a **solução**: um arquivo que agrupa projetos, para você compilar
  todos com um comando.

Fixe a porta, para não mudar a cada execução. Em `src/Loja.Api/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5100",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

⚠️ **Armadilha** — o `dotnet new webapi` gera uma porta aleatória. Se você não fixar, o
proxy do Angular (passo 4) aponta para uma porta que muda, e você perde meia hora achando
que o problema é o Angular.

Agora abra `src/Loja.Api/Program.cs` e deixe assim:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Gera o documento OpenAPI — o contrato que o frontend vai consumir.
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();                       // expõe em /openapi/v1.json
app.MapGet("/api/v1/produtos", () => new[]
{
    new Produto(1, "Café", 24.90m),
    new Produto(2, "Filtro", 8.50m),
});

app.Run();

record Produto(int Id, string Nome, decimal Preco);
```

Rode:

```bash
dotnet run --project src/Loja.Api
```

Abra `http://localhost:5100/api/v1/produtos`. Você vê JSON. **O backend está de pé.**

### Passo 3 — o frontend

Em outro terminal:

```bash
mkdir -p web && cd web
ng new loja-web --style=css --ssr=false --zoneless
cd loja-web
```

### Passo 4 — ligando os dois: o proxy

Aqui está o ponto mais importante do capítulo.

O Angular roda em `localhost:4200`. A API roda em `localhost:5100`. **Portas diferentes.**

Para o navegador, isso são **origens diferentes** — e por segurança ele **bloqueia** que a
página de uma origem chame outra. Essa regra se chama *Same-Origin Policy*, e o mecanismo
para relaxá-la é o **CORS**.

Existem duas saídas:

**(a) Configurar CORS no backend.** Funciona, mas você configura uma permissão em
desenvolvimento que não deveria existir em produção — e a chance de vazar para produção
mal configurada é real.

**(b) Usar um proxy.** O servidor de desenvolvimento do Angular finge que a API é dele.
Tudo que começar com `/api` ele encaminha para a porta 5100, nos bastidores.

**O Mamão usa proxy.** Crie `web/loja-web/proxy.conf.json`:

```json
{
  "/api": {
    "target": "http://localhost:5100",
    "secure": false,
    "changeOrigin": true
  }
}
```

É idêntico ao do Mamão. E registre em `angular.json`, dentro de `serve` →
`configurations` → `development`:

```json
"development": {
  "buildTarget": "loja-web:build:development",
  "proxyConfig": "proxy.conf.json"
}
```

Do ponto de vista do navegador, **tudo vem de `localhost:4200`**. Não há requisição entre
origens. Não há CORS. Não há configuração de segurança em produção só para o dev funcionar.

```
     O que o navegador acha                O que acontece de verdade
  ┌──────────────────────────┐        ┌──────────────────────────────┐
  │                          │        │  localhost:4200 (ng serve)   │
  │   tudo em                │        │       │                      │
  │   localhost:4200         │  ────> │       ├── /          → Angular│
  │                          │        │       └── /api/*  ──┐        │
  └──────────────────────────┘        │                      ▼        │
                                       │            localhost:5100     │
                                       │            (a API .NET)       │
                                       └──────────────────────────────┘
```

E em produção? Aí a API e o front estão em domínios diferentes de verdade
(`app.mamao.tech` e o backend atrás dele), e quem faz o mesmo papel é o **Caddy** — um
servidor que fica na frente e roteia. Capítulo 11 detalha.

### Passo 5 — chamando a API da tela

Em `src/app/app.config.ts`, habilite o cliente HTTP:

```typescript
import { provideHttpClient, withFetch } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(withFetch()),
  ],
};
```

E em `src/app/app.ts`:

```typescript
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Produto { id: number; nome: string; preco: number; }

@Component({
  selector: 'app-root',
  template: `
    <h1>Produtos</h1>
    <button (click)="carregar()">Carregar</button>

    <ul>
      @for (p of produtos(); track p.id) {
        <li>{{ p.nome }} — {{ p.preco | currency: 'BRL' }}</li>
      }
    </ul>
  `,
})
export class App {
  private readonly http = inject(HttpClient);
  readonly produtos = signal<Produto[]>([]);

  async carregar(): Promise<void> {
    const dados = await firstValueFrom(
      this.http.get<Produto[]>('/api/v1/produtos'),
    );
    this.produtos.set(dados);
  }
}
```

Repare no endereço: `'/api/v1/produtos'`. **Relativo, sem `http://localhost:5100`.** É o
proxy que resolve. E é por isso que o mesmo código funciona em produção sem mudar uma
letra — lá, quem resolve é o Caddy.

Com os dois terminais rodando (`dotnet run` e `npm start`), abra `localhost:4200`, clique
em Carregar. **Os dois lados estão conversando.**

## 2.4 Rodando tudo com um comando só

Manter dois ou três terminais abertos cansa. O Mamão usa o **.NET Aspire**, que sobe
Postgres, API e Worker juntos, com um painel de logs e traces:

```bash
dotnet run --project src/Mamao.AppHost
```

E o frontend à parte:

```bash
cd web/mamao-web && npm start
```

> **Chimpanzé pergunta:** *"Por que o frontend não entra no Aspire também?"*
>
> Poderia. Mas o ciclo de trabalho é diferente: o backend você sobe e esquece; o frontend
> você fica olhando recarregar. Misturar os logs dos dois atrapalha mais do que ajuda.

## 2.5 O que é cada projeto .NET do Mamão

Para você não se perder ao abrir a solução:

| Projeto | Papel |
|---|---|
| `Mamao.Api` | O servidor HTTP. Define endpoints e traduz requisição em chamada de serviço |
| `Mamao.Worker` | Tarefas de fundo: aplica migrations no startup, publica a outbox, avisa sobre documentos vencendo |
| `Mamao.AppHost` | Aspire — só desenvolvimento. Sobe Postgres + API + Worker |
| `Mamao.Identity` | Usuário, empresa, login, JWT, convites |
| `Mamao.SharedKernel` | O que todo módulo usa: tenancy, `Result`, permissões, outbox |
| `Mamao.Audit` | Trilha de auditoria append-only |
| `Mamao.Messaging` | A outbox: publicação confiável de eventos |
| `Mamao.Notifications` | E-mail (MailKit) e templates |
| `Modules/People` | Um módulo de negócio, dividido em Contracts / Domain / Application / Infrastructure |

⚠️ **Armadilha real do Mamão.** O `Mamao.Worker` deixou de subir porque o módulo People
passou a precisar de `IEmailSender`, e o Worker nunca tinha chamado `AddNotifications`. O
build ficava verde: o erro só aparecia na injeção de dependência, em tempo de execução, no
startup. Um build verde não prova que o programa **inicia**.

---

## Para fixar

1. **Por que o Mamão usa proxy em vez de configurar CORS no backend?**
   <details><summary>Resposta</summary>
   Porque com o proxy o navegador enxerga uma origem só, então não existe requisição
   cross-origin — e não é preciso manter no backend uma permissão que só serve para
   desenvolvimento e pode vazar mal configurada para produção. De quebra, o código do
   frontend usa caminho relativo e funciona igual nos dois ambientes.
   </details>

2. **Por que `web/openapi.json` não fica dentro de `web/mamao-web/`?**
   <details><summary>Resposta</summary>
   Porque não é um arquivo do frontend: é o contrato entre os dois lados, produzido pelo
   backend e consumido pelo frontend. Guardá-lo fora deixa claro que ninguém deve editá-lo
   à mão.
   </details>

3. **O que você perde ao escolher monorepo?**
   <details><summary>Resposta</summary>
   Independência de times e de deploy. Se dois times mexem em partes diferentes, eles
   disputam o mesmo repositório, o mesmo CI e a mesma fila de merge. Para uma pessoa ou um
   time pequeno, o custo é próximo de zero e o ganho de consistência é grande.
   </details>

## Laboratório

1. Monte o projeto `loja` inteiro seguindo 2.3. Faça a lista aparecer na tela.
2. **Quebre o proxy de propósito:** troque `"target"` para a porta `5999`. Recarregue,
   clique em Carregar, abra o DevTools → Network. Que erro aparece? Guarde a cara dele —
   você vai reencontrá-la.
3. Conserte e adicione no backend `app.MapGet("/api/v1/produtos/{id}", ...)`. Chame do
   frontend com `this.http.get<Produto>(\`/api/v1/produtos/${id}\`)`.
4. Adicione um campo `estoque` ao `record Produto` no C#. Note que **nada** acontece no
   TypeScript: a interface continua desatualizada e ninguém avisa. Segure esse incômodo
   até o Capítulo 9 — é exatamente o problema que o contrato gerado resolve.

---

**Anterior:** [Capítulo 1](01-do-zero-ao-primeiro-hello.md) ·
**Próximo:** [Capítulo 3 — TypeScript: o mínimo necessário](03-typescript-minimo.md)
