using Kognit.Api.Bancos;
using Kognit.Api.Contratos;
using Kognit.Api.Infra;
using Kognit.Api.Pluggy;
using Kognit.Api.Servicos;
using Kognit.Dominio.Demo;
using Kognit.Dominio.Modelos;
using Kognit.Dominio.Regras;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kognit.Api.Controllers;

/// <summary>Conectar o banco do usuário, sincronizar e (no modo apresentação) simular movimentos.</summary>
[ApiController]
[Route("api/conexoes")]
public class ConexoesController(
    AppDbContext db,
    PluggyCliente pluggy,
    IOptions<PluggyOpcoes> pluggyOpcoes,
    IEnumerable<IProvedorBancario> provedores,
    Sincronizador sincronizador,
    FilaSincronizacao fila,
    RepositorioEstado repositorio,
    TravaPorUsuario trava,
    Relogio relogio,
    IConfiguration config) : ControllerBase
{
    private bool DemoHabilitado => config.GetValue("Demo:Habilitado", true);
    private bool SimulacaoHabilitada => config.GetValue("Simulacao:Habilitada", true);

    /// <summary>O que está disponível no servidor (o front decide se mostra "Conectar meu banco" ou o banco de demonstração).</summary>
    [HttpGet("provedores")]
    public ProvedoresDto Provedores() => new(pluggyOpcoes.Value.Configurado, pluggyOpcoes.Value.Sandbox, DemoHabilitado, SimulacaoHabilitada);

    [HttpGet]
    public async Task<List<ConexaoDto>> Listar(CancellationToken ct)
    {
        var id = User.Id();
        var conexoes = await db.Conexoes.Where(c => c.UsuarioId == id).OrderBy(c => c.CriadaEm).ToListAsync(ct);
        var contas = await db.ContasBancarias.Where(c => c.UsuarioId == id).ToListAsync(ct);
        return conexoes.Select(c => ConexaoDto.De(c, contas.Where(x => x.ConexaoId == c.Id))).ToList();
    }

    /// <summary>
    /// Passo 1: token temporário para abrir o widget Pluggy Connect no front.
    /// Vai com o id do usuário (clientUserId) e o endereço do webhook.
    /// </summary>
    [HttpPost("token")]
    public async Task<ActionResult<ConnectTokenResponse>> Token(ConnectTokenRequest req, CancellationToken ct)
    {
        if (!pluggyOpcoes.Value.Configurado) return Problem(statusCode: 503, title: "Conexão com bancos reais não configurada no servidor.");
        if (req.ItemId is not null && !await db.Conexoes.AnyAsync(c => c.ItemId == req.ItemId && c.UsuarioId == User.Id(), ct)) return NotFound();
        var token = await pluggy.CriarConnectTokenAsync(req.ItemId, User.Id().ToString(), UrlWebhook(), ct);
        return new ConnectTokenResponse(token, pluggyOpcoes.Value.Sandbox);
    }

    private string? UrlWebhook()
    {
        var o = pluggyOpcoes.Value;
        if (string.IsNullOrWhiteSpace(o.UrlPublicaApi) || string.IsNullOrWhiteSpace(o.SegredoWebhook)) return null;
        return $"{o.UrlPublicaApi.TrimEnd('/')}/api/webhooks/pluggy?segredo={Uri.EscapeDataString(o.SegredoWebhook)}";
    }

    /// <summary>
    /// Passo 2: depois do widget (onSuccess), o front envia o item.id. A API confere na Pluggy que o item
    /// foi criado por este usuário (clientUserId) — ninguém consegue "pegar" o banco de outra pessoa.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ConexaoDto>> Conectar(NovaConexaoRequest req, CancellationToken ct)
    {
        if (!pluggyOpcoes.Value.Configurado) return Problem(statusCode: 503, title: "Conexão com bancos reais não configurada no servidor.");
        var usuarioId = User.Id();
        var existente = await db.Conexoes.SingleOrDefaultAsync(c => c.ItemId == req.ItemId, ct);
        if (existente is not null && existente.UsuarioId != usuarioId) return Problem(statusCode: 403, title: "Este banco foi conectado por outro usuário.");

        PluggyItem item;
        try { item = await pluggy.BuscarItemAsync(req.ItemId, ct); }
        catch (PluggyException e) when (e.Status is 400 or 404) { return Problem(statusCode: 404, title: "Conexão não encontrada na Pluggy."); }
        if (item.ClientUserId != usuarioId.ToString()) return Problem(statusCode: 403, title: "Este banco não foi conectado a partir da sua conta.");

        var conexao = existente ?? new ConexaoBancaria { UsuarioId = usuarioId, Provedor = ProvedorBancario.Pluggy, ItemId = item.Id };
        conexao.NomeBanco = item.Connector?.Name ?? "Banco";
        if (existente is null) db.Conexoes.Add(conexao);
        await db.SaveChangesAsync(ct);
        return await SincronizarEResponder(conexao, ct);
    }

    /// <summary>Conecta o banco de demonstração (extrato simulado), para testar sem chaves da Pluggy.</summary>
    [HttpPost("demo")]
    public async Task<ActionResult<ConexaoDto>> ConectarDemo(CancellationToken ct)
    {
        if (!DemoHabilitado) return Problem(statusCode: 403, title: "Banco de demonstração desabilitado.");
        var usuarioId = User.Id();
        var conexao = await db.Conexoes.FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.Provedor == ProvedorBancario.Demo, ct);
        if (conexao is null)
        {
            conexao = new ConexaoBancaria { UsuarioId = usuarioId, Provedor = ProvedorBancario.Demo, ItemId = $"demo-{Guid.NewGuid():N}", NomeBanco = "Banco de demonstração" };
            db.Conexoes.Add(conexao);
            await db.SaveChangesAsync(ct);
        }
        return await SincronizarEResponder(conexao, ct);
    }

    private async Task<ActionResult<ConexaoDto>> SincronizarEResponder(ConexaoBancaria conexao, CancellationToken ct)
    {
        var r = await sincronizador.SincronizarAsync(conexao.Id, ct);
        if (r.Resultado == ResultadoSincronizacao.AindaAtualizando) fila.Enfileirar(conexao.Id);
        await db.Entry(conexao).ReloadAsync(ct);
        var contas = await db.ContasBancarias.Where(c => c.ConexaoId == conexao.Id).ToListAsync(ct);
        return Created($"/api/conexoes/{conexao.Id}", ConexaoDto.De(conexao, contas));
    }

    /// <summary>"Atualizar agora": pede dados novos ao banco; a sincronização termina em segundo plano.</summary>
    [HttpPost("{id:guid}/sincronizar")]
    public async Task<IActionResult> Sincronizar(Guid id, CancellationToken ct)
    {
        var conexao = await db.Conexoes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == User.Id(), ct);
        if (conexao is null) return NotFound();
        try { await provedores.Single(p => p.Tipo == conexao.Provedor).SolicitarAtualizacaoAsync(conexao, ct); }
        catch (PluggyException) { /* se o pedido falhar, ainda buscamos o que já existe */ }
        fila.Enfileirar(conexao.Id);
        return Accepted();
    }

    /// <summary>Desconecta o banco (apaga o item na Pluggy). Os lançamentos já importados continuam.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Desconectar(Guid id, CancellationToken ct)
    {
        var conexao = await db.Conexoes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == User.Id(), ct);
        if (conexao is null) return NotFound();
        try { await provedores.Single(p => p.Tipo == conexao.Provedor).RemoverAsync(conexao, ct); }
        catch (PluggyException e) when (e.Status == 404) { }
        db.ContasBancarias.RemoveRange(db.ContasBancarias.Where(c => c.ConexaoId == id));
        db.Conexoes.Remove(conexao);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- Modo apresentação ----------

    /// <summary>Cria um movimento simulado (Pix recebido, Pix enviado, boleto pago, retirada, gasto pessoal) e mostra o que o app fez.</summary>
    [HttpPost("{id:guid}/simular")]
    public async Task<ActionResult<SimulacaoResponse>> Simular(Guid id, SimularRequest req, CancellationToken ct)
    {
        if (!SimulacaoHabilitada) return Problem(statusCode: 403, title: "Modo apresentação desabilitado.");
        var conexao = await db.Conexoes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == User.Id(), ct);
        if (conexao is null) return NotFound();
        using var _ = await trava.EntrarAsync(conexao.UsuarioId, ct);
        var sessao = await repositorio.CarregarAsync(conexao.UsuarioId, ct);
        var estado = sessao.Estado;
        var transacao = Simulador.Criar(req.Tipo, estado, relogio.Hoje, req.Valor, req.Nome);
        if (transacao is null)
            return new SimulacaoResponse(false, "Nenhuma conta a pagar nos próximos dias", ["Gere o histórico ou cadastre um boleto em Contas."], null);

        var pagasAntes = estado.Contas.Where(c => c.Paga).ToHashSet();
        var lancamento = ProcessadorExtrato.Importar(estado, [transacao], relogio.Hoje, conexao.Id).Single();
        await repositorio.SalvarAsync(sessao, ct);

        var linhas = new List<string> { Explicar(lancamento) };
        linhas.AddRange(estado.Contas.Where(c => c.Paga && !pagasAntes.Contains(c)).Select(c => $"Conta \"{c.Descricao}\" marcada como paga sozinha"));
        var titulo = $"{(transacao.Credito ? "Entrou" : "Saiu")} {Texto.Reais(lancamento.Valor)}: {lancamento.Descricao}";
        return new SimulacaoResponse(true, titulo, linhas, LancamentoDto.De(lancamento, estado.Contas));
    }

    private static string Explicar(Lancamento l)
    {
        if (l.Revisar) return $"Foi para o \"Confira\": {l.Motivo?.ToLowerInvariant()}";
        var tipo = l.Tipo switch
        {
            TipoLancamento.Entrada => "Venda",
            TipoLancamento.Aporte => "Dinheiro do dono",
            TipoLancamento.Retirada => "Retirada pessoal",
            _ => $"Gasto do negócio ({l.Categoria})",
        };
        return $"Classificado sozinho como {tipo}: {l.Motivo?.ToLowerInvariant()}";
    }

    /// <summary>3 meses de histórico simulado (gráficos + contas fixas descobertas). Não muda o saldo de hoje.</summary>
    [HttpPost("{id:guid}/simular/historico")]
    public async Task<ActionResult<SimulacaoResponse>> SimularHistorico(Guid id, CancellationToken ct)
    {
        if (!SimulacaoHabilitada) return Problem(statusCode: 403, title: "Modo apresentação desabilitado.");
        var conexao = await db.Conexoes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == User.Id(), ct);
        if (conexao is null) return NotFound();
        using var _ = await trava.EntrarAsync(conexao.UsuarioId, ct);
        var sessao = await repositorio.CarregarAsync(conexao.UsuarioId, ct);
        var novos = ProcessadorExtrato.Importar(sessao.Estado, Simulador.Historico(sessao.Estado, relogio.Hoje), relogio.Hoje, conexao.Id);
        await repositorio.SalvarAsync(sessao, ct);
        var fixas = sessao.Estado.Contas.Where(c => c.Origem == OrigemConta.Detectada && !c.Paga).Select(c => c.Descricao).ToList();
        return new SimulacaoResponse(novos.Count > 0, novos.Count > 0 ? $"{novos.Count} movimentos de 3 meses importados" : "O histórico já foi gerado",
            [$"Contas fixas descobertas sozinhas: {(fixas.Count > 0 ? string.Join(", ", fixas) : "nenhuma")}"], null);
    }

    /// <summary>Apaga todos os movimentos simulados e desfaz o que eles causaram (baixas e contas descobertas).</summary>
    [HttpDelete("simulacoes")]
    public async Task<IActionResult> ApagarSimulacoes(CancellationToken ct)
    {
        var usuarioId = User.Id();
        using var _ = await trava.EntrarAsync(usuarioId, ct);
        var sessao = await repositorio.CarregarAsync(usuarioId, ct);
        var e = sessao.Estado;
        var simulados = e.Lancamentos.Where(l => l.Simulado).ToHashSet();
        e.Lancamentos.RemoveAll(simulados.Contains);
        var restantes = e.Lancamentos.Where(l => l.Origem == OrigemLancamento.Banco).Select(l => l.Chave).ToHashSet();
        // Contas fixas que só existiam por causa do histórico simulado
        e.Contas.RemoveAll(c => c.Origem == OrigemConta.Detectada && !restantes.Contains(c.Chave));
        // Contas que foram "pagas" por um movimento simulado voltam a ficar em aberto
        foreach (var conta in e.Contas.Where(c => c.Paga && simulados.Any(l => l.ContaPagaId == c.Id)).ToList())
        {
            e.Contas.RemoveAll(x => x != conta && !x.Paga && x.Serie is not null && x.Serie == conta.Serie && x.Vencimento > conta.Vencimento);
            conta.Paga = false;
            conta.PagaEm = null;
            conta.PagaPeloBanco = false;
        }
        foreach (var l in e.Lancamentos.Where(l => l.Serie is not null && !e.Contas.Any(c => c.Serie == l.Serie))) l.Serie = null;
        ProcessadorExtrato.DetectarContasRecorrentes(e);
        await repositorio.SalvarAsync(sessao, ct);
        return NoContent();
    }
}
