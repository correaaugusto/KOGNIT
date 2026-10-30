using Kognit.Dominio.Modelos;
using static Kognit.Dominio.Regras.Texto;

namespace Kognit.Dominio.Regras;

public record Totais(decimal Entrou, decimal Saiu, decimal Retiradas)
{
    public decimal Lucro => Entrou - Saiu;
}

public record Medias(decimal Entrada, decimal GastoVariavel, decimal Retirada, int Dias);

public record ContaPrevista(Conta Conta, DateOnly Vencimento, bool Prevista);

public record PontoProjecao(DateOnly Data, decimal Saldo, IReadOnlyList<ContaPrevista> Contas);

public record Projecao(IReadOnlyList<PontoProjecao> Pontos, Medias Medias, decimal RetiradaDia, IReadOnlyList<ContaPrevista> Contas);

public record Ajustes(decimal MenosRetiradaMes = 0, decimal MaisEntradaMes = 0);

public enum CorSemaforo { Verde, Amarelo, Vermelho }

public record Semaforo(CorSemaforo Cor, string Titulo, string Texto, PontoProjecao Menor, PontoProjecao? PrimeiroNegativo);

public enum NivelAlerta { Alto, Medio, Info }

public record Alerta(string Id, NivelAlerta Nivel, string Titulo, string Texto, string? Sugestao = null);

public record GastoCategoria(string Categoria, decimal Valor);

public record TotaisMes(int Ano, int Mes, Totais Totais);

/// <summary>Regras financeiras do KOGNIT. Funções puras sobre o estado do usuário.</summary>
public static class Financas
{
    private const int JanelaMedia = 30;
    private const decimal LimiteGastoForaPadrao = 1.3m;

    public static decimal Sinal(Lancamento l) => l.Tipo is TipoLancamento.Entrada or TipoLancamento.Aporte ? l.Valor : -l.Valor;

    /// <summary>
    /// Saldo = saldo das contas bancárias (vem do banco) + dinheiro vivo inicial + anotações manuais
    /// + movimentos simulados "ao vivo". O que veio do extrato já está dentro do saldo do banco.
    /// </summary>
    public static decimal SaldoAtual(EstadoFinanceiro e, DateOnly hoje) =>
        e.SaldoBancos + e.CaixaDinheiro + e.Lancamentos
            .Where(l => l.Data <= hoje && (l.Origem == OrigemLancamento.Manual || (l.Simulado && !l.Historico)))
            .Sum(Sinal);

    public static Totais CalcularTotais(IEnumerable<Lancamento> lancamentos, DateOnly de, DateOnly ate)
    {
        var periodo = lancamentos.Where(l => l.Data >= de && l.Data <= ate).ToList();
        return new(
            periodo.Where(l => l.Tipo == TipoLancamento.Entrada).Sum(l => l.Valor),
            periodo.Where(l => l.Tipo == TipoLancamento.Saida).Sum(l => l.Valor),
            periodo.Where(l => l.Tipo == TipoLancamento.Retirada).Sum(l => l.Valor));
    }

    public static DateOnly InicioDoMes(DateOnly d) => new(d.Year, d.Month, 1);

    public static DateOnly FimDoMes(DateOnly d) => InicioDoMes(d).AddMonths(1).AddDays(-1);

    /// <summary>Médias diárias dos últimos 30 dias (ou desde o primeiro lançamento).
    /// Pagamentos de contas não entram: já são projetados pelo vencimento.</summary>
    public static Medias MediasDiarias(EstadoFinanceiro e, DateOnly hoje)
    {
        var lancs = e.Lancamentos.Where(l => l.Data <= hoje).ToList();
        if (lancs.Count == 0) return new(0, 0, 0, 0);
        var primeiro = lancs.Min(l => l.Data);
        var dias = Math.Min(JanelaMedia, hoje.DayNumber - primeiro.DayNumber + 1);
        var inicio = hoje.AddDays(-(dias - 1));
        var janela = lancs.Where(l => l.Data >= inicio).ToList();
        return new(
            janela.Where(l => l.Tipo == TipoLancamento.Entrada).Sum(l => l.Valor) / dias,
            janela.Where(l => l.Tipo == TipoLancamento.Saida && !l.EhConta).Sum(l => l.Valor) / dias,
            janela.Where(l => l.Tipo == TipoLancamento.Retirada).Sum(l => l.Valor) / dias,
            dias);
    }

    /// <summary>Contas em aberto até a data. Recorrentes se repetem a cada mês ("previstas").</summary>
    public static List<ContaPrevista> ContasEmAberto(IEnumerable<Conta> contas, DateOnly ate)
    {
        var lista = new List<ContaPrevista>();
        foreach (var c in contas.Where(c => !c.Paga))
        {
            var venc = c.Vencimento;
            var n = 0;
            while (venc <= ate)
            {
                lista.Add(new(c, venc, n > 0));
                if (!c.Recorrente) break;
                venc = c.Vencimento.AddMonths(++n);
            }
        }
        return lista.OrderBy(c => c.Vencimento).ToList();
    }

