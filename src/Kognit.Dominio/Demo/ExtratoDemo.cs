using Kognit.Dominio.Modelos;

namespace Kognit.Dominio.Demo;

/// <summary>
/// Extrato SIMULADO de um pet shop de 1 pessoa (~R$ 8 mil/mês, perfil do "grupo B" da pesquisa),
/// no mesmo formato do extrato real. Usado pelo provedor de demonstração e pelo modo apresentação.
/// A semente é a data: a mesma data gera sempre o mesmo extrato.
/// </summary>
public static class ExtratoDemo
{
    public static readonly Titular DonoPadrao = new("JOAO PEREIRA DA SILVA", "123.456.789-09");

    private static readonly string[] Clientes =
        ["MARIA S OLIVEIRA", "CARLOS A SOUZA", "ANA P LIMA", "BRUNO COSTA", "JULIANA R ALVES", "PEDRO H ROCHA", "FERNANDA MELO", "LUCAS GOMES"];

    private static readonly (string Descricao, string? Nome, string? Documento, decimal Valor, int Dia)[] Fixas =
    [
        ("PIX ENVIADO IMOBILIARIA LAR FELIZ", "IMOBILIARIA LAR FELIZ LTDA", "11.222.333/0001-44", 1200m, 10),
        ("PAGAMENTO BOLETO CEMIG DISTRIBUICAO", null, null, 260m, 15),
        ("DEB AUTOMATICO SABESP", null, null, 65m, 20),
        ("DEB AUTOMATICO VIVO FIBRA", null, null, 75m, 20),
        ("PAGAMENTO DAS SIMPLES NACIONAL", null, null, 76m, 20),
    ];

    /// <summary>Gerador pseudoaleatório com semente (mulberry32), para a demo ser reproduzível.</summary>
    public sealed class Aleatorio(uint semente)
    {
        private uint _a = semente;

        public double Proximo()
        {
            _a += 0x6D2B79F5;
            var t = _a;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return ((t ^ (t >> 14)) >> 0) / 4294967296.0;
        }

        public decimal Entre(double min, double max) => Math.Round((decimal)(min + Proximo() * (max - min)), 2);
    }

    /// <summary>
    /// Gera os últimos 3 meses até <paramref name="hoje"/>. Cada dia tem a própria semente e ids estáveis
    /// ("demo-AAAAMMDD-n"): sincronizar de novo em outro dia não duplica nem muda o passado.
    /// </summary>
    public static List<TransacaoBancaria> Gerar(DateOnly hoje, Titular? dono = null, string prefixoId = "demo")
    {
        dono ??= DonoPadrao;
        var lista = new List<TransacaoBancaria>();
        var inicioMesAtual = new DateOnly(hoje.Year, hoje.Month, 1);

        for (var d = inicioMesAtual.AddMonths(-3); d <= hoje; d = d.AddDays(1))
        {
            var rnd = new Aleatorio((uint)(d.Year * 10000 + d.Month * 100 + d.Day));
            var seq = 0;
            var dia = d;
            void Add(bool credito, decimal valor, string descricao, string? nome = null, string? doc = null) =>
                lista.Add(new($"{prefixoId}-{dia:yyyyMMdd}-{++seq}", dia, Math.Round(valor, 2), credito, descricao, nome, doc));

            var semana = d.DayOfWeek;
            // Sábado é o dia mais forte; meses alternados mais fracos (para aparecer "resultado piorando").
            var fator = (semana == DayOfWeek.Saturday ? 1.5 : 1) * ((d.Year + d.Month) % 2 == 0 ? 1 : 0.9);
            if (semana != DayOfWeek.Sunday)
            {
                Add(true, rnd.Entre(150 * fator, 260 * fator), "VENDA CARTAO DEBITO/CREDITO STONE", "STONE INSTITUICAO DE PAGAMENTO", "16.501.555/0001-57");
                if (rnd.Proximo() < 0.75)
                {
                    var cliente = Clientes[(int)(rnd.Proximo() * Clientes.Length)];
                    Add(true, rnd.Entre(60 * fator, 130 * fator), $"PIX RECEBIDO {cliente}", cliente);
                }
            }
            if (semana == DayOfWeek.Monday)
                Add(false, rnd.Entre(520, 700), "PIX ENVIADO DISTRIBUIDORA PET MAIS LTDA", "DISTRIBUIDORA PET MAIS LTDA", "22.333.444/0001-55");
            if (semana != DayOfWeek.Sunday && rnd.Proximo() < 0.4)
                Add(false, rnd.Entre(120, 280), $"PIX ENVIADO {dono.Nome}", dono.Nome, dono.CpfDono ?? dono.Documento);
            if (rnd.Proximo() < 0.08)
                Add(false, rnd.Entre(25, 85), rnd.Proximo() < 0.5 ? "COMPRA CARTAO DROGARIA SAO PAULO" : "COMPRA CARTAO IFOOD *RESTAURANTE");
            if (rnd.Proximo() < 0.04) Add(false, rnd.Entre(40, 100), "COMPRA CARTAO FERRAGENS SAO JORGE");
            if (rnd.Proximo() < 0.05) Add(false, rnd.Entre(30, 80), "COMPRA CARTAO PRODUTOS DE LIMPEZA ALVORADA");
            foreach (var f in Fixas.Where(f => f.Dia == d.Day)) Add(false, f.Valor, f.Descricao, f.Nome, f.Documento);
            // Retirada grande no início do mês (o app deve alertar "retirou mais do que lucrou")
            if (d.Day == 6) Add(false, 1400m, $"PIX ENVIADO {dono.Nome}", dono.Nome, dono.CpfDono ?? dono.Documento);
            // Gastos eventuais (não são contas fixas): a cada 3 ou 4 meses
            if (d.Day == 4 && d.Month % 3 == 1) Add(false, 480m, "PIX ENVIADO ASSISTENCIA TECNICA ELETROPET", "ASSISTENCIA TECNICA ELETROPET", "33.444.555/0001-66");
            if (d.Day == 25 && d.Month % 4 == 2) Add(false, 189.90m, "PIX ENVIADO AUTO CENTER BOA VIAGEM", "AUTO CENTER BOA VIAGEM", "44.555.666/0001-77");
        }
        return lista;
    }
}

