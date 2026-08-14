# ADR-0020 — Usuário pertence à empresa

**Status:** aceita · **Data:** 2026-08 · **Substitui:** [ADR-0006](0006-identidade.md), decisão S6

## Contexto

A [ADR-0006](0006-identidade.md) definiu `User` **global** por e-mail + `Membership(UserId,
TenantId, Role)`, para que a mesma pessoa (contador, consultor, sócio de duas empresas)
atendesse várias empresas com uma senha só.

Duas coisas derrubaram essa justificativa:

1. **A [ADR-0018](0018-organizacoes-e-unidades.md).** Grupo com subordinadas passou a ser
   **um** tenant com árvore de unidades. O caso "uma pessoa precisa ver A e B" deixou de
   precisar de conta compartilhada — resolve-se dentro do tenant, por escopo. O que sobra é
   "empresas independentes que por acaso compartilham uma pessoa", que é hipótese, não
   demanda de cliente.
2. **O público mudou.** Com organização militar entre os pilotos
   ([ICP](../produto/icp-e-pilotos.md)), "descobrir que este e-mail também existe em outra
   unidade" deixa de ser detalhe e vira enumeração de efetivo.

O autor pediu a ordem natural: **cria a empresa → a empresa cria os usuários → os usuários
têm papéis e níveis diferentes.**

## Decisão

```
Tenant (empresa)
  └── MamaoUser        TenantId obrigatório · Role · e-mail ÚNICO GLOBALMENTE
        └── RefreshToken
```

- `MamaoUser` ganha `TenantId`. Excluir a empresa exclui os usuários dela.
- **`Membership` deixa de existir.** Um usuário tem um papel em uma empresa; a tabela de
  ligação N:N não descreve mais nada que o modelo não diga.
- O e-mail continua **único no sistema inteiro**, e essa é a decisão que mais economiza
  trabalho — ver abaixo.

## A única dificuldade real, e por que ela não vira problema

No login a pessoa digita e-mail e senha. **Não existe tenant ainda** — ele só é conhecido
depois de achar o usuário. Se o e-mail fosse único apenas *dentro* da empresa, dois
`joao@gmail.com` em empresas diferentes seriam indistinguíveis, e o login passaria a
exigir que a pessoa informasse a empresa antes: subdomínio (`aurora.mamao.tech`) ou um
campo a mais na tela.

**Mantendo o e-mail único globalmente, esse problema simplesmente não nasce.** O login
continua exatamente como hoje — e-mail + senha, um resultado só — e ainda ganhamos:

- o índice único global do ASP.NET Core Identity continua valendo, sem `IUserStore`
  customizado nem `UserName` com prefixo de tenant;
- some a tela de "escolha a empresa" e o `requiresTenantSelection` da resposta de login;
- nenhum certificado curinga, nenhuma resolução de tenant por Host.

Unicidade global do e-mail **não** significa usuário global. A linha pertence a uma
empresa; o índice apenas garante que o endereço aponta para uma pessoa só.

### Preço, dito com clareza

Uma pessoa que trabalhe em duas empresas clientes precisa de **dois e-mails**. É o preço
do isolamento, e é aceito. A mensagem de erro ao cadastrar um e-mail já usado **não pode
dizer onde ele está em uso** — senão vira exatamente a enumeração que motivou a mudança.
Texto: "Este e-mail não está disponível."

### Por que `identity.users` continua fora da RLS

Toda tabela tenant-owned tem RLS ([ADR-0003](0003-multi-tenancy.md)). `users` é a exceção
consciente: o login precisa de uma consulta **sem tenant definido**, e uma policy que
permitisse isso não protegeria nada. O que compensa:

- nenhum endpoint devolve usuário de outro tenant; toda listagem filtra por `TenantId`
  explicitamente, com teste de arquitetura cobrindo;
- o token sempre carrega o tenant, e tudo depois dele é filtrado;
- a tabela guarda e-mail, hash de senha e nome — nunca dado de funcionário, documento ou
  escala, que são de módulos com RLS.

## Consequências

- **Migração com backfill:** `users.tenant_id` vem do `Membership` existente. Usuário com
  mais de um vínculo é conflito real e **para a migração** — igual ao backfill de cargo, e
  pelo mesmo motivo: melhor falhar alto que decidir por conta própria de quem é a conta.
- O fluxo de login simplifica dos dois lados; a tela de seleção de empresa some.
- A exclusão de conta pela LGPD fica mais simples e a nota sobre `User` global no
  [inventário](../arquitetura/multi-tenancy-e-seguranca.md#inventario) deixa de valer:
  excluir a empresa passa a excluir os usuários dela, sem ressalva.
- Convite de funcionário (V1.5) fica direto: `Employee.Email` → cria `MamaoUser` no mesmo
  tenant. Sem procurar conta existente em outro lugar, sem fundir nada.

## <a name="mesma-pessoa"></a>A mesma pessoa em duas empresas

O caso é raro e vai acontecer: sócio de duas empresas, consultor, militar transferido de
unidade. Três coisas a separar, porque só uma delas é problema.

**Não é problema:** ser **funcionário** em duas empresas. `Employee` já é por tenant desde
sempre; nada impede a mesma pessoa estar cadastrada nas duas. O que colide é só o **login**.

**Também não é problema:** mudar de empresa. É criar o usuário no destino e desativar na
origem. Nenhum dado precisa se mover.

**O caso real:** precisar **acessar as duas ao mesmo tempo**.

### Hoje: um segundo endereço

O e-mail é único no sistema, então a segunda conta precisa de outro endereço. Na prática
resolve-se com sufixo (`joao+aurora@gmail.com`, `joao+batalhao@gmail.com`), que a maior
parte dos provedores entrega na mesma caixa. É contorno, e está assumido como tal.

### Quando doer: desambiguação depois da senha

Se aparecer **cliente pedindo** — não hipótese —, o caminho já está escolhido, e não é
voltar ao usuário global:

1. A unicidade do e-mail passa de global para **`(TenantId, NormalizedEmail)`**.
2. O login procura o e-mail entre os tenants e verifica a senha contra **cada** candidato.
3. Um candidato bate → entra direto, exatamente como hoje. Mais de um bate (mesma senha nas
   duas) → **só aí** pergunta em qual empresa entrar.

O caso comum não muda em nada. A pergunta extra só aparece para quem realmente tem duas
contas, e **só depois de a senha ser conferida** — perguntar antes revelaria a quem digitasse
um e-mail qualquer em quais empresas ele existe, que é justamente a enumeração que motivou
esta ADR.

Custo estimado: índice único trocado, um `IUserValidator` que valida por tenant (o padrão do
Identity consulta o sistema inteiro) e a consulta de login escrita à mão em vez de
`FindByEmailAsync`. Ordem de 100 linhas, nenhuma delas urgente.

### Por que dá para adiar sem risco

**Ir de restrito para permissivo é trocar um índice.** `UNIQUE (normalized_email)` vira
`UNIQUE (tenant_id, normalized_email)` — sem migração de dado, sem fusão de conta, sem
decidir de quem é o quê. É exatamente o contrário do que a [ADR-0006](0006-identidade.md)
temia ao escolher o usuário global: lá, sair do global exigiria separar contas já
compartilhadas. Aqui, o arrependimento é barato — e por isso a decisão certa hoje é a mais
restrita.

## Quando revisitar

Quando um cliente pedir acesso a duas empresas com o mesmo login. Aí executa-se o plano
acima. Antes disso, construir a desambiguação seria complexidade permanente no caminho mais
sensível do sistema para atender um caso que ainda não existe.
