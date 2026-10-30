using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kognit.Api.Infra;
using Kognit.Api.Pluggy;
using Kognit.Api.Servicos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kognit.Api.Controllers;

/// <summary>
/// A Pluggy avisa aqui quando o banco tem novidades (transactions/created, item/updated…).
/// Segurança: o endereço leva um segredo, e o conteúdo do aviso nunca é usado como dado —
/// ele só diz QUAL conexão sincronizar; os dados são buscados de novo direto na Pluggy.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController(AppDbContext db, FilaSincronizacao fila, IOptions<PluggyOpcoes> opcoes, ILogger<WebhooksController> log) : ControllerBase
{
    [HttpPost("pluggy")]
    public async Task<IActionResult> Pluggy([FromQuery] string? segredo, [FromBody] JsonElement corpo, CancellationToken ct)
    {
        var esperado = opcoes.Value.SegredoWebhook;
        if (string.IsNullOrEmpty(esperado) || segredo is null ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(esperado)))
            return Unauthorized();

        var evento = corpo.TryGetProperty("event", out var e) ? e.GetString() : null;
        var itemId = corpo.TryGetProperty("itemId", out var i) ? i.GetString() : null;
        if (itemId is null) return Ok(); // eventos que não são de um item (ex.: connector/status_updated)

        var conexao = await db.Conexoes.SingleOrDefaultAsync(c => c.ItemId == itemId, ct);
        if (conexao is null) return Ok(); // item desconhecido: responde 200 para a Pluggy não ficar reenviando
        log.LogInformation("Webhook Pluggy {Evento} para a conexão {Conexao}", evento, conexao.Id);

        switch (evento)
        {
            case "transactions/deleted":
                if (corpo.TryGetProperty("transactionIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                {
                    var lista = ids.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToList();
                    await db.Lancamentos.Where(l => l.UsuarioId == conexao.UsuarioId && lista.Contains(l.IdExterno)).ExecuteDeleteAsync(ct);
                }
                break;
            case "item/deleted":
                conexao.Status = StatusConexao.Erro;
                conexao.Mensagem = "A conexão com o banco foi removida. Conecte o banco de novo.";
                await db.SaveChangesAsync(ct);
                break;
            case "item/error":
            case "item/waiting_user_action":
                conexao.Status = StatusConexao.Erro;
                conexao.Mensagem = "O banco pediu para autorizar de novo. Toque em \"Reconectar\".";
                await db.SaveChangesAsync(ct);
                break;
            default: // item/created, item/updated, transactions/created, transactions/updated…
                fila.Enfileirar(conexao.Id);
                break;
        }
        return Ok();
    }
}