public enum TipoSimulacao { PixRecebido, PixFornecedor, BoletoPago, Retirada, GastoPessoal }

/// <summary>Modo apresentação: cria uma transação no formato do extrato, marcada como simulada.</summary>
public static class Simulador
{
    private static readonly string[] Clientes = ["MARIA S OLIVEIRA", "CARLOS A SOUZA", "ANA P LIMA", "BRUNO COSTA", "JULIANA R ALVES", "PEDRO H ROCHA"];
    private static readonly string[] Pessoais = ["COMPRA CARTAO IFOOD *RESTAURANTE", "COMPRA CARTAO DROGARIA SAO PAULO", "COMPRA CARTAO NETFLIX.COM"];

    /// <summary>Devolve null quando não há o que simular (ex.: nenhuma conta para pagar nos próximos dias).</summary>
    public static TransacaoBancaria? Criar(TipoSimulacao tipo, EstadoFinanceiro estado, DateOnly hoje, decimal? valor = null, string? nome = null)
    {
        var rnd = Random.Shared;
        var id = $"sim-{Guid.NewGuid():N}";
        decimal Entre(int min, int max) => Math.Round(min + (decimal)rnd.NextDouble() * (max - min), 2);
        switch (tipo)
        {
            case TipoSimulacao.PixRecebido:
            {
                var quem = nome?.ToUpperInvariant() ?? Clientes[rnd.Next(Clientes.Length)];
                return new(id, hoje, valor ?? Entre(60, 280), true, $"PIX RECEBIDO {quem}", quem, Simulado: true);
            }
            case TipoSimulacao.PixFornecedor:
            {
                var quem = nome?.ToUpperInvariant() ?? "DISTRIBUIDORA PET MAIS LTDA";
                return new(id, hoje, valor ?? Entre(300, 700), false, $"PIX ENVIADO {quem}", quem, Simulado: true);
            }
            case TipoSimulacao.BoletoPago:
            {
                // Pagamento antecipado é reconhecido: até 30 dias com favorecido conhecido, 10 sem.
                var conta = estado.Contas
                    .Where(c => !c.Paga && c.Vencimento <= hoje.AddDays(c.Chave is null && c.Origem != OrigemConta.FaturaCartao ? 10 : 30))
                    .MinBy(c => c.Vencimento);
                if (conta is null) return null;
                var doc = conta.Chave?.StartsWith("doc:") == true ? conta.Chave[4..] : null;
                var descricao = conta.Origem == OrigemConta.FaturaCartao ? "PAGAMENTO FATURA CARTAO" : $"PAGAMENTO BOLETO {conta.Descricao}";
                return new(id, hoje, conta.Valor, false, descricao, doc is null ? null : conta.Descricao, doc, Simulado: true);
            }
            case TipoSimulacao.Retirada:
            {
                var titular = estado.Titular;
                return new(id, hoje, valor ?? Entre(150, 400), false, $"PIX ENVIADO {titular.Nome}", titular.Nome ?? "TITULAR DA CONTA",
                    titular.CpfDono ?? titular.Documento, Simulado: true);
            }
            case TipoSimulacao.GastoPessoal:
                return new(id, hoje, valor ?? Entre(25, 90), false, Pessoais[rnd.Next(Pessoais.Length)], Simulado: true);
            default:
                return null;
        }
    }

    /// <summary>3 meses de histórico (passado: não muda o saldo de hoje).</summary>
    public static List<TransacaoBancaria> Historico(EstadoFinanceiro estado, DateOnly hoje)
    {
        if (estado.Lancamentos.Any(l => l.IdExterno?.StartsWith("sim-hist-") == true)) return [];
        var dono = new Titular(estado.Titular.Nome ?? "TITULAR DA CONTA", estado.Titular.Documento, estado.Titular.CpfDono);
        return ExtratoDemo.Gerar(hoje.AddDays(-1), dono, "sim-hist")
            .Select(t => t with { Simulado = true, Historico = true })
            .ToList();
    }
}
