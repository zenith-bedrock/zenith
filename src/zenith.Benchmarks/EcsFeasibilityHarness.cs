using System.Diagnostics;

namespace Zenith.Benchmarks;

/// <summary>
/// Isolated Phase-E experiment. It intentionally has no dependency on the production runtime or
/// wire stack: equal actor work is compared as direct concrete lists versus contiguous archetypes.
/// </summary>
internal static class EcsFeasibilityHarness
{
    private const int DefaultTicks = 400;

    public static int Run(string[] args)
    {
        var counts = args.Length == 0 ? new[] { 100, 1_000, 10_000 } : args
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(int.Parse).ToArray();
        foreach (var count in counts)
        {
            Run("direct simulation", new DirectWorld(), count, project: false);
            Run("soa simulation", new SoaWorld(), count, project: false);
            Run("direct projection", new DirectWorld(), count, project: true);
            Run("soa projection", new SoaWorld(), count, project: true);
        }
        return 0;
    }

    private static void Run(string name, IWorld world, int count, bool project)
    {
        world.Seed(count);
        for (var tick = 0; tick < 100; tick++) world.Tick(tick, project); // JIT and buffer growth warmup.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var before = Gc.Capture();
        var samples = new long[DefaultTicks];
        for (var tick = 0; tick < samples.Length; tick++)
        {
            var started = Stopwatch.GetTimestamp();
            world.Tick(tick, project);
            samples[tick] = Stopwatch.GetTimestamp() - started;
        }
        var result = Result.Create(samples, GC.GetAllocatedBytesForCurrentThread() - allocated, Gc.Capture() - before);
        Console.WriteLine($"ecs-spike {name,-6} actors={count,5} active={world.Active,5} " +
            $"avg={result.Average:F3}ms p50={result.P50:F3}ms p95={result.P95:F3}ms p99={result.P99:F3}ms max={result.Max:F3}ms " +
            $"alloc={result.Allocated / (double)DefaultTicks:F0}B/tick gc={result.Gc.Gen0}/{result.Gc.Gen1}/{result.Gc.Gen2} " +
            $"created={world.Created} removed={world.Removed} queries={world.Queries} projectionOps={world.ProjectionOps}");
    }

    private interface IWorld
    {
        int Active { get; }
        long Created { get; }
        long Removed { get; }
        long Queries { get; }
        long ProjectionOps { get; }
        void Seed(int count);
        void Tick(int tick, bool project);
    }

    // Control shaped after the current independent Projectile/Zombie/FallingBlock/FloorDrop lists.
    private sealed class DirectWorld : IWorld
    {
        private readonly List<Actor> _actors = [];
        private int _target, _next;
        public int Active => _actors.Count; public long Created { get; private set; } public long Removed { get; private set; }
        public long Queries { get; private set; } public long ProjectionOps { get; private set; }
        public void Seed(int count) { _target = Math.Max(_target, count); while (_actors.Count < count) { var n = _next++; _actors.Add(new Actor(n, (Kind)(n % 4))); Created++; } }
        public void Tick(int tick, bool project)
        {
            for (var i = _actors.Count - 1; i >= 0; i--)
            {
                var a = _actors[i];
                if (a.Kind is Kind.Projectile or Kind.Falling) { a.X += a.Vx; a.Y += a.Vy; a.Vy -= .03f; a.Age++; Queries++; }
                else if (a.Kind == Kind.Zombie) { a.X += .08f; a.Health -= tick % 67 == 0 ? 1 : 0; Queries++; }
                else { a.Age++; Queries++; }
                if ((a.Kind == Kind.Projectile && a.Age >= 80) || (a.Kind == Kind.Zombie && a.Health <= 0) || (a.Kind == Kind.Drop && a.Age >= 160)) { _actors.RemoveAt(i); Removed++; }
                else _actors[i] = a;
            }
            Seed(_target); // replenishes actual expiry/death churn to the original population.
            if (project) for (var observer = 0; observer < 10; observer++)
                for (var actor = 0; actor < _actors.Count; actor++) ProjectionOps += (long)_actors[actor].X;
        }
    }

