using CaixaClaro.Api.Infra;
using CaixaClaro.Api.Pluggy;
using CaixaClaro.Dominio.Demo;
using CaixaClaro.Dominio.Modelos;
using CaixaClaro.Dominio.Regras;
using Microsoft.Extensions.Options;

namespace CaixaClaro.Api.Bancos;

public record ContaBancariaDados(string IdExterno, string Tipo, string Nome, decimal Saldo);

public record FaturaCartao(string IdExterno, string Descricao, decimal Valor, DateOnly Vencimento);

/// <summary>Tudo o que veio do banco numa sincronização.</summary>
public record DadosBancarios(
    string NomeBanco,
    bool Pronto,
    string? Erro,
    Titular Titular,
    List<ContaBancariaDados> Contas,
    List<TransacaoBancaria> Transacoes,
    List<FaturaCartao> Faturas,
    List<Conta> ContasIniciais)
{
    public static DadosBancarios AindaAtualizando(string nome) => new(nome, false, null, new(null, null), [], [], [], []);
}

/// <summary>Fonte dos dados bancários. Pluggy (real) ou Demo; outro agregador entraria aqui.</summary>
public interface IProvedorBancario
{
    ProvedorBancario Tipo { get; }
    Task<DadosBancarios> BuscarAsync(ConexaoBancaria conexao, DateOnly desde, DateOnly hoje, CancellationToken ct);
    Task SolicitarAtualizacaoAsync(ConexaoBancaria conexao, CancellationToken ct);
    Task RemoverAsync(ConexaoBancaria conexao, CancellationToken ct);
}

public class ProvedorPluggy(PluggyCliente pluggy) : IProvedorBancario
{
    public ProvedorBancario Tipo => ProvedorBancario.Pluggy;

    private static readonly Dictionary<string, string> MotivosErro = new()
    {
        ["INVALID_CREDENTIALS"] = "O banco não aceitou usuário ou senha. Reconecte o banco.",
        ["INVALID_CREDENTIALS_MFA"] = "O código de verificação não foi aceito. Reconecte o banco.",
        ["USER_INPUT_TIMEOUT"] = "O banco pediu uma informação extra e o tempo acabou. Reconecte o banco.",
        ["SITE_NOT_AVAILABLE"] = "O banco está fora do ar agora. Tentaremos de novo mais tarde.",
        ["CONNECTION_ERROR"] = "Falha de comunicação com o banco. Tentaremos de novo mais tarde.",
        ["USER_AUTHORIZATION_NOT_GRANTED"] = "A autorização no app do banco foi recusada ou cancelada.",
        ["ACCOUNT_NEEDS_ACTION"] = "O banco pede uma ação no app dele antes de liberar os dados.",
    };

    public async Task<DadosBancarios> BuscarAsync(ConexaoBancaria conexao, DateOnly desde, DateOnly hoje, CancellationToken ct)
    {
        var item = await pluggy.BuscarItemAsync(conexao.ItemId, ct);
        var nome = item.Connector?.Name ?? conexao.NomeBanco;
        if (item.Status is "UPDATING" or "MERGING") return DadosBancarios.AindaAtualizando(nome);
        if (item.Status is "LOGIN_ERROR" or "WAITING_USER_ACTION" or "WAITING_USER_INPUT")
        {
            var codigo = item.Error?.Code ?? item.ExecutionStatus ?? item.Status!;
            return new(nome, true, MotivosErro.GetValueOrDefault(codigo, $"O banco pediu para autorizar de novo ({codigo})."), new(null, null), [], [], [], []);
        }

        var contas = await pluggy.BuscarContasAsync(conexao.ItemId, ct);
        var transacoes = new List<TransacaoBancaria>();
        var faturas = new List<FaturaCartao>();
        foreach (var c in contas)
        {
            if (c.Type == "BANK")
            {
                // Só a conta corrente/poupança entra como movimento de caixa. Compras no cartão entram
                // pela fatura (uma conta a pagar), para não contar o mesmo dinheiro duas vezes.
                foreach (var t in await pluggy.BuscarTransacoesAsync(c.Id, desde, ct)) transacoes.Add(NormalizadorPluggy.Normalizar(t));
            }
            else if (c.Type == "CREDIT")
            {
                var fatura = await FaturaEmAbertoAsync(c, hoje, ct);
                if (fatura is not null) faturas.Add(fatura);
            }
        }
        var principal = contas.FirstOrDefault(c => c.Type == "BANK") ?? contas.FirstOrDefault();
        return new(
            nome,
            true,
            null,
            new Titular(principal?.Owner, principal?.TaxNumber),
            contas.Select(c => new ContaBancariaDados(c.Id, c.Type, c.MarketingName ?? c.Name ?? c.Type, c.Balance)).ToList(),
            transacoes,
            faturas,
            []);
    }

