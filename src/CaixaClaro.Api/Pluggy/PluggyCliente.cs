using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CaixaClaro.Api.Pluggy;

public class PluggyException(string mensagem, int? status = null) : Exception(mensagem)
{
    public int? Status { get; } = status;
}

/// <summary>
/// Cliente HTTP da Pluggy (Open Finance). Não existe SDK oficial em C#; as rotas abaixo são as mesmas
/// usadas pelo SDK oficial em Node (pluggy-sdk): POST auth, POST connect_token, GET/PATCH/DELETE items/{id},
/// GET accounts?itemId, GET v2/transactions?accountId (paginação por cursor) e GET bills?accountId.
/// </summary>
public class PluggyCliente(HttpClient http, IOptions<PluggyOpcoes> opcoes, TimeProvider tempo, ILogger<PluggyCliente> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _travaChave = new(1, 1);
    private string? _apiKey;
    private DateTimeOffset _expiraEm;

    private PluggyOpcoes Op => opcoes.Value;

    /// <summary>A "API key" é um JWT de curta duração obtido com clientId/clientSecret; renova antes de expirar.</summary>
    private async Task<string> ApiKeyAsync(CancellationToken ct)
    {
        if (_apiKey is not null && tempo.GetUtcNow() < _expiraEm) return _apiKey;
        await _travaChave.WaitAsync(ct);
        try
        {
            if (_apiKey is not null && tempo.GetUtcNow() < _expiraEm) return _apiKey;
            using var resp = await http.PostAsJsonAsync("auth", new { clientId = Op.ClientId, clientSecret = Op.ClientSecret, nonExpiring = false }, Json, ct);
            if (!resp.IsSuccessStatusCode) throw new PluggyException("A Pluggy recusou as credenciais do servidor (verifique ClientId e ClientSecret).", (int)resp.StatusCode);
            var corpo = await resp.Content.ReadFromJsonAsync<PluggyAuthResposta>(Json, ct) ?? throw new PluggyException("Resposta vazia da Pluggy");
            _apiKey = corpo.ApiKey;
            _expiraEm = ExpiracaoDoJwt(corpo.ApiKey) ?? tempo.GetUtcNow().AddMinutes(25);
            return _apiKey;
        }
        finally
        {
            _travaChave.Release();
        }
    }

    /// <summary>Lê o campo "exp" do JWT (sem validar assinatura: só para saber quando renovar), com 5 min de folga.</summary>
    internal static DateTimeOffset? ExpiracaoDoJwt(string jwt)
    {
        try
        {
            var partes = jwt.Split('.');
            if (partes.Length < 2) return null;
            var payload = partes[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("exp", out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).AddMinutes(-5) : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string caminho, object? corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, caminho);
        req.Headers.Add("X-API-KEY", await ApiKeyAsync(ct));
        if (corpo is not null) req.Content = JsonContent.Create(corpo, options: Json);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // O corpo pode trazer detalhes técnicos: vai só para o log, nunca para o usuário.
            log.LogWarning("Pluggy {Metodo} {Caminho} respondeu {Status}: {Corpo}", metodo, caminho, (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
            throw new PluggyException($"A Pluggy respondeu {(int)resp.StatusCode} em {caminho.Split('?')[0]}", (int)resp.StatusCode);
        }
        if (typeof(T) == typeof(bool)) return (T)(object)true;
        return await resp.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new PluggyException($"Resposta vazia em {caminho}");
    }

    public async Task<string> CriarConnectTokenAsync(string? itemId, string clientUserId, string? webhookUrl, CancellationToken ct = default)
    {
        var options = new Dictionary<string, object?> { ["clientUserId"] = clientUserId, ["avoidDuplicates"] = true };
        if (!string.IsNullOrEmpty(webhookUrl)) options["webhookUrl"] = webhookUrl;
        var r = await EnviarAsync<PluggyConnectTokenResposta>(HttpMethod.Post, "connect_token", new { itemId, options }, ct);
        return r.AccessToken;
    }

    public Task<PluggyItem> BuscarItemAsync(string itemId, CancellationToken ct = default) =>
        EnviarAsync<PluggyItem>(HttpMethod.Get, $"items/{Uri.EscapeDataString(itemId)}", null, ct);

    /// <summary>Pede ao banco dados novos (a coleta acontece em segundo plano; o item fica "UPDATING").</summary>
    public Task<PluggyItem> AtualizarItemAsync(string itemId, CancellationToken ct = default) =>
        EnviarAsync<PluggyItem>(HttpMethod.Patch, $"items/{Uri.EscapeDataString(itemId)}", new { id = itemId }, ct);

    public Task ApagarItemAsync(string itemId, CancellationToken ct = default) =>
        EnviarAsync<bool>(HttpMethod.Delete, $"items/{Uri.EscapeDataString(itemId)}", null, ct);

    public async Task<List<PluggyConta>> BuscarContasAsync(string itemId, CancellationToken ct = default) =>
        (await EnviarAsync<PluggyPagina<PluggyConta>>(HttpMethod.Get, $"accounts?itemId={Uri.EscapeDataString(itemId)}", null, ct)).Results;

    /// <summary>Todas as transações desde a data, seguindo o cursor "next" (parâmetro "after").</summary>
    public async Task<List<PluggyTransacao>> BuscarTransacoesAsync(string accountId, DateOnly desde, CancellationToken ct = default)
    {
        var todas = new List<PluggyTransacao>();
        string? after = null;
        for (var pagina = 0; pagina < 200; pagina++) // limite de segurança
        {
            var caminho = $"v2/transactions?accountId={Uri.EscapeDataString(accountId)}&dateFrom={desde:yyyy-MM-dd}";
            if (after is not null) caminho += $"&after={Uri.EscapeDataString(after)}";
            var r = await EnviarAsync<PluggyPaginaCursor<PluggyTransacao>>(HttpMethod.Get, caminho, null, ct);
            todas.AddRange(r.Results);
            after = ParametroAfter(r.Next);
            if (after is null) break;
        }
        return todas;
    }

    internal static string? ParametroAfter(string? next)
    {
        if (string.IsNullOrEmpty(next)) return null;
        var consulta = next.Contains('?') ? next[(next.IndexOf('?') + 1)..] : next;
        foreach (var par in consulta.Split('&'))
        {
            var kv = par.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "after") return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    public async Task<List<PluggyFatura>> BuscarFaturasAsync(string accountId, CancellationToken ct = default) =>
        (await EnviarAsync<PluggyPagina<PluggyFatura>>(HttpMethod.Get, $"bills?accountId={Uri.EscapeDataString(accountId)}", null, ct)).Results;
}
