using CaixaClaro.Dominio.Modelos;

namespace CaixaClaro.Dominio.Regras;

/// <summary>
/// Transforma o extrato em lançamentos organizados, sem o dono digitar:
/// importa sem duplicar, classifica, descobre contas fixas e dá baixa nas contas pagas.
/// </summary>
public static class ProcessadorExtrato
{
    /// <summary>Só pede confirmação ("Confira") do que é recente.</summary>
    public const int DiasParaRevisar = 30;

    /// <summary>Junta as transações ao estado. Devolve os lançamentos criados.</summary>
    public static List<Lancamento> Importar(EstadoFinanceiro estado, IEnumerable<TransacaoBancaria> transacoes, DateOnly hoje, Guid? conexaoId = null)
    {
        var jaTem = estado.Lancamentos.Where(l => l.IdExterno is not null).Select(l => l.IdExterno!).ToHashSet();
        var novos = new List<Lancamento>();
        foreach (var b in transacoes)
        {
            if (b.Pendente || !jaTem.Add(b.IdExterno)) continue;
            var c = Classificador.Classificar(b, estado.Regras, estado.Titular);
            novos.Add(new Lancamento
            {
                UsuarioId = estado.UsuarioId,
                ConexaoId = conexaoId,
                IdExterno = b.IdExterno,
                Origem = OrigemLancamento.Banco,
                Data = b.Data,
                Valor = b.Valor,
                Tipo = c.Tipo,
                Categoria = c.Categoria,
                Descricao = Texto.NomeAmigavel(b),
                DescricaoBanco = b.Descricao,
                Meio = Texto.MeioPagamento(b),
                Chave = Texto.Chave(b),
                Motivo = c.Motivo,
                Revisar = c.Confianca == Confianca.Baixa && hoje.DayNumber - b.Data.DayNumber <= DiasParaRevisar,
                Simulado = b.Simulado,
                Historico = b.Historico,
            });
        }
        estado.Lancamentos.AddRange(novos);
        DetectarContasRecorrentes(estado);
        ConciliarContas(estado);
        return novos;
    }

    /// <summary>
    /// Mesmo favorecido pago 1 vez por mês, em pelo menos 2 meses seguidos, com valor parecido (±20%)
    /// e dia parecido: é conta fixa. Cria a próxima, com vencimento previsto.
    /// </summary>
    public static void DetectarContasRecorrentes(EstadoFinanceiro estado)
    {
        var grupos = estado.Lancamentos
            .Where(l => l.Origem == OrigemLancamento.Banco && l.Tipo == TipoLancamento.Saida && l.Chave is not null)
            .GroupBy(l => l.Chave!);

        foreach (var grupo in grupos)
        {
            var serie = $"auto:{grupo.Key}";
            if (estado.SeriesIgnoradas.Contains(serie)) continue;
            var itens = grupo.ToList();
            var meses = itens.Select(l => l.Data.Year * 12 + l.Data.Month).Distinct().Order().ToList();
            if (meses.Count < 2 || itens.Count > meses.Count) continue; // precisa ser 1 vez por mês…
            if (meses.Zip(meses.Skip(1)).Any(m => m.Second - m.First != 1)) continue; // …em meses seguidos
            var valores = itens.Select(l => l.Valor).Order().ToList();
            var mediana = valores[valores.Count / 2];
            if (valores.Any(v => Math.Abs(v - mediana) > mediana * 0.2m)) continue;
            var dias = itens.Select(l => l.Data.Day).ToList();
            if (dias.Max() - dias.Min() > 12) continue;

            // Pagamentos de conta fixa saem da média de gastos do dia a dia (já são previstos pela conta).
            foreach (var l in itens.Where(l => l.ContaPagaId is null)) l.Serie = serie;
            if (estado.Contas.Any(c => c.Serie == serie && !c.Paga)) continue;

            var ultimo = itens.MaxBy(l => l.Data)!;
            estado.Contas.Add(new Conta
            {
                UsuarioId = estado.UsuarioId,
                Serie = serie,
                Chave = grupo.Key,
                Descricao = ultimo.Descricao,
                Valor = ultimo.Valor,
                Categoria = ultimo.Categoria,
                Vencimento = ultimo.Data.AddMonths(1),
                Recorrente = true,
                Origem = OrigemConta.Detectada,
            });
        }
    }

