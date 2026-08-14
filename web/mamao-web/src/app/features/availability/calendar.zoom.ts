/**
 * A escala de tempo do calendário.
 *
 * O mês mostra dia a dia porque a decisão diária é essa. Mas planejar férias é decisão de
 * trimestre, e "quantos dias o fulano ficou fora este ano" é de ano — e nenhuma das duas
 * cabe numa grade de 365 colunas. Então a granularidade muda junto com o alcance: quanto
 * mais longe se olha, mais grosso é o balde.
 */
export type Zoom = 'mes' | 'trimestre' | 'semestre' | 'ano';

export interface Balde {
  /** Rótulo da coluna: "12", "S3", "mar". */
  readonly rotulo: string;

  /**
   * Linha de cima do cabeçalho. No mês é o dia da semana — sem ele, "17" obriga a contar
   * nos dedos para saber se cai numa quarta, que é a primeira coisa que se pergunta ao
   * marcar folga. Vazio quando a coluna não é um dia.
   */
  readonly sub: string;
  readonly inicio: Date;
  readonly fim: Date;
  /** Um dia só — é quando a célula mostra a letra do motivo em vez da contagem. */
  readonly unico: boolean;
  readonly fimDeSemana: boolean;
}

export const ZOOMS: ReadonlyArray<{ valor: Zoom; rotulo: string }> = [
  { valor: 'mes', rotulo: 'Mês' },
  { valor: 'trimestre', rotulo: 'Trimestre' },
  { valor: 'semestre', rotulo: 'Semestre' },
  { valor: 'ano', rotulo: 'Ano' },
];

const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

/**
 * Uma letra por dia da semana, como em qualquer calendário brasileiro. Ambíguo lido
 * isolado (S serve para segunda, sexta e sábado), e mesmo assim é o certo: numa coluna de
 * 24px não cabe mais, e ninguém lê a letra sozinha — lê a posição na semana, com o fim de
 * semana sombreado ao lado.
 */
const DIAS_DA_SEMANA = ['D', 'S', 'T', 'Q', 'Q', 'S', 'S'];

/** Início do período que contém a data, na escala pedida. */
export function inicioDoPeriodo(d: Date, zoom: Zoom): Date {
  const ano = d.getFullYear();

  switch (zoom) {
    case 'mes':
      return new Date(ano, d.getMonth(), 1);
    case 'trimestre':
      return new Date(ano, Math.floor(d.getMonth() / 3) * 3, 1);
    case 'semestre':
      return new Date(ano, d.getMonth() < 6 ? 0 : 6, 1);
    case 'ano':
      return new Date(ano, 0, 1);
  }
}

export function fimDoPeriodo(inicio: Date, zoom: Zoom): Date {
  const meses = { mes: 1, trimestre: 3, semestre: 6, ano: 12 }[zoom];
  return new Date(inicio.getFullYear(), inicio.getMonth() + meses, 0);
}

export function moverPeriodo(inicio: Date, zoom: Zoom, passos: number): Date {
  const meses = { mes: 1, trimestre: 3, semestre: 6, ano: 12 }[zoom];
  return new Date(inicio.getFullYear(), inicio.getMonth() + meses * passos, 1);
}

export function rotuloDoPeriodo(inicio: Date, zoom: Zoom): string {
  const ano = inicio.getFullYear();

  switch (zoom) {
    case 'mes':
      return `${inicio.toLocaleDateString('pt-BR', { month: 'long' })} de ${ano}`;
    case 'trimestre':
      return `${Math.floor(inicio.getMonth() / 3) + 1}º trimestre de ${ano}`;
    case 'semestre':
      return `${inicio.getMonth() < 6 ? 1 : 2}º semestre de ${ano}`;
    case 'ano':
      return String(ano);
  }
}

/**
 * As colunas da grade.
 *
 * Mês vira dia; trimestre e semestre viram semana; ano vira mês. A conta que decide isso
 * é simples: acima de ~40 colunas a célula fica estreita demais para caber qualquer coisa
 * legível, e a grade deixa de ser vista de relance — que é a única razão de ela existir.
 */
export function baldes(inicio: Date, zoom: Zoom): Balde[] {
  const fim = fimDoPeriodo(inicio, zoom);

  if (zoom === 'mes') {
    const total = fim.getDate();
    return Array.from({ length: total }, (_, i) => {
      const dia = new Date(inicio.getFullYear(), inicio.getMonth(), i + 1);
      const semana = dia.getDay();
      return {
        rotulo: String(i + 1),
        sub: DIAS_DA_SEMANA[semana],
        inicio: dia,
        fim: dia,
        unico: true,
        fimDeSemana: semana === 0 || semana === 6,
      };
    });
  }

  if (zoom === 'ano') {
    return Array.from({ length: 12 }, (_, i) => ({
      rotulo: MESES[i],
      sub: '',
      inicio: new Date(inicio.getFullYear(), i, 1),
      fim: new Date(inicio.getFullYear(), i + 1, 0),
      unico: false,
      fimDeSemana: false,
    }));
  }

  // Trimestre e semestre por semana. A semana começa na segunda: no Brasil o descanso é
  // o fim de semana, e uma coluna que corta o sábado no meio atrapalha a leitura.
  const lista: Balde[] = [];
  const cursor = new Date(inicio);
  cursor.setDate(cursor.getDate() - ((cursor.getDay() + 6) % 7));

  let n = 1;
  while (cursor <= fim) {
    const fimDaSemana = new Date(cursor);
    fimDaSemana.setDate(fimDaSemana.getDate() + 6);

    lista.push({
      // Semana identificada pela segunda-feira: "17/8" e a semana que comeca ali.
      rotulo: `${cursor.getDate()}/${cursor.getMonth() + 1}`,
      sub: 'seg',
      inicio: new Date(cursor),
      fim: fimDaSemana,
      unico: false,
      fimDeSemana: false,
    });

    cursor.setDate(cursor.getDate() + 7);
    n++;
  }

  return lista;
}
