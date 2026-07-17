using System.Collections.Concurrent;
using System.Net;

namespace Zenith.Raknet.Extension;

/// <summary>
/// Rate limiter simples baseado em token bucket, por IP. Usado pra conter um mesmo host
/// mandando pacotes unconnected (pings, handshake) demais por segundo.
///
/// Isso NÃO protege contra spoofing de IP de origem - qualquer UDP pode forjar o endereço
/// de quem manda. O que resolve é o caso comum: um cliente com bug, um scanner de porta,
/// ou um script simples martelando o servidor sem se importar em forjar nada.
/// </summary>
public class IpRateLimiter
{
    private class Bucket
    {
        public double Tokens;
        public long LastRefillMs;
    }

    private readonly double _capacity;
    private readonly double _refillPerMs;
    private readonly Func<long> _nowMs;
    private readonly ConcurrentDictionary<IPAddress, Bucket> _buckets = new();

    /// <param name="capacity">Quantos tokens o bucket aguenta guardar (rajada máxima).</param>
    /// <param name="refillPerSecond">Quantos tokens são devolvidos por segundo.</param>
    /// <param name="nowMs">Optional clock for tests (unix ms).</param>
    public IpRateLimiter(double capacity, double refillPerSecond, Func<long>? nowMs = null)
    {
        _capacity = capacity;
        _refillPerMs = refillPerSecond / 1000.0;
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Tenta consumir <paramref name="cost"/> tokens do bucket do IP. Retorna false se não tem saldo.</summary>
    public bool TryConsume(IPAddress address, double cost = 1)
    {
        var now = _nowMs();
        var bucket = _buckets.GetOrAdd(address, _ => new Bucket { Tokens = _capacity, LastRefillMs = now });

        lock (bucket)
        {
            var elapsed = now - bucket.LastRefillMs;
            if (elapsed > 0)
            {
                bucket.Tokens = Math.Min(_capacity, bucket.Tokens + elapsed * _refillPerMs);
                bucket.LastRefillMs = now;
            }

            if (bucket.Tokens < cost) return false;
            bucket.Tokens -= cost;
            return true;
        }
    }

    /// <summary>
    /// Remove buckets ociosos há mais de <paramref name="maxIdleMs"/>, pra não vazar memória
    /// com IPs que só apareceram uma vez (scanners, tráfego de fundo da internet, etc.).
    /// Chame periodicamente, não a cada pacote.
    /// </summary>
    public void Cleanup(long maxIdleMs)
    {
        var now = _nowMs();
        foreach (var (address, bucket) in _buckets)
        {
            bool idle;
            lock (bucket) idle = now - bucket.LastRefillMs > maxIdleMs;
            if (idle) _buckets.TryRemove(address, out _);
        }
    }
}
