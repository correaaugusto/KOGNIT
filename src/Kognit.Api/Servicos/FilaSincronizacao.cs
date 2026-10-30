using System.Collections.Concurrent;
using System.Threading.Channels;
using Kognit.Api.Infra;
using Microsoft.EntityFrameworkCore;

namespace Kognit.Api.Servicos;

/// <summary>Pedidos de sincronização (webhook, botão "atualizar", rotina periódica) processados em segundo plano.</summary>
public sealed class FilaSincronizacao
{
    private readonly Channel<(Guid ConexaoId, int Tentativa)> _canal = Channel.CreateUnbounded<(Guid, int)>();
    private readonly ConcurrentDictionary<Guid, byte> _pendentes = new();

    /// <summary>Ignora se a conexão já está na fila (vários webhooks seguidos viram uma sincronização só).</summary>
    public void Enfileirar(Guid conexaoId, int tentativa = 0)
    {
        if (_pendentes.TryAdd(conexaoId, 0)) _canal.Writer.TryWrite((conexaoId, tentativa));
    }

    internal async IAsyncEnumerable<(Guid ConexaoId, int Tentativa)> LerAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var pedido in _canal.Reader.ReadAllAsync(ct))
        {
            _pendentes.TryRemove(pedido.ConexaoId, out _);
            yield return pedido;
        }
    }
}

public sealed class TrabalhadorSincronizacao(FilaSincronizacao fila, IServiceScopeFactory escopos, ILogger<TrabalhadorSincronizacao> log) : BackgroundService
{
    private static readonly TimeSpan EsperaEntreTentativas = TimeSpan.FromSeconds(5);
    private const int MaxTentativas = 24; // ~2 minutos esperando o banco terminar a coleta

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var (conexaoId, tentativa) in fila.LerAsync(ct))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var r = await escopo.ServiceProvider.GetRequiredService<Sincronizador>().SincronizarAsync(conexaoId, ct);
                if (r.Resultado == ResultadoSincronizacao.AindaAtualizando && tentativa < MaxTentativas)
                    _ = Task.Delay(EsperaEntreTentativas, ct).ContinueWith(_ => fila.Enfileirar(conexaoId, tentativa + 1), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Erro ao sincronizar a conexão {Conexao}", conexaoId);
            }
        }
    }
}

/// <summary>De tempos em tempos, sincroniza todos os bancos conectados (rede de segurança caso um webhook se perca).</summary>
public sealed class SincronizacaoPeriodica(FilaSincronizacao fila, IServiceScopeFactory escopos, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var intervalo = TimeSpan.FromHours(config.GetValue("Sincronizacao:IntervaloHoras", 6.0));
        if (intervalo <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(intervalo);
        while (await timer.WaitForNextTickAsync(ct))
        {
            using var escopo = escopos.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in await db.Conexoes.Select(c => c.Id).ToListAsync(ct)) fila.Enfileirar(id);
        }
    }
}
