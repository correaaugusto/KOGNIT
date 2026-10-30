namespace Kognit.Dominio.Regras;

public record BoletoLido(decimal? Valor, DateOnly? Vencimento, string Tipo);

/// <summary>
/// Lê a linha digitável de um boleto (padrão FEBRABAN) para cadastrar a conta sem digitar valor e vencimento.
/// - Boleto bancário (47 dígitos): campo 5 = fator de vencimento (4) + valor (10).
/// - Conta de consumo/tributo (48 dígitos, começa com 8): valor nas posições 5–15 do código de barras; sem vencimento padronizado.
/// </summary>
public static class LinhaDigitavel
{
    private static readonly DateOnly BaseFatorAntiga = new(1997, 10, 7);
    // O fator chegou a 9999 em 21/02/2025 e recomeçou em 1000 no dia seguinte.
    private static readonly DateOnly BaseFatorNova = new(2025, 2, 22);

    public static BoletoLido? Ler(string? linha, DateOnly hoje)
    {
        var d = Texto.SoDigitos(linha);
        if (d.Length == 47) return LerBancario(d, hoje);
        if (d.Length == 48 && d[0] == '8') return LerArrecadacao(d);
        return null;
    }

    private static BoletoLido? LerBancario(string d, DateOnly hoje)
    {
        // Cada um dos 3 primeiros campos tem dígito verificador módulo 10.
        if (Modulo10(d[..9]) != d[9] - '0' || Modulo10(d[10..20]) != d[20] - '0' || Modulo10(d[21..31]) != d[31] - '0') return null;
        var fator = int.Parse(d[33..37]);
        var valor = long.Parse(d[37..47]) / 100m;
        return new(valor > 0 ? valor : null, fator == 0 ? null : VencimentoPorFator(fator, hoje), "bancario");
    }

    private static BoletoLido? LerArrecadacao(string d)
    {
        // 4 blocos de 11 dígitos + 1 verificador cada; o código de barras é a junção dos blocos.
        var blocos = Enumerable.Range(0, 4).Select(i => d.Substring(i * 12, 11)).ToArray();
        var verificadores = Enumerable.Range(0, 4).Select(i => d[i * 12 + 11] - '0').ToArray();
        var usaModulo10 = d[2] is '6' or '7';
        for (var i = 0; i < 4; i++)
            if ((usaModulo10 ? Modulo10(blocos[i]) : Modulo11Arrecadacao(blocos[i])) != verificadores[i]) return null;
        var barras = string.Concat(blocos);
        var valorReal = d[2] is '6' or '8';
        var valor = long.Parse(barras[4..15]) / 100m;
        return new(valorReal && valor > 0 ? valor : null, null, "arrecadacao");
    }

    /// <summary>Escolhe o ciclo do fator (antigo ou novo) cuja data fica mais perto de hoje.</summary>
    public static DateOnly VencimentoPorFator(int fator, DateOnly hoje)
    {
        var antiga = BaseFatorAntiga.AddDays(fator);
        var nova = BaseFatorNova.AddDays(fator - 1000);
        return Math.Abs(antiga.DayNumber - hoje.DayNumber) <= Math.Abs(nova.DayNumber - hoje.DayNumber) ? antiga : nova;
    }

    public static int Modulo10(string numero)
    {
        int soma = 0, peso = 2;
        for (var i = numero.Length - 1; i >= 0; i--)
        {
            var p = (numero[i] - '0') * peso;
            soma += p > 9 ? p / 10 + p % 10 : p;
            peso = peso == 2 ? 1 : 2;
        }
        return (10 - soma % 10) % 10;
    }

    public static int Modulo11Arrecadacao(string numero)
    {
        int soma = 0, peso = 2;
        for (var i = numero.Length - 1; i >= 0; i--)
        {
            soma += (numero[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        return resto is 0 or 1 ? 0 : 11 - resto;
    }
}
