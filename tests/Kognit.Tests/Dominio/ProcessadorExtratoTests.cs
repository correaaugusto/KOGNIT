using Kognit.Dominio.Modelos;
using Kognit.Dominio.Regras;

namespace Kognit.Tests.Dominio;

public class ProcessadorExtratoTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 5);
    private static readonly Titular Dono = new("JOAO PEREIRA DA SILVA", "123.456.789-09");

    private static EstadoFinanceiro Estado() => new() { UsuarioId = Guid.NewGuid(), Titular = Dono, SaldoBancos = 1000 };

    private static TransacaoBancaria T(string id, string data, bool credito, decimal valor, string descricao, string? nome = null, string? doc = null) =>
        new(id, DateOnly.Parse(data), valor, credito, descricao, nome, doc);

    [Fact]
    public void Classifica_sem_digitar_contas_impostos_fornecedor_retirada_e_vendas()
    {
        var c = (TransacaoBancaria t) => Classificador.Classificar(t, new Dictionary<string, RegraClassificacao>(), Dono);
        Assert.Equal("Energia/água/internet", c(T("1", "2026-11-05", false, 260, "PAGAMENTO BOLETO CEMIG")).Categoria);
        Assert.Equal("Impostos", c(T("2", "2026-11-05", false, 76, "PAGAMENTO DAS SIMPLES NACIONAL")).Categoria);
        Assert.Equal("Fornecedor", c(T("3", "2026-11-05", false, 600, "PIX ENVIADO DISTRIBUIDORA PET MAIS LTDA")).Categoria);
        var retirada = c(T("4", "2026-11-05", false, 300, "PIX ENVIADO", "Joao Pereira da Silva", "12345678909"));
        Assert.Equal((TipoLancamento.Retirada, Confianca.Alta), (retirada.Tipo, retirada.Confianca));
        Assert.Equal(TipoLancamento.Aporte, c(T("5", "2026-11-05", true, 500, "PIX RECEBIDO", "JOAO PEREIRA DA SILVA")).Tipo);
        var venda = c(T("6", "2026-11-05", true, 80, "PIX RECEBIDO MARIA S"));
        Assert.Equal((TipoLancamento.Entrada, "Vendas"), (venda.Tipo, venda.Categoria));
        var farmacia = c(T("7", "2026-11-05", false, 45, "COMPRA CARTAO DROGARIA SAO PAULO"));
        Assert.Equal((TipoLancamento.Retirada, Confianca.Baixa), (farmacia.Tipo, farmacia.Confianca));
        Assert.Equal(Confianca.Baixa, c(T("8", "2026-11-05", false, 99, "PIX ENVIADO FULANO DE TAL")).Confianca);
        Assert.Equal(Categorias.FaturaCartao, c(T("9", "2026-11-05", false, 900, "PAGAMENTO FATURA CARTAO")).Categoria);
    }

    [Fact]
    public void Nome_amigavel_chave_e_meio_de_pagamento()
    {
        Assert.Equal("Cemig Distribuicao", Texto.NomeAmigavel(T("x", "2026-11-05", false, 1, "PAGAMENTO BOLETO CEMIG DISTRIBUICAO 05/11")));
        Assert.Equal("Drogaria Sao Paulo", Texto.NomeAmigavel(T("x", "2026-11-05", false, 1, "COMPRA CARTAO DEBITO DROGARIA SAO PAULO")));
        Assert.Equal("doc:11222333000144", Texto.Chave(T("x", "2026-11-05", false, 1, "PIX", "IMOB", "11.222.333/0001-44")));
        Assert.Equal("Pix", Texto.MeioPagamento(T("x", "2026-11-05", true, 1, "PIX RECEBIDO MARIA")));
        Assert.Equal("Débito automático", Texto.MeioPagamento(T("x", "2026-11-05", false, 1, "DEB AUTOMATICO SABESP")));
        Assert.Equal("Maquininha", Texto.MeioPagamento(T("x", "2026-11-05", true, 1, "VENDA CARTAO STONE")));
    }

    [Fact]
    public void Importar_nao_duplica_e_ignora_pendentes()
    {
        var e = Estado();
        var lista = new[] { T("a", "2026-11-01", true, 1000, "PIX RECEBIDO CLIENTE"), T("b", "2026-11-02", false, 300, "PAGAMENTO DAS") };
        Assert.Equal(2, ProcessadorExtrato.Importar(e, lista, Hoje).Count);
        Assert.Empty(ProcessadorExtrato.Importar(e, lista, Hoje));
        Assert.Empty(ProcessadorExtrato.Importar(e, [T("p", "2026-11-05", false, 10, "X") with { Pendente = true }], Hoje));
        Assert.Equal(2, e.Lancamentos.Count);
    }

    [Fact]
    public void Descobre_conta_fixa_e_preve_a_proxima_sem_contar_duas_vezes_na_media()
    {
        var e = Estado();
        var lista = new List<TransacaoBancaria>
        {
            T("a1", "2026-08-10", false, 1200, "PIX ENVIADO IMOBILIARIA", "IMOBILIARIA LAR", "11222333000144"),
            T("a2", "2026-09-10", false, 1200, "PIX ENVIADO IMOBILIARIA", "IMOBILIARIA LAR", "11222333000144"),
            T("a3", "2026-10-09", false, 1200, "PIX ENVIADO IMOBILIARIA", "IMOBILIARIA LAR", "11222333000144"),
            T("f1", "2026-10-06", false, 500, "PIX ENVIADO DISTRIBUIDORA PET"),
            T("f2", "2026-10-13", false, 520, "PIX ENVIADO DISTRIBUIDORA PET"),
            T("f3", "2026-09-14", false, 510, "PIX ENVIADO DISTRIBUIDORA PET"),
        };
        ProcessadorExtrato.Importar(e, lista, Hoje);
        var fixa = Assert.Single(e.Contas, c => c.Origem == OrigemConta.Detectada);
        Assert.Equal((new DateOnly(2026, 11, 9), 1200m), (fixa.Vencimento, fixa.Valor));
        Assert.All(e.Lancamentos.Where(l => l.Chave == "doc:11222333000144"), l => Assert.NotNull(l.Serie));
        Assert.True(Financas.MediasDiarias(e, Hoje).GastoVariavel > 0);
    }

    [Fact]
    public void Da_baixa_quando_o_pagamento_aparece_e_cria_a_do_proximo_mes()
    {
        var e = Estado();
        var luz = new Conta { Descricao = "Luz", Valor = 260, Vencimento = new(2026, 11, 3), Recorrente = true };
        e.Contas.Add(luz);
        ProcessadorExtrato.Importar(e, [T("p1", "2026-11-03", false, 260, "PAGAMENTO BOLETO CEMIG")], Hoje);
        Assert.True(luz.Paga && luz.PagaPeloBanco);
        Assert.Contains(e.Contas, c => !c.Paga && c.Vencimento == new DateOnly(2026, 12, 3));
    }

    [Fact]
    public void Pagamento_antecipado_do_mesmo_favorecido_vale_ate_30_dias_antes()
    {
        var e = Estado();
        var aluguel = new Conta { Descricao = "Aluguel", Valor = 1200, Vencimento = new(2026, 12, 1), Chave = "doc:11222333000144" };
        e.Contas.Add(aluguel);
        ProcessadorExtrato.Importar(e, [T("p1", "2026-11-05", false, 1200, "PIX ENVIADO IMOB", "IMOB", "11222333000144")], Hoje);
        Assert.True(aluguel.Paga);
    }

    [Fact]
    public void Se_o_dono_ja_tinha_pago_a_mao_o_manual_e_trocado_pelo_do_banco()
    {
        var e = Estado();
        var boleto = new Conta { Descricao = "Boleto ração", Valor = 1650, Vencimento = new(2026, 11, 4) };
        e.Contas.Add(boleto);
        ProcessadorExtrato.PagarManualmente(e, boleto, new(2026, 11, 4));
        ProcessadorExtrato.Importar(e, [T("bb", "2026-11-04", false, 1650, "PAGAMENTO BOLETO RACOES BRASIL")], Hoje);
        var unico = Assert.Single(e.Lancamentos);
        Assert.Equal((OrigemLancamento.Banco, boleto.Id), (unico.Origem, unico.ContaPagaId));
    }

    [Fact]
    public void Fatura_do_cartao_e_quitada_pelo_pagamento_de_fatura()
    {
        var e = Estado();
        var fatura = new Conta { Descricao = "Fatura do cartão", Valor = 900, Vencimento = new(2026, 11, 10), Origem = OrigemConta.FaturaCartao };
        e.Contas.Add(fatura);
        ProcessadorExtrato.Importar(e, [T("ft", "2026-11-05", false, 850, "PAGAMENTO FATURA CARTAO NUBANK")], Hoje);
        Assert.True(fatura.Paga);
    }

    [Fact]
    public void Confira_so_mostra_o_incerto_recente_e_a_escolha_vira_regra()
    {
        var e = Estado();
        ProcessadorExtrato.Importar(e,
        [
            T("n1", "2026-11-01", false, 90, "PIX ENVIADO AUTO CENTER BOA VIAGEM"),
            T("n2", "2026-11-03", false, 110, "PIX ENVIADO AUTO CENTER BOA VIAGEM"),
            T("velho", "2026-08-01", false, 70, "PIX ENVIADO LOJA QUALQUER"),
            T("ok", "2026-11-02", false, 76, "PAGAMENTO DAS"),
        ], Hoje);
        Assert.Equal(["n1", "n2"], e.Lancamentos.Where(l => l.Revisar).Select(l => l.IdExterno!).Order());
        ProcessadorExtrato.Confirmar(e, e.Lancamentos.First(l => l.IdExterno == "n1"), TipoLancamento.Retirada, Categorias.Retirada);
        Assert.DoesNotContain(e.Lancamentos, l => l.Revisar);
        ProcessadorExtrato.Importar(e, [T("n3", "2026-11-05", false, 95, "PIX ENVIADO AUTO CENTER BOA VIAGEM")], Hoje);
        var n3 = e.Lancamentos.Single(l => l.IdExterno == "n3");
        Assert.Equal((TipoLancamento.Retirada, false), (n3.Tipo, n3.Revisar));
    }

    [Fact]
    public void Pagamento_do_mes_passado_nao_quita_a_conta_fixa_deste_mes()
    {
        var e = Estado();
        // Pago todo dia 29; hoje é 29/10: a conta de 29/10 está em aberto (o pagamento de 29/09 é do mês anterior).
        var hoje = new DateOnly(2026, 10, 29);
        ProcessadorExtrato.Importar(e, new[] { 1, 2, 3 }.Select(i => T($"c{i}", hoje.AddMonths(-i).ToString("yyyy-MM-dd"), false, 260, "PAGAMENTO BOLETO CEMIG")).ToList(), hoje);
        var conta = Assert.Single(e.Contas);
        Assert.Equal((hoje, false), (conta.Vencimento, conta.Paga));
        ProcessadorExtrato.Importar(e, [T("c0", "2026-10-28", false, 262, "PAGAMENTO BOLETO CEMIG")], hoje);
        Assert.True(conta.Paga);
    }

    [Fact]
    public void Gasto_que_se_repete_em_meses_nao_seguidos_nao_vira_conta_fixa()
    {
        var e = Estado();
        ProcessadorExtrato.Importar(e,
        [
            T("q1", "2026-04-04", false, 480, "PIX ENVIADO ASSISTENCIA TECNICA"),
            T("q2", "2026-07-04", false, 480, "PIX ENVIADO ASSISTENCIA TECNICA"),
            T("q3", "2026-10-04", false, 480, "PIX ENVIADO ASSISTENCIA TECNICA"),
        ], Hoje);
        Assert.Empty(e.Contas);
    }
}
