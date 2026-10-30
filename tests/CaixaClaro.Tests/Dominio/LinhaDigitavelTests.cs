using CaixaClaro.Dominio.Regras;

namespace CaixaClaro.Tests.Dominio;

public class LinhaDigitavelTests
{
    private static readonly DateOnly Hoje = new(2026, 10, 30);

    /// <summary>Monta uma linha digitável bancária válida (com os dígitos verificadores) a partir das partes.</summary>
    private static string Montar(string campoLivreA, string campoLivreB, string campoLivreC, int fator, long centavos)
    {
        var c1 = "0019" + campoLivreA; // banco 001, moeda 9, 5 dígitos
        var c2 = campoLivreB;          // 10 dígitos
        var c3 = campoLivreC;          // 10 dígitos
        return $"{c1}{LinhaDigitavel.Modulo10(c1)}{c2}{LinhaDigitavel.Modulo10(c2)}{c3}{LinhaDigitavel.Modulo10(c3)}1{fator:D4}{centavos:D10}";
    }

    [Fact]
    public void Le_valor_e_vencimento_do_boleto_bancario_no_ciclo_novo_do_fator()
    {
        // fator 1000 = 22/02/2025; 1000 + 615 dias = 30/10/2026
        var linha = Montar("12345", "6789012345", "6789012345", 1615, 42000);
        var b = LinhaDigitavel.Ler(linha, Hoje)!;
        Assert.Equal((420.00m, new DateOnly(2026, 10, 30), "bancario"), (b.Valor, b.Vencimento, b.Tipo));
    }

    [Fact]
    public void Aceita_pontos_e_espacos_e_rejeita_digito_errado()
    {
        var linha = Montar("12345", "6789012345", "6789012345", 1615, 42000);
        var formatada = $"{linha[..5]}.{linha[5..10]} {linha[10..15]}.{linha[15..21]} {linha[21..26]}.{linha[26..32]} {linha[32]} {linha[33..]}";
        Assert.NotNull(LinhaDigitavel.Ler(formatada, Hoje));
        var errada = linha[..9] + ((linha[9] - '0' + 1) % 10) + linha[10..];
        Assert.Null(LinhaDigitavel.Ler(errada, Hoje));
        Assert.Null(LinhaDigitavel.Ler("123", Hoje));
    }

    [Fact]
    public void Fator_antigo_continua_valido_para_datas_antigas()
    {
        Assert.Equal(new DateOnly(2000, 7, 3), LinhaDigitavel.VencimentoPorFator(1000, new DateOnly(2000, 7, 1)));
    }
}
