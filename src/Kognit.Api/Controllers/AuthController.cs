using Kognit.Api.Contratos;
using Kognit.Api.Infra;
using Kognit.Api.Servicos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Kognit.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class AuthController(AppDbContext db, ServicoToken tokens, IPasswordHasher<Usuario> hasher) : ControllerBase
{
    /// <summary>Cria a conta do dono do negócio e já devolve o token de acesso.</summary>
    [HttpPost("registrar")]
    public async Task<ActionResult<TokenResponse>> Registrar(RegistrarRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            return Problem(statusCode: 409, title: "Já existe uma conta com este e-mail.");
        var usuario = new Usuario
        {
            Nome = req.Nome.Trim(),
            Email = email,
            NomeNegocio = string.IsNullOrWhiteSpace(req.NomeNegocio) ? "Meu negócio" : req.NomeNegocio.Trim(),
            Cpf = string.IsNullOrWhiteSpace(req.Cpf) ? null : Dominio.Regras.Texto.SoDigitos(req.Cpf),
        };
        usuario.SenhaHash = hasher.HashPassword(usuario, req.Senha);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        var (token, expira) = tokens.Emitir(usuario);
        return Created("/api/usuario", new TokenResponse(token, expira, UsuarioDto.De(usuario)));
    }

    [HttpPost("entrar")]
    public async Task<ActionResult<TokenResponse>> Entrar(EntrarRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Email == email, ct);
        // Mesma mensagem para e-mail inexistente e senha errada: não revela quem tem conta.
        if (usuario is null || hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, req.Senha) == PasswordVerificationResult.Failed)
            return Problem(statusCode: 401, title: "E-mail ou senha incorretos.");
        var (token, expira) = tokens.Emitir(usuario);
        return new TokenResponse(token, expira, UsuarioDto.De(usuario));
    }
}

[ApiController]
[Route("api/usuario")]
public class UsuarioController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UsuarioDto>> Obter(CancellationToken ct) =>
        UsuarioDto.De(await db.Usuarios.SingleAsync(u => u.Id == User.Id(), ct));

    /// <summary>Nome do negócio, CPF pessoal (reconhece retiradas em conta PJ) e dinheiro vivo inicial.</summary>
    [HttpPut]
    public async Task<ActionResult<UsuarioDto>> Atualizar(AtualizarUsuarioRequest req, CancellationToken ct)
    {
        var u = await db.Usuarios.SingleAsync(x => x.Id == User.Id(), ct);
        if (req.Nome is not null) u.Nome = req.Nome.Trim();
        if (req.NomeNegocio is not null) u.NomeNegocio = req.NomeNegocio.Trim();
        if (req.Cpf is not null) u.Cpf = Dominio.Regras.Texto.SoDigitos(req.Cpf) is { Length: > 0 } cpf ? cpf : null;
        if (req.CaixaDinheiro is not null) u.CaixaDinheiro = req.CaixaDinheiro.Value;
        await db.SaveChangesAsync(ct);
        return UsuarioDto.De(u);
    }
}
