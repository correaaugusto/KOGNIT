using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CaixaClaro.Api.Bancos;
using CaixaClaro.Api.Infra;
using CaixaClaro.Api.Pluggy;
using CaixaClaro.Api.Servicos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Configuração (appsettings.json + variáveis de ambiente, ex.: Pluggy__ClientSecret) ----------
builder.Services.Configure<PluggyOpcoes>(config.GetSection("Pluggy"));
builder.Services.Configure<JwtOpcoes>(config.GetSection("Jwt"));
var jwt = config.GetSection("Jwt").Get<JwtOpcoes>() ?? new JwtOpcoes();
if (jwt.Chave.Length < 32)
    throw new InvalidOperationException("Configure Jwt:Chave com pelo menos 32 caracteres (em produção: variável de ambiente Jwt__Chave).");

// ---------- Banco de dados ----------
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(config.GetConnectionString("Padrao") ?? "Data Source=caixaclaro.db"));
builder.Services.AddScoped<RepositorioEstado>();

// ---------- Autenticação: JWT; tudo exige login, exceto o que tiver [AllowAnonymous] ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Emissor,
        ValidAudience = jwt.Publico,
        IssuerSigningKey = jwt.ChaveAssinatura(),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<ServicoToken>();

// Limita tentativas de login/cadastro (dificulta adivinhar senhas).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("Seguranca:TentativasLoginPorMinuto", 10), Window = TimeSpan.FromMinutes(1) }));
});

// ---------- Pluggy (Open Finance) e provedores bancários ----------
builder.Services.AddHttpClient<PluggyCliente>((sp, http) =>
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<PluggyOpcoes>>().Value.UrlBase);
    http.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<IProvedorBancario, ProvedorPluggy>();
builder.Services.AddScoped<IProvedorBancario, ProvedorDemo>();

// ---------- Sincronização (webhook, botão "atualizar", rotina periódica) ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Relogio>();
builder.Services.AddSingleton<TravaPorUsuario>();
builder.Services.AddSingleton<FilaSincronizacao>();
builder.Services.AddScoped<Sincronizador>();
builder.Services.AddHostedService<TrabalhadorSincronizacao>();
builder.Services.AddHostedService<SincronizacaoPeriodica>();

// ---------- API ----------
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Caixa Claro API";
    doc.Info.Description = "Previsão de caixa e controle de contas para pequenos negócios, a partir do banco conectado (Open Finance via Pluggy).";
    return Task.CompletedTask;
}));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origens").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Cria o banco de dados na primeira execução (ver docs/DECISOES.md sobre migrações).
using (var escopo = app.Services.CreateScope())
    escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference("/docs", o => o.WithTitle("Caixa Claro API")).AllowAnonymous();
app.MapGet("/", () => Results.Redirect("/docs")).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/saude", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
