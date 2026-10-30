using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kognit.Api.Infra;

/// <summary>
/// Escolha do banco de dados pela configuração "BancoDeDados:Provedor":
/// - "Sqlite" (padrão): arquivo local, para desenvolvimento e testes rápidos. Tabelas criadas na hora (EnsureCreated).
/// - "Postgres": PostgreSQL / Supabase. Tabelas criadas e alteradas por migrações (pasta Infra/Migracoes).
/// </summary>
public static class BancoDeDados
{
    public static bool EhPostgres(IConfiguration config) =>
        string.Equals(config.GetValue<string>("BancoDeDados:Provedor"), "Postgres", StringComparison.OrdinalIgnoreCase);

    public static void Configurar(DbContextOptionsBuilder o, IConfiguration config)
    {
        var conexao = config.GetConnectionString("Padrao");
        if (EhPostgres(config))
        {
            if (string.IsNullOrWhiteSpace(conexao))
                throw new InvalidOperationException("Configure ConnectionStrings:Padrao com a conexão do PostgreSQL/Supabase.");
            o.UseNpgsql(conexao, n => n
                .MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)
                // Quedas rápidas de rede até o Supabase são refeitas sozinhas.
                .EnableRetryOnFailure(maxRetryCount: 3));
        }
        else
        {
            o.UseSqlite(conexao ?? "Data Source=kognit.db");
        }
    }

    /// <summary>Na inicialização: aplica as migrações pendentes (Postgres) ou cria o arquivo (SQLite).</summary>
    public static void Preparar(AppDbContext db, IConfiguration config)
    {
        if (EhPostgres(config)) db.Database.Migrate();
        else db.Database.EnsureCreated();
    }
}

/// <summary>Usada só pela ferramenta "dotnet ef" para gerar migrações do PostgreSQL (não conecta em banco nenhum).</summary>
public class FabricaDesignTime : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<AppDbContext>();
        o.UseNpgsql("Host=localhost;Database=kognit_design", n => n.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema));
        return new AppDbContext(o.Options);
    }
}
