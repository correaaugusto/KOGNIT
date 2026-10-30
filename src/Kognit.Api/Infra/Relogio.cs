namespace Kognit.Api.Infra;

/// <summary>"Hoje" no fuso de Brasília (o servidor costuma rodar em UTC).</summary>
public sealed class Relogio(TimeProvider tempo)
{
    public static readonly TimeZoneInfo Brasilia = CarregarFuso();

    private static TimeZoneInfo CarregarFuso()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília"); }
    }

    public DateTime AgoraUtc => tempo.GetUtcNow().UtcDateTime;

    public DateOnly Hoje => ParaDataLocal(tempo.GetUtcNow());

    public static DateOnly ParaDataLocal(DateTimeOffset instante) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, Brasilia).DateTime);
}
