using CaixaClaro.Api.Infra;
using CaixaClaro.Dominio.Modelos;

namespace CaixaClaro.Api.Pluggy;

/// <summary>Transação da Pluggy → formato do domínio (independente do agregador).</summary>
public static class NormalizadorPluggy
{
    public static TransacaoBancaria Normalizar(PluggyTransacao t)
    {
        var credito = t.Type == "CREDIT";
        // Quem pagou (crédito) ou quem recebeu (débito); senão, o estabelecimento da compra.
        var parte = credito ? t.PaymentData?.Payer : t.PaymentData?.Receiver;
        string? nome = parte?.Name, documento = parte?.DocumentNumber?.Value;
        if (nome is null && documento is null && t.Merchant is not null)
        {
            nome = t.Merchant.Name ?? t.Merchant.BusinessName;
            documento = t.Merchant.Cnpj;
        }
        return new TransacaoBancaria(
            IdExterno: t.Id,
            // O banco manda horário UTC: uma compra às 22h em Brasília não pode cair no dia seguinte.
            Data: Relogio.ParaDataLocal(t.Date),
            Valor: Math.Round(Math.Abs(t.Amount), 2),
            Credito: credito,
            Descricao: t.Description ?? t.DescriptionRaw ?? "",
            ContraparteNome: nome,
            ContraparteDocumento: documento,
            CategoriaBanco: t.Category,
            Pendente: t.Status == "PENDING");
    }
}
