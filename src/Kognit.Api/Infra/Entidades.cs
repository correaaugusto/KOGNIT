namespace Kognit.Api.Infra;

public class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string NomeNegocio { get; set; } = "Meu negócio";
    /// <summary>CPF pessoal do dono (opcional): reconhece retiradas quando a conta conectada é PJ.</summary>
    public string? Cpf { get; set; }
    /// <summary>Dinheiro vivo que o dono tinha quando começou a usar o app.</summary>
    public decimal CaixaDinheiro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public enum ProvedorBancario { Pluggy, Demo }

public enum StatusConexao { Conectando, Conectado, Atualizando, Erro }

/// <summary>Um banco conectado pelo usuário (na Pluggy, um "item").</summary>
public class ConexaoBancaria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public ProvedorBancario Provedor { get; set; }
    /// <summary>Id do item na Pluggy (ou "demo-…").</summary>
    public string ItemId { get; set; } = "";
    public string NomeBanco { get; set; } = "";
    public StatusConexao Status { get; set; } = StatusConexao.Conectando;
    public string? Mensagem { get; set; }
    public string? Titular { get; set; }
    public string? DocumentoTitular { get; set; }
    public DateTime? UltimaSincronizacao { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Conta dentro do banco conectado (corrente/poupança ou cartão de crédito), com o saldo informado pelo banco.</summary>
public class ContaBancaria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid ConexaoId { get; set; }
    public string IdExterno { get; set; } = "";
    /// <summary>"BANK" (corrente/poupança) ou "CREDIT" (cartão).</summary>
    public string Tipo { get; set; } = "BANK";
    public string Nome { get; set; } = "";
    public decimal Saldo { get; set; }
    public DateTime AtualizadaEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Conta fixa descoberta que o dono apagou: não recriar.</summary>
public class SerieIgnorada
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string Serie { get; set; } = "";
}
