# Capítulo 13 — Bugs reais e o que eles ensinam

> **Objetivo:** estudar sete defeitos verdadeiros do Mamão — o sintoma, a investigação, a
> causa e a lição. Nenhum é inventado.

Este é o capítulo que justifica a apostila existir. Tutorial ensina o caminho feliz;
software real é feito de desvios.

Um fio conecta quase todos eles:

> **O que não roda de verdade não está testado.**

Build verde não é prova de nada. Teste que não executa é pior que teste ausente, porque dá
a sensação de rede de segurança sem a rede.

---

## Caso 1 — O sorteio que sorteava sempre igual

**Sintoma.** Um teste que rodava com sementes diferentes falhava de vez em quando. Parecia
flaky — daqueles que você roda de novo e passa.

**Contexto.** O Mamão escala pessoas por rodízio. Quando a empresa configura o desempate
como "sorteio", a ordem deveria ser diferente a cada missão.

**A investigação.** Em vez de rodar de novo, medimos: em 200 missões diferentes, quantas
produziam ordem idêntica? **28%.** Isso não é sorte ruim, é defeito.

**A causa.** O embaralhamento usava um hash caseiro:

```csharp
hash = hash * 31 + b;   // ❌
```

Isso é **aditivo**. A semente da missão entrava somando uma constante ao valor de todo
mundo — e somar a mesma constante a todos **não muda a ordem relativa**. O sorteio era uma
ordem fixa disfarçada de aleatória.

**A correção.** FNV-1a, que mistura os bits:

```csharp
hash = (hash ^ b) * 16777619;
```

Colisões de ordem caíram para cerca de 1 em 2000.

**A lição — duas, na verdade.**

1. **Teste intermitente é sintoma, não incômodo.** Rodar de novo até passar é apagar o
   alarme de incêndio.
2. **Teste a propriedade, não o exemplo.** O teste antigo comparava duas execuções. O novo
   mede a distribuição em 200 sementes. Propriedade estatística exige verificação
   estatística.

E o mais importante: **este era um bug de produto, não de código.** Ninguém veria uma
exceção. As pessoas é que reclamariam de sempre entrar nos mesmos dias — e ninguém
associaria isso a um `*31`.

---

## Caso 2 — `OrderBy` depois de `Select` derruba o painel

**Sintoma.** `/api/v1/today` respondia **500**. O build estava verde e os testes unitários
passavam.

**A causa.**

```csharp
// ❌
var itens = await query
    .Select(x => new TodayItem(x.Nome, x.Data))
    .OrderBy(x => x.Data)
    .Take(10)
    .ToListAsync(ct);
```

Depois do `Select`, o EF Core precisa traduzir a ordenação sobre a **projeção**. Naquele
caso não conseguiu e estourou em execução.

**A correção:**

```csharp
// ✅
var itens = await query
    .OrderBy(x => x.Data)
    .Take(10)
    .Select(x => new TodayItem(x.Nome, x.Data))
    .ToListAsync(ct);
```

**A lição.** LINQ tem duas naturezas com a **mesma sintaxe**: `IEnumerable` (em memória,
sempre funciona) e `IQueryable` (vira SQL, nem tudo é traduzível). O compilador não
diferencia. Toda consulta nova precisa **rodar contra um banco de verdade** pelo menos uma
vez — o teste unitário com lista em memória usaria `IEnumerable` e passaria alegremente.

---

## Caso 3 — O kanban ordenado alfabeticamente

**Sintoma.** No quadro de demandas, prioridade "Normal" aparecia acima de "Alta".

**A causa.** O enum é gravado como **texto** no banco (decisão consciente — ver Capítulo 9).
E então:

```csharp
.OrderByDescending(w => w.Priority)   // ❌ ordena a STRING
```

Alfabeticamente decrescente: `Normal` > `Baixa` > `Alta`.

**A correção** — um CASE explícito:

```csharp
.OrderByDescending(w => w.Priority == Priority.Alta ? 3
                      : w.Priority == Priority.Normal ? 2 : 1)
```

**A lição.** A decisão de guardar enum como texto é boa (legível no log, resistente a
reordenação) e **tem consequência**: ordenação passa a ser alfabética. Toda decisão de
armazenamento tem efeito colateral em algum lugar, geralmente longe.

E note: build verde, testes de domínio passando. Só aparecia num quadro **com dados
variados** — que é o que ninguém tem no ambiente de teste.

---

## Caso 4 — O campo estreito que insistia em ser largo