    /// <summary>saldo(d) = saldo(d−1) + entradas/dia − gastos/dia − retiradas/dia − contas que vencem em d.
    /// Contas atrasadas ou que vencem hoje saem já no dia 0.</summary>
    public static Projecao Projetar(EstadoFinanceiro e, DateOnly hoje, int dias = 30, Ajustes? ajustes = null)
    {
        ajustes ??= new();
        var m = MediasDiarias(e, hoje);
        var retiradaDia = Math.Max(0, m.Retirada - ajustes.MenosRetiradaMes / 30);
        var liquidoDia = m.Entrada + ajustes.MaisEntradaMes / 30 - m.GastoVariavel - retiradaDia;
        var contas = ContasEmAberto(e.Contas, hoje.AddDays(dias));

        var saldo = SaldoAtual(e, hoje);
        var pontos = new List<PontoProjecao>();
        for (var i = 0; i <= dias; i++)
        {
            var data = hoje.AddDays(i);
            var doDia = contas.Where(c => i == 0 ? c.Vencimento <= data : c.Vencimento == data).ToList();
            if (i > 0) saldo += liquidoDia;
            saldo -= doDia.Sum(c => c.Conta.Valor);
            pontos.Add(new(data, Math.Round(saldo, 2), doDia));
        }
        return new(pontos, m, retiradaDia, contas);
    }

    /// <summary>VERMELHO: saldo projetado fica negativo. AMARELO: caixa de hoje não cobre as contas dos
    /// próximos 7 dias, ou no pior dia sobra menos que 1 semana de gastos + retiradas. VERDE: o resto.</summary>
    public static Semaforo CalcularSemaforo(EstadoFinanceiro e, DateOnly hoje, Projecao? proj = null)
    {
        proj ??= Projetar(e, hoje);
        var negativo = proj.Pontos.FirstOrDefault(p => p.Saldo < 0);
        var menor = proj.Pontos.MinBy(p => p.Saldo)!;
        var contas7 = proj.Contas.Where(c => c.Vencimento <= hoje.AddDays(7)).Sum(c => c.Conta.Valor);
        var saldo = SaldoAtual(e, hoje);

        if (negativo is not null)
            return new(CorSemaforo.Vermelho, $"Vai faltar dinheiro em {DiaMes(negativo.Data)}",
                $"No pior dia ({DiaMes(menor.Data)}) o caixa fica em {ReaisCurto(menor.Saldo)}.", menor, negativo);
        if (saldo < contas7)
            return new(CorSemaforo.Amarelo, "Caixa apertado",
                $"Você tem {ReaisCurto(saldo)} hoje e {ReaisCurto(contas7)} em contas nos próximos 7 dias.", menor, null);
        var reservaSemana = (proj.Medias.GastoVariavel + proj.RetiradaDia) * 7;
        if (menor.Saldo < reservaSemana)
            return new(CorSemaforo.Amarelo, "Caixa apertado",
                $"No pior dia ({DiaMes(menor.Data)}) sobram só {ReaisCurto(menor.Saldo)}, menos que uma semana dos seus gastos ({ReaisCurto(reservaSemana)}).", menor, null);
        return new(CorSemaforo.Verde, "Caixa cobre tudo",
            $"Pelo seu ritmo atual, o menor saldo nos próximos 30 dias é {ReaisCurto(menor.Saldo)}.", menor, null);
    }

