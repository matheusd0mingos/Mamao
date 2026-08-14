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

### 4.1 Slogan — resolvido

**Posicionamento: "Mamão — gestão sem complicação"**, com a referência a "mamão com
açúcar". "Quer moleza? Senta no Pudim" fica fora do produto e do material comercial.

Registro do motivo, para que a decisão não seja reaberta por engano: em português
brasileiro "senta no pudim" carrega conotação sexual imediata e reconhecida, o que
contrariava a regra do próprio briefing de evitar duplo sentido. O comprador é RH,
sócio ou gerente de operações, e a apresentação costuma ser interna e para mais de
uma pessoa — numa demo de sistema que gerencia atestados, vira objeção comercial.

"Gestão sem complicação" entrega a leveza pretendida sem esse custo, e conversa
diretamente com a origem do nome.

### 4.2 Domínio e marca

`mamao.tech` registrado. Dois pontos ainda abertos:

**Marca no INPI.** Domínio não é marca. Busca e depósito nas classes relevantes
(NCL 9 para software e 42 para SaaS) continuam pendentes, e é o item barato hoje /
caro depois — descobrir conflito com o primeiro cliente e o primeiro material
impresso na rua é outra ordem de grandeza de custo.

**`.tech` versus `.com.br`.** `.tech` funciona e é coerente com produto de
tecnologia. Mas o comprador deste segmento — sócio ou gerente de operações de uma
empresa de manutenção ou segurança — reconhece `.com.br` como sinal de empresa
brasileira estabelecida, e `.tech` ainda é pouco familiar fora do meio técnico.

Recomendação: registrar `mamao.com.br` se disponível (custo anual baixo) e apontar
para o mesmo produto, mantendo `.tech` como principal ou secundário. Vale também
garantir os handles em redes sociais antes do lançamento.

O resto da identidade está bem resolvido: o "ã" como símbolo, o til como elemento
proprietário, a paleta verde/creme/amarelo e a decisão de não desenhar o fruto —
tudo consistente com "nome inesperado, produto sério".

---

## 5. CLT — a regra que vende também é a regra que erra

[ADR-0014](adr/0014-regras-clt-de-ferias.md) (férias) e
[ADR-0015](adr/0015-regras-de-jornada-e-escala.md) (jornada) tratam regras
trabalhistas como diferencial. O outro lado: calcular errado gera prejuízo ao
cliente e exposição sua.

O risco cresceu com a entrada de Escalas na V1. Jornada é a área onde a **convenção
coletiva** mais frequentemente impõe regra diferente da CLT — e ela varia por
categoria, por sindicato e por região. Duas empresas do mesmo segmento em cidades
diferentes podem ter limites distintos.

Mitigações:

- Regras **configuráveis por tenant**, nunca hard-coded. Vale especialmente para
  jornada.
- Validação de escala em **modo alerta**, nunca bloqueio
  ([ADR-0015](adr/0015-regras-de-jornada-e-escala.md)). O sistema aponta; quem
  decide é o coordenador, que conhece o acordo da categoria dele.
- Comunicar o produto como **auxiliar**: "o Mamão avisa antes de você furar a
  escala", não "o Mamão garante conformidade de jornada".
- Limitação de responsabilidade nos termos de uso.
- Validar a suíte de testes com um contador e, para jornada, com um técnico de
  segurança do trabalho ou advogado trabalhista. Os testes são nomeados em português
  justamente para permitir essa revisão por quem não programa.
- Peça a convenção coletiva dos três pilotos. É o insumo que mostra o quanto de
  configuração o produto vai precisar de verdade.

### Risco de escopo: banco de horas

Com escalas no produto, o pedido "e o banco de horas?" vai chegar — provavelmente do
primeiro piloto. É a porta de entrada para apuração de jornada e, dali, para ponto
eletrônico, que é escopo explicitamente recusado
([P8](00-sumario-de-decisoes.md)).

A fronteira que separa os dois: o Mamão **planeja** a jornada; ele não **apura**.
Tenha a resposta pronta antes da pergunta.

---

## 6. Riscos de produto

| Risco | Sinal | Mitigação |
|---|---|---|
| **Ativação**: cliente cadastra 3 funcionários e para | Trial sem CSV importado | Importação CSV no Marco 1; onboarding assistido nos primeiros clientes |
| **Sistema virar cemitério**: dados entram e envelhecem | Ninguém volta depois da semana 1 | Notificação e digest diário; a tela "Meu dia"; tarefas mínimas para criar hábito |
| **Comparação com Trello** | Comprador abre o Trello ao lado na demo | Kanban fora da V1 ([P2](produto/mvp-e-posicionamento.md#p2)); demo começa por documentos e férias |
| **Pedido de folha/ponto/banco de horas** | Cliente pede na primeira reunião | Resposta pronta e firme. É a fronteira que protege o roadmap |
| **Escala do piloto mais complexa que o modelo** | A planilha tem um padrão que `ScheduleCycle` não expressa | Colete as três planilhas **antes** do Marco 4. A interseção é a V1; as diferenças viram configuração, não código por cliente |
| **Escala publicada com erro** | Equipe organiza a vida pelo turno errado | Rascunho ≠ publicada; publicar notifica; toda alteração pós-publicação é auditada e avisada |
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
