using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CaixaClaro.Tests.Api;

/// <summary>
/// Simula a API REST da Pluggy nas mesmas rotas e formatos usados pelo SDK oficial
/// (auth, connect_token, items, accounts, v2/transactions com cursor, bills).
/// </summary>
public class PluggyFalsa : HttpMessageHandler
{
    public readonly Dictionary<string, string> DonoDoItem = new();      // itemId -> clientUserId
    public readonly List<JsonObject> Transacoes = new();                 // da conta corrente "acc-1"
    public readonly List<JsonObject> CorposConnectToken = new();
    public readonly List<string> Chamadas = new();
    public string StatusItem = "UPDATED";
    public decimal SaldoConta = 3210.50m;
    public DateOnly Hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    public void AdicionarTransacao(string id, DateOnly data, string tipo, decimal valor, string descricao, string? nome = null, string? doc = null)
    {
        var t = new JsonObject
        {
            ["id"] = id,
            ["accountId"] = "acc-1",
            ["date"] = $"{data:yyyy-MM-dd}T15:00:00.000Z",
            ["description"] = descricao,
            ["type"] = tipo,
            ["amount"] = tipo == "DEBIT" ? -valor : valor,
            ["status"] = "POSTED",
            ["category"] = null,
        };
        if (nome is not null || doc is not null)
        {
            var parte = new JsonObject { ["name"] = nome, ["documentNumber"] = doc is null ? null : new JsonObject { ["value"] = doc, ["type"] = "CPF" } };
            t["paymentData"] = new JsonObject { [tipo == "CREDIT" ? "payer" : "receiver"] = parte, ["boletoMetadata"] = null };
        }
        Transacoes.Add(t);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var caminho = req.RequestUri!.AbsolutePath.Trim('/');
        var consulta = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query);
        Chamadas.Add($"{req.Method} {caminho}");
        if (caminho != "auth" && !req.Headers.Contains("X-API-KEY")) return Json(new { message = "missing api key" }, HttpStatusCode.Unauthorized);

        switch (req.Method.Method, caminho)
        {
            case ("POST", "auth"):
                var exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
                var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"exp\":{exp}}}")).TrimEnd('=');
                return Json(new { apiKey = $"cabecalho.{payload}.assinatura" });
            case ("POST", "connect_token"):
                CorposConnectToken.Add(JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject());
                return Json(new { accessToken = "token-do-widget" });
            case ("GET", var p) when p.StartsWith("items/"):
            {
                var id = p["items/".Length..];
                if (!DonoDoItem.TryGetValue(id, out var dono)) return Json(new { message = "not found" }, HttpStatusCode.NotFound);
                return Json(new { id, status = StatusItem, executionStatus = "SUCCESS", clientUserId = dono, lastUpdatedAt = DateTimeOffset.UtcNow, connector = new { name = "Pluggy Bank" }, error = (object?)null });
            }
            case ("PATCH", var p) when p.StartsWith("items/"):
                return Json(new { id = p["items/".Length..], status = "UPDATING" });
            case ("DELETE", var p) when p.StartsWith("items/"):
                return Json(new { });
            case ("GET", "accounts"):
                return Json(new
                {
                    page = 1, total = 2, totalPages = 1,
                    results = new object[]
                    {
                        new { id = "acc-1", type = "BANK", subtype = "CHECKING_ACCOUNT", name = "Conta Corrente", marketingName = (string?)null, balance = SaldoConta, owner = "JOAO PEREIRA DA SILVA", taxNumber = "123.456.789-09", creditData = (object?)null },
                        new { id = "card-1", type = "CREDIT", subtype = "CREDIT_CARD", name = "Cartão", marketingName = "Cartão Gold", balance = 900m, owner = "JOAO PEREIRA DA SILVA", taxNumber = "123.456.789-09", creditData = new { balanceDueDate = $"{Hoje.AddDays(10):yyyy-MM-dd}T03:00:00.000Z", minimumPayment = 90m, brand = "Visa", level = "GOLD" } },
                    },
                });
            case ("GET", "v2/transactions"):
            {
                // Duas páginas, para testar o cursor "next"/"after".
                var metade = Transacoes.Count / 2;
                var primeira = consulta["after"] is null;
                var itens = primeira ? Transacoes.Take(metade) : Transacoes.Skip(metade);
                var next = primeira ? $"/v2/transactions?accountId=acc-1&after=cursor-2" : null;
                return Json(new { results = itens.ToList(), next });
            }
            case ("GET", "bills"):
                return Json(new { page = 1, total = 1, totalPages = 1, results = new[] { new { id = "bill-1", dueDate = $"{Hoje.AddDays(10):yyyy-MM-dd}T03:00:00.000Z", totalAmount = 850.40m, minimumPaymentAmount = 85m } } });
            default:
                return Json(new { message = $"rota não simulada: {req.Method} {caminho}" }, HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(object corpo, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json") };
}