    /// <summary>
    /// Baixa automática: conta em aberto é dada como paga quando aparece no extrato
    /// uma saída do mesmo favorecido (de 30 dias antes — mas depois do vencimento anterior — até 10 depois),
    /// com o valor exato (±10 dias) ou, para fatura de cartão, um "PAGAMENTO FATURA".
    /// Se o dono tinha marcado como paga à mão, o lançamento manual é trocado pelo do banco.
    /// </summary>
    public static void ConciliarContas(EstadoFinanceiro estado)
    {
        var usados = estado.Lancamentos.Where(l => l.ContaPagaId is not null).Select(l => l.Id).ToHashSet();

        bool Casa(Lancamento l, Conta conta, DateOnly referencia)
        {
            if (l.Origem != OrigemLancamento.Banco || l.Tipo != TipoLancamento.Saida || usados.Contains(l.Id)) return false;
            var dias = l.Data.DayNumber - referencia.DayNumber; // negativo = pago antes
            if (conta.Origem == OrigemConta.FaturaCartao)
                return Texto.SemAcento(l.DescricaoBanco).Contains("FATURA") && dias is >= -30 and <= 10;
            var mesmoFavorecido = conta.Chave is not null && l.Chave == conta.Chave && Math.Abs(l.Valor - conta.Valor) <= conta.Valor * 0.2m;
            // Pagamento antecipado vale até 30 dias, mas tem que ser depois do vencimento do mês anterior:
            // senão o pagamento do mês passado "quitaria" a conta deste mês.
            if (mesmoFavorecido) return dias is >= -30 and <= 10 && l.Data > referencia.AddMonths(-1);
            return Math.Abs(l.Valor - conta.Valor) < 0.01m && Math.Abs(dias) <= 10;
        }

        foreach (var conta in estado.Contas.ToList())
        {
            if (!conta.Paga)
            {
                var l = estado.Lancamentos.Where(x => Casa(x, conta, conta.Vencimento)).OrderBy(x => x.Data).FirstOrDefault();
                if (l is null) continue;
                usados.Add(l.Id);
                l.ContaPagaId = conta.Id;
                conta.Paga = true;
                conta.PagaEm = l.Data;
                conta.PagaPeloBanco = true;
                CriarProximaSeRecorrente(estado, conta);
            }
            else if (!conta.PagaPeloBanco && conta.PagaEm is { } pagaEm)
            {
                var manual = estado.Lancamentos.FirstOrDefault(x => x.ContaPagaId == conta.Id && x.Origem == OrigemLancamento.Manual);
                if (manual is null) continue;
                var l = estado.Lancamentos.FirstOrDefault(x => Casa(x, conta, pagaEm));
                if (l is null) continue;
                usados.Add(l.Id);
                estado.Lancamentos.Remove(manual);
                l.ContaPagaId = conta.Id;
                conta.PagaEm = l.Data;
                conta.PagaPeloBanco = true;
            }
        }
    }

    private static void CriarProximaSeRecorrente(EstadoFinanceiro estado, Conta conta)
    {
        if (!conta.Recorrente) return;
        conta.Serie ??= $"manual:{conta.Id}";
        if (estado.Contas.Any(c => c != conta && c.Serie == conta.Serie && !c.Paga)) return;
        estado.Contas.Add(conta.CopiarParaProximoMes());
    }

    /// <summary>Dono marca uma conta como paga sem o banco (ex.: pagou em dinheiro).</summary>
    public static Lancamento PagarManualmente(EstadoFinanceiro estado, Conta conta, DateOnly hoje)
    {
        conta.Paga = true;
        conta.PagaEm = hoje;
        var l = new Lancamento
        {
            UsuarioId = estado.UsuarioId,
            Origem = OrigemLancamento.Manual,
            Data = hoje,
            Valor = conta.Valor,
            Tipo = TipoLancamento.Saida,
            Categoria = conta.Categoria,
            Descricao = conta.Descricao,
            ContaPagaId = conta.Id,
            Motivo = "Marcado como pago por você",
        };
        estado.Lancamentos.Add(l);
        CriarProximaSeRecorrente(estado, conta);
        return l;
    }

    /// <summary>"Confira": aplica a escolha a todos os lançamentos do mesmo favorecido e guarda como regra.</summary>
    public static void Confirmar(EstadoFinanceiro estado, Lancamento alvo, TipoLancamento tipo, string categoria)
    {
        if (alvo.Chave is not null)
        {
            if (estado.Regras.TryGetValue(alvo.Chave, out var regra))
            {
                regra.Tipo = tipo;
                regra.Categoria = categoria;
            }
            else
            {
                estado.Regras[alvo.Chave] = new RegraClassificacao { UsuarioId = estado.UsuarioId, Chave = alvo.Chave, Tipo = tipo, Categoria = categoria };
            }
        }
        foreach (var l in estado.Lancamentos.Where(l => l == alvo || (alvo.Chave is not null && l.Origem == OrigemLancamento.Banco && l.Chave == alvo.Chave)))
        {
            l.Tipo = tipo;
            l.Categoria = categoria;
            l.Revisar = false;
            l.Motivo = "Confirmado por você";
        }
        DetectarContasRecorrentes(estado);
    }
}
