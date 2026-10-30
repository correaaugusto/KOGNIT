namespace CaixaClaro.Dominio.Modelos;

/// <summary>Entrada = venda/serviço; Saida = gasto do negócio; Retirada = dinheiro para o dono;
/// Aporte = dinheiro do dono colocado no caixa (aumenta o saldo, mas não é venda).</summary>
public enum TipoLancamento { Entrada, Saida, Retirada, Aporte }

public enum OrigemLancamento { Banco, Manual }

public enum OrigemConta { Manual, Detectada, FaturaCartao }

public enum Confianca { Alta, Baixa }

/// <summary>Uma movimentação de dinheiro: veio do extrato do banco ou foi anotada à mão (dinheiro vivo).</summary>
public class Lancamento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid? ConexaoId { get; set; }
    /// <summary>Id da transação no banco/agregador: impede importar a mesma transação duas vezes.</summary>
    public string? IdExterno { get; set; }
    public OrigemLancamento Origem { get; set; }
    public DateOnly Data { get; set; }
    public decimal Valor { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Categoria { get; set; } = "";
    public string Descricao { get; set; } = "";
    /// <summary>Descrição original do extrato (ex.: "PIX RECEBIDO MARIA S").</summary>
    public string? DescricaoBanco { get; set; }
    /// <summary>Pix, Boleto, Cartão, Maquininha, Débito automático ou Transferência.</summary>
    public string? Meio { get; set; }
    /// <summary>Identifica o mesmo favorecido entre meses (CPF/CNPJ ou nome limpo).</summary>
    public string? Chave { get; set; }
    /// <summary>Por que o app classificou assim (mostrado ao dono).</summary>
    public string? Motivo { get; set; }
    /// <summary>Classificação incerta e recente: aparece no "Confira".</summary>
    public bool Revisar { get; set; }
    /// <summary>Conta a pagar quitada por este lançamento.</summary>
    public Guid? ContaPagaId { get; set; }
    /// <summary>Série de conta fixa descoberta (ex.: "auto:doc:11222333000144").</summary>
    public string? Serie { get; set; }
    /// <summary>Criado no modo apresentação (não existe no banco).</summary>
    public bool Simulado { get; set; }
    /// <summary>Histórico simulado: representa o passado, já incluído no saldo do banco.</summary>
    public bool Historico { get; set; }

    public bool EhConta => ContaPagaId is not null || Serie is not null;
}

/// <summary>Conta/boleto a pagar: cadastrada, descoberta no extrato (conta fixa) ou fatura do cartão.</summary>
public class Conta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Valor { get; set; }
    public string Categoria { get; set; } = "Outros";
    public DateOnly Vencimento { get; set; }
    public bool Recorrente { get; set; }
    public bool Paga { get; set; }
    public DateOnly? PagaEm { get; set; }
    public bool PagaPeloBanco { get; set; }
    public OrigemConta Origem { get; set; }
    public string? Serie { get; set; }
    public string? Chave { get; set; }
    public string? LinhaDigitavel { get; set; }
    /// <summary>Para faturas de cartão: "{contaCartao}:{vencimento}".</summary>
    public string? IdExterno { get; set; }

    public Conta CopiarParaProximoMes() => new()
    {
        UsuarioId = UsuarioId,
        Descricao = Descricao,
        Valor = Valor,
        Categoria = Categoria,
        Vencimento = Vencimento.AddMonths(1),
        Recorrente = true,
        Origem = Origem,
        Serie = Serie,
        Chave = Chave,
    };
}

/// <summary>Escolha do dono para um favorecido ("é pessoal", "é Fornecedor"…), aplicada nas próximas vezes.</summary>
public class RegraClassificacao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string Chave { get; set; } = "";
    public TipoLancamento Tipo { get; set; }
    public string Categoria { get; set; } = "";
}

/// <summary>Transação do extrato já normalizada (independente do agregador).</summary>
public record TransacaoBancaria(
    string IdExterno,
    DateOnly Data,
    decimal Valor,
    bool Credito,
    string Descricao,
    string? ContraparteNome = null,
    string? ContraparteDocumento = null,
    string? CategoriaBanco = null,
    bool Pendente = false,
    bool Simulado = false,
    bool Historico = false);

/// <summary>Titular da conta conectada (+ CPF pessoal do dono, se for conta PJ): reconhece retiradas e aportes.</summary>
public record Titular(string? Nome, string? Documento, string? CpfDono = null);

public record Classificacao(TipoLancamento Tipo, string Categoria, Confianca Confianca, string Motivo);

/// <summary>Tudo o que as regras precisam de um usuário, carregado na memória.</summary>
public class EstadoFinanceiro
{
    public Guid UsuarioId { get; init; }
    public List<Lancamento> Lancamentos { get; init; } = [];
    public List<Conta> Contas { get; init; } = [];
    public Dictionary<string, RegraClassificacao> Regras { get; init; } = [];
    public HashSet<string> SeriesIgnoradas { get; init; } = [];
    /// <summary>Soma dos saldos das contas bancárias conectadas (vem do banco).</summary>
    public decimal SaldoBancos { get; set; }
    /// <summary>Dinheiro vivo que o dono tinha quando começou a usar o app.</summary>
    public decimal CaixaDinheiro { get; set; }
    public Titular Titular { get; set; } = new(null, null);
}

public static class Categorias
{
    public const string Retirada = "Retirada pessoal";
    public const string Aporte = "Dinheiro do dono";
    public const string FaturaCartao = "Fatura do cartão";

    public static readonly string[] Saida =
        ["Fornecedor", "Aluguel", "Energia/água/internet", "Impostos", "Salários", "Manutenção", FaturaCartao, "Outros"];

    public static readonly string[] Entrada = ["Vendas", "Serviços", "Outros"];
}
