namespace Zenith.NetherNet.Axolotl.Utils;

public interface IAddressBlockList
{
    bool IsBlocked(string address);
}

/// <summary>Time-limited address blocks driven by an external clock (port of <c>AddressBlockTracker</c>).</summary>
public sealed class AddressBlockTracker : IAddressBlockList
{
    private readonly Dictionary<string, double> _blocked = new();
    private double _now;
    private double _nextExpiry = double.PositiveInfinity;

    public void Block(string address, double until = double.PositiveInfinity)
    {
        address = Key(address);
        if (_blocked.TryGetValue(address, out var existing) && existing >= until)
            return;
        _blocked[address] = until;
        if (until < _nextExpiry)
            _nextExpiry = until;
    }

    public void Unblock(string address) => _blocked.Remove(Key(address));

    public bool IsBlocked(string address) => _blocked.TryGetValue(Key(address), out var until) && until > _now;

    public void Update(double now)
    {
        _now = now;
        if (now < _nextExpiry)
            return;
        _nextExpiry = double.PositiveInfinity;
        foreach (var (address, until) in _blocked.ToArray())
        {
            if (until <= now)
                _blocked.Remove(address);
            else if (until < _nextExpiry)
                _nextExpiry = until;
        }
    }

    private static string Key(string address) => InternetAddress.NormalizeIp(address) ?? address;
}
