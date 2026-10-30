using CaixaClaro.Api.Contratos;
using CaixaClaro.Api.Infra;
using CaixaClaro.Api.Servicos;
using CaixaClaro.Dominio.Modelos;
using CaixaClaro.Dominio.Regras;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaixaClaro.Api.Controllers;

/// <summary>Tela inicial numa chamada só: saldo, semáforo, resumo de hoje, mês, alertas, "Confira", últimas movimentações e próximas contas.</summary>
[ApiController]
[Route("api/painel")]
public class PainelController(AppDbContext db, RepositorioEstado repositorio, FilaSincronizacao fila, Relogio relogio) : ControllerBase
{
    private static readonly TimeSpan SincronizarSeMaisAntigoQue = TimeSpan.FromHours(1);

    [HttpGet]
    public async Task<PainelResponse> Obter(CancellationToken ct)
    {
        var usuarioId = User.Id();
        var hoje = relogio.Hoje;
        var conexoes = await db.Conexoes.Where(c => c.UsuarioId == usuarioId).ToListAsync(ct);
        // Abriu o app e a última busca tem mais de 1 h: atualiza em segundo plano (a resposta não espera).
        foreach (var c in conexoes.Where(c => c.UltimaSincronizacao is null || relogio.AgoraUtc - c.UltimaSincronizacao > SincronizarSeMaisAntigoQue))
            fila.Enfileirar(c.Id);
        var contasBancarias = await db.ContasBancarias.Where(c => c.UsuarioId == usuarioId).ToListAsync(ct);

        var (estado, usuario, _, _) = await repositorio.CarregarAsync(usuarioId, ct);
        var hojeTotais = Financas.CalcularTotais(estado.Lancamentos, hoje, hoje);
        var amanha = hoje.AddDays(1);
        var ultimas = estado.Lancamentos.Where(l => l.Data <= hoje).OrderByDescending(l => l.Data).ThenByDescending(l => l.Id).Take(5);

        return new PainelResponse(
            usuario.NomeNegocio,
            Financas.SaldoAtual(estado, hoje),
            SemaforoDto.De(Financas.CalcularSemaforo(estado, hoje)),
            new ResumoHojeDto(hojeTotais.Entrou, hojeTotais.Saiu, hojeTotais.Lucro,
                Financas.ContasEmAberto(estado.Contas, amanha).Where(c => c.Vencimento == amanha).Select(c => new ContaResumoDto(c.Conta.Descricao, c.Conta.Valor, c.Vencimento)).ToList()),
            TotaisDto.De(Financas.CalcularTotais(estado.Lancamentos, Financas.InicioDoMes(hoje), hoje)),
            Financas.CalcularAlertas(estado, hoje).Select(AlertaDto.De).ToList(),
            estado.Lancamentos.Count(l => l.Revisar),
            ultimas.Select(l => LancamentoDto.De(l, estado.Contas)).ToList(),
            estado.Contas.Where(c => !c.Paga).OrderBy(c => c.Vencimento).Take(5).Select(c => ContaDto.De(c, hoje)).ToList(),
            conexoes.Select(c => ConexaoDto.De(c, contasBancarias.Where(x => x.ConexaoId == c.Id))).ToList());
    }
}

public enum FiltroExtrato { Tudo, Entrou, Saiu, ContasPagas, PraMim }

