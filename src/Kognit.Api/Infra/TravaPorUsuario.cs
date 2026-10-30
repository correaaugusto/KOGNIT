using System.Collections.Concurrent;

namespace Kognit.Api.Infra;

/// <summary>
/// Uma alteração por vez em cada usuário: a sincronização em segundo plano (webhook) e uma ação
/// do usuário (ex.: confirmar um lançamento) não podem salvar versões diferentes ao mesmo tempo.
/// Vale para um servidor só; com vários servidores, trocar por trava no banco de dados (ver docs/DECISOES.md).
/// </summary>
public sealed class TravaPorUsuario
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _travas = new();

    public async Task<IDisposable> EntrarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var trava = _travas.GetOrAdd(usuarioId, _ => new SemaphoreSlim(1, 1));
        await trava.WaitAsync(ct);
        return new Liberar(trava);
    }

    private sealed class Liberar(SemaphoreSlim trava) : IDisposable
    {
        private int _liberado;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0) trava.Release();
        }
    }
}
