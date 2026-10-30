using CaixaClaro.Dominio.Modelos;

namespace CaixaClaro.Dominio.Regras;

/// <summary>
/// Decide sozinho o que cada transação do extrato é, nesta ordem:
/// 1) regra que o dono já confirmou para o favorecido;
/// 2) dinheiro do próprio dono (Pix para o CPF/nome do titular = retirada; vindo dele = aporte);
/// 3) palavras-chave do nome (CEMIG → Energia, DAS → Impostos…);
/// 4) "parece pessoal" (farmácia, iFood…) → pede confirmação;
/// 5) categoria sugerida pelo agregador (só como pista);
/// 6) não reconhecido → "Outros", pede confirmação.
/// </summary>
public static class Classificador
{
    private static readonly (string Categoria, string[] Termos)[] RegrasPalavras =
    [
        ("Energia/água/internet",
        [
            "CEMIG", "ENEL", "CPFL", "LIGHT", "COPEL", "CELESC", "EQUATORIAL", "ENERGISA", "NEOENERGIA", "COELBA", "CELPE", "COSERN", "ELEKTRO", "CEEE", "ENERGIA", "ELETRICIDADE",
            "SABESP", "COPASA", "SANEPAR", "CASAN", "CEDAE", "EMBASA", "CORSAN", "SANEAGO", "COMPESA", "CAGECE", "SANEAMENTO", "AGUA E ESGOTO",
            "VIVO", "CLARO", "TIM", "OI", "NET", "INTERNET", "TELEFONICA", "FIBRA",
        ]),
        ("Impostos", ["DAS", "SIMPLES NACIONAL", "DARF", "RECEITA FEDERAL", "GPS", "INSS", "IPTU", "DAE", "SEFAZ", "ICMS", "ISS", "PREFEITURA", "TRIBUTO", "IMPOSTO", "MEI"]),
        ("Aluguel", ["ALUGUEL", "IMOBILIARIA", "CONDOMINIO", "LOCACAO"]),
        ("Salários", ["SALARIO", "FOLHA", "FGTS", "VALE TRANSPORTE"]),
        ("Fornecedor", ["DISTRIBUIDORA", "DISTRIBUIDOR", "ATACADAO", "ATACADO", "ATACADISTA", "ASSAI", "MAKRO", "ATACAREJO", "INSUMOS", "FORNECEDOR", "INDUSTRIA", "RACOES"]),
        ("Manutenção", ["MANUTENCAO", "CONSERTO", "ASSISTENCIA TECNICA", "FERRAGENS", "FERRAGISTA", "MATERIAL DE CONSTRUCAO", "ELETRICISTA", "ENCANADOR", "REPARO"]),
        ("Outros", ["LIMPEZA", "PAPELARIA", "MATERIAL DE ESCRITORIO", "TARIFA", "CESTA DE SERVICOS", "MENSALIDADE PACOTE"]),
    ];

    private static readonly string[] TermosPessoais =
    [
        "FARMACIA", "DROGARIA", "DROGASIL", "RAIA", "PAGUE MENOS", "IFOOD", "RAPPI", "UBER", "99APP", "NETFLIX", "SPOTIFY", "AMAZON PRIME", "DISNEY", "GLOBOPLAY",
        "HBO", "CINEMA", "ACADEMIA", "SMART FIT", "SHOPPING", "RESTAURANTE", "LANCHONETE", "SUPERMERCADO", "MERCADO", "PADARIA", "PIZZARIA", "HAMBURGUERIA",
    ];

    // Categorias que o agregador sugere (em inglês). Usadas só como pista extra.
    private static readonly (string[] Termos, TipoLancamento Tipo, string Categoria, Confianca Confianca)[] PistasBanco =
    [
        (["electricity", "water", "telecommunication", "internet", "phone"], TipoLancamento.Saida, "Energia/água/internet", Confianca.Alta),
        (["tax"], TipoLancamento.Saida, "Impostos", Confianca.Alta),
        (["rent"], TipoLancamento.Saida, "Aluguel", Confianca.Alta),
        (["salary", "payroll"], TipoLancamento.Saida, "Salários", Confianca.Alta),
        (["pharmacy", "drugstore", "food delivery", "restaurant", "streaming", "entertainment", "leisure", "groceries", "supermarket", "gym", "fitness", "taxi", "ride"],
            TipoLancamento.Retirada, Categorias.Retirada, Confianca.Baixa),
    ];

    /// <summary>Termo curto (DAS, TIM, OI) só vale como palavra inteira; termo longo vale em qualquer parte.</summary>
    private static bool Contem(string texto, IEnumerable<string> termos) =>
        termos.Any(p => $" {texto} ".Contains($" {p} ") || (p.Length > 5 && texto.Contains(p)));

    public static bool EhDono(TransacaoBancaria b, Titular dono)
    {
        var doc = Texto.SoDigitos(b.ContraparteDocumento);
        var docsDono = new[] { dono.Documento, dono.CpfDono }.Select(Texto.SoDigitos).Where(d => d.Length > 0);
        if (doc.Length > 0 && docsDono.Contains(doc)) return true;
        var nome = Texto.SemAcento(b.ContraparteNome).Trim();
        var nomeDono = Texto.SemAcento(dono.Nome).Trim();
        return nome.Length > 0 && nomeDono.Length > 5 && nome == nomeDono;
    }

    public static Classificacao Classificar(TransacaoBancaria b, IReadOnlyDictionary<string, RegraClassificacao> regras, Titular dono)
    {
        if (regras.TryGetValue(Texto.Chave(b), out var regra))
            return new(regra.Tipo, regra.Categoria, Confianca.Alta, "Você já confirmou este favorecido antes");

        var texto = Texto.Limpar($"{b.Descricao} {b.ContraparteNome}");

        if (b.Credito)
        {
            if (EhDono(b, dono)) return new(TipoLancamento.Aporte, Categorias.Aporte, Confianca.Alta, "Dinheiro seu colocado no caixa");
            if (Contem(texto, ["ESTORNO", "DEVOLUCAO", "REEMBOLSO"])) return new(TipoLancamento.Entrada, "Outros", Confianca.Baixa, "Estorno ou devolução");
            if (Contem(texto, ["RENDIMENTO", "JUROS", "REMUNERACAO", "RESGATE"])) return new(TipoLancamento.Entrada, "Outros", Confianca.Alta, "Rendimento");
            return new(TipoLancamento.Entrada, "Vendas", Confianca.Alta, "Recebimento de cliente");
        }

        if (EhDono(b, dono)) return new(TipoLancamento.Retirada, Categorias.Retirada, Confianca.Alta, "Transferência para você");
        if (Contem(texto, ["FATURA"])) return new(TipoLancamento.Saida, Categorias.FaturaCartao, Confianca.Alta, "Pagamento de fatura do cartão");
        foreach (var (categoria, termos) in RegrasPalavras)
            if (Contem(texto, termos)) return new(TipoLancamento.Saida, categoria, Confianca.Alta, "Reconhecido pelo nome");
        if (Contem(texto, TermosPessoais)) return new(TipoLancamento.Retirada, Categorias.Retirada, Confianca.Baixa, "Parece gasto pessoal");

        var cat = (b.CategoriaBanco ?? "").ToLowerInvariant();
        if (cat.Length > 0)
            foreach (var (termos, tipo, categoria, confianca) in PistasBanco)
                if (termos.Any(cat.Contains)) return new(tipo, categoria, confianca, "Categoria informada pelo banco");

        return new(TipoLancamento.Saida, "Outros", Confianca.Baixa, "Não reconhecemos este gasto");
    }
}
