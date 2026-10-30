using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kognit.Dominio.Modelos;

namespace Kognit.Dominio.Regras;

/// <summary>Limpeza das descrições do extrato e formatação de valores em reais.</summary>
public static partial class Texto
{
    public static string SemAcento(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    // Prefixos que o banco coloca antes do nome ("PIX ENVIADO", "PAGAMENTO BOLETO"...).
    private static readonly Regex[] Prefixos =
    [
        new(@"^PIX\s+(ENVIADO|RECEBIDO|TRANSF\w*|ENV|REC|QR\s*CODE)?\b"),
        new(@"^TRANSF(ERENCIA)?\b(\s+PIX)?(\s+(ENVIADA|RECEBIDA|ENTRE CONTAS))?"),
        new(@"^PAG(AMENTO|TO)?\b(\s+DE)?(\s+(BOLETO|CONTA|TITULO|CONVENIO))?"),
        new(@"^COMPRA\b(\s+(NO|COM))?(\s+CART(AO|\.))?(\s+(DEBITO|CREDITO|DEB|CRED))?"),
        new(@"^DEB(ITO)?\b(\s+(AUTOMATICO|AUT\.?))?"),
        new(@"^(TED|DOC)\b(\s+(ENVIADA|RECEBIDA|ENVIADO|RECEBIDO))?"),
        new(@"^(BOLETO|VENDA)\b"),
    ];

    public static string Limpar(string? texto)
    {
        var t = SemAcento(texto);
        t = DataCurta().Replace(t, " ");
        t = Simbolos().Replace(t, " ");
        t = Numeros().Replace(t, " ");
        return Espacos().Replace(t, " ").Trim();
    }

    /// <summary>Nome "limpo" do favorecido/pagador, sem "PIX ENVIADO", datas e números.</summary>
    public static string NomeBase(TransacaoBancaria b)
    {
        if (!string.IsNullOrWhiteSpace(b.ContraparteNome)) return Limpar(b.ContraparteNome);
        var t = Limpar(b.Descricao);
        for (var i = 0; i < 3; i++)
            foreach (var p in Prefixos) t = p.Replace(t, "").Trim();
        return t.Length > 0 ? t : Limpar(b.Descricao);
    }

    private static readonly HashSet<string> Minusculas = ["de", "da", "do", "das", "dos", "e"];

    public static string NomeAmigavel(TransacaoBancaria b)
    {
        var palavras = NomeBase(b).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', palavras.Select((p, i) =>
            i > 0 && Minusculas.Contains(p) ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Identifica o mesmo favorecido entre meses: CPF/CNPJ quando o banco informa,
    /// senão as 3 primeiras palavras do nome limpo.</summary>
    public static string Chave(TransacaoBancaria b)
    {
        var doc = SoDigitos(b.ContraparteDocumento);
        if (doc.Length > 0) return $"doc:{doc}";
        return "txt:" + string.Join(' ', NomeBase(b).Split(' ').Take(3));
    }

    public static string SoDigitos(string? texto) => texto is null ? "" : NaoDigitos().Replace(texto, "");

    /// <summary>Como o dinheiro passou, deduzido da descrição do banco.</summary>
    public static string? MeioPagamento(TransacaoBancaria b)
    {
        var t = SemAcento(b.Descricao);
        if (b.Credito && Maquininha().IsMatch(t)) return "Maquininha";
        if (Pix().IsMatch(t)) return "Pix";
        if (Boleto().IsMatch(t)) return "Boleto";
        if (DebitoAutomatico().IsMatch(t)) return "Débito automático";
        if (Cartao().IsMatch(t)) return "Cartão";
        if (Transferencia().IsMatch(t)) return "Transferência";
        return null;
    }

    private static readonly CultureInfo PtBr = CriarCulturaPtBr();

    // Formatação própria (não depende dos dados de idioma do sistema operacional, ex.: imagens Docker enxutas).
    private static CultureInfo CriarCulturaPtBr()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.NumberDecimalSeparator = ",";
        return c;
    }

    /// <summary>"R$ 1.200" (sem centavos) — usado nos textos dos alertas.</summary>
    public static string ReaisCurto(decimal v)
    {
        var arredondado = Math.Round(v, 0, MidpointRounding.AwayFromZero);
        var texto = "R$ " + Math.Abs(arredondado).ToString("#,0", PtBr);
        return arredondado < 0 ? "−" + texto : texto;
    }

    public static string Reais(decimal v)
    {
        var texto = "R$ " + Math.Abs(v).ToString("#,0.00", PtBr);
        return v < 0 ? "−" + texto : texto;
    }

    public static string DiaMes(DateOnly d) => d.ToString("dd/MM", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d{2}/\d{2}(/\d{2,4})?")] private static partial Regex DataCurta();
    [GeneratedRegex(@"[*#/\\.,:;|_-]+")] private static partial Regex Simbolos();
    [GeneratedRegex(@"\b\d+\b")] private static partial Regex Numeros();
    [GeneratedRegex(@"\s+")] private static partial Regex Espacos();
    [GeneratedRegex(@"\D")] private static partial Regex NaoDigitos();
    [GeneratedRegex(@"VENDA|STONE|CIELO|REDE |GETNET|PAGSEGURO|MAQUININHA")] private static partial Regex Maquininha();
    [GeneratedRegex(@"\bPIX\b")] private static partial Regex Pix();
    [GeneratedRegex(@"BOLETO|TITULO|\bDAS\b|SIMPLES NACIONAL|DARF|CONVENIO")] private static partial Regex Boleto();
    [GeneratedRegex(@"DEB(ITO)? AUT")] private static partial Regex DebitoAutomatico();
    [GeneratedRegex(@"CART(AO|\.)|COMPRA")] private static partial Regex Cartao();
    [GeneratedRegex(@"\bTED\b|\bDOC\b|TRANSF")] private static partial Regex Transferencia();
}
