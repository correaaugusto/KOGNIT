using CaixaClaro.Dominio.Demo;
using CaixaClaro.Dominio.Modelos;
using CaixaClaro.Dominio.Regras;

namespace CaixaClaro.Tests.Dominio;

public class FinancasTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 5);

    private static Lancamento L(TipoLancamento tipo, decimal valor, DateOnly data, OrigemLancamento origem = OrigemLancamento.Banco) =>
        new() { Tipo = tipo, Valor = valor, Data = data, Origem = origem, Categoria = "X" };

    [Fact]
    public void Saldo_e_o_do_banco_mais_o_dinheiro_vivo_anotado()
    {
        var e = new EstadoFinanceiro { SaldoBancos = 1000, CaixaDinheiro = 50 };
        e.Lancamentos.Add(L(TipoLancamento.Entrada, 300, Hoje)); // do banco: já está no saldo do banco
        e.Lancamentos.Add(L(TipoLancamento.Entrada, 80, Hoje, OrigemLancamento.Manual));
        e.Lancamentos.Add(L(TipoLancamento.Saida, 30, Hoje.AddDays(3), OrigemLancamento.Manual)); // futuro: não conta
        Assert.Equal(1130m, Financas.SaldoAtual(e, Hoje));
    }

    [Fact]
    public void Contas_recorrentes_se_repetem_na_projecao()
    {
        var contas = new[] { new Conta { Valor = 100, Vencimento = new(2026, 11, 10), Recorrente = true } };
        var lista = Financas.ContasEmAberto(contas, new DateOnly(2027, 1, 31));
        Assert.Equal([new(2026, 11, 10), new(2026, 12, 10), new DateOnly(2027, 1, 10)], lista.Select(c => c.Vencimento));
        Assert.True(lista[1].Prevista);
    }

    [Fact]
    public void Semaforo_vermelho_amarelo_e_verde()
    {
        var vermelho = new EstadoFinanceiro { SaldoBancos = 1000 };
        vermelho.Contas.Add(new Conta { Descricao = "Aluguel", Valor = 1500, Vencimento = new(2026, 11, 15) });
        var s = Financas.CalcularSemaforo(vermelho, Hoje);
        Assert.Equal((CorSemaforo.Vermelho, new DateOnly(2026, 11, 15), -500m), (s.Cor, s.PrimeiroNegativo!.Data, s.Menor.Saldo));

        var amarelo = new EstadoFinanceiro { SaldoBancos = 200 };
        for (var i = 0; i < 30; i++) amarelo.Lancamentos.Add(L(TipoLancamento.Entrada, 100, Hoje.AddDays(-i)));
        amarelo.Contas.Add(new Conta { Valor = 400, Vencimento = new(2026, 11, 11) });
        Assert.Equal(CorSemaforo.Amarelo, Financas.CalcularSemaforo(amarelo, Hoje).Cor);

        Assert.Equal(CorSemaforo.Verde, Financas.CalcularSemaforo(new EstadoFinanceiro { SaldoBancos = 1000 }, Hoje).Cor);
    }

    [Fact]
    public void Alerta_de_retirada_acima_do_lucro_e_conta_vencendo_amanha()
    {
        var e = new EstadoFinanceiro { SaldoBancos = 1000 };
        e.Lancamentos.Add(L(TipoLancamento.Entrada, 1000, new(2026, 11, 2)));
        e.Lancamentos.Add(L(TipoLancamento.Saida, 700, new(2026, 11, 3)));
        e.Lancamentos.Add(L(TipoLancamento.Retirada, 500, new(2026, 11, 4)));
        e.Contas.Add(new Conta { Descricao = "Boleto", Valor = 50, Vencimento = Hoje.AddDays(1) });
        var alertas = Financas.CalcularAlertas(e, Hoje);
        Assert.Contains("lucrou R$ 300", alertas.Single(a => a.Id == "retirada").Texto);
        Assert.Contains(alertas, a => a.Id == "amanha");
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-02-28")]
    [InlineData("2026-11-05")]
    [InlineData("2026-12-31")]
    public void Demonstracao_gera_extrato_com_contas_fixas_descobertas(string dia)
    {
        var hoje = DateOnly.Parse(dia);
        var e = new EstadoFinanceiro { Titular = ExtratoDemo.DonoPadrao, SaldoBancos = 3000 };
        ProcessadorExtrato.Importar(e, ExtratoDemo.Gerar(hoje), hoje);
        Assert.True(e.Contas.Count(c => c.Origem == OrigemConta.Detectada) >= 4);
        Assert.Contains(e.Lancamentos, l => l.Tipo == TipoLancamento.Retirada && !l.Revisar);
        Assert.True(Financas.CalcularTotais(e.Lancamentos, hoje.AddMonths(-3), hoje).Entrou > 0);
    }

    [Fact]
    public void Formata_reais_sem_depender_do_idioma_do_sistema()
    {
        Assert.Equal("R$ 1.200", Texto.ReaisCurto(1200.4m));
        Assert.Equal("−R$ 450", Texto.ReaisCurto(-450.28m));
        Assert.Equal("R$ 1.234,56", Texto.Reais(1234.56m));
    }
}

public class ExtratoDemoTests
{
    [Fact]
    public void Mesmo_dia_gera_sempre_as_mesmas_transacoes_com_ids_estaveis()
    {
        var ontem = ExtratoDemo.Gerar(new DateOnly(2026, 11, 4));
        var hoje = ExtratoDemo.Gerar(new DateOnly(2026, 11, 5));
        var doDia3Ontem = ontem.Where(t => t.Data == new DateOnly(2026, 11, 3)).ToList();
        var doDia3Hoje = hoje.Where(t => t.Data == new DateOnly(2026, 11, 3)).ToList();
        Assert.NotEmpty(doDia3Hoje);
        Assert.Equal(doDia3Ontem, doDia3Hoje);
        Assert.Equal(hoje.Count, hoje.Select(t => t.IdExterno).Distinct().Count());
    }
}
