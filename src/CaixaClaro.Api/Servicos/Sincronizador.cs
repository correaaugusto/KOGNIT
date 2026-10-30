using CaixaClaro.Api.Bancos;
using CaixaClaro.Api.Infra;
using CaixaClaro.Dominio.Modelos;
using CaixaClaro.Dominio.Regras;
using Microsoft.EntityFrameworkCore;

namespace CaixaClaro.Api.Servicos;

public enum ResultadoSincronizacao { Concluida, AindaAtualizando, Erro, NaoEncontrada }

public record Sincronizacao(ResultadoSincronizacao Resultado, int Novos = 0, string? Mensagem = null);

/// <summary>
/// Busca os dados do banco conectado e passa pelo processamento do domínio:
/// saldos → transações novas (classificadas) → contas fixas → faturas do cartão → baixa automática.
/// </summary>
public class Sincronizador(
    AppDbContext db,
    RepositorioEstado repositorio,
    IEnumerable<IProvedorBancario> provedores,
    TravaPorUsuario trava,
    Relogio relogio,
    IConfiguration config,
    ILogger<Sincronizador> log)
{
    public async Task<Sincronizacao> SincronizarAsync(Guid conexaoId, CancellationToken ct = default)
    {
        var conexao = await db.Conexoes.FindAsync([conexaoId], ct);
        if (conexao is null) return new(ResultadoSincronizacao.NaoEncontrada);
        using var _ = await trava.EntrarAsync(conexao.UsuarioId, ct);

        var hoje = relogio.Hoje;
        var diasHistorico = config.GetValue("Sincronizacao:DiasHistorico", 120);
        // Depois da primeira vez, busca só os últimos dias (com folga para transações que o banco confirma depois).
        var desde = conexao.UltimaSincronizacao is { } ultima ? DateOnly.FromDateTime(ultima).AddDays(-7) : hoje.AddDays(-diasHistorico);
        var provedor = provedores.Single(p => p.Tipo == conexao.Provedor);

        DadosBancarios dados;
        try
        {
            dados = await provedor.BuscarAsync(conexao, desde, hoje, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Falha ao buscar dados da conexão {Conexao}", conexao.Id);
            conexao.Status = StatusConexao.Erro;
            conexao.Mensagem = "Não foi possível buscar o extrato agora. Tentaremos de novo mais tarde.";
            await db.SaveChangesAsync(ct);
            return new(ResultadoSincronizacao.Erro, Mensagem: conexao.Mensagem);
        }

        conexao.NomeBanco = dados.NomeBanco;
        if (!dados.Pronto)
        {
            conexao.Status = StatusConexao.Atualizando;
            await db.SaveChangesAsync(ct);
            return new(ResultadoSincronizacao.AindaAtualizando);
        }
        if (dados.Erro is not null)
        {
            conexao.Status = StatusConexao.Erro;
            conexao.Mensagem = dados.Erro;
            await db.SaveChangesAsync(ct);
            return new(ResultadoSincronizacao.Erro, Mensagem: dados.Erro);
        }

        conexao.Titular = dados.Titular.Nome ?? conexao.Titular;
        conexao.DocumentoTitular = dados.Titular.Documento ?? conexao.DocumentoTitular;
        await AtualizarSaldosAsync(conexao, dados.Contas, ct);
        await db.SaveChangesAsync(ct);

        var sessao = await repositorio.CarregarAsync(conexao.UsuarioId, ct);
        var estado = sessao.Estado;
        foreach (var c in dados.ContasIniciais.Where(c => !estado.Contas.Any(x => x.IdExterno == c.IdExterno)))
        {
            c.UsuarioId = estado.UsuarioId;
            estado.Contas.Add(c);
        }
        var novos = ProcessadorExtrato.Importar(estado, dados.Transacoes, hoje, conexao.Id);
        AtualizarFaturas(estado, dados.Faturas);
        ProcessadorExtrato.ConciliarContas(estado);
        await repositorio.SalvarAsync(sessao, ct);

        conexao.Status = StatusConexao.Conectado;
        conexao.Mensagem = null;
        conexao.UltimaSincronizacao = relogio.AgoraUtc;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Conexão {Conexao} sincronizada: {Novos} lançamentos novos", conexao.Id, novos.Count);
        return new(ResultadoSincronizacao.Concluida, novos.Count);
    }

    private async Task AtualizarSaldosAsync(ConexaoBancaria conexao, List<ContaBancariaDados> contas, CancellationToken ct)
    {
        var existentes = await db.ContasBancarias.Where(c => c.ConexaoId == conexao.Id).ToListAsync(ct);
        foreach (var c in contas)
        {
            var conta = existentes.FirstOrDefault(x => x.IdExterno == c.IdExterno);
            if (conta is null)
            {
                conta = new ContaBancaria { UsuarioId = conexao.UsuarioId, ConexaoId = conexao.Id, IdExterno = c.IdExterno };
                db.ContasBancarias.Add(conta);
            }
            conta.Tipo = c.Tipo;
            conta.Nome = c.Nome;
            conta.Saldo = c.Saldo;
            conta.AtualizadaEm = relogio.AgoraUtc;
        }
    }

    /// <summary>Fatura do cartão vira conta a pagar; o valor é atualizado enquanto a fatura estiver aberta.</summary>
    private static void AtualizarFaturas(EstadoFinanceiro estado, List<FaturaCartao> faturas)
    {
        foreach (var f in faturas)
        {
            var conta = estado.Contas.FirstOrDefault(c => c.IdExterno == f.IdExterno);
            if (conta is null)
            {
                estado.Contas.Add(new Conta
                {
                    UsuarioId = estado.UsuarioId,
                    IdExterno = f.IdExterno,
                    Descricao = f.Descricao,
                    Valor = f.Valor,
                    Vencimento = f.Vencimento,
                    Categoria = Categorias.FaturaCartao,
                    Origem = OrigemConta.FaturaCartao,
                });
            }
            else if (!conta.Paga)
            {
                conta.Valor = f.Valor;
            }
        }
    }
}