    /// <summary>Próxima fatura do cartão (lista de faturas; se não houver, usa saldo + vencimento da conta de crédito).</summary>
    private async Task<FaturaCartao?> FaturaEmAbertoAsync(PluggyConta conta, DateOnly hoje, CancellationToken ct)
    {
        var nome = $"Fatura do cartão {conta.CreditData?.Brand ?? conta.MarketingName ?? conta.Name}".Trim();
        try
        {
            var proxima = (await pluggy.BuscarFaturasAsync(conta.Id, ct))
                .Select(f => (f, venc: Relogio.ParaDataLocal(f.DueDate)))
                .Where(x => x.venc >= hoje.AddDays(-10) && x.f.TotalAmount > 0)
                .OrderBy(x => x.venc)
                .FirstOrDefault();
            if (proxima.f is not null) return new($"{conta.Id}:{proxima.venc:yyyy-MM-dd}", nome, proxima.f.TotalAmount, proxima.venc);
        }
        catch (PluggyException)
        {
            // Nem todo banco/conector informa as faturas; cai no saldo da conta de crédito.
        }
        if (conta.CreditData?.BalanceDueDate is { } vencimento && conta.Balance > 0)
        {
            var venc = Relogio.ParaDataLocal(vencimento);
            return new($"{conta.Id}:{venc:yyyy-MM-dd}", nome, conta.Balance, venc);
        }
        return null;
    }

    public Task SolicitarAtualizacaoAsync(ConexaoBancaria conexao, CancellationToken ct) => pluggy.AtualizarItemAsync(conexao.ItemId, ct);

    public Task RemoverAsync(ConexaoBancaria conexao, CancellationToken ct) => pluggy.ApagarItemAsync(conexao.ItemId, ct);
}

/// <summary>Banco de demonstração: extrato simulado (pet shop) no mesmo formato, sem precisar de chaves da Pluggy.</summary>
public class ProvedorDemo : IProvedorBancario
{
    public ProvedorBancario Tipo => ProvedorBancario.Demo;

    public Task<DadosBancarios> BuscarAsync(ConexaoBancaria conexao, DateOnly desde, DateOnly hoje, CancellationToken ct)
    {
        var transacoes = ExtratoDemo.Gerar(hoje);
        var boletos = new List<Conta>
        {
            new() { Descricao = "Boleto petiscos e areia", Valor = 420, Categoria = "Fornecedor", Vencimento = hoje.AddDays(1), IdExterno = "demo-boleto-1" },
            new() { Descricao = "Boleto ração (distribuidora)", Valor = 1650, Categoria = "Fornecedor", Vencimento = hoje.AddDays(12), IdExterno = "demo-boleto-2" },
        };
        var cartao = new FaturaCartao("demo-cartao:" + hoje.AddDays(18).ToString("yyyy-MM-dd"), "Fatura do cartão Visa", 640.35m, hoje.AddDays(18));

        // Saldo calibrado para o cenário típico da pesquisa: hoje tem dinheiro, mas vai faltar (pior dia ≈ −R$ 450).
        var teste = new EstadoFinanceiro { Titular = ExtratoDemo.DonoPadrao, Contas = [.. boletos.Select(Copiar)] };
        teste.Contas.Add(new Conta { Valor = cartao.Valor, Vencimento = cartao.Vencimento, Origem = OrigemConta.FaturaCartao });
        ProcessadorExtrato.Importar(teste, transacoes, hoje);
        var menor = Financas.Projetar(teste, hoje).Pontos.Min(p => p.Saldo);
        var saldo = Math.Round(-menor - 450, 2);

        return Task.FromResult(new DadosBancarios(
            "Banco de demonstração",
            true,
            null,
            ExtratoDemo.DonoPadrao,
            [new("demo-conta-corrente", "BANK", "Conta corrente (simulada)", saldo), new("demo-cartao", "CREDIT", "Cartão Visa (simulado)", cartao.Valor)],
            transacoes,
            [cartao],
            boletos));
    }

    private static Conta Copiar(Conta c) => new() { Descricao = c.Descricao, Valor = c.Valor, Vencimento = c.Vencimento, Categoria = c.Categoria };

    public Task SolicitarAtualizacaoAsync(ConexaoBancaria conexao, CancellationToken ct) => Task.CompletedTask;

    public Task RemoverAsync(ConexaoBancaria conexao, CancellationToken ct) => Task.CompletedTask;
}
