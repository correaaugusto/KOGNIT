using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kognit.Api.Infra.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kognit");

            migrationBuilder.CreateTable(
                name: "Conexoes",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provedor = table.Column<string>(type: "text", nullable: false),
                    ItemId = table.Column<string>(type: "text", nullable: false),
                    NomeBanco = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Mensagem = table.Column<string>(type: "text", nullable: true),
                    Titular = table.Column<string>(type: "text", nullable: true),
                    DocumentoTitular = table.Column<string>(type: "text", nullable: true),
                    UltimaSincronizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conexoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contas",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Categoria = table.Column<string>(type: "text", nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    Recorrente = table.Column<bool>(type: "boolean", nullable: false),
                    Paga = table.Column<bool>(type: "boolean", nullable: false),
                    PagaEm = table.Column<DateOnly>(type: "date", nullable: true),
                    PagaPeloBanco = table.Column<bool>(type: "boolean", nullable: false),
                    Origem = table.Column<string>(type: "text", nullable: false),
                    Serie = table.Column<string>(type: "text", nullable: true),
                    Chave = table.Column<string>(type: "text", nullable: true),
                    LinhaDigitavel = table.Column<string>(type: "text", nullable: true),
                    IdExterno = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContasBancarias",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConexaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdExterno = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Saldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasBancarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lancamentos",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConexaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdExterno = table.Column<string>(type: "text", nullable: true),
                    Origem = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Categoria = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    DescricaoBanco = table.Column<string>(type: "text", nullable: true),
                    Meio = table.Column<string>(type: "text", nullable: true),
                    Chave = table.Column<string>(type: "text", nullable: true),
                    Motivo = table.Column<string>(type: "text", nullable: true),
                    Revisar = table.Column<bool>(type: "boolean", nullable: false),
                    ContaPagaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serie = table.Column<string>(type: "text", nullable: true),
                    Simulado = table.Column<bool>(type: "boolean", nullable: false),
                    Historico = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lancamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regras",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Chave = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Categoria = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeriesIgnoradas",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serie = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesIgnoradas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                schema: "kognit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    NomeNegocio = table.Column<string>(type: "text", nullable: false),
                    Cpf = table.Column<string>(type: "text", nullable: true),
                    CaixaDinheiro = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conexoes_ItemId",
                schema: "kognit",
                table: "Conexoes",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conexoes_UsuarioId",
                schema: "kognit",
                table: "Conexoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Contas_UsuarioId",
                schema: "kognit",
                table: "Contas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasBancarias_ConexaoId_IdExterno",
                schema: "kognit",
                table: "ContasBancarias",
                columns: new[] { "ConexaoId", "IdExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_UsuarioId_Data",
                schema: "kognit",
                table: "Lancamentos",
                columns: new[] { "UsuarioId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_UsuarioId_IdExterno",
                schema: "kognit",
                table: "Lancamentos",
                columns: new[] { "UsuarioId", "IdExterno" },
                unique: true,
                filter: "\"IdExterno\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Regras_UsuarioId_Chave",
                schema: "kognit",
                table: "Regras",
                columns: new[] { "UsuarioId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesIgnoradas_UsuarioId_Serie",
                schema: "kognit",
                table: "SeriesIgnoradas",
                columns: new[] { "UsuarioId", "Serie" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                schema: "kognit",
                table: "Usuarios",
                column: "Email",
                unique: true);

            // Supabase: o schema "kognit" não é exposto pela Data API, e ainda assim tiramos qualquer acesso
            // dos papéis públicos (anon / authenticated). Só a API (usuário do banco) lê e grava os dados.
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conexoes",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "Contas",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "ContasBancarias",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "Lancamentos",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "Regras",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "SeriesIgnoradas",
                schema: "kognit");

            migrationBuilder.DropTable(
                name: "Usuarios",
                schema: "kognit");
        }
    }
}
