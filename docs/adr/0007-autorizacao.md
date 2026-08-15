# ADR-0007 — Autorização por permissão + escopo de dados

**Status:** aceita · **Data:** 2026-08

## Contexto

O briefing pede RBAC. RBAC responde *"o que esta pessoa pode fazer?"*. Num sistema
de RH, a pergunta que mais importa é outra: *"sobre quem?"*.

Um gestor pode aprovar férias — da própria equipe. O RH pode ver atestado — de todo
mundo. O funcionário pode ver o próprio registro — e só. Papel sozinho não expressa
isso.

## Decisão

Duas dimensões independentes, combinadas.

### 1. Permissão (o quê)

Claims granulares, agrupadas em papéis:

```
people.read  people.write  people.delete
timeoff.request  timeoff.approve
documents.read  documents.upload  documents.approve
work.read  work.assign
schedule.read  schedule.write
audit.read  settings.write  billing.manage
```

Verificação sempre por policy, nunca por papel:

```csharp
[Authorize(Policy = "documents.approve")]      // sim
if (user.Role == "RH") { … }                   // não
```

A primeira forma sobrevive à criação de papéis customizados por tenant; a segunda
exige caçar `if` pelo código inteiro.

### 2. Escopo de dados (sobre quem)

```csharp
public enum DataScope { Self, Team, Department, Company }
```

Resolvido uma vez por request e convertido em predicado por um serviço único:

```csharp
public interface IAccessScope
{
    Task<EmployeeFilter> ForAsync(string permission, CancellationToken ct);
}
```

`Self` → id próprio · `Team` → subordinados diretos · `Department` → subárvore do
setor · `Company` → todos.

Centralizar aqui evita que cada endpoint reinvente o filtro — e que um deles o
reinvente errado, que é o vazamento interno mais provável.

Para autorizar **um** recurso específico, use autorização baseada em recurso do
ASP.NET Core (`IAuthorizationHandler<TRequirement, TResource>`), capaz de perguntar
"este documento pertence a alguém da minha equipe?".

## Papéis iniciais

| Papel | Permissões | Escopo padrão |
|---|---|---|
| Owner | todas | Company |
| RH | pessoas, documentos, férias (aprovar), auditoria, disponibilidade | Company |
| Gestor | leitura, aprovação, atribuição, disponibilidade | Team ou Department |
| Gerente de TI | contas, configuração, auditoria, quadro de pessoas, **estrutura** | Company |
| Funcionário | próprio registro, solicitar, enviar documento, disponibilidade | Self |

Papéis customizados por tenant entram na V2 — o modelo já suporta, é só UI.

### Por que o gerente de TI existe, e por que ele não vê disponibilidade

Quem cuida das contas não é quem cuida das pessoas — isso é verdade até numa empresa de
quinze. Quem instala o computador do novo funcionário não é quem aprova as férias dele.

A consequência de desenho é a permissão `availability.read`, separada de `people.read`.
"Quem trabalha aqui" e "quem está de afastamento médico" não têm a mesma sensibilidade: a
segunda pode revelar dado de saúde. Sem essa separação, dar acesso ao sistema para quem
administra as contas obrigaria a entregar junto a agenda médica da empresa inteira — e o
próprio texto da política de privacidade deixaria de ser verdade.

Ele também mantém o organograma: cria seção e cargo, nomeia chefe e move gente de setor
(`org.write`). Isso é uma segunda separação, e pelo mesmo raciocínio: **reorganizar a
estrutura é administração; contratar, desligar e mexer em contrato é RH.** Dar
`people.write` "porque ele precisa mover fulano de seção" entregaria junto o botão de
desligar. Por isso mover de setor tem rota própria (`PUT /employees/{id}/department`), com
o de/para na auditoria — em vez de ser mais um campo do formulário completo.

**O que isto NÃO é:** o gerente de TI tem `settings.write` e `users.invite`, então ele pode
se conceder outro papel. A separação é organizacional e auditável, não uma barreira contra
quem administra o sistema. Barreira contra o administrador exige separação de ambiente, e
isso não existe neste estágio. Está escrito aqui para ninguém vender o que não temos.

## Dado sensível de saúde

ASO, atestado e licença médica recebem tratamento além do papel:

- Visíveis apenas para RH e para o próprio funcionário.
- **O gestor direto não vê o conteúdo** — vê que o documento existe e se está válido.
- Todo acesso ao arquivo é auditado, sem exceção.

Isso é boa prática de privacidade sob a LGPD e, na prática comercial, um argumento
de venda para quem compra RH.

## Consequências

- Toda consulta de lista passa pelo `IAccessScope`. Endpoint que ignora isso é bug
  de segurança — cubra com o teste genérico de varredura.
- O frontend replica a verificação para **não frustrar** (esconder botão); o backend
  verifica para **proteger**. Diretiva sem policy correspondente é falha disfarçada
  de feature.
- Permissões viajam no token. Mudança de papel só vale no próximo refresh (~15 min).
  Para revogação imediata, invalide o refresh token.

## Quando revisitar

Ao surgir necessidade de permissão por objeto (ex.: "só este projeto") ou de papéis
customizados pelo cliente.
