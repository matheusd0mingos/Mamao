# Cliente ideal e plano dos pilotos

Segmento inicial definido: **operação com plantão, turno e rodízio.**

Isso não é um detalhe de marketing — é a decisão que reordenou a V1 e trouxe
Escalas para dentro dela ([ADR-0015](../adr/0015-regras-de-jornada-e-escala.md)).

---

## Perfil do cliente ideal

| Dimensão | Definição |
|---|---|
| Porte | 15 a 60 funcionários (abaixo de 15, a planilha ainda funciona; acima de 60, entra concorrente maior) |
| Operação | Turnos, plantões, rodízio de folga, equipe em campo |
| Setores | Manutenção industrial e predial, segurança patrimonial, clínicas e home care, facilities e limpeza, logística, serviços de campo |
| Marca da dor | A escala vive numa planilha que **uma pessoa só** sabe mexer |
| Segunda dor | Certificação obrigatória com validade (NR, ASO, reciclagem, CNH) |
| Quem compra | Sócio, gerente de operações ou coordenador de RH — quase nunca um departamento de TI |
| Quem usa todo dia | O coordenador que monta a escala. É ele quem renova ou cancela |

## Por que esse segmento

1. **A dor tem consequência imediata e visível.** Escala furada é cliente sem
   atendimento hoje, não um relatório ruim no fim do mês.
2. **A planilha realmente não resolve.** Ela não valida interjornada, não sabe que
   o Carlos entra de férias, não avisa que a NR-35 venceu. Aqui o software ganha da
   planilha por mérito, não por conveniência.
3. **Concorrência fraca no porte certo.** Existe software de escala bom para
   hospital de 800 leitos. Para a empresa de 30 pessoas, o que sobra é planilha ou
   sistema caro e pesado demais.
4. **Documento com validade é regra, não exceção.** NR-10, NR-35, NR-33, ASO
   periódico, reciclagem de vigilante, CNH. A conformidade é fiscalizável, e o
   custo de errar é multa ou embargo.
5. **Férias doem mais.** Numa operação 24×7, tirar uma pessoa do noturno afeta a
   cobertura imediatamente — o que dá sentido à linha de cobertura por turno.

## O que muda no produto por causa disso

| Item | Efeito |
|---|---|
| Escalas | Sai da V1.5, entra na V1, antes de Férias |
| Disponibilidade | `OffShift` vira status de primeira classe; escala passa a ser a fonte primária de horas do dia |
| Cobertura de férias | Passa a ser **por turno**, não por dia |
| Capacidade | Deriva das horas de turno, não da jornada contratada |
| "Meu dia" | Gira em torno do turno, não de um quadro de tarefas |
| Tarefas | Escopo ainda menor na V1 |
| Documentos | Catálogo pré-configurado com os tipos do segmento |
| Mobile | Sobe de prioridade — o funcionário de campo consulta o turno no celular |

---

## Catálogo de documentos pré-configurado

Entregar o tenant novo já com os tipos certos elimina a primeira hora de
configuração — e demonstra, na primeira tela, que o produto conhece a operação do
cliente.

| Tipo | Validade típica | Observação |
|---|---|---|
| ASO admissional / periódico / demissional | Conforme PCMSO | Periodicidade varia por risco e idade |
| NR-10 (elétrica) e complementar | Reciclagem periódica | Manutenção, indústria |
| NR-35 (trabalho em altura) | Reciclagem periódica | |
| NR-33 (espaço confinado) | Reciclagem periódica | |
| NR-12, NR-11 (operador) | Conforme treinamento | |
| Reciclagem de vigilante | Conforme legislação do setor | Segurança patrimonial |
| CNH | Vencimento na carteira | Campo, logística |
| Registro de conselho (CREA, COREN, CRM) | Anuidade | Manutenção, saúde |
| Documentos pessoais (RG, CPF, comprovante) | Sem validade | |
| Acordo de compensação / 12×36 | Vigência | Ligado a [ADR-0015](../adr/0015-regras-de-jornada-e-escala.md) |

Confirme as periodicidades exatas com os pilotos e com um técnico de segurança do
trabalho — elas variam por norma, por risco e por revisão da NR. O sistema deve
tratar periodicidade como **configuração por tipo de documento**, não como constante
no código.

---

## Plano dos pilotos

Com três empresas dispostas a usar em troca de feedback, o roadmap deixa de ser
chute informado. Para isso valer, o piloto precisa de método.

### Antes de escrever mais código

Uma conversa de 45 minutos com cada uma, sem demo, só perguntas:

1. **Me mostra a planilha da escala.** Peça o arquivo. É o documento de requisitos
   mais honesto que existe — os padrões de turno, as exceções e as gambiarras estão
   todos ali.
2. Quantas pessoas, quantos turnos, qual o padrão (12×36? 5×2? 6×1?), quem monta,
   quanto tempo leva por mês.
3. O que acontece quando alguém falta às 5h da manhã? Quem descobre, quem resolve,
   por onde?
4. Quais documentos vencem, quem controla, e alguma vez venceu sem ninguém ver?
5. Como as férias são programadas hoje? Já pagaram em dobro alguma vez?
6. O que você **já paga** hoje em software ou serviço para resolver isso?

A pergunta 1 sozinha vale mais do que semanas de especificação. As planilhas das
três vão divergir — a interseção é a V1, as diferenças viram configuração por
tenant.

A pergunta 6 calibra o preço, e é a que quase nunca se faz.

### Durante

| Regra | Motivo |
|---|---|
| Entre em produção com o piloto a partir do Marco 2 | Documento é o marco mais barato de gerar valor real |
| Um piloto por vez nas primeiras semanas | Três clientes quebrando ao mesmo tempo consome o tempo de desenvolvimento |
| Importação de CSV feita **por você**, junto com o cliente | É onde você aprende o formato real dos dados |
| Contato semanal, sempre com pergunta específica | "Como está indo?" não produz informação |
| Registre o que pedem, entregue pouco | Piloto que dita roadmap vira consultoria; você precisa de produto |

### Critério de saída

O piloto virou validação quando:

- [ ] A escala do mês seguinte foi montada **no Mamão**, e não na planilha
- [ ] Alguém foi avisado de um vencimento de documento **antes** de vencer
- [ ] Uma solicitação de férias foi aprovada com a informação de cobertura à vista
- [ ] O coordenador abre o sistema sem você pedir
- [ ] Ao ser perguntado, o cliente diz que **pagaria** — e por quanto

O último é o único que importa de verdade. Faça a pergunta explicitamente, e cedo:
elogio não é validação.

### Cobrança dos pilotos

Sugestão: gratuito durante o piloto, com desconto vitalício de 30–50% em troca de
depoimento e estudo de caso. Não deixe "gratuito para sempre" — cliente que nunca
paga não valida preço, que é justamente a variável mais difícil de acertar depois.