**Sintoma.** Um `<input type="number">` com classe `.ordem`, estilizado para 80 px, ocupava
a largura toda.

**A causa.** Especificidade CSS.

```css
/* global, em styles.css */
input:not([type=checkbox]):not([type=radio]) { width: 100%; }   /* peso (0,2,1) */

/* local, no componente */
.ordem { width: 80px; }                                          /* peso (0,1,0) */
```

O seletor global tem **duas** pseudoclasses `:not()` mais o elemento — mais específico que
uma classe sozinha. O global ganhou.

**A correção**, feita no design system e não com `!important`:

```css
input.estreito:not([type=checkbox]):not([type=radio]) { width: auto; }
```

**A lição.** Estilo de componente **não** ganha automaticamente de estilo global. Quem ganha
é o seletor mais específico. E quando você se pega escrevendo `!important`, o problema
quase sempre é uma regra global genérica demais — conserte lá.

---

## Caso 5 — O `<select>` que não selecionava

**Sintoma.** A tela do chefe de setor mostrava "Ninguém" mesmo em setores que tinham chefe.

**A causa.** O valor vinha de uma requisição; as opções, de outra.

```html
<select [value]="chefe.setorId">
  @for (s of setores(); track s.id) { <option [value]="s.id">{{ s.nome }}</option> }
</select>
```

`[value]` é aplicado quando o elemento é criado. Nesse instante o `<select>` está **vazio** —
as `<option>` ainda não chegaram. Definir o valor de um `<select>` sem a opção
correspondente **não faz nada, silenciosamente**. Quando as opções chegam, ninguém reaplica.

**A correção:**

```html
<select [ngModel]="chefe.setorId" [ngModelOptions]="{ standalone: true }">
```

`ngModel` reconcilia: observa o valor e as opções, e aplica quando ambos existem.

**A lição.** Binding que depende de dado assíncrono precisa se **reconciliar** quando o dado
chega. Mesma família do `track $index` e de qualquer coisa que assume ordem de chegada.

---

## Caso 6 — Oito testes que nunca tinham rodado

**Sintoma.** O CI acusou 8 de 10 testes de integração falhando. E o e-mail dizia "backend
quebrado".

**A investigação.** Nada tinha sido quebrado. Aqueles testes **nunca haviam executado**:

1. Na máquina de desenvolvimento eles se marcam como `skipped` sem Docker.
2. No CI, o gatilho é `push` em `main`. Até aquele dia, a branch tinha outro nome. **O
   primeiro `main` da história do repositório foi o primeiro CI da história.**

Duas redes de segurança, ambas furadas no mesmo ponto.

**As causas — em camadas.** Cada uma só ficou visível depois de corrigir a anterior:

**(a)** Os testes mandavam `positionName`; a API passou a exigir `positionId`. → 400 em todo
cadastro.

**(b)** Corrigido isso, virou **500**: a fábrica de teste migrava três `DbContext` e não o
`AuditDbContext`. Como a auditoria é gravada **na mesma transação** do fato, a tabela
faltando derrubava toda admissão.

**(c)** O teste de RLS concedia permissão nos schemas mas não na auditoria, e o `INSERT`
cru ainda citava a coluna `position_name`, que já não existia. O teste provava a política de
segurança contra um banco **mais permissivo** que o de produção.

**(d)** Um bug de verdade, e não só nesse endpoint: o handler global transformava
**qualquer** exceção em 500 — inclusive `BadHttpRequestException`, que já carrega o status
400. Parâmetro de query obrigatório faltando virava "erro interno". Mentia duas vezes: para
o cliente, dizendo que a culpa era do servidor; e no log, enchendo o nível `Error` de erro
de digitação, que é exatamente onde a falha de verdade se esconde.

**A lição.** "Skipped: 10, Failed: 0" **se lê como verde**. Por isso a fábrica hoje falha
explicitamente quando está no CI:

```csharp
if (Environment.GetEnvironmentVariable("CI") is not null)
{
    throw new InvalidOperationException(
        "Docker indisponivel no CI. Os testes de integracao nao podem ser pulados aqui: " +
        "sem eles a suite passa a aprovar qualquer coisa.", ex);
}
```

E o README do projeto passou a dizer, sem rodeios, que sem Docker o total honesto é 204 e
não 214.

---

## Caso 7 — O teste que nunca poderia passar duas vezes

**Sintoma.** Com os testes já verdes, o CI continuou vermelho — agora em outro passo:

```
##[error]web/openapi.json desatualizado. Regenere e comite.
-      "url": "http://127.0.0.1:36335"
+      "url": "http://127.0.0.1:44737"
```

