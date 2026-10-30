using CaixaClaro.Dominio.Modelos;
using Microsoft.EntityFrameworkCore;

namespace CaixaClaro.Api.Infra;

/// <summary>Estado de um usuário carregado do banco de dados + o que havia antes, para saber o que foi removido.</summary>
public sealed record SessaoEstado(EstadoFinanceiro Estado, Usuario Usuario, HashSet<Lancamento> LancamentosOriginais, HashSet<Conta> ContasOriginais);

/// <summary>
/// Carrega tudo de um usuário para a memória (as regras do domínio trabalham em listas) e salva as diferenças.
/// Um pequeno negócio tem alguns milhares de lançamentos por ano: cabe folgado na memória.
/// </summary>
public sealed class RepositorioEstado(AppDbContext db)
{
    public async Task<SessaoEstado> CarregarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == usuarioId, ct);
        var lancamentos = await db.Lancamentos.Where(l => l.UsuarioId == usuarioId).ToListAsync(ct);
        var contas = await db.Contas.Where(c => c.UsuarioId == usuarioId).ToListAsync(ct);
        var regras = await db.Regras.Where(r => r.UsuarioId == usuarioId).ToListAsync(ct);
        var series = await db.SeriesIgnoradas.Where(s => s.UsuarioId == usuarioId).Select(s => s.Serie).ToListAsync(ct);
        var saldosBancarios = await db.ContasBancarias.Where(c => c.UsuarioId == usuarioId && c.Tipo == "BANK").Select(c => c.Saldo).ToListAsync(ct);
        var conexao = await db.Conexoes.Where(c => c.UsuarioId == usuarioId).OrderBy(c => c.CriadaEm).FirstOrDefaultAsync(ct);

        var estado = new EstadoFinanceiro
        {
            UsuarioId = usuarioId,
            Lancamentos = lancamentos,
            Contas = contas,
            Regras = regras.ToDictionary(r => r.Chave),
            SeriesIgnoradas = [.. series],
            SaldoBancos = saldosBancarios.Sum(),
            CaixaDinheiro = usuario.CaixaDinheiro,
            Titular = new Titular(conexao?.Titular, conexao?.DocumentoTitular, usuario.Cpf),
        };
        return new SessaoEstado(estado, usuario, [.. lancamentos], [.. contas]);
    }

    public async Task SalvarAsync(SessaoEstado s, CancellationToken ct = default)
    {
        var e = s.Estado;
        foreach (var l in e.Lancamentos.Where(l => db.Entry(l).State == EntityState.Detached)) db.Lancamentos.Add(l);
        foreach (var removido in s.LancamentosOriginais.Except(e.Lancamentos)) db.Lancamentos.Remove(removido);
        foreach (var c in e.Contas.Where(c => db.Entry(c).State == EntityState.Detached)) db.Contas.Add(c);
        foreach (var removida in s.ContasOriginais.Except(e.Contas)) db.Contas.Remove(removida);
        foreach (var r in e.Regras.Values.Where(r => db.Entry(r).State == EntityState.Detached)) db.Regras.Add(r);
        var seriesSalvas = await db.SeriesIgnoradas.Where(x => x.UsuarioId == e.UsuarioId).Select(x => x.Serie).ToListAsync(ct);
        foreach (var serie in e.SeriesIgnoradas.Except(seriesSalvas))
            db.SeriesIgnoradas.Add(new SerieIgnorada { UsuarioId = e.UsuarioId, Serie = serie });
        await db.SaveChangesAsync(ct);
    }
}
