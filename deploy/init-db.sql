-- Roda uma vez, na criacao do volume do Postgres.
--
-- O papel da aplicacao e SEPARADO do dono das tabelas e NAO tem BYPASSRLS. Isso e o que
-- faz a Row-Level Security valer de verdade: superusuario ignora policy em silencio, que
-- e a forma mais facil de achar que se esta protegido sem estar.
-- Ver docs/adr/0003-multi-tenancy.md.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'mamao_app') THEN
        CREATE ROLE mamao_app LOGIN PASSWORD 'trocar-no-primeiro-deploy'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE mamao TO mamao_app;

-- As migrations rodam com o usuario dono e criam os schemas; estes defaults garantem que o
-- papel da aplicacao enxergue o que vier depois.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO mamao_app;
