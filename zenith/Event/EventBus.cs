namespace Zenith.Event;

/// <summary>
/// Barramento de eventos tipado. Sistemas se inscrevem por tipo de evento (<c>Subscribe&lt;T&gt;</c>)
/// e o publisher dispara pra todo mundo inscrito (<c>Publish&lt;T&gt;</c>), sem os dois lados se
/// conhecerem diretamente. Isso é o que falta hoje pra, por exemplo, um sistema de scoreboard
/// reagir a login/disconnect sem o LoginSessionHandler ter que saber que scoreboard existe.
///
/// Deliberadamente sem reflection, sem prioridade de handler, sem cancelamento - só
/// publish/subscribe puro. Complexidade extra entra depois, se algum caso de uso real pedir.
/// </summary>
class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _listeners = new();

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

    /// <summary>Dispara <paramref name="event"/> pra todos os listeners inscritos em <typeparamref name="T"/>, em ordem de inscrição.</summary>
    public void Publish<T>(T @event)
    {
        if (!_listeners.TryGetValue(typeof(T), out var list)) return;

        // Itera por índice sobre a lista real (sem alocar cópia a cada Publish). Só é inseguro
        // se um listener se desinscrever no meio do próprio Publish, o que hoje não é suportado;
        // Subscribe durante o Publish é seguro (o novo listener só entra na próxima chamada).
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            ((Action<T>)list[i])(@event);
        }
    }
}
