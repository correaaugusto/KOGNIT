-- =====================================================================================
-- KOGNIT: script completo do banco de dados (PostgreSQL / Supabase)
--
-- Gerado a partir das migrações do Entity Framework:
--   dotnet ef migrations script --idempotent -p src/Kognit.Api -o database/kognit.sql
--
-- Você NÃO precisa rodar este script se a API for publicada (Render): ela aplica as migrações
-- sozinha ao iniciar. Use-o para criar o banco à mão (Supabase → SQL Editor → colar → Run)
-- ou para conferir a estrutura. É idempotente: pode rodar mais de uma vez sem erro.
--
-- O que ele cria:
--   - schema "kognit" (fora da Data API pública do Supabase) e 7 tabelas;
--   - índices (inclusive o que impede importar a mesma transação do banco duas vezes);
--   - tabela "__EFMigrationsHistory" (para a API saber que a migração já foi aplicada);
--   - remove o acesso dos papéis públicos do Supabase (anon, authenticated) ao schema.
--
-- Sempre que uma migração nova for criada, gere este arquivo de novo.
-- =====================================================================================

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kognit') THEN
        CREATE SCHEMA kognit;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS kognit."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kognit') THEN
            CREATE SCHEMA kognit;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."Conexoes" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "Provedor" text NOT NULL,
        "ItemId" text NOT NULL,
        "NomeBanco" text NOT NULL,
        "Status" text NOT NULL,
        "Mensagem" text,
        "Titular" text,
        "DocumentoTitular" text,
        "UltimaSincronizacao" timestamp with time zone,
        "CriadaEm" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Conexoes" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."Contas" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "Descricao" text NOT NULL,
        "Valor" numeric(18,2) NOT NULL,
        "Categoria" text NOT NULL,
        "Vencimento" date NOT NULL,
        "Recorrente" boolean NOT NULL,
        "Paga" boolean NOT NULL,
        "PagaEm" date,
        "PagaPeloBanco" boolean NOT NULL,
        "Origem" text NOT NULL,
        "Serie" text,
        "Chave" text,
        "LinhaDigitavel" text,
        "IdExterno" text,
        CONSTRAINT "PK_Contas" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."ContasBancarias" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "ConexaoId" uuid NOT NULL,
        "IdExterno" text NOT NULL,
        "Tipo" text NOT NULL,
        "Nome" text NOT NULL,
        "Saldo" numeric(18,2) NOT NULL,
        "AtualizadaEm" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ContasBancarias" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."Lancamentos" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "ConexaoId" uuid,
        "IdExterno" text,
        "Origem" text NOT NULL,
        "Data" date NOT NULL,
        "Valor" numeric(18,2) NOT NULL,
        "Tipo" text NOT NULL,
        "Categoria" text NOT NULL,
        "Descricao" text NOT NULL,
        "DescricaoBanco" text,
        "Meio" text,
        "Chave" text,
        "Motivo" text,
        "Revisar" boolean NOT NULL,
        "ContaPagaId" uuid,
        "Serie" text,
        "Simulado" boolean NOT NULL,
        "Historico" boolean NOT NULL,
        CONSTRAINT "PK_Lancamentos" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."Regras" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "Chave" text NOT NULL,
        "Tipo" text NOT NULL,
        "Categoria" text NOT NULL,
        CONSTRAINT "PK_Regras" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."SeriesIgnoradas" (
        "Id" uuid NOT NULL,
        "UsuarioId" uuid NOT NULL,
        "Serie" text NOT NULL,
        CONSTRAINT "PK_SeriesIgnoradas" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE TABLE kognit."Usuarios" (
        "Id" uuid NOT NULL,
        "Nome" text NOT NULL,
        "Email" character varying(200) NOT NULL,
        "SenhaHash" text NOT NULL,
        "NomeNegocio" text NOT NULL,
        "Cpf" text,
        "CaixaDinheiro" numeric(18,2) NOT NULL,
        "CriadoEm" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Usuarios" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_Conexoes_ItemId" ON kognit."Conexoes" ("ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE INDEX "IX_Conexoes_UsuarioId" ON kognit."Conexoes" ("UsuarioId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE INDEX "IX_Contas_UsuarioId" ON kognit."Contas" ("UsuarioId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_ContasBancarias_ConexaoId_IdExterno" ON kognit."ContasBancarias" ("ConexaoId", "IdExterno");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE INDEX "IX_Lancamentos_UsuarioId_Data" ON kognit."Lancamentos" ("UsuarioId", "Data");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_Lancamentos_UsuarioId_IdExterno" ON kognit."Lancamentos" ("UsuarioId", "IdExterno") WHERE "IdExterno" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_Regras_UsuarioId_Chave" ON kognit."Regras" ("UsuarioId", "Chave");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_SeriesIgnoradas_UsuarioId_Serie" ON kognit."SeriesIgnoradas" ("UsuarioId", "Serie");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    CREATE UNIQUE INDEX "IX_Usuarios_Email" ON kognit."Usuarios" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
            REVOKE ALL ON SCHEMA kognit FROM anon;
            REVOKE ALL ON ALL TABLES IN SCHEMA kognit FROM anon;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
            REVOKE ALL ON SCHEMA kognit FROM authenticated;
            REVOKE ALL ON ALL TABLES IN SCHEMA kognit FROM authenticated;
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kognit."__EFMigrationsHistory" WHERE "MigrationId" = '20261030020652_Inicial') THEN
    INSERT INTO kognit."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261030020652_Inicial', '10.0.14');
    END IF;
END $EF$;
COMMIT;

