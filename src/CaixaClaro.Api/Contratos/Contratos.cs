using System.ComponentModel.DataAnnotations;
using CaixaClaro.Api.Infra;
using CaixaClaro.Dominio.Modelos;
using CaixaClaro.Dominio.Regras;

namespace CaixaClaro.Api.Contratos;

// ---------- Autenticação ----------

public record RegistrarRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Nome,
    [Required, EmailAddress, StringLength(200)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Senha,
    [StringLength(100)] string? NomeNegocio,
    [StringLength(14)] string? Cpf);

public record EntrarRequest([Required, EmailAddress] string Email, [Required] string Senha);

public record UsuarioDto(Guid Id, string Nome, string Email, string NomeNegocio, string? Cpf, decimal CaixaDinheiro)
{
    public static UsuarioDto De(Usuario u) => new(u.Id, u.Nome, u.Email, u.NomeNegocio, u.Cpf, u.CaixaDinheiro);
}

public record TokenResponse(string Token, DateTime ExpiraEm, UsuarioDto Usuario);

public record AtualizarUsuarioRequest(
    [StringLength(100, MinimumLength = 2)] string? Nome,
    [StringLength(100)] string? NomeNegocio,
    [StringLength(14)] string? Cpf,
    decimal? CaixaDinheiro);

// ---------- Conexões bancárias ----------

public record ProvedoresDto(bool PluggyConfigurado, bool Sandbox, bool DemoHabilitado, bool SimulacaoHabilitada);

public record ConnectTokenRequest(string? ItemId);

public record ConnectTokenResponse(string AccessToken, bool IncluirSandbox);

public record NovaConexaoRequest([Required] string ItemId);

public record ContaBancariaDto(string Tipo, string Nome, decimal Saldo);

public record ConexaoDto(
    Guid Id,
    ProvedorBancario Provedor,
    string NomeBanco,
    StatusConexao Status,
    string? Mensagem,
    DateTime? UltimaSincronizacao,
    decimal SaldoContas,
    List<ContaBancariaDto> Contas)
{
    public static ConexaoDto De(ConexaoBancaria c, IEnumerable<ContaBancaria> contas)
    {
        var lista = contas.ToList();
        return new(c.Id, c.Provedor, c.NomeBanco, c.Status, c.Mensagem, c.UltimaSincronizacao,
            lista.Where(x => x.Tipo == "BANK").Sum(x => x.Saldo), lista.Select(x => new ContaBancariaDto(x.Tipo, x.Nome, x.Saldo)).ToList());
    }
}

public record SimularRequest([Required] Dominio.Demo.TipoSimulacao Tipo, [Range(0.01, 1_000_000)] decimal? Valor, [StringLength(80)] string? Nome);

public record SimulacaoResponse(bool Ok, string Titulo, List<string> Linhas, LancamentoDto? Lancamento);

// ---------- Lançamentos / extrato ----------

public record ContaPagaDto(Guid? Id, string Descricao, bool Fixa);

public record LancamentoDto(
    Guid Id,
    DateOnly Data,
    decimal Valor,
    TipoLancamento Tipo,
    string Categoria,
    string Descricao,
    string? Meio,
    OrigemLancamento Origem,
    string? Motivo,
    bool Revisar,
    bool Simulado,
    ContaPagaDto? ContaPaga)
{
    public static LancamentoDto De(Lancamento l, IReadOnlyCollection<Conta> contas)
    {
        ContaPagaDto? conta = null;
        if (l.ContaPagaId is { } id && contas.FirstOrDefault(c => c.Id == id) is { } c)
            conta = new(c.Id, c.Descricao, c.Recorrente || c.Origem == OrigemConta.Detectada);
        else if (l.Serie is not null)
            conta = new(null, l.Descricao, true);
        return new(l.Id, l.Data, l.Valor, l.Tipo, l.Categoria, l.Descricao, l.Meio ?? (l.Origem == OrigemLancamento.Manual ? "Dinheiro vivo" : null),
            l.Origem, l.Motivo, l.Revisar, l.Simulado, conta);
    }
}

public record TotaisDto(decimal Entrou, decimal Saiu, decimal PraMim, decimal Lucro)
{
    public static TotaisDto De(Totais t) => new(t.Entrou, t.Saiu, t.Retiradas, t.Lucro);
}

public record PaginaDto<T>(List<T> Itens, int Pagina, int TamanhoPagina, int Total);