    public static List<Alerta> CalcularAlertas(EstadoFinanceiro e, DateOnly hoje)
    {
        var lista = new List<Alerta>();
        var proj = Projetar(e, hoje);
        var amanha = hoje.AddDays(1);
        var doMes = CalcularTotais(e.Lancamentos, InicioDoMes(hoje), hoje);

        var atrasadas = e.Contas.Where(c => !c.Paga && c.Vencimento < hoje).ToList();
        if (atrasadas.Count > 0)
            lista.Add(new("atrasada", NivelAlerta.Alto, atrasadas.Count == 1 ? "Conta atrasada" : $"{atrasadas.Count} contas atrasadas",
                string.Join(" · ", atrasadas.Select(c => $"{c.Descricao} {ReaisCurto(c.Valor)} (venceu {DiaMes(c.Vencimento)})"))));
        var venceHoje = e.Contas.Where(c => !c.Paga && c.Vencimento == hoje).ToList();
        if (venceHoje.Count > 0)
            lista.Add(new("hoje", NivelAlerta.Alto, "Vence hoje", string.Join(" · ", venceHoje.Select(c => $"{c.Descricao} {ReaisCurto(c.Valor)}"))));
        var venceAmanha = proj.Contas.Where(c => c.Vencimento == amanha).ToList();
        if (venceAmanha.Count > 0)
            lista.Add(new("amanha", NivelAlerta.Medio, "Amanhã vence", string.Join(" · ", venceAmanha.Select(c => $"{c.Conta.Descricao} {ReaisCurto(c.Conta.Valor)}"))));

        var sem = CalcularSemaforo(e, hoje, proj);
        if (sem.Cor == CorSemaforo.Vermelho)
        {
            var deficit = -sem.Menor.Saldo;
            var maiorConta = proj.Contas.Where(c => c.Vencimento <= sem.PrimeiroNegativo!.Data).MaxBy(c => c.Conta.Valor);
            var sugestoes = new List<string>();
            if (proj.Medias.Retirada * 30 >= deficit) sugestoes.Add($"reduzir suas retiradas em {ReaisCurto(deficit)} neste mês");
            if (maiorConta is not null) sugestoes.Add($"negociar o vencimento de \"{maiorConta.Conta.Descricao}\" ({ReaisCurto(maiorConta.Conta.Valor)})");
            lista.Add(new("faltar", NivelAlerta.Alto, "O caixa vai faltar",
                $"No dia {DiaMes(sem.PrimeiroNegativo!.Data)} o caixa pode ficar negativo. No pior dia ({DiaMes(sem.Menor.Data)}) faltam {ReaisCurto(deficit)}.",
                sugestoes.Count > 0 ? $"Sugestão: {string.Join(" ou ", sugestoes)}." : null));
        }
        else if (sem.Cor == CorSemaforo.Amarelo)
        {
            lista.Add(new("apertado", NivelAlerta.Medio, sem.Titulo, sem.Texto));
        }

        if (doMes.Retiradas > 0 && doMes.Retiradas > doMes.Lucro)
            lista.Add(new("retirada", NivelAlerta.Alto, "Você retirou mais do que o negócio lucrou",
                $"Neste mês o negócio lucrou {ReaisCurto(doMes.Lucro)}, mas você retirou {ReaisCurto(doMes.Retiradas)} para uso pessoal.",
                $"A diferença de {ReaisCurto(doMes.Retiradas - doMes.Lucro)} está saindo do caixa da empresa."));

        foreach (var (categoria, atual, media) in GastosForaDoPadrao(e.Lancamentos, hoje))
            lista.Add(new($"padrao-{categoria}", NivelAlerta.Medio, $"Gasto alto com {categoria}",
                $"Neste mês já foram {ReaisCurto(atual)}, contra uma média de {ReaisCurto(media)} nos meses anteriores (+{Math.Round((atual / media - 1) * 100)}%)."));

        var mesAnterior = InicioDoMes(hoje).AddMonths(-1);
        if (e.Lancamentos.Any(l => InicioDoMes(l.Data) == mesAnterior))
        {
            var anterior = CalcularTotais(e.Lancamentos, mesAnterior, mesAnterior.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(mesAnterior.Year, mesAnterior.Month)) - 1));
            if (doMes.Lucro < anterior.Lucro)
                lista.Add(new("piorando", NivelAlerta.Info, "Resultado piorando",
                    $"Até hoje o lucro do mês é {ReaisCurto(doMes.Lucro)}. No mesmo período do mês passado era {ReaisCurto(anterior.Lucro)}."));
        }

        return lista.OrderBy(a => a.Nivel).ToList();
    }

    public static List<GastoCategoria> GastosPorCategoria(IEnumerable<Lancamento> lancamentos, DateOnly de, DateOnly ate) =>
        lancamentos.Where(l => l.Tipo == TipoLancamento.Saida && l.Data >= de && l.Data <= ate)
            .GroupBy(l => l.Categoria)
            .Select(g => new GastoCategoria(g.Key, g.Sum(l => l.Valor)))
            .OrderByDescending(g => g.Valor)
            .ToList();

    /// <summary>Categoria no mês &gt; 130% da média dos até 3 meses anteriores com movimento.</summary>
    public static List<(string Categoria, decimal Atual, decimal Media)> GastosForaDoPadrao(IReadOnlyCollection<Lancamento> lancamentos, DateOnly hoje)
    {
        var inicio = InicioDoMes(hoje);
        var atual = GastosPorCategoria(lancamentos, inicio, hoje);
        var anteriores = Enumerable.Range(1, 3)
            .Select(i => inicio.AddMonths(-i))
            .Where(m => lancamentos.Any(l => InicioDoMes(l.Data) == m))
            .Select(m => GastosPorCategoria(lancamentos, m, FimDoMes(m)))
            .ToList();
        if (anteriores.Count == 0) return [];
        var resultado = new List<(string, decimal, decimal)>();
        foreach (var g in atual)
        {
            var media = anteriores.Sum(a => a.FirstOrDefault(x => x.Categoria == g.Categoria)?.Valor ?? 0) / anteriores.Count;
            if (media > 0 && g.Valor > media * LimiteGastoForaPadrao) resultado.Add((g.Categoria, g.Valor, media));
        }
        return resultado;
    }

    public static List<TotaisMes> EvolucaoMensal(IEnumerable<Lancamento> lancamentos, DateOnly hoje, int meses = 4)
    {
        var lista = lancamentos.ToList();
        return Enumerable.Range(0, meses).Reverse()
            .Select(i => InicioDoMes(hoje).AddMonths(-i))
            .Select(m => new TotaisMes(m.Year, m.Month, CalcularTotais(lista, m, FimDoMes(m))))
            .ToList();
    }
}
