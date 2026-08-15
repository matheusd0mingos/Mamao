# Capítulo 1 — Do zero ao primeiro Hello

> **Objetivo:** entender o que é uma aplicação web moderna, instalar as ferramentas, criar
> um projeto Angular do nada e saber o que é cada arquivo que apareceu.

---

## 1.1 O que acontece quando você abre um site

Vamos começar mesmo do começo.

Você digita `app.mamao.tech` e aperta Enter. O que acontece:

1. O **navegador** (Chrome, Firefox…) descobre o endereço numérico daquele nome. Isso é o
   **DNS** — uma lista telefônica da internet.
2. O navegador manda uma mensagem para aquele endereço pedindo: *"me dá a página inicial"*.
   Essa mensagem tem um formato padronizado chamado **HTTP**.
3. Um computador do outro lado — o **servidor** — responde com um arquivo de texto.
4. O navegador lê esse texto e **desenha** a tela.

O arquivo que volta é **HTML**: texto com marcações que dizem "isto é um título", "isto é
um parágrafo", "isto é um botão".

```html
<h1>Pessoas</h1>
<p>42 pessoas cadastradas</p>
<button>Cadastrar funcionário</button>
```

Junto vêm dois companheiros:

- **CSS** — diz como aquilo *aparece*: cor, tamanho, espaçamento, posição.
- **JavaScript** — a única linguagem de programação que o navegador executa. É o que faz a
  página *reagir*: clicou no botão, algo acontece.

> **Chimpanzé pergunta:** *"Se o navegador só entende JavaScript, o que é TypeScript, que
> vocês usam?"*
>
> TypeScript é JavaScript com um sistema de tipos por cima. Ele **não roda** no navegador:
> antes de publicar, uma ferramenta converte TypeScript em JavaScript comum. É como
> escrever num idioma com corretor ortográfico e entregar o texto sem as marcações. O
> Capítulo 3 é só sobre isso.

## 1.2 Site tradicional vs. SPA

Existem duas formas de montar um sistema web.

### O jeito tradicional (multi-página)

Cada clique pede uma página nova ao servidor. Você clica em "Pessoas", o navegador
**descarta** tudo, pede `/pessoas`, e o servidor devolve um HTML novo, inteiro, já com os
dados dentro.

```
Navegador                          Servidor
   |  GET /pessoas                    |
   |--------------------------------->|
   |  <html>…tabela pronta…</html>    |
   |<---------------------------------|
   |  (tela pisca, redesenha tudo)    |
```

É simples e funciona. PHP, Rails e ASP.NET MVC clássico trabalham assim.

### O jeito SPA (*Single Page Application*)

O navegador baixa **uma vez** um aplicativo em JavaScript. Depois disso, clicar em
"Pessoas" não pede página nenhuma: o próprio JavaScript reescreve o pedaço da tela que
mudou, e busca só os **dados** no servidor — em formato **JSON**, que é texto estruturado.

```
Navegador                          Servidor
   |  GET /  (uma vez só)             |
   |--------------------------------->|
   |  app.js  (o aplicativo inteiro)  |
   |<---------------------------------|
   |                                  |
   |  GET /api/v1/employees           |
   |--------------------------------->|
   |  {"items":[{"fullName":"Ana"}]}  |   ← só dados, sem HTML
   |<---------------------------------|
   |  (JavaScript desenha a tabela)   |
```

**O Mamão é uma SPA.** Angular é uma ferramenta para construir SPAs.

| | Tradicional | SPA |
|---|---|---|
| Primeiro carregamento | Rápido | Mais lento (baixa o app) |
| Navegação depois | Recarrega tudo | Instantânea |
| Servidor devolve | HTML pronto | JSON com dados |
| Funciona sem JavaScript | Sim | Não |
| Complexidade | Menor | Maior |

> **Chimpanzé pergunta:** *"Se SPA é mais complexo, por que usar?"*
>
> Porque o Mamão é um **sistema**, não um site. O gestor abre de manhã e fica ali dentro
> por vinte minutos, trocando de tela dez vezes. Recarregar a página inteira a cada clique
> seria lento e perderia o estado — o filtro que ele digitou, a rolagem, o formulário
> meio preenchido. Para um blog, SPA seria exagero.

## 1.3 Os dois lados do Mamão

O Mamão tem dois programas que rodam em computadores diferentes:

```
┌────────────────────────────┐        ┌──────────────────────────────┐
│  FRONTEND  (Angular)       │        │  BACKEND  (.NET / C#)        │
│  roda no navegador da       │        │  roda no servidor            │
│  pessoa                     │        │                              │
│                             │ HTTP   │  ┌────────────────────────┐  │
│  • desenha as telas         │───────>│  │ regras de negócio      │  │
│  • valida o formulário      │  JSON  │  │ (quem pode ser         │  │
│  • guarda o token           │<───────│  │  escalado amanhã?)     │  │
│  • NÃO decide nada          │        │  └───────────┬────────────┘  │
│    importante               │        │              │               │
└────────────────────────────┘        │  ┌───────────▼────────────┐  │
                                       │  │ PostgreSQL (o banco)   │  │
                                       │  └────────────────────────┘  │
                                       └──────────────────────────────┘
```

