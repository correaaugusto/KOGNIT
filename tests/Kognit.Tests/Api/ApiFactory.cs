using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kognit.Api.Pluggy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kognit.Tests.Api;

/// <summary>A API de verdade, rodando em memória, com banco SQLite temporário e a Pluggy simulada.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public PluggyFalsa Pluggy { get; } = new();
    public const string SegredoWebhook = "segredo-de-teste";
    private readonly string _arquivoBanco = Path.Combine(Path.GetTempPath(), $"kognit-teste-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Com a variável KOGNIT_TESTE_POSTGRES (ex.: "Host=localhost;Username=kognit;Password=…"), os testes rodam
    /// num PostgreSQL de verdade: cada execução cria um banco novo, aplica as migrações e apaga no final.
    /// Sem ela, usam um arquivo SQLite temporário.
    /// </summary>
    private static readonly string? BasePostgres = Environment.GetEnvironmentVariable("KOGNIT_TESTE_POSTGRES");
    private readonly string _bancoPostgres = $"kognit_teste_{Guid.NewGuid():N}";
    public static bool UsandoPostgres => !string.IsNullOrWhiteSpace(BasePostgres);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        if (UsandoPostgres)
        {
            builder.UseSetting("BancoDeDados:Provedor", "Postgres");
            builder.UseSetting("ConnectionStrings:Padrao", $"{BasePostgres};Database={_bancoPostgres}");
        }
        else
        {
            builder.UseSetting("ConnectionStrings:Padrao", $"Data Source={_arquivoBanco}");
        }
        builder.UseSetting("Jwt:Chave", "chave-de-teste-com-pelo-menos-32-caracteres!!");
        builder.UseSetting("Pluggy:ClientId", "id-teste");
        builder.UseSetting("Pluggy:ClientSecret", "segredo-teste");
        builder.UseSetting("Pluggy:Sandbox", "true");
        builder.UseSetting("Pluggy:UrlBase", "https://pluggy.falsa/");
        builder.UseSetting("Pluggy:UrlPublicaApi", "https://api.kognit.teste");
        builder.UseSetting("Pluggy:SegredoWebhook", SegredoWebhook);
        builder.UseSetting("Seguranca:TentativasLoginPorMinuto", "1000");
        builder.ConfigureServices(s => s.AddHttpClient<PluggyCliente>().ConfigurePrimaryHttpMessageHandler(() => Pluggy));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (UsandoPostgres)
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            using var conexao = new Npgsql.NpgsqlConnection($"{BasePostgres};Database=postgres");
            conexao.Open();
            using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_bancoPostgres}\" WITH (FORCE)", conexao);
            cmd.ExecuteNonQuery();
            return;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_arquivoBanco); } catch (IOException) { }
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Cria um usuário novo e devolve um cliente HTTP já autenticado + o id do usuário.</summary>
    public async Task<(HttpClient Cliente, string UsuarioId)> NovoUsuarioAsync(string? cpf = null)
    {
        var cliente = CreateClient();
        var resp = await cliente.PostAsJsonAsync("/api/auth/registrar", new
        {
            nome = "Dono de Teste",
            email = $"dono-{Guid.NewGuid():N}@teste.com",
            senha = "senha-forte-123",
            nomeNegocio = "Pet Shop Teste",
            cpf,
        });
        resp.EnsureSuccessStatusCode();
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo.GetProperty("token").GetString());
        return (cliente, corpo.GetProperty("usuario").GetProperty("id").GetString()!);
    }
}