/// <summary>Extrato: cada Pix recebido, conta paga e retirada, com o motivo de cada classificação.</summary>
[ApiController]
[Route("api/extrato")]
public class ExtratoController(RepositorioEstado repositorio, Relogio relogio) : ControllerBase
{
    /// <param name="filtro">tudo | entrou | saiu | contasPagas | praMim</param>
    /// <param name="de">Início (padrão: 1º dia do mês atual)</param>
    /// <param name="ate">Fim (padrão: hoje)</param>
    [HttpGet]
    public async Task<ExtratoResponse> Obter([FromQuery] FiltroExtrato filtro = FiltroExtrato.Tudo, [FromQuery] DateOnly? de = null, [FromQuery] DateOnly? ate = null,
        [FromQuery] int pagina = 1, [FromQuery] int tamanho = 50, CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        var inicio = de ?? Financas.InicioDoMes(hoje);
        var fim = ate ?? hoje;
        tamanho = Math.Clamp(tamanho, 1, 200);
        pagina = Math.Max(1, pagina);
        var estado = (await repositorio.CarregarAsync(User.Id(), ct)).Estado;

        var lista = estado.Lancamentos
            .Where(l => l.Data >= inicio && l.Data <= fim)
            .Where(l => filtro switch
            {
                FiltroExtrato.Entrou => l.Tipo is TipoLancamento.Entrada or TipoLancamento.Aporte,
                FiltroExtrato.Saiu => l.Tipo == TipoLancamento.Saida,
                FiltroExtrato.ContasPagas => l.EhConta,
                FiltroExtrato.PraMim => l.Tipo == TipoLancamento.Retirada,
                _ => true,
            })
            .OrderByDescending(l => l.Data).ThenByDescending(l => l.Id)
            .ToList();
        var itens = lista.Skip((pagina - 1) * tamanho).Take(tamanho).Select(l => LancamentoDto.De(l, estado.Contas)).ToList();
        return new ExtratoResponse(TotaisDto.De(Financas.CalcularTotais(estado.Lancamentos, inicio, fim)), new PaginaDto<LancamentoDto>(itens, pagina, tamanho, lista.Count));
    }
}

/// <summary>Dinheiro vivo (anotado à mão), "Confira" e correção de classificação.</summary>
[ApiController]
[Route("api/lancamentos")]
public class LancamentosController(RepositorioEstado repositorio, TravaPorUsuario trava, Relogio relogio) : ControllerBase
{
    /// <summary>Lançamentos que o app não reconheceu com segurança (recentes).</summary>
    [HttpGet("confira")]
    public async Task<List<LancamentoDto>> Confira(CancellationToken ct)
    {
        var e = (await repositorio.CarregarAsync(User.Id(), ct)).Estado;
        return e.Lancamentos.Where(l => l.Revisar).OrderByDescending(l => l.Data).Select(l => LancamentoDto.De(l, e.Contas)).ToList();
    }

    /// <summary>"É pessoal" / "É do negócio (categoria)": vale para todos os lançamentos do mesmo favorecido e vira regra.</summary>
    [HttpPut("{id:guid}/classificacao")]
    public async Task<ActionResult<LancamentoDto>> Classificar(Guid id, ClassificacaoRequest req, CancellationToken ct)
    {
        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var l = sessao.Estado.Lancamentos.SingleOrDefault(x => x.Id == id);
        if (l is null) return NotFound();
        var categoria = req.Tipo switch
        {
            TipoLancamento.Retirada => Categorias.Retirada,
            TipoLancamento.Aporte => Categorias.Aporte,
            TipoLancamento.Entrada => string.IsNullOrWhiteSpace(req.Categoria) ? "Vendas" : req.Categoria,
            _ => string.IsNullOrWhiteSpace(req.Categoria) ? "Outros" : req.Categoria,
        };
        ProcessadorExtrato.Confirmar(sessao.Estado, l, req.Tipo, categoria);
        await repositorio.SalvarAsync(sessao, ct);
        return LancamentoDto.De(l, sessao.Estado.Contas);
    }

    /// <summary>Anotar dinheiro vivo (o que passa pelo banco entra sozinho).</summary>
    [HttpPost]
    public async Task<ActionResult<LancamentoDto>> Criar(NovoLancamentoRequest req, CancellationToken ct)
    {
        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var categoria = req.Tipo switch
        {
            TipoLancamento.Retirada => Categorias.Retirada,
            TipoLancamento.Aporte => Categorias.Aporte,
            TipoLancamento.Entrada => req.Categoria ?? "Vendas",
            _ => req.Categoria ?? "Outros",
        };
        var l = new Lancamento
        {
            UsuarioId = sessao.Estado.UsuarioId,
            Origem = OrigemLancamento.Manual,
            Tipo = req.Tipo,
            Valor = Math.Round(req.Valor, 2),
            Categoria = categoria,
            Data = req.Data ?? relogio.Hoje,
            Descricao = string.IsNullOrWhiteSpace(req.Descricao) ? categoria : req.Descricao.Trim(),
            Motivo = "Anotado por você",
        };
        sessao.Estado.Lancamentos.Add(l);
        await repositorio.SalvarAsync(sessao, ct);
        return Created($"/api/lancamentos/{l.Id}", LancamentoDto.De(l, sessao.Estado.Contas));
    }