Guarde esta frase, ela volta várias vezes:

> **O frontend esconde para não frustrar. O backend impede para proteger.**

Se o botão "Cadastrar funcionário" só aparece para quem tem permissão, isso é
**conveniência** — evita que a pessoa clique e leve um "não pode". Mas se alguém mandar a
requisição na mão, driblando a tela, quem barra é o servidor. Sempre. Este princípio está
escrito no próprio código do Mamão:

```typescript
// src/app/core/auth/session.service.ts
/**
 * O frontend esconde para NAO FRUSTRAR; o backend impede para PROTEGER. Toda checagem
 * aqui tem policy correspondente no endpoint.
 */
has(permission: string): boolean {
  return this.permissions().includes(permission);
}
```

## 1.4 Instalando as ferramentas

Você precisa de duas coisas para o frontend e uma para o backend.

### Node.js

**Node** é o JavaScript rodando fora do navegador. Você não vai escrever código Node — ele
existe para que as *ferramentas* de build funcionem: o compilador do TypeScript, o servidor
de desenvolvimento, o gerador de tipos.

Junto vem o **npm** (*Node Package Manager*), que baixa bibliotecas.

```bash
# Debian/Ubuntu
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt-get install -y nodejs

# macOS
brew install node

# confira
node --version    # precisa ser 20.19+, 22.12+ ou 24+
npm --version
```

⚠️ **Armadilha** — o Angular 22 exige Node ≥ 22.22.3 ou ≥ 24.15.0. Uma versão abaixo dá
uma mensagem clara (`The Angular CLI requires a minimum Node.js version…`), mas só na hora
de compilar, o que costuma ser tarde.

### Angular CLI

O **CLI** (*Command Line Interface*) é o assistente do Angular: cria projeto, cria
componente, compila, sobe servidor de desenvolvimento.

```bash
npm install -g @angular/cli
ng version
```

### .NET SDK

Para o backend:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0
echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc
source ~/.bashrc
dotnet --version
```

> **Chimpanzé pergunta:** *"Por que instalar o CLI com `-g`?"*
>
> `-g` é *global*: fica disponível em qualquer pasta, como um programa do sistema. Sem o
> `-g`, o pacote só valeria dentro de uma pasta específica — e você precisa do `ng` para
> *criar* a pasta.

## 1.5 Criando o projeto

Um comando:

```bash
ng new meu-primeiro-app
```

Ele faz perguntas. Para acompanhar esta apostila, responda:

| Pergunta | Resposta | Por quê |
|---|---|---|
| Stylesheet format? | **CSS** | Sass adiciona uma etapa de aprendizado sem necessidade agora |
| Server-Side Rendering (SSR)? | **No** | SSR renderiza no servidor para o primeiro carregamento. Sistema atrás de login não precisa — ninguém indexa no Google uma tela que exige senha |
| Zoneless? | **Yes** | Capítulo 5 explica; é o padrão moderno |

Entre e rode:

```bash
cd meu-primeiro-app
npm start
```

Abra `http://localhost:4200`. Está no ar.

### O que é `localhost:4200`?

- `localhost` é o seu próprio computador. Não sai para a internet.
- `4200` é a **porta** — um número que identifica *qual programa* naquele computador vai
  atender. Como o ramal de um telefone. O Angular usa 4200 por convenção; a API do Mamão
  usa 5100.

## 1.6 O que apareceu na pasta

Isto é o que assusta. Vamos por partes — e a boa notícia é que **você mexe em pouca coisa**.

```
meu-primeiro-app/
├── src/                      ← 95% do seu tempo é aqui
│   ├── index.html            ← a única página HTML de verdade
│   ├── main.ts               ← o primeiro código que roda
│   ├── styles.css            ← estilos globais
│   └── app/
│       ├── app.ts            ← o componente raiz
│       ├── app.config.ts     ← configuração da aplicação
│       └── app.routes.ts     ← o mapa de telas
├── public/                   ← arquivos copiados como estão (favicon, imagens)
├── angular.json              ← como compilar. Você quase nunca mexe
├── package.json              ← dependências e atalhos de comando
├── package-lock.json         ← versões exatas. NUNCA edite à mão
├── tsconfig.json             ← configuração do TypeScript
└── node_modules/             ← as bibliotecas baixadas. NUNCA commite
```

### `src/index.html` — a única página

```html
<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <title>Mamão — gestão sem complicação</title>
  <base href="/">
  <link rel="icon" type="image/svg+xml" href="favicon.svg">
</head>
<body>
  <app-root></app-root>
</body>
</html>
```

Repare no `<body>`: **está vazio**, tirando uma etiqueta esquisita, `<app-root>`. Não
existe nessa página nenhuma tabela, nenhum menu, nenhum texto do sistema.

É aqui que a ficha cai sobre o que é uma SPA: **o Angular preenche `<app-root>` com a
aplicação inteira, em tempo de execução.** Se você desligar o JavaScript do navegador e
abrir o Mamão, vê uma página branca.