public record ExtratoResponse(TotaisDto Totais, PaginaDto<LancamentoDto> Movimentacoes);

public record NovoLancamentoRequest(
    [Required] TipoLancamento Tipo,
    [Range(0.01, 1_000_000)] decimal Valor,
    [StringLength(60)] string? Categoria,
    DateOnly? Data,
    [StringLength(120)] string? Descricao);

/// <summary>Categoria só é usada para "saida"/"entrada"; para retirada e aporte é fixa.</summary>
public record ClassificacaoRequest([Required] TipoLancamento Tipo, [StringLength(60)] string? Categoria);

// ---------- Contas a pagar ----------

public enum SituacaoConta { Atrasada, VenceHoje, Proximos7Dias, Depois, Paga }

public record ContaDto(
    Guid Id,
    string Descricao,
    decimal Valor,
    string Categoria,
    DateOnly Vencimento,
    bool Recorrente,
    bool Paga,
    DateOnly? PagaEm,
    bool PagaPeloBanco,
    OrigemConta Origem,
    SituacaoConta Situacao)
{
    public static ContaDto De(Conta c, DateOnly hoje) => new(c.Id, c.Descricao, c.Valor, c.Categoria, c.Vencimento, c.Recorrente, c.Paga, c.PagaEm, c.PagaPeloBanco, c.Origem,
        c.Paga ? SituacaoConta.Paga
        : c.Vencimento < hoje ? SituacaoConta.Atrasada
        : c.Vencimento == hoje ? SituacaoConta.VenceHoje
        : c.Vencimento <= hoje.AddDays(7) ? SituacaoConta.Proximos7Dias
        : SituacaoConta.Depois);
}

public record TotalAPagarDto(decimal Ate30Dias, decimal De31a60Dias, decimal De61a90Dias);

public record ContasResponse(TotalAPagarDto TotalAPagar, List<ContaDto> Contas);

public record NovaContaRequest(
    [StringLength(120)] string? Descricao,
    [Range(0.01, 1_000_000)] decimal? Valor,
    DateOnly? Vencimento,
    [StringLength(60)] string? Categoria,
    bool Recorrente,
    [StringLength(60)] string? LinhaDigitavel);

public record LerBoletoRequest([Required, StringLength(60)] string LinhaDigitavel);

public record BoletoLidoDto(decimal? Valor, DateOnly? Vencimento, string Tipo);

// ---------- Painel e previsão ----------

public record SemaforoDto(CorSemaforo Cor, string Titulo, string Texto, decimal MenorSaldo, DateOnly DiaMenorSaldo, DateOnly? PrimeiroDiaNegativo)
{
    public static SemaforoDto De(Semaforo s) => new(s.Cor, s.Titulo, s.Texto, s.Menor.Saldo, s.Menor.Data, s.PrimeiroNegativo?.Data);
}

public record AlertaDto(string Id, NivelAlerta Nivel, string Titulo, string Texto, string? Sugestao)
{
    public static AlertaDto De(Alerta a) => new(a.Id, a.Nivel, a.Titulo, a.Texto, a.Sugestao);
}

public record ContaResumoDto(string Descricao, decimal Valor, DateOnly Vencimento);

public record ResumoHojeDto(decimal Entrou, decimal Saiu, decimal Sobrou, List<ContaResumoDto> VenceAmanha);

public record PainelResponse(
    string NomeNegocio,
    decimal Saldo,
    SemaforoDto Semaforo,
    ResumoHojeDto Hoje,
    TotaisDto Mes,
    List<AlertaDto> Alertas,
    int ParaConferir,
    List<LancamentoDto> UltimasMovimentacoes,
    List<ContaDto> ProximasContas,
    List<ConexaoDto> Conexoes);

public record PontoPrevisaoDto(DateOnly Data, decimal Saldo, List<ContaResumoDto> Contas);

public record MediasDto(decimal EntradaPorDia, decimal GastoPorDia, decimal RetiradaPorDia, int DiasConsiderados);

public record GastoCategoriaDto(string Categoria, decimal Valor, decimal Percentual);

public record MesDto(int Ano, int Mes, TotaisDto Totais);

public record PrevisaoResponse(
    decimal SaldoAtual,
    SemaforoDto Semaforo,
    List<PontoPrevisaoDto> Pontos,
    MediasDto Medias,
    decimal ContasNoPeriodo,
    List<GastoCategoriaDto> GastosDoMes,
    List<MesDto> Evolucao);
