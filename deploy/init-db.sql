-- Roda uma vez, na criacao do volume do Postgres.
--
-- O role da aplicacao (mamao_app) NAO e criado aqui: quem cria e o Worker, a cada startup,
-- porque este arquivo roda uma unica vez e trocar a senha depois exigiria recriar o banco.
-- Ver TenantRls.EnsureRole e docs/adr/0003-multi-tenancy.md.
--
-- Sobra o que precisa existir antes de qualquer coisa.

-- gen_random_uuid(), usada em scripts de manutencao e nos testes de RLS.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