> ⚠️ **Armadilha real do Mamão.** Por meses o nosso `index.html` dizia `<html lang="en">`,
> porque é o que o `ng new` gera — e o sistema é inteiro em português. Isso muda como o
> leitor de tela pronuncia o conteúdo e faz o navegador oferecer traduzir português para
> português. Trocamos para `lang="pt-BR"` no commit `074ffff`. Detalhe pequeno, consequência
> real para quem depende de acessibilidade.

### `src/main.ts` — a ignição

```typescript
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
```

Três linhas de verdade: *"pegue o componente `App`, aplique a configuração `appConfig`, e
plante isso dentro do `index.html`"*.

> 🔬 **Sob o capô:** `bootstrapApplication` procura no HTML a etiqueta que corresponde ao
> `selector` do componente `App` (por padrão, `app-root`), cria o componente e o insere
> ali dentro.

### `src/app/app.ts` — o componente raiz

Todo pedaço visual do Angular é um **componente**. O `App` é o de fora, que contém todos
os outros.

```typescript
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
export class App {}
```

O `template` do Mamão é literalmente isso: um buraco onde o roteador encaixa a tela atual.

### `package.json` — os atalhos

```json
{
  "scripts": {
    "start": "ng serve",
    "build": "ng build",
    "generate:api": "node ../normalize-openapi.mjs ../openapi.json && openapi-typescript ../openapi.json -o src/app/core/http/api-schema.d.ts"
  }
}
```

`npm start` executa o que estiver em `scripts.start`. O terceiro é do Mamão e é o coração
do Capítulo 9 — guarde o nome.

### `node_modules/` — a pasta gigante

Cinquenta mil arquivos, centenas de megabytes. São as bibliotecas de que o projeto depende,
e as bibliotecas das quais *elas* dependem.

**Nunca vai para o Git.** O `package.json` diz o que é preciso, o `package-lock.json` diz
em qual versão exata, e `npm ci` reconstrói a pasta idêntica em qualquer máquina. É por
isso que o `.gitignore` do Mamão tem `node_modules/` na primeira linha.

## 1.7 Seu primeiro componente de verdade

Abra `src/app/app.ts` e substitua por:

```typescript
import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-root',
  template: `
    <h1>Olá, {{ nome() }}!</h1>
    <button (click)="trocar()">Trocar nome</button>
  `,
  styles: `
    h1 { color: #11362d; font-family: sans-serif; }
  `,
})
export class App {
  readonly nome = signal('mundo');

  trocar(): void {
    this.nome.set(this.nome() === 'mundo' ? 'Mamão' : 'mundo');
  }
}
```

Salve. **Não recarregue o navegador** — olhe para ele. A tela já mudou sozinha.

Isso é o *hot reload*: o servidor de desenvolvimento percebeu o arquivo salvo, recompilou
só o que mudou e trocou na tela sem perder o estado. É a diferença mais imediata entre
desenvolver assim e desenvolver dando F5.

Clique no botão. O nome alterna.

Quatro coisas novas aconteceram aí, e cada uma tem um capítulo:

| No código | Nome | Capítulo |
|---|---|---|
| `{{ nome() }}` | interpolação | 4 |
| `(click)="trocar()"` | *event binding* | 4 |
| `signal('mundo')` | signal | 5 |
| `@Component({...})` | decorator | 4 |

---

## Para fixar

1. **Por que o `index.html` de um projeto Angular tem o `<body>` praticamente vazio?**
   <details><summary>Resposta</summary>
   Porque numa SPA o conteúdo não vem pronto do servidor: o Angular constrói a interface
   inteira em JavaScript e a insere dentro de `<app-root>` quando a aplicação inicia.
   </details>

2. **Qual a diferença entre `npm install` e `npm ci`?**
   <details><summary>Resposta</summary>
   `npm install` resolve versões a partir do `package.json` e *pode* atualizar o
   `package-lock.json`. `npm ci` apaga o `node_modules` e instala exatamente o que está no
   lock, sem alterar nada. Por ser determinístico, é o que se usa no CI — e é o que está
   no CI do Mamão.
   </details>

3. **Uma pessoa mal-intencionada abre o DevTools e apaga o `*mamaoHasPermission` de um
   botão de exclusão. Ela consegue excluir?**
   <details><summary>Resposta</summary>
   Ela consegue fazer o botão *aparecer* e consegue *disparar* a requisição. Mas o endpoint
   no .NET exige a permissão via policy e responde 403. O frontend esconde para não
   frustrar; o backend impede para proteger.
   </details>

## Laboratório

1. Crie o projeto e faça o Hello funcionar.
2. Adicione um segundo botão que **zera** o nome para string vazia. Note que o `<h1>` fica
   `Olá, !`.
3. Abra o DevTools (F12) → aba **Network** → recarregue. Veja os arquivos `.js` baixados.
   Esse é o "aplicativo inteiro" da SPA.
4. Ainda no DevTools, aba **Elements**, ache o `<app-root>` e veja que agora ele tem
   conteúdo dentro — conteúdo que não existe no `index.html` em disco.

---

**Próximo:** [Capítulo 2 — A solução completa: Angular + .NET juntos](02-solucao-angular-e-dotnet.md)
