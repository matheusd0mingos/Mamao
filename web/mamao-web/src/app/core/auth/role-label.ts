/**
 * Codigo do papel (persistido, em ingles) -> rotulo em pt-BR.
 * A traducao vive aqui e nao no banco: mudar o texto nao pode exigir migration.
 */
const ROTULOS: Record<string, string> = {
  Owner: 'Proprietário',
  Hr: 'RH',
  Manager: 'Gestor',
  Employee: 'Funcionário',
};

export const rotuloDoPapel = (codigo: string): string => ROTULOS[codigo] ?? codigo;