    // Three concrete archetypes: moving (Projectile/Falling), health (Zombie), and lifetime (Drop).
    private sealed class SoaWorld : IWorld
    {
        private readonly Moving _moving = new(); private readonly Health _health = new(); private readonly Lifetime _drops = new();
        private int _next, _target; public int Active => _moving.Count + _health.Count + _drops.Count;
        public long Created { get; private set; } public long Removed { get; private set; } public long Queries { get; private set; } public long ProjectionOps { get; private set; }
        public void Seed(int count) { _target = Math.Max(_target, count); while (Active < count) { switch ((_next++) % 4) { case 0: _moving.Add(true); break; case 1: _health.Add(); break; case 2: _moving.Add(false); break; default: _drops.Add(); break; } Created++; } }
        public void Tick(int tick, bool project)
        {
            var queries = Queries;
            Removed += _moving.Tick(ref queries) + _health.Tick(tick, ref queries) + _drops.Tick(ref queries);
            Queries = queries;
            Seed(_target);
            if (project) for (var observer = 0; observer < 10; observer++)
                ProjectionOps += _moving.Project() + _health.Project() + _drops.Project();
        }
    }

    private enum Kind : byte { Projectile, Zombie, Falling, Drop }
    private struct Actor { public Actor(int id, Kind kind) { Id=id; Kind=kind; X=id; Y=100; Vx=.05f; Vy=0; Age=0; Health=20; } public int Id, Age; public Kind Kind; public float X,Y,Vx,Vy,Health; }
    private sealed class Moving
    {
        private readonly List<float> _x=[]; private readonly List<float> _y=[]; private readonly List<float> _vx=[]; private readonly List<float> _vy=[]; private readonly List<int> _age=[]; private readonly List<bool> _projectile=[];
        public int Count=>_x.Count; public void Add(bool projectile) { _x.Add(0);_y.Add(100);_vx.Add(.05f);_vy.Add(0);_age.Add(0);_projectile.Add(projectile); }
        public long Project() { long sum=0; for(var i=0;i<Count;i++) sum+=(long)_x[i]+(long)_y[i]; return sum; }
        public int Tick(ref long queries) { var removed=0; for(var i=Count-1;i>=0;i--) { _x[i]+=_vx[i];_y[i]+=_vy[i];_vy[i]-=.03f;_age[i]++;queries++; if(_projectile[i]&&_age[i]>=80){ RemoveAt(i);removed++; } } return removed; }
        private void RemoveAt(int i) { var last=Count-1; _x[i]=_x[last];_y[i]=_y[last];_vx[i]=_vx[last];_vy[i]=_vy[last];_age[i]=_age[last];_projectile[i]=_projectile[last];_x.RemoveAt(last);_y.RemoveAt(last);_vx.RemoveAt(last);_vy.RemoveAt(last);_age.RemoveAt(last);_projectile.RemoveAt(last); }
    }
    private sealed class Health { private readonly List<float> _x=[]; private readonly List<float> _health=[]; public int Count=>_x.Count; public void Add(){_x.Add(0);_health.Add(20);} public long Project(){long sum=0;for(var i=0;i<Count;i++)sum+=(long)_x[i]+(long)_health[i];return sum;} public int Tick(int tick,ref long q){var r=0;for(var i=Count-1;i>=0;i--){_x[i]+=.08f;_health[i]-=tick%67==0?1:0;q++;if(_health[i]<=0){var l=Count-1;_x[i]=_x[l];_health[i]=_health[l];_x.RemoveAt(l);_health.RemoveAt(l);r++;}}return r;} }
    private sealed class Lifetime { private readonly List<int> _age=[]; public int Count=>_age.Count; public void Add()=>_age.Add(0); public long Project(){long sum=0;for(var i=0;i<Count;i++)sum+=_age[i];return sum;} public int Tick(ref long q){var r=0;for(var i=Count-1;i>=0;i--){_age[i]++;q++;if(_age[i]>=160){_age[i]=_age[^1];_age.RemoveAt(_age.Count-1);r++;}}return r;} }
    private readonly record struct Gc(int Gen0,int Gen1,int Gen2) { public static Gc Capture()=>new(GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)); public static Gc operator -(Gc a,Gc b)=>new(a.Gen0-b.Gen0,a.Gen1-b.Gen1,a.Gen2-b.Gen2); }
    private readonly record struct Result(double Average,double P50,double P95,double P99,double Max,long Allocated,Gc Gc) { public static Result Create(long[] ticks,long allocated,Gc gc){var s=ticks.Order().ToArray();double M(long x)=>x*1000d/Stopwatch.Frequency;return new(ticks.Average(M),M(s[199]),M(s[379]),M(s[395]),M(s[^1]),allocated,gc);} }
}
