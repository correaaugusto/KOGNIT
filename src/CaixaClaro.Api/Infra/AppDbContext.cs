using CaixaClaro.Dominio.Modelos;
using Microsoft.EntityFrameworkCore;

namespace CaixaClaro.Api.Infra;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ConexaoBancaria> Conexoes => Set<ConexaoBancaria>();
    public DbSet<ContaBancaria> ContasBancarias => Set<ContaBancaria>();
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<Conta> Contas => Set<Conta>();
    public DbSet<RegraClassificacao> Regras => Set<RegraClassificacao>();
    public DbSet<SerieIgnorada> SeriesIgnoradas => Set<SerieIgnorada>();

    /// <summary>Todo DateTime é gravado e lido como UTC (o SQLite devolve sem fuso; sem isto o JSON sairia sem "Z"
    /// e o front interpretaria o horário como local).</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<DataUtcConverter>();
        b.Properties<DateTime?>().HaveConversion<DataUtcNulavelConverter>();
    }

    private sealed class DataUtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class DataUtcNulavelConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(200);
        });
        b.Entity<ConexaoBancaria>(e =>
        {
            e.HasIndex(c => c.ItemId).IsUnique();
            e.HasIndex(c => c.UsuarioId);
            e.Property(c => c.Provedor).HasConversion<string>();
            e.Property(c => c.Status).HasConversion<string>();
        });
        b.Entity<ContaBancaria>().HasIndex(c => new { c.ConexaoId, c.IdExterno }).IsUnique();
        b.Entity<Lancamento>(e =>
        {
            e.Ignore(l => l.EhConta);
            // Mesma transação do banco nunca entra duas vezes, mesmo com sincronizações simultâneas.
            e.HasIndex(l => new { l.UsuarioId, l.IdExterno }).IsUnique().HasFilter("\"IdExterno\" IS NOT NULL");
            e.HasIndex(l => new { l.UsuarioId, l.Data });
            e.Property(l => l.Tipo).HasConversion<string>();
            e.Property(l => l.Origem).HasConversion<string>();
        });
        b.Entity<Conta>(e =>
        {
            e.HasIndex(c => c.UsuarioId);
            e.Property(c => c.Origem).HasConversion<string>();
        });
        b.Entity<RegraClassificacao>(e =>
        {
            e.HasIndex(r => new { r.UsuarioId, r.Chave }).IsUnique();
            e.Property(r => r.Tipo).HasConversion<string>();
        });
        b.Entity<SerieIgnorada>().HasIndex(s => new { s.UsuarioId, s.Serie }).IsUnique();
    }
}
