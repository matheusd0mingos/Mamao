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

// Fora o `servers`.
//
// O host escreve ali a URL em que ele subiu para gerar o documento — com uma porta
// EFEMERA, diferente a cada execucao. Comitado, isso transforma a checagem de contrato do
// CI num teste que nunca passa duas vezes seguidas: o diff acusa "openapi desatualizado"
// quando a unica coisa que mudou foi o numero da porta sorteada.
//
// Tirar nao perde nada: este documento existe para gerar os tipos do frontend, que fala
// com a API por caminho relativo atras do proxy. O endereco de verdade e do deploy, nao
// do contrato.
const removidos = documento.servers?.length ?? 0;
delete documento.servers;

writeFileSync(caminho, `${JSON.stringify(documento, null, 2)}\n`);

console.log(
  `openapi normalizado: ${corrigidos} schema(s) numerico(s) com type ausente, ` +
  `${removidos} servidor(es) de porta efemera removido(s).`);
