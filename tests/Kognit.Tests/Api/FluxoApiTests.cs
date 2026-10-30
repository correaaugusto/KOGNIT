using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kognit.Dominio.Regras;

namespace Kognit.Tests.Api;

public class FluxoApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public FluxoApiTests(ApiFactory api) => _api = api;

    private DateOnly Hoje => _api.Pluggy.Hoje;

    private void PrepararExtrato()
    {
        var p = _api.Pluggy;
        if (p.Transacoes.Count > 0) return;
        p.AdicionarTransacao("t-pix", Hoje.AddDays(-1), "CREDIT", 150m, "PIX RECEBIDO MARIA S", "MARIA S OLIVEIRA", "987.654.321-00");
        p.AdicionarTransacao("t-venda", Hoje.AddDays(-2), "CREDIT", 420m, "VENDA CARTAO STONE");
        // Energia paga em 3 meses seguidos: o app deve descobrir a conta fixa sozinho.
        for (var i = 1; i <= 3; i++)
            p.AdicionarTransacao($"t-cemig-{i}", Hoje.AddMonths(-i), "DEBIT", 260m, "PAGAMENTO BOLETO CEMIG DISTRIBUICAO");
        p.AdicionarTransacao("t-retirada", Hoje.AddDays(-3), "DEBIT", 300m, "PIX ENVIADO JOAO PEREIRA DA SILVA", "JOAO PEREIRA DA SILVA", "123.456.789-09");
        p.AdicionarTransacao("t-farmacia", Hoje.AddDays(-1), "DEBIT", 45.90m, "COMPRA CARTAO DROGARIA SAO PAULO");
        p.AdicionarTransacao("t-fornecedor", Hoje.AddDays(-4), "DEBIT", 610m, "PIX ENVIADO DISTRIBUIDORA PET MAIS", "DISTRIBUIDORA PET MAIS LTDA", "22.333.444/0001-55");
    }

    private async Task<(HttpClient Cliente, string ConexaoId)> UsuarioComBancoAsync()
    {
        PrepararExtrato();
        var (cliente, usuarioId) = await _api.NovoUsuarioAsync();
        var itemId = Guid.NewGuid().ToString();
        _api.Pluggy.DonoDoItem[itemId] = usuarioId;
        var resp = await cliente.PostAsJsonAsync("/api/conexoes", new { itemId });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var conexao = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (cliente, conexao.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Sem_login_tudo_e_bloqueado_menos_cadastro_saude_e_documentacao()
    {
        var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/painel")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/extrato")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/saude")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Login_com_senha_errada_e_email_repetido()
    {
        var anonimo = _api.CreateClient();
        var email = $"x-{Guid.NewGuid():N}@teste.com";
        var dados = new { nome = "Ana", email, senha = "senha-forte-123" };
        Assert.Equal(HttpStatusCode.Created, (await anonimo.PostAsJsonAsync("/api/auth/registrar", dados)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await anonimo.PostAsJsonAsync("/api/auth/registrar", dados)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/auth/entrar", new { email, senha = "errada-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.PostAsJsonAsync("/api/auth/entrar", new { email = email.ToUpperInvariant(), senha = "senha-forte-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonimo.PostAsJsonAsync("/api/auth/registrar", new { nome = "A", email = "invalido", senha = "123" })).StatusCode);
    }

    [Fact]
    public async Task Token_do_widget_vai_com_o_usuario_e_o_webhook()
    {
        var (cliente, usuarioId) = await _api.NovoUsuarioAsync();
        var provedores = await cliente.GetFromJsonAsync<JsonElement>("/api/conexoes/provedores");
        Assert.True(provedores.GetProperty("pluggyConfigurado").GetBoolean());
        var resp = await cliente.PostAsJsonAsync("/api/conexoes/token", new { });
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("token-do-widget", corpo.GetProperty("accessToken").GetString());
        Assert.True(corpo.GetProperty("incluirSandbox").GetBoolean());
        var enviado = _api.Pluggy.CorposConnectToken.Last();
        Assert.Equal(usuarioId, enviado["options"]!["clientUserId"]!.GetValue<string>());
        Assert.Equal($"https://api.kognit.teste/api/webhooks/pluggy?segredo={ApiFactory.SegredoWebhook}", enviado["options"]!["webhookUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Conectar_banco_traz_entrou_saiu_contas_pagas_pra_mim_fatura_e_confira()
    {
        var (cliente, _) = await UsuarioComBancoAsync();

        var painel = await cliente.GetFromJsonAsync<JsonElement>("/api/painel");
        Assert.Equal(3210.50m, painel.GetProperty("saldo").GetDecimal());
        Assert.Equal(1, painel.GetProperty("paraConferir").GetInt32());
        Assert.Equal("conectado", painel.GetProperty("conexoes")[0].GetProperty("status").GetString());
        Assert.EndsWith("Z", painel.GetProperty("conexoes")[0].GetProperty("ultimaSincronizacao").GetString()); // horário em UTC explícito
        Assert.Equal(5, painel.GetProperty("ultimasMovimentacoes").GetArrayLength());

        var de = Hoje.AddMonths(-4).ToString("yyyy-MM-dd");
        async Task<List<JsonElement>> Extrato(string filtro) =>
            (await cliente.GetFromJsonAsync<JsonElement>($"/api/extrato?filtro={filtro}&de={de}")).GetProperty("movimentacoes").GetProperty("itens").EnumerateArray().ToList();

        var entrou = await Extrato("entrou");
        var pix = entrou.Single(x => x.GetProperty("descricao").GetString() == "Maria S Oliveira");
        Assert.Equal(("Pix", "entrada", "Vendas"), (pix.GetProperty("meio").GetString(), pix.GetProperty("tipo").GetString(), pix.GetProperty("categoria").GetString()));
        Assert.Contains(entrou, x => x.GetProperty("meio").GetString() == "Maquininha"); // venda da maquininha (o teste de webhook pode ter somado outro Pix)

        var praMim = Assert.Single(await Extrato("praMim"), x => x.GetProperty("descricao").GetString() == "Joao Pereira da Silva");
        Assert.Equal("Transferência para você", praMim.GetProperty("motivo").GetString());

        var pagas = await Extrato("contasPagas");
        Assert.Equal(3, pagas.Count); // os 3 pagamentos da CEMIG
        Assert.All(pagas, x => Assert.True(x.GetProperty("contaPaga").GetProperty("fixa").GetBoolean()));

        var saiu = await Extrato("saiu");
        Assert.Contains(saiu, x => x.GetProperty("categoria").GetString() == "Fornecedor");

        // Contas a pagar descobertas só com o banco: a próxima conta da CEMIG e a fatura do cartão.
        var contas = await cliente.GetFromJsonAsync<JsonElement>("/api/contas");
        var lista = contas.GetProperty("contas").EnumerateArray().ToList();
        var cemig = Assert.Single(lista, c => c.GetProperty("origem").GetString() == "detectada");
        Assert.Equal((260m, Hoje.AddMonths(-1).AddMonths(1)), (cemig.GetProperty("valor").GetDecimal(), DateOnly.Parse(cemig.GetProperty("vencimento").GetString()!)));
        var fatura = Assert.Single(lista, c => c.GetProperty("origem").GetString() == "faturaCartao");
        Assert.Equal((850.40m, "Fatura do cartão Visa"), (fatura.GetProperty("valor").GetDecimal(), fatura.GetProperty("descricao").GetString()));
        Assert.True(contas.GetProperty("totalAPagar").GetProperty("ate30Dias").GetDecimal() >= 850.40m + 260m);

        // Confira: a farmácia ficou incerta; o dono confirma "é pessoal" e vira regra.
        var confira = await cliente.GetFromJsonAsync<JsonElement>("/api/lancamentos/confira");
        var farmacia = Assert.Single(confira.EnumerateArray());
        var r = await cliente.PutAsJsonAsync($"/api/lancamentos/{farmacia.GetProperty("id").GetString()}/classificacao", new { tipo = "retirada" });
        Assert.Equal("Retirada pessoal", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoria").GetString());
        Assert.Equal(0, (await cliente.GetFromJsonAsync<JsonElement>("/api/lancamentos/confira")).GetArrayLength());

        var previsao = await cliente.GetFromJsonAsync<JsonElement>("/api/previsao?menosRetiradaMes=500");
        Assert.Equal(31, previsao.GetProperty("pontos").GetArrayLength());
        Assert.Contains(previsao.GetProperty("semaforo").GetProperty("cor").GetString(), new[] { "verde", "amarelo", "vermelho" });
    }

    [Fact]
    public async Task Nao_da_para_conectar_banco_de_outra_pessoa_nem_ver_dados_de_outro_usuario()
    {
        var (dono, conexaoId) = await UsuarioComBancoAsync();
        var (intruso, intrusoId) = await _api.NovoUsuarioAsync();

        // Item criado por outro usuário (clientUserId diferente)
        var itemAlheio = Guid.NewGuid().ToString();
        _api.Pluggy.DonoDoItem[itemAlheio] = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Forbidden, (await intruso.PostAsJsonAsync("/api/conexoes", new { itemId = itemAlheio })).StatusCode);

        Assert.Equal(0, (await intruso.GetFromJsonAsync<JsonElement>("/api/conexoes")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.PostAsync($"/api/conexoes/{conexaoId}/sincronizar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.DeleteAsync($"/api/conexoes/{conexaoId}")).StatusCode);
        var extratoIntruso = await intruso.GetFromJsonAsync<JsonElement>("/api/extrato");
        Assert.Equal(0, extratoIntruso.GetProperty("movimentacoes").GetProperty("total").GetInt32());
        Assert.NotEqual(intrusoId, (await dono.GetFromJsonAsync<JsonElement>("/api/usuario")).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Webhook_exige_segredo_e_traz_a_transacao_nova_sozinho()
    {
        var (cliente, conexaoId) = await UsuarioComBancoAsync();
        var conexoes = await cliente.GetFromJsonAsync<JsonElement>("/api/conexoes");
        var anonimo = _api.CreateClient();
        var itemId = _api.Pluggy.DonoDoItem.Last().Key;

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/webhooks/pluggy?segredo=errado", new { @event = "transactions/created", itemId })).StatusCode);

        // Chega um Pix novo no banco e a Pluggy avisa.
        _api.Pluggy.AdicionarTransacao($"t-novo-{Guid.NewGuid():N}", Hoje, "CREDIT", 99.90m, "PIX RECEBIDO CLIENTE NOVO", "CLIENTE NOVO");
        var ok = await anonimo.PostAsJsonAsync($"/api/webhooks/pluggy?segredo={ApiFactory.SegredoWebhook}", new { @event = "transactions/created", itemId, accountId = "acc-1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // O trabalhador em segundo plano sincroniza; esperamos o Pix aparecer no extrato.
        var apareceu = false;
        for (var i = 0; i < 50 && !apareceu; i++)
        {
            await Task.Delay(100);
            var hoje = await cliente.GetFromJsonAsync<JsonElement>($"/api/extrato?filtro=entrou&de={Hoje:yyyy-MM-dd}");
            apareceu = hoje.GetProperty("movimentacoes").GetProperty("itens").EnumerateArray().Any(x => x.GetProperty("descricao").GetString() == "Cliente Novo");
        }
        Assert.True(apareceu, "o Pix do webhook deveria aparecer no extrato");
    }

    [Fact]
    public async Task Dinheiro_vivo_soma_ao_saldo_do_banco_e_lancamento_do_banco_nao_pode_ser_apagado()
    {
        var (cliente, _) = await UsuarioComBancoAsync();
        var r = await cliente.PostAsJsonAsync("/api/lancamentos", new { tipo = "entrada", valor = 50m, categoria = "Vendas" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(3260.50m, (await cliente.GetFromJsonAsync<JsonElement>("/api/painel")).GetProperty("saldo").GetDecimal());

        var doBanco = (await cliente.GetFromJsonAsync<JsonElement>("/api/extrato?filtro=entrou&de=2000-01-01")).GetProperty("movimentacoes").GetProperty("itens")
            .EnumerateArray().First(x => x.GetProperty("origem").GetString() == "banco");
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.DeleteAsync($"/api/lancamentos/{doBanco.GetProperty("id").GetString()}")).StatusCode);
    }

    [Fact]
    public async Task Boleto_pela_linha_digitavel()
    {
        var (cliente, _) = await _api.NovoUsuarioAsync();
        var c1 = "001912345"; var c2 = "6789012345"; var c3 = "6789012345";
        var fator = 1000 + (Hoje.DayNumber - new DateOnly(2025, 2, 22).DayNumber) + 5; // vence em 5 dias
        var linha = $"{c1}{LinhaDigitavel.Modulo10(c1)}{c2}{LinhaDigitavel.Modulo10(c2)}{c3}{LinhaDigitavel.Modulo10(c3)}1{fator:D4}{31590:D10}";

        var lido = await (await cliente.PostAsJsonAsync("/api/contas/ler-boleto", new { linhaDigitavel = linha })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((315.90m, Hoje.AddDays(5)), (lido.GetProperty("valor").GetDecimal(), DateOnly.Parse(lido.GetProperty("vencimento").GetString()!)));

        var criada = await cliente.PostAsJsonAsync("/api/contas", new { descricao = "Boleto ração", linhaDigitavel = linha });
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        Assert.Equal("proximos7Dias", (await criada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("situacao").GetString());
        Assert.Equal((HttpStatusCode)422, (await cliente.PostAsJsonAsync("/api/contas/ler-boleto", new { linhaDigitavel = "123" })).StatusCode);
    }

    [Fact]
    public async Task Banco_de_demonstracao_e_modo_apresentacao()
    {
        var (cliente, _) = await _api.NovoUsuarioAsync();
        var conexao = await (await cliente.PostAsync("/api/conexoes/demo", null)).Content.ReadFromJsonAsync<JsonElement>();
        var id = conexao.GetProperty("id").GetString();
        var painel = await cliente.GetFromJsonAsync<JsonElement>("/api/painel");
        Assert.Equal("vermelho", painel.GetProperty("semaforo").GetProperty("cor").GetString());

        var pix = await (await cliente.PostAsJsonAsync($"/api/conexoes/{id}/simular", new { tipo = "pixRecebido", valor = 150m, nome = "Maria" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Entrou R$ 150,00", pix.GetProperty("titulo").GetString());
        Assert.Contains("Venda", pix.GetProperty("linhas")[0].GetString());
        Assert.Equal(painel.GetProperty("saldo").GetDecimal() + 150m, (await cliente.GetFromJsonAsync<JsonElement>("/api/painel")).GetProperty("saldo").GetDecimal());

        var boleto = await (await cliente.PostAsJsonAsync($"/api/conexoes/{id}/simular", new { tipo = "boletoPago" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(boleto.GetProperty("linhas").EnumerateArray(), l => l.GetString()!.Contains("marcada como paga sozinha"));

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync("/api/conexoes/simulacoes")).StatusCode);
        Assert.Equal(painel.GetProperty("saldo").GetDecimal(), (await cliente.GetFromJsonAsync<JsonElement>("/api/painel")).GetProperty("saldo").GetDecimal());
    }
}
