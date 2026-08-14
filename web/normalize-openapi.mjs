#!/usr/bin/env node
/**
 * Normaliza o documento OpenAPI antes de gerar os tipos.
 *
 * O gerador do .NET 10 emite `format: int32` sem o `type: integer` em algumas
 * propriedades; o openapi-typescript entao infere `unknown` e o build do frontend quebra
 * em cima de um contrato que, na intencao, esta correto.
 *
 * Tentamos primeiro um IOpenApiDocumentTransformer no proprio host, mas nesta versao os
 * schemas so entram em components DEPOIS dos transformers de documento — entao a correcao
 * vive aqui, num passo explicito do pipeline de geracao, e nao escondida no cliente gerado.
 *
 * Uso: node normalize-openapi.mjs openapi.json
 */
import { readFileSync, writeFileSync } from 'node:fs';

const caminho = process.argv[2] ?? 'openapi.json';
const documento = JSON.parse(readFileSync(caminho, 'utf8'));

let corrigidos = 0;

function normalizar(no) {
  if (!no || typeof no !== 'object') return;

  if (Array.isArray(no)) {
    no.forEach(normalizar);
    return;
  }

  if (no.type === undefined && (no.format === 'int32' || no.format === 'int64')) {
    no.type = 'integer';
    delete no.pattern;
    corrigidos++;
  }

  if (no.type === undefined && (no.format === 'double' || no.format === 'float')) {
    no.type = 'number';
    delete no.pattern;
    corrigidos++;
  }

  Object.values(no).forEach(normalizar);
}

normalizar(documento);
writeFileSync(caminho, `${JSON.stringify(documento, null, 2)}\n`);

console.log(`openapi normalizado: ${corrigidos} schema(s) numerico(s) com type ausente.`);
