# ADR-0006 — ASP.NET Core Identity + JWT próprio; User global com Membership

> **Parcialmente substituída pela [ADR-0020](0020-usuario-pertence-a-empresa.md).**
> O `User` deixou de ser global e passou a pertencer a uma empresa; `Membership` deixou de
> existir. O resto desta ADR — Identity + JWT próprio, refresh com rotação e detecção de
> reuso — continua valendo.

**Status:** aceita · **Data:** 2026-08

## Contexto

SaaS multi-tenant. Cenários reais que o modelo precisa suportar desde o começo:

- Contador ou consultor que atende três empresas com o mesmo e-mail
- Sócio com duas empresas
- **Funcionário que não tem login** — e talvez nunca tenha
  ([P1](../produto/mvp-e-posicionamento.md#p1))

## Opções avaliadas

| Opção | Avaliação |
|---|---|
| **ASP.NET Core Identity + JWT emitido pela própria API** | **Escolhida.** Zero custo, controle total do modelo de tenant |
| Duende IdentityServer | Licença comercial acima do faturamento mínimo; complexidade de OIDC completo sem cliente terceiro para justificar |
| Auth0 / Entra External ID | Custo por usuário ativo corrói a margem de um produto PEPM; ainda assim exigiria modelar `Membership` do lado do Mamão |
| Keycloak self-hosted | Um container pesado a mais para operar num VPS já apertado |

## Decisão

```
User        (global)   e-mail único no sistema, hash de senha, MFA, nome
Tenant                 empresa, plano, status
Membership  (N:N)      UserId × TenantId × Role × status de convite
Employee    (People)   UserId nullable
```

- Identidade com ASP.NET Core Identity (hashing, lockout, confirmação de e-mail,
  MFA, reset de senha — tudo pronto e testado).
- Access token JWT curto (~15 min) com `sub`, `tenant_id`, `role`, `scope`,
  `employee_id?`. Refresh token com rotação e detecção de reuso.
- Trocar de empresa emite **novo token**. O `tenant_id` do token é a única fonte
  do tenant ([ADR-0003](0003-multi-tenancy.md)).

## Por que `User` global desde já

Se o usuário nascer dentro do tenant, a mesma pessoa em duas empresas precisa de
duas contas e duas senhas. Corrigir depois é migração de dados com merge de contas
— caro e arriscado.

O custo hoje é uma tabela `Membership` e um seletor de empresa no header. É uma das
poucas decisões em que antecipar vale claramente a pena, porque o retrofit é
desproporcionalmente doloroso.

## Por que `Employee.UserId` é nullable — permanentemente

Consequência direta de [P1](../produto/mvp-e-posicionamento.md#p1): o produto tem
que ser útil com uma pessoa logada. Funcionário é registro de RH, não conta de
acesso.

Isso também alinha a cobrança: PEPM conta **funcionário ativo**, não login.

## Consequências

- Access token curto faz a revogação de acesso (demissão) valer em minutos —
  cenário frequente neste produto, não hipotético.
- Convite é fluxo próprio: cria `Membership` pendente, envia link com token de uso
  único e expiração.
- Ao desligar um funcionário com login, `EmployeeTerminated` desativa a
  `Membership` correspondente. Esse é um dos consumidores de evento mais críticos —
  ex-funcionário com acesso ativo é incidente de segurança.
- Se um dia for preciso SSO corporativo (cliente maior pedindo Entra ID), adiciona-se
  um provedor externo ao mesmo `User`. O modelo já comporta.

## Quando revisitar

Exigência de SSO/SAML por cliente, ou necessidade de expor OAuth para integradores
terceiros.
