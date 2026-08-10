using Zenith.Raknet.Log;

namespace Zenith.Event;

/// <summary>
/// Barramento tipado publish/subscribe. Login/quit publicam; primeiro consumidor de domínio é
/// <see cref="Zenith.Gameplay.PlayerPresenceAnnouncer"/> (ADR §78, join/leave chat), registrado
/// no composition root. Deliberadamente sem prioridade/cancelamento/unsubscribe (ADR §21).
/// </summary>
class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _listeners = new();
    private readonly ILogger? _logger;

    public EventBus(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Registra <paramref name="listener"/> pra ser chamado toda vez que um <typeparamref name="T"/> for publicado.</summary>
    public void Subscribe<T>(Action<T> listener)
    {
        if (!_listeners.TryGetValue(typeof(T), out var list))
        {
            list = new List<Delegate>();
            _listeners[typeof(T)] = list;
        }

        list.Add(listener);
    }

    /// <summary>
    /// Dispara <paramref name="event"/> pra todos os listeners inscritos em <typeparamref name="T"/>.
    /// Exceção em um listener é isolada (log + continua) — mesmo padrão do GameLoop por sistema —
    /// para não derrubar caminhos como <c>NetworkSession.HandleClose</c>.
    /// </summary>
    public void Publish<T>(T @event)
    {
        if (!_listeners.TryGetValue(typeof(T), out var list)) return;

        // Itera por índice sobre a lista real (sem alocar cópia a cada Publish). Só é inseguro
        // se um listener se desinscrever no meio do próprio Publish, o que hoje não é suportado;
        // Subscribe durante o Publish é seguro (o novo listener só entra na próxima chamada).
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            try
            {
                ((Action<T>)list[i])(@event);
            }
            catch (Exception ex)
            {
                _logger?.Error($"EventBus listener for {typeof(T).Name} failed: {ex}");
            }
        }
    }
}
