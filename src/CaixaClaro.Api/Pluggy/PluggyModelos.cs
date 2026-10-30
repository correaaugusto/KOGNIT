namespace CaixaClaro.Api.Pluggy;

// Formatos da API REST da Pluggy (conferidos nos tipos do SDK oficial pluggy-sdk 0.94).
// Só os campos que o Caixa Claro usa.

public class PluggyOpcoes
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string UrlBase { get; set; } = "https://api.pluggy.ai/";
    /// <summary>Mostra os bancos de teste da Pluggy no widget (contas em período de teste só conectam esses).</summary>
    public bool Sandbox { get; set; }
    /// <summary>Endereço público desta API (ex.: https://caixa-claro-api.onrender.com), para a Pluggy chamar o webhook.</summary>
    public string? UrlPublicaApi { get; set; }
    /// <summary>Segredo que vai no endereço do webhook: só quem conhece consegue chamá-lo.</summary>
    public string? SegredoWebhook { get; set; }

    public bool Configurado => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public record PluggyPagina<T>(List<T> Results, int Page, int Total, int TotalPages);

public record PluggyPaginaCursor<T>(List<T> Results, string? Next);

public record PluggyConector(string? Name);

public record PluggyErro(string? Code, string? Message);

public record PluggyItem(
    string Id,
    string? Status,
    string? ExecutionStatus,
    string? ClientUserId,
    DateTimeOffset? LastUpdatedAt,
    PluggyConector? Connector,
    PluggyErro? Error);

public record PluggyDadosCredito(DateTimeOffset? BalanceDueDate, decimal? MinimumPayment, string? Brand, string? Level);

public record PluggyConta(
    string Id,
    string Type,
    string? Subtype,
    string? Name,
    string? MarketingName,
    decimal Balance,
    string? Owner,
    string? TaxNumber,
    PluggyDadosCredito? CreditData);

public record PluggyDocumento(string? Value, string? Type);

public record PluggyParticipante(string? Name, PluggyDocumento? DocumentNumber);

public record PluggyDadosPagamento(PluggyParticipante? Payer, PluggyParticipante? Receiver);

public record PluggyEstabelecimento(string? Name, string? BusinessName, string? Cnpj);

public record PluggyTransacao(
    string Id,
    string AccountId,
    DateTimeOffset Date,
    string? Description,
    string? DescriptionRaw,
    string Type,
    decimal Amount,
    string? Status,
    string? Category,
    PluggyDadosPagamento? PaymentData,
    PluggyEstabelecimento? Merchant);

public record PluggyFatura(string Id, DateTimeOffset DueDate, decimal TotalAmount, decimal? MinimumPaymentAmount);

public record PluggyAuthResposta(string ApiKey);

public record PluggyConnectTokenResposta(string AccessToken);
