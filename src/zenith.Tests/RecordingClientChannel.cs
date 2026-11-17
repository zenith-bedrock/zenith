using System.Reflection;
using Zenith.Gameplay.Ports;

namespace Zenith.Tests;

/// <summary>
/// A client that records what gameplay says to it, as calls on the ports (name + arguments), instead of packets. Attach it with
/// <see cref="Zenith.Player.Player.AttachClient"/>. Each port is a runtime proxy over its interface, so a method added to
/// <c>ClientPorts.cs</c> is recorded with no edit here (the hand-written recorders this replaces had already drifted: one dropped an
/// argument). A call returns the default of its type, except a <c>bool</c>, which answers true (a stack id the client "advertised" matches).
/// </summary>
internal sealed class RecordingClientChannel : IClientChannel
{
    public List<(string Channel, string Call, object?[] Args)> Calls { get; } = [];

    public RecordingClientChannel()
    {
        Entity = Record<IEntityChannel>("Entity");
        Inventory = Record<IInventoryChannel>("Inventory");
        World = Record<IWorldChannel>("World");
        Chat = Record<IChatChannel>("Chat");
        Ui = Record<IUiChannel>("Ui");
    }

    public IEntityChannel Entity { get; }
    public IInventoryChannel Inventory { get; }
    public IWorldChannel World { get; }
    public IChatChannel Chat { get; }
    public IUiChannel Ui { get; }
    public bool IsClosed => false;
    public string Xuid => "";

    public IEnumerable<object?[]> Of(string call) => Calls.Where(c => c.Call == call).Select(c => c.Args);
    public int Count(string call) => Calls.Count(c => c.Call == call);

    private T Record<T>(string channel) where T : class
    {
        var proxy = DispatchProxy.Create<T, Recorder>();
        var recorder = (Recorder)(object)proxy;
        recorder.Calls = Calls;
        recorder.Channel = channel;
        return proxy;
    }

    private class Recorder : DispatchProxy
    {
        public List<(string Channel, string Call, object?[] Args)> Calls { get; set; } = [];
        public string Channel { get; set; } = "";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add((Channel, targetMethod!.Name, args is null ? [] : (object?[])args.Clone()));
            var returns = targetMethod.ReturnType;
            if (returns == typeof(void)) return null;
            return returns == typeof(bool) ? true : returns.IsValueType ? Activator.CreateInstance(returns) : null;
        }
    }
}