    /// <summary>Só lançamentos manuais podem ser apagados (o extrato do banco é a fonte da verdade).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Apagar(Guid id, CancellationToken ct)
    {
        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var l = sessao.Estado.Lancamentos.SingleOrDefault(x => x.Id == id);
        if (l is null) return NotFound();
        if (l.Origem == OrigemLancamento.Banco && !l.Simulado) return Problem(statusCode: 409, title: "Lançamentos do banco não podem ser apagados. Corrija a classificação.");
        sessao.Estado.Lancamentos.Remove(l);
        await repositorio.SalvarAsync(sessao, ct);
        return NoContent();
    }
}

public enum FiltroContas { Abertas, Pagas, Todas }

/// <summary>Contas e boletos a pagar: descobertos no extrato, faturas do cartão e cadastrados (com leitura da linha digitável).</summary>
[ApiController]
[Route("api/contas")]
public class ContasController(RepositorioEstado repositorio, TravaPorUsuario trava, Relogio relogio) : ControllerBase
{
    [HttpGet]
    public async Task<ContasResponse> Listar([FromQuery] FiltroContas status = FiltroContas.Abertas, CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        var e = (await repositorio.CarregarAsync(User.Id(), ct)).Estado;
        // Visão de 3 meses, incluindo as repetições das contas fixas (resolve o "só sei as contas do próximo mês").
        var previstas = Financas.ContasEmAberto(e.Contas, hoje.AddDays(90));
        decimal Total(int de, int ate) => previstas.Where(c => c.Vencimento <= hoje.AddDays(ate) && (de == 0 || c.Vencimento >= hoje.AddDays(de))).Sum(c => c.Conta.Valor);
        var contas = e.Contas
            .Where(c => status == FiltroContas.Todas || c.Paga == (status == FiltroContas.Pagas))
            .OrderBy(c => c.Paga).ThenBy(c => c.Paga ? DateOnly.MaxValue.DayNumber - (c.PagaEm ?? c.Vencimento).DayNumber : c.Vencimento.DayNumber)
            .Select(c => ContaDto.De(c, hoje)).ToList();
        return new ContasResponse(new TotalAPagarDto(Total(0, 30), Total(31, 60), Total(61, 90)), contas);
    }

    /// <summary>Lê valor e vencimento da linha digitável (para o front pré-preencher).</summary>
    [HttpPost("ler-boleto")]
    public ActionResult<BoletoLidoDto> LerBoleto(LerBoletoRequest req)
    {
        var b = LinhaDigitavel.Ler(req.LinhaDigitavel, relogio.Hoje);
        if (b is null) return Problem(statusCode: 422, title: "Linha digitável inválida. Confira os números.");
        return new BoletoLidoDto(b.Valor, b.Vencimento, b.Tipo);
    }

    /// <summary>Cadastra um boleto: pela linha digitável (valor e vencimento lidos sozinhos) ou informando os dados.</summary>
    [HttpPost]
    public async Task<ActionResult<ContaDto>> Criar(NovaContaRequest req, CancellationToken ct)
    {
        var hoje = relogio.Hoje;
        BoletoLido? boleto = null;
        if (!string.IsNullOrWhiteSpace(req.LinhaDigitavel))
        {
            boleto = LinhaDigitavel.Ler(req.LinhaDigitavel, hoje);
            if (boleto is null) return Problem(statusCode: 422, title: "Linha digitável inválida. Confira os números.");
        }
        var valor = req.Valor ?? boleto?.Valor;
        var vencimento = req.Vencimento ?? boleto?.Vencimento;
        if (valor is null || vencimento is null)
            return Problem(statusCode: 422, title: "Informe valor e vencimento (este boleto não traz essa informação na linha digitável).");

        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var conta = new Conta
        {
            UsuarioId = sessao.Estado.UsuarioId,
            Descricao = string.IsNullOrWhiteSpace(req.Descricao) ? "Boleto" : req.Descricao.Trim(),
            Valor = Math.Round(valor.Value, 2),
            Vencimento = vencimento.Value,
            Categoria = req.Categoria ?? "Fornecedor",
            Recorrente = req.Recorrente,
            Origem = OrigemConta.Manual,
            LinhaDigitavel = boleto is null ? null : Texto.SoDigitos(req.LinhaDigitavel),
        };
        sessao.Estado.Contas.Add(conta);
        // Se o pagamento já apareceu no extrato, a conta já nasce paga.
        ProcessadorExtrato.ConciliarContas(sessao.Estado);
        await repositorio.SalvarAsync(sessao, ct);
        return Created($"/api/contas/{conta.Id}", ContaDto.De(conta, hoje));
    }

