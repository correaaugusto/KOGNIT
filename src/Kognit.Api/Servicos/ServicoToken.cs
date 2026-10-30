using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kognit.Api.Infra;
using Microsoft.IdentityModel.Tokens;

namespace Kognit.Api.Servicos;

public class JwtOpcoes
{
    /// <summary>Segredo para assinar os tokens (no mínimo 20 caracteres). Em produção: variável de ambiente Jwt__Chave
    /// (no Render, gerada sozinha pelo render.yaml).</summary>
    public string Chave { get; set; } = "";

    public const int TamanhoMinimoChave = 20;
    public string Emissor { get; set; } = "kognit-api";
    public string Publico { get; set; } = "kognit-app";
    public int DiasValidade { get; set; } = 7;

    /// <summary>A chave HMAC-SHA256 precisa de 256 bits: o segredo (de qualquer tamanho) passa por SHA-256,
    /// que sempre devolve 32 bytes. Assim um segredo gerado pelo provedor de hospedagem nunca é "curto demais".</summary>
    public SymmetricSecurityKey ChaveAssinatura() => new(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Chave)));
}

public class ServicoToken(Microsoft.Extensions.Options.IOptions<JwtOpcoes> opcoes, TimeProvider tempo)
{
    public (string Token, DateTime ExpiraEm) Emitir(Usuario u)
    {
        var o = opcoes.Value;
        var expira = tempo.GetUtcNow().UtcDateTime.AddDays(o.DiasValidade);
        var token = new JwtSecurityToken(
            o.Emissor,
            o.Publico,
            [new Claim(JwtRegisteredClaimNames.Sub, u.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, u.Email), new Claim("name", u.Nome)],
            expires: expira,
            signingCredentials: new SigningCredentials(o.ChaveAssinatura(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}

public static class UsuarioLogado
{
    /// <summary>Id do usuário do token. Todas as consultas filtram por ele: ninguém vê dados de outro.</summary>
    public static Guid Id(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new UnauthorizedAccessException());
}
