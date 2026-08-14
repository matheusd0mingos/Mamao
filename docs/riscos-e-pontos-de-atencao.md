# Riscos e pontos de atenção

Itens ausentes ou subdimensionados no briefing que podem custar caro. Ordenados por
consequência.

---

## 1. LGPD — o Mamão trata dado pessoal sensível

Ausente no briefing e não é opcional.

ASO, atestado, licença médica e licença maternidade são **dados pessoais sensíveis**
(dados de saúde) nos termos do art. 5º, II da LGPD. Isso eleva o regime de proteção
acima do de um sistema de gestão comum.

Além disso, a relação é de **operador**: o cliente é o controlador dos dados dos
funcionários dele; o Mamão trata em nome dele. Isso implica obrigações contratuais
específicas.

### O que precisa existir antes do primeiro cliente pagante

| Item | Nota |
|---|---|
| Contrato de tratamento de dados (DPA) anexo aos termos | O comprador de RH um pouco mais estruturado vai pedir |
| Política de privacidade e termos de uso | Público |
| Base legal documentada | Execução de contrato de trabalho e obrigação legal cobrem a maior parte |
| Política de retenção e exclusão | Inclui excluir de verdade ao encerrar a conta — chave por tenant no storage ([ADR-0010](adr/0010-armazenamento-de-arquivos.md)) |
| Exportação dos dados do titular | Também é retenção de cliente: "seus dados são seus" |
| Registro de acesso a documento sensível | Já previsto na [auditoria](arquitetura/multi-tenancy-e-seguranca.md#auditoria) |
| Restrição de acesso a documento de saúde | Gestor não vê conteúdo. [ADR-0007](adr/0007-autorizacao.md) |
| Plano de resposta a incidente | Há dever de comunicação à ANPD e aos titulares |
| Criptografia em repouso e em trânsito | TLS + backup criptografado |

Ponto positivo: fazer isso bem **vende**. "Documento de saúde só o RH vê, e todo
acesso fica registrado" é uma frase que fecha reunião com quem já se preocupou com o
assunto.

---

## 2. Backup — o risco mais provável de todos

VPS de nó único, sem réplica, guardando documentos de terceiros. Falha de disco ou
erro de operação sem backup externo testado significa perda irreversível de dado de
cliente.

Não é hipótese remota: é o incidente mais comum nesse tipo de hospedagem.

Ver [infraestrutura](arquitetura/infraestrutura-e-deploy.md#backup). Custa um script
e poucos dólares por mês. **Faça no Marco 0.**

---

## 3. Vazamento entre tenants

Segundo risco existencial. Um único endpoint sem filtro expõe dados de RH de uma
empresa para outra — com dever de notificação à ANPD e fim da reputação do produto.

Mitigação em quatro camadas: [ADR-0003](adr/0003-multi-tenancy.md). A camada que
mais importa é o **teste automatizado de varredura**, porque ele cobre o endpoint
que você ainda não escreveu.

---

## <a name="marca"></a>4. Marca — dois pontos concretos

### 4.1 "Quer moleza? Senta no Pudim."

O slogan aparece no brand board. Em português brasileiro, "senta no pudim" carrega
uma conotação sexual imediata e amplamente reconhecida.

Isso contraria diretamente uma regra que você mesmo definiu no briefing: *"evitar
qualquer símbolo com duplo sentido"*.

O contexto agrava: o comprador do Mamão é RH, sócio ou gestor de operações, e o
produto é apresentado dentro da empresa, muitas vezes para um comitê. Uma piada
sexual no material de um sistema que gerencia atestados e férias vira objeção
comercial — e, num ambiente corporativo, potencialmente uma reclamação interna.

Recomendação: retirar de qualquer material voltado ao comprador (site, deck,
proposta, produto). Se quiser preservar o tom irreverente, ele cabe em canais
informais — e "gestão sem complicação" já entrega leveza sem esse custo.

Decisão sua. Está registrada como pendência Q1 no
[sumário](00-sumario-de-decisoes.md).

### 4.2 Registro e domínio

Verifique agora, porque é barato agora:

- Busca no INPI nas classes relevantes (NCL 9 para software, 42 para SaaS)
- `mamao.com.br`, `mamaoapp.com.br`, `usemamao.com.br` e afins
- Handles em redes sociais

Descobrir conflito depois do primeiro material impresso e do primeiro cliente é
outra ordem de grandeza de custo.

O resto da identidade está bem resolvido: o "ã" como símbolo, o til como elemento
proprietário, a paleta verde/creme/amarelo e a decisão de não desenhar o fruto —
tudo consistente com "nome inesperado, produto sério".

---

## 5. CLT — a regra que vende também é a regra que erra

[ADR-0014](adr/0014-regras-clt-de-ferias.md) trata as regras de férias como
diferencial. O outro lado: calcular errado gera prejuízo ao cliente e
responsabilidade sua.

Mitigações:

- Regras **configuráveis**, nunca hard-coded — convenção coletiva pode ser mais
  benéfica que a CLT.
- Comunicar o sistema como **auxiliar**, não como garantia de conformidade. Ver a
  fronteira em [ADR-0014](adr/0014-regras-clt-de-ferias.md).
- Limitação de responsabilidade nos termos de uso.
- Validar a suíte de testes com um contador antes do lançamento. Os testes são
  nomeados em português justamente para isso.

---

## 6. Riscos de produto

| Risco | Sinal | Mitigação |
|---|---|---|
| **Ativação**: cliente cadastra 3 funcionários e para | Trial sem CSV importado | Importação CSV no Marco 1; onboarding assistido nos primeiros clientes |
| **Sistema virar cemitério**: dados entram e envelhecem | Ninguém volta depois da semana 1 | Notificação e digest diário; a tela "Meu dia"; tarefas mínimas para criar hábito |
| **Comparação com Trello** | Comprador abre o Trello ao lado na demo | Kanban fora da V1 ([P2](produto/mvp-e-posicionamento.md#p2)); demo começa por documentos e férias |
| **Pedido de folha/ponto** | Cliente pede na primeira reunião | Resposta pronta e firme. É a fronteira que protege o roadmap |
| **Customização por cliente** | "Só preciso deste campo" | Configuração por tenant (tipos de documento, tipos de ausência, políticas), nunca código por cliente |
| **Preço errado** | Fecha rápido demais ou não fecha nunca | Comece mais alto do que o instinto sugere; desconto é reversível, aumento não é |

---

## 7. Risco de um desenvolvedor só

O maior risco de cronograma não é técnico — é o escopo do briefing versus a
capacidade de um desenvolvedor.

O briefing descreve, somando V1 a V3, algo que uma equipe de 4 a 6 pessoas levaria
mais de um ano para construir. A V1 proposta neste documento é a menor fatia que
alguém paga para usar.

Sinais de que o escopo escapou:

- Um marco passa de 3 semanas
- Você está construindo algo que nenhum piloto pediu
- Duas semanas sem deploy
- Uma decisão de arquitetura está sendo revisitada pela terceira vez

Regra: quando atrasar, **corte campos e telas**. Nunca corte teste, filtro de
tenant, tratamento de erro ou backup — essas são as coisas cuja ausência só aparece
no pior momento possível.

---

## 8. Riscos técnicos com gatilho definido

| Risco | Gatilho de ação |
|---|---|
| Dashboard lento com fan-out de pendências | p95 > 300 ms medido → projeção materializada |
| Timeline lenta com muitas pessoas | > 100 pessoas × 12 meses → tabela `availability_day` |
| Outbox acumulando | métrica `mamao.outbox.pending` crescendo → alerta já configurado |
| VPS no limite | uso de CPU/memória sustentado > 70% → subir plano antes de migrar de nuvem |
| Postgres sem tuning | consultas lentas no log → `shared_buffers`, `work_mem`, índices |
| Índice sem `tenant_id` na frente | plano de execução com seq scan → revisão de índices |

Nenhum deles justifica ação preventiva agora. Todos justificam **medição** desde o
Marco 0 — que é o motivo de a observabilidade estar no primeiro marco e não no
último.
