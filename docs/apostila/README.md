# Apostila: Angular + .NET, do zero, usando o Mamão como estudo de caso

Este material ensina desenvolvimento web moderno com **Angular** no navegador e **.NET**
no servidor, usando um sistema real como laboratório: o [Mamão](../../README.md), que
gerencia escalas, disponibilidade e documentos de equipes pequenas.

Não é um tutorial de "faça um to-do list". Todo exemplo aqui é código que está rodando em
produção, com o motivo pelo qual ele é daquele jeito — inclusive os erros que a gente
cometeu e como eles foram descobertos.

## Para quem é

Para quem **nunca** escreveu uma linha de Angular. Começamos explicando o que é uma página
web, e não paramos até você entender por que um `OrderBy` depois de um `Select` faz uma
tela responder 500.

Se você já sabe Angular, comece no [Capítulo 9](09-o-contrato-openapi.md) — a parte da
integração com .NET é onde mora o conteúdo que quase não existe escrito em português.

## Como estudar

Leia na ordem. Cada capítulo assume o anterior. Ao final de cada um há:

- **Para fixar** — perguntas de compreensão, com resposta.
- **Laboratório** — algo para digitar e rodar. Não pule: ler código não ensina a escrever.

Você vai precisar de um computador com Linux, macOS ou Windows, e cerca de 40 minutos de
instalação no [Capítulo 1](01-do-zero-ao-primeiro-hello.md).

## Índice

### Parte I — Fundamentos

| # | Capítulo | O que você sai sabendo |
|---|---|---|
| 1 | [Do zero ao primeiro Hello](01-do-zero-ao-primeiro-hello.md) | O que é uma SPA, o que o navegador faz, `ng new`, e o que é cada arquivo gerado |
| 2 | [A solução completa: Angular + .NET juntos](02-solucao-angular-e-dotnet.md) | Como nasce um projeto com os dois lados, quem serve o quê, e a estrutura de pastas |
| 3 | [TypeScript: o mínimo necessário](03-typescript-minimo.md) | Tipos, interfaces, `null` vs `undefined`, `async/await`, genéricos |
| 4 | [Componentes e templates](04-componentes-e-templates.md) | O átomo do Angular, interpolação, bindings, `@if`/`@for` |
| 5 | [Signals: como o Angular sabe redesenhar](05-signals.md) | Reatividade, `signal`, `computed`, `effect`, e por que somos *zoneless* |
| 6 | [Injeção de dependência](06-injecao-de-dependencia.md) | `inject()`, serviços, escopo, e por que isso não é frescura |
| 7 | [Rotas, guardas e lazy loading](07-rotas-e-guardas.md) | Navegação sem recarregar, proteção de tela, carregamento sob demanda |
| 8 | [Formulários](08-formularios.md) | Reactive Forms, validação, e o erro do servidor caindo no campo certo |

### Parte II — A ponte com o .NET

| # | Capítulo | O que você sai sabendo |
|---|---|---|
| 9 | [O contrato: OpenAPI como fonte da verdade](09-o-contrato-openapi.md) | Por que ninguém escreve DTO à mão, e como o C# gera o TypeScript |
| 10 | [HTTP, interceptors, autenticação e erros](10-http-e-interceptors.md) | `HttpClient`, JWT, refresh silencioso, `ProblemDetails` virando mensagem |
| 11 | [Desenvolvimento vs. produção: proxy, CORS e Caddy](11-dev-vs-producao.md) | Por que não temos CORS, e o que muda quando vai para o servidor |

### Parte III — Estudo de caso

| # | Capítulo | O que você sai sabendo |
|---|---|---|
| 12 | [Uma funcionalidade ponta a ponta](12-ponta-a-ponta.md) | Da tabela no Postgres ao pixel na tela, seguindo um dado só |
| 13 | [Bugs reais e o que eles ensinam](13-bugs-reais.md) | Sete defeitos verdadeiros do Mamão, com o diagnóstico completo |
| 14 | [Exercícios](14-exercicios.md) | Onze exercícios com gabarito, do fácil ao difícil |

### Parte IV — A fundação

Os dois capítulos que a Parte II assume que você já sabe. Se em algum momento você se
pegou pensando *"tá, mas o que é um container, afinal?"* ou *"por que o backend tem
catorze projetos?"*, é aqui.

| # | Capítulo | O que você sai sabendo |
|---|---|---|
| 15 | [Docker para macacos](15-docker-para-macacos.md) | Container vs. VM, Dockerfile, camadas, Compose, volumes, rede — e os dois bugs de produção que moldaram o Dockerfile do Mamão |
| 16 | [A solução .NET por dentro](16-a-solucao-dotnet.md) | Monolito modular, os 4 projetos de um módulo, Result, outbox, as 3 camadas de multi-tenancy, testes de arquitetura |

> **Pode ler fora de ordem.** O 15 combina bem logo depois do
> [Capítulo 11](11-dev-vs-producao.md), e o 16 logo depois do
> [Capítulo 12](12-ponta-a-ponta.md).

### Apoio

- [Glossário](99-glossario.md) — todo termo em negrito da apostila está aqui.

## Convenções

Ao longo do texto:

> **Chimpanzé pergunta:** perguntas ingênuas que todo mundo tem e ninguém faz em voz alta.
> Elas são respondidas na hora.

⚠️ **Armadilha** — um erro que a gente cometeu de verdade e você vai cometer também.

🔬 **Sob o capô** — o que está acontecendo um nível abaixo. Pode pular na primeira leitura.

## Aviso honesto

Esta apostila reflete o Angular na versão **22** e o .NET na versão **10**. Angular mudou
muito entre a versão 14 e a 17: NgModules deixaram de ser obrigatórios, `*ngIf` virou
`@if`, e signals substituíram boa parte do RxJS. **Boa parte dos tutoriais que você achar
no Google ensina o jeito antigo.** Quando o texto disser "o jeito antigo era X", é
justamente para você reconhecer o que encontrar por aí.

---

*Material de estudo do projeto Mamão. Licença [Apache-2.0](../../LICENSE), igual ao resto
do repositório — use em aula, em treinamento, onde quiser.*
