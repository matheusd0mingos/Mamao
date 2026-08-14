# ADR-0010 — IFileStorage com URL assinada; local no VPS, objeto depois

**Status:** aceita · **Data:** 2026-08

## Contexto

Documentos são módulo core: RG, CPF, comprovante de residência, ASO, NR-10, NR-35,
atestado. Dado pessoal e, em parte, **sensível**. Hoje o destino é o disco de um
VPS; amanhã, provavelmente Blob Storage ou S3.

## Decisão

```csharp
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, FileMetadata meta, CancellationToken ct);
    Task<Stream>     OpenAsync(string key, CancellationToken ct);
    Task             DeleteAsync(string key, CancellationToken ct);
    Task<Uri>        GetSignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct);
}
```

Implementações: `LocalFileStorage` (agora), `S3FileStorage`, `AzureBlobStorage`
(depois). Uma das poucas abstrações que valem o custo — porque a troca é certa e o
domínio não pode saber onde o arquivo mora.

No banco, apenas metadados:

```
key           caminho/objeto opaco: {tenant}/{ano}/{guid}{ext}
original_name content_type  size_bytes
sha256        detecta duplicata e comprova integridade
uploaded_by   uploaded_at   virus_scan_status
```

Nada de `bytea`. Arquivo em Postgres infla o backup, o WAL e o cache — e é a
recomendação que o próprio briefing já traz, corretamente.

## URLs assinadas

Documento **nunca** é servido por URL pública nem estaticamente pelo Caddy. Acesso
sempre por URL de vida curta.

Com S3/Blob, é o mecanismo nativo (presigned URL). Com `LocalFileStorage`, um
endpoint da própria API:

```
GET /api/v1/files/{key}?exp=1755180000&sig={hmac}
```

- HMAC-SHA256 sobre `key + exp + tenantId + userId`, com chave do servidor.
- TTL curto (5 min é suficiente para o navegador baixar).
- O endpoint **revalida a autorização** além de conferir a assinatura. A assinatura
  impede link forjado; a autorização impede link compartilhado indevidamente.
- Todo acesso é auditado ([segurança](../arquitetura/multi-tenancy-e-seguranca.md#auditoria)).

## Chave por tenant

O prefixo `{tenant}/` é obrigatório. Facilita quota, exclusão de conta (LGPD) e
migração seletiva — e torna um erro de vazamento visível na própria chave.

## Upload

- Limite de tamanho por tipo de documento (10 MB cobre foto de RG e PDF de ASO).
- Whitelist de content-type: PDF, JPEG, PNG, HEIC. **Validar pelos magic bytes**,
  não pela extensão.
- Imagem recomprimida no servidor: foto de celular de 8 MB vira 400 KB. Economia
  real de disco e banda, e o funcionário sobe do celular.
- Antivírus (ClamAV em container ou serviço externo) — desejável, não bloqueador.
  Enquanto não existir, `virus_scan_status = 'skipped'` deixa o campo pronto.

## Consequências

- Backup dos uploads é rotina separada do `pg_dump` e igualmente obrigatória.
- Migrar para S3/Blob é copiar objetos e trocar o registro de DI. Sem mudança de
  domínio — que é o critério para dizer que a arquitetura permite a evolução.
- Excluir tenant é excluir o prefixo + as linhas. Exigência prática de LGPD.
