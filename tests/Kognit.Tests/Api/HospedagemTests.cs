using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Kognit.Tests.Api;

/// <summary>Configuração como no Render: endereço público vindo de RENDER_EXTERNAL_URL e segredos gerados
/// automaticamente (base64, com "+", "/" e "=", e mais curtos que 32 caracteres).</summary>
public class ApiFactoryRender : ApiFactory
{
    public const string SegredoGerado = "q8+Zt/1Kx9mN0pL=";
    public const string UrlRender = "https://kognit-api.onrender.com";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Pluggy:UrlPublicaApi", "");
        builder.UseSetting("RENDER_EXTERNAL_URL", UrlRender);
        builder.UseSetting("Pluggy:SegredoWebhook", SegredoGerado);
        builder.UseSetting("Jwt:Chave", "c2VncmVkby1kby1yZW5kZXI="); // 24 caracteres, como um segredo base64 gerado
    }
}

public class HospedagemTests(ApiFactoryRender api) : IClassFixture<ApiFactoryRender>
{
    [Fact]
    public async Task Webhook_usa_o_endereco_do_render_e_aceita_segredo_com_simbolos()
    {
        var (cliente, usuarioId) = await api.NovoUsuarioAsync(); // login funciona com o segredo JWT curto
        await cliente.PostAsJsonAsync("/api/conexoes/token", new { });
        var webhookUrl = api.Pluggy.CorposConnectToken.Last()["options"]!["webhookUrl"]!.GetValue<string>();
        Assert.StartsWith($"{ApiFactoryRender.UrlRender}/api/webhooks/pluggy?segredo=", webhookUrl);

        // A Pluggy chama exatamente o endereço recebido: o segredo com +, / e = tem que continuar valendo.
        var itemId = Guid.NewGuid().ToString();
        api.Pluggy.DonoDoItem[itemId] = usuarioId;
        var caminho = new Uri(webhookUrl).PathAndQuery;
        var resp = await api.CreateClient().PostAsJsonAsync(caminho, new { @event = "item/updated", itemId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var errado = await api.CreateClient().PostAsJsonAsync("/api/webhooks/pluggy?segredo=q8 Zt/1Kx9mN0pL=", new { @event = "item/updated", itemId });
        Assert.Equal(HttpStatusCode.Unauthorized, errado.StatusCode);
    }
}
