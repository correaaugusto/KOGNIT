using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kognit.Api.Infra;
using Microsoft.IdentityModel.Tokens;

namespace Kognit.Api.Servicos;

public class JwtOpcoes
{
    /// <summary>Chave secreta para assinar os tokens (no mínimo 32 caracteres). Em produção: variável de ambiente Jwt__Chave.</summary>
    public string Chave { get; set; } = "";
    public string Emissor { get; set; } = "kognit-api";
    public string Publico { get; set; } = "kognit-app";
    public int DiasValidade { get; set; } = 7;

    public SymmetricSecurityKey ChaveAssinatura() => new(Encoding.UTF8.GetBytes(Chave));
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