    /// <summary>Marcar como paga à mão (ex.: pagou em dinheiro). Se depois o pagamento aparecer no banco, o app troca sozinho.</summary>
    [HttpPost("{id:guid}/pagar")]
    public async Task<ActionResult<ContaDto>> Pagar(Guid id, CancellationToken ct)
    {
        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var conta = sessao.Estado.Contas.SingleOrDefault(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.Paga) return Problem(statusCode: 409, title: "Esta conta já está paga.");
        ProcessadorExtrato.PagarManualmente(sessao.Estado, conta, relogio.Hoje);
        await repositorio.SalvarAsync(sessao, ct);
        return ContaDto.De(conta, relogio.Hoje);
    }

    /// <summary>Apagar. Se for uma conta fixa descoberta, a série não é recriada.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Apagar(Guid id, CancellationToken ct)
    {
        using var _ = await trava.EntrarAsync(User.Id(), ct);
        var sessao = await repositorio.CarregarAsync(User.Id(), ct);
        var conta = sessao.Estado.Contas.SingleOrDefault(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.Serie?.StartsWith("auto:") == true) sessao.Estado.SeriesIgnoradas.Add(conta.Serie);
        sessao.Estado.Contas.Remove(conta);
        foreach (var l in sessao.Estado.Lancamentos.Where(l => l.ContaPagaId == conta.Id)) l.ContaPagaId = null;
        await repositorio.SalvarAsync(sessao, ct);
        return NoContent();
    }
}

/// <summary>Previsão de caixa de 30 dias, simulador "E se…?", gastos do mês e evolução mensal.</summary>
[ApiController]
[Route("api/previsao")]
public class PrevisaoController(RepositorioEstado repositorio, Relogio relogio) : ControllerBase
{
    /// <param name="menosRetiradaMes">"E se eu retirar R$ X a menos por mês?"</param>
    /// <param name="maisEntradaMes">"E se eu vender R$ X a mais por mês?"</param>
    [HttpGet]
    public async Task<PrevisaoResponse> Obter([FromQuery] int dias = 30, [FromQuery] decimal menosRetiradaMes = 0, [FromQuery] decimal maisEntradaMes = 0, CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        dias = Math.Clamp(dias, 7, 90);
        var e = (await repositorio.CarregarAsync(User.Id(), ct)).Estado;
        var proj = Financas.Projetar(e, hoje, dias, new Ajustes(Math.Max(0, menosRetiradaMes), Math.Max(0, maisEntradaMes)));
        var gastos = Financas.GastosPorCategoria(e.Lancamentos, Financas.InicioDoMes(hoje), hoje);
        var totalGastos = gastos.Sum(g => g.Valor);
        return new PrevisaoResponse(
            Financas.SaldoAtual(e, hoje),
            SemaforoDto.De(Financas.CalcularSemaforo(e, hoje, proj)),
            proj.Pontos.Select(p => new PontoPrevisaoDto(p.Data, p.Saldo, p.Contas.Select(c => new ContaResumoDto(c.Conta.Descricao, c.Conta.Valor, c.Vencimento)).ToList())).ToList(),
            new MediasDto(Math.Round(proj.Medias.Entrada, 2), Math.Round(proj.Medias.GastoVariavel, 2), Math.Round(proj.RetiradaDia, 2), proj.Medias.Dias),
            proj.Contas.Sum(c => c.Conta.Valor),
            gastos.Select(g => new GastoCategoriaDto(g.Categoria, g.Valor, totalGastos == 0 ? 0 : Math.Round(g.Valor / totalGastos * 100, 1))).ToList(),
            Financas.EvolucaoMensal(e.Lancamentos, hoje).Select(m => new MesDto(m.Ano, m.Mes, TotaisDto.De(m.Totais))).ToList());
    }
}