**A causa.** Para gerar o contrato, o host sobe numa **porta efêmera** e escreve essa URL no
bloco `servers` do documento. Uma porta diferente a cada execução.

A checagem comparava o arquivo commitado com o recém-gerado. Como a porta sempre mudava,
**a comparação nunca poderia dar igual**. Era uma trava impossível, e a mensagem de erro
apontava para o lugar errado — não havia nada desatualizado.

**A correção:** remover `servers` na normalização.

```javascript
delete documento.servers;
```

Não se perde nada: o documento existe para gerar tipos, e o frontend fala com a API por
caminho relativo.

**A lição.** Ao escrever uma checagem de "está atualizado?", pergunte primeiro: **este
processo é determinístico?** Timestamp, porta aleatória, ordem de dicionário, caminho
absoluto — qualquer um deles transforma a verificação em ruído. E ruído recorrente treina o
time a ignorar o CI, que é o pior estrago possível.

---

## Caso bônus — a verificação falsa

Este aconteceu enquanto o Caso 7 estava sendo corrigido, e é sobre método.

Para provar que a geração virou determinística, rodei duas vezes e comparei os arquivos.
**Idênticos.** Ia declarar resolvido.

Só que a geração estava **falhando** — faltava a variável `Jwt__SigningKey` no ambiente
local — e por isso **não escrevia arquivo nenhum**. Eu comparei duas cópias do arquivo
antigo. O "idênticos" era verdadeiro e completamente sem valor.

**A lição.** Antes de comparar saídas, verifique que a saída foi **produzida**. Um teste que
compara dois nadas passa sempre.

---

## O padrão por trás dos sete

| Caso | O que enganou |
|---|---|
| 1 | Teste intermitente tratado como incômodo |
| 2 | Build verde ≠ consulta traduzível |
| 3 | Testes passando com dados pobres demais |
| 4 | Supor que o estilo local vence o global |
| 5 | Supor que o dado já chegou |
| 6 | "Skipped" lido como "passou" |
| 7 | Comparação sobre processo não determinístico |
| bônus | Comparar saídas sem checar se foram geradas |

Em nenhum deles o compilador reclamou. Em nenhum deles o build ficou vermelho antes da
correção.

É por isso que, no Mamão, toda funcionalidade é verificada contra a coisa real — Postgres de
verdade, API rodando com o mesmo papel de banco de produção, navegador de verdade abrindo a
tela. Não porque somos caprichosos: porque **essa** foi a única forma de achar sete
defeitos que passaram por revisão de código, tipagem estrita e testes unitários.

---

## Para fixar

1. **Por que `hash * 31 + b` não embaralha?**
   <details><summary>Resposta</summary>
   Porque é aditivo: a semente soma a mesma constante ao valor de todos os elementos, e
   somar a mesma constante a todos não altera a ordem relativa entre eles.
   </details>

2. **Por que o Caso 2 não foi pego por teste unitário?**
   <details><summary>Resposta</summary>
   Porque teste unitário com lista em memória usa `IEnumerable`, onde toda operação
   funciona. O problema só existe em `IQueryable`, que precisa traduzir para SQL.
   </details>

3. **Como transformar "skipped" de armadilha em proteção?**
   <details><summary>Resposta</summary>
   Permitindo o skip apenas no ambiente de desenvolvimento e falhando explicitamente no CI,
   como o Mamão faz checando a variável `CI`.
   </details>

## Laboratório

1. **Reproduza o Caso 4.** Coloque no CSS global
   `input:not([type=checkbox]) { width: 100% }`, crie um componente com `.estreito { width: 80px }`
   e veja o campo largo. Abra o DevTools → Styles: a regra perdedora aparece **riscada**.
   Conserte sem `!important`.
2. **Reproduza o Caso 5** com `setTimeout` de 2 segundos para popular as opções.
3. **Reproduza o Caso 1** em qualquer linguagem: ordene uma lista por
   `hash(item) * 31 + semente` e conte quantas ordens diferentes saem em 200 sementes.
   Depois troque por FNV-1a e conte de novo.
4. **Escreva uma checagem determinística.** Faça um script que gera um arquivo com
   timestamp dentro e depois compara com o commitado. Veja falhar sempre. Remova o
   timestamp e veja passar. É o Caso 7 em cinco minutos.

---

**Anterior:** [Capítulo 12](12-ponta-a-ponta.md) ·
**Próximo:** [Capítulo 14 — Exercícios](14-exercicios.md)
