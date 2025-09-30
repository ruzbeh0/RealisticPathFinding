// ------------------------------------------------------------
//  Burst sampler + EWMA applier (single system)
// ------------------------------------------------------------
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;                         // CarCurrentLane
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    public struct CarCongestionConfig : IComponentData
    {
        // EWMA: new = alpha*sample + (1-alpha)*old
        public float Alpha;              // e.g., 0.20 (responsive) … 0.05 (smooth)
        public float UpdateThresholdSec; // only push if EWMA change ≥ this (e.g., 0.5s)
        public float MaxSlowdownRatio;   // cap (EWMA/freeflow), e.g., 3.0
        public float MaxDensityAdd;      // cap density add, e.g., 0.50
        public float MinFreeflowMps;     // floor for speed when computing freeflow (e.g., 1.0)
        public float MinSampleEmitSec;   // ignore micro-samples under this, e.g., 0.2
    }

    // Per-lane EWMA state stored on lane entities
    public struct CarEdgeTiming : IComponentData
    {
        public float FreeflowSec; // computed once from length/speed
        public float EwmaSec;     // smoothed
    }

    // Per-vehicle timing scratch (which lane we’re timing + elapsed sec)
    public struct LaneSampleState : IComponentData
    {
        public Entity Lane;
        public float ElapsedSec;
    }
    public sealed partial class CarCongestionEwmaSystem : GameSystemBase
    {
        // --- queries ---
        EntityQuery _carsWithLaneQ;        // cars with CarCurrentLane AND LaneSampleState (for sampling)
        EntityQuery _carsMissingStateQ;    // cars missing our LaneSampleState (to initialize)
        ComponentLookup<CarCurrentLane> _carLaneLk;
        ComponentLookup<Curve> _curveLk;
        ComponentLookup<PrefabRef> _prefabLk;
        ComponentLookup<RoadData> _roadDataLk;

        // Samples produced by the Burst job (this frame)
        NativeQueue<Sample> _sampleQueue;

        // Remember last density we pushed per lane (apply deltas, not stacks)
        NativeParallelHashMap<Entity, float> _lastDensityAdd;

        // Temporary aggregator (lane → sum,count) reused per frame
        NativeParallelHashMap<Entity, Agg> _agg;

        // Simple POD structs used in jobs
        struct Sample { public Entity Owner; public float TravelSec; }
        struct Agg { public float Sum; public int Cnt; }

        protected override void OnCreate()
        {
            base.OnCreate();

            _carsMissingStateQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CarCurrentLane>() },
                None = new[] { ComponentType.ReadOnly<LaneSampleState>() }
            });

            _carsWithLaneQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<LaneSampleState>(),
                                ComponentType.ReadOnly<CarCurrentLane>() }
            });

            _sampleQueue = new NativeQueue<Sample>(Allocator.Persistent);
            _lastDensityAdd = new NativeParallelHashMap<Entity, float>(4096, Allocator.Persistent);
            _agg = new NativeParallelHashMap<Entity, Agg>(4096, Allocator.Persistent);

            // Create config singleton with sane defaults if missing
            if (!SystemAPI.HasSingleton<CarCongestionConfig>())
            {
                var e = EntityManager.CreateEntity(typeof(CarCongestionConfig));
                EntityManager.SetComponentData(e, new CarCongestionConfig
                {
                    Alpha = 0.20f,
                    UpdateThresholdSec = 0.5f,
                    MaxSlowdownRatio = 3.0f,
                    MaxDensityAdd = 0.50f,
                    MinFreeflowMps = 1.0f,
                    MinSampleEmitSec = 0.2f
                });
            }

            RequireForUpdate(_carsWithLaneQ);
        }

        protected override void OnDestroy()
        {
            if (_sampleQueue.IsCreated) _sampleQueue.Dispose();
            if (_lastDensityAdd.IsCreated) _lastDensityAdd.Dispose();
            if (_agg.IsCreated) _agg.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // light but effective cadence; tweak as desired
            return 262144 / 64; // ~every 7.5 in-game minutes
        }

        protected override void OnUpdate()
        {
            _carLaneLk = GetComponentLookup<CarCurrentLane>(true);
            _curveLk = GetComponentLookup<Curve>(true);
            _prefabLk = GetComponentLookup<PrefabRef>(true);
            _roadDataLk = GetComponentLookup<RoadData>(true);

            var cfg = SystemAPI.GetSingleton<CarCongestionConfig>();
            var s = Mod.m_Setting;

            cfg.Alpha = s?.cong_alpha ?? cfg.Alpha;
            cfg.UpdateThresholdSec = s?.cong_min_push_sec ?? cfg.UpdateThresholdSec;
            cfg.MaxSlowdownRatio = s?.cong_max_ratio ?? cfg.MaxSlowdownRatio;
            cfg.MaxDensityAdd = s?.cong_max_density ?? cfg.MaxDensityAdd;
            cfg.MinFreeflowMps = s?.cong_min_ff_mps ?? cfg.MinFreeflowMps;
            cfg.MinSampleEmitSec = s?.cong_min_sample_sec ?? cfg.MinSampleEmitSec;

            // 1) Ensure newly-seen cars have our LaneSampleState (main thread init)
            using (var newCars = _carsMissingStateQ.ToEntityArray(Allocator.Temp))
            {
                foreach (var v in newCars)
                {
                    var cur = _carLaneLk[v];
                    EntityManager.AddComponentData(v, new LaneSampleState { Lane = cur.m_Lane, ElapsedSec = 0f });
                }
            }

            // 2) Burst job: accumulate time per vehicle and emit a sample on lane change
            var job = new SampleJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                MinEmitSec = cfg.MinSampleEmitSec,
                Out = _sampleQueue.AsParallelWriter()
            };
            Dependency = job.ScheduleParallel(_carsWithLaneQ, Dependency);
            Dependency.Complete(); // we’ll drain queue on main thread right after

            // 3) Drain samples into a per-lane aggregator
            _agg.Clear();
            while (_sampleQueue.TryDequeue(out var sample))
            {
                if (!_agg.TryGetValue(sample.Owner, out var a)) a = default;
                a.Sum += math.max(0.01f, sample.TravelSec);
                a.Cnt += 1;
                _agg[sample.Owner] = a;
            }

            if (_agg.Count() == 0) return;

            // 4) Apply EWMA & update path graph density (main thread)
            var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            var data = pqs.GetDataContainer(out var dep); dep.Complete();

            using var lanes = _agg.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i];
                var a = _agg[lane];

                // Guard: need prefab to read speed limit; skip if missing
                if (!_prefabLk.HasComponent(lane)) continue;
                var prefab = _prefabLk[lane].m_Prefab;
                if (prefab == Entity.Null || !_roadDataLk.HasComponent(prefab)) continue;

                var rd = _roadDataLk[prefab];

                // State component on lane (create if absent)
                CarEdgeTiming t;
                bool has = EntityManager.HasComponent<CarEdgeTiming>(lane);
                t = has ? EntityManager.GetComponentData<CarEdgeTiming>(lane) : default;

                // Compute baseline freeflow once
                if (t.FreeflowSec <= 0f)
                {
                    float lengthM = EstimateLaneLengthMeters(lane, _curveLk);
                    float mps = math.max(cfg.MinFreeflowMps, rd.m_SpeedLimit); // prefab speed limit as proxy
                    t.FreeflowSec = math.max(0.01f, lengthM / mps);
                    if (t.EwmaSec <= 0f) t.EwmaSec = t.FreeflowSec;
                }

                float sampleAvg = a.Sum / math.max(1, a.Cnt);
                float old = t.EwmaSec;
                float ewma = cfg.Alpha * sampleAvg + (1f - cfg.Alpha) * old;

                if (math.abs(ewma - old) < cfg.UpdateThresholdSec)
                {
                    // small change → skip write
                    continue;
                }

                t.EwmaSec = ewma;
                if (has) EntityManager.SetComponentData(lane, t);
                else EntityManager.AddComponentData(lane, t);

                // Convert slowdown into density add (cap both ratio & density)
                float ratio = math.saturate(ewma / math.max(0.01f, t.FreeflowSec));
                ratio = math.min(ratio, cfg.MaxSlowdownRatio);

                // Simple mapping: densityAdd = clamp(ratio - 1, 0, MaxDensityAdd)
                float densityAdd = math.min(math.max(0f, ratio - 1f), cfg.MaxDensityAdd);

                // Push delta into graph
                if (TryGetEdge(data, lane, out var eid))
                {
                    float prev = _lastDensityAdd.TryGetValue(lane, out var p) ? p : 0f;
                    float delta = densityAdd - prev;
                    if (math.abs(delta) >= 1e-4f)
                    {
                        ref float density = ref data.SetDensity(eid);
                        density += delta;
                        _lastDensityAdd[lane] = densityAdd;
                    }
                }
            }

            pqs.AddDataReader(default);
        }

        // ---------- Burst job: per-vehicle sampler ----------
        [BurstCompile]
        partial struct SampleJob : IJobEntity
        {
            public float DeltaTime;
            public float MinEmitSec;

            [WriteOnly] public NativeQueue<Sample>.ParallelWriter Out;

            // Runs for entities that have both LaneSampleState and CarCurrentLane
            void Execute(Entity v, ref LaneSampleState st, in CarCurrentLane cur)
            {
                Entity laneNow = cur.m_Lane;

                // Still on same lane (or lane unknown): accumulate time
                if (laneNow == Entity.Null || laneNow == st.Lane)
                {
                    st.ElapsedSec += DeltaTime;
                    return;
                }

                // Lane changed → emit sample if meaningful
                if (st.Lane != Entity.Null && st.ElapsedSec >= MinEmitSec)
                {
                    Out.Enqueue(new Sample { Owner = st.Lane, TravelSec = st.ElapsedSec });
                }

                // Start timing new lane
                st.Lane = laneNow;
                st.ElapsedSec = 0f;
            }
        }

        // ---------- helpers ----------
        static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }

        static float EstimateLaneLengthMeters(Entity lane, ComponentLookup<Curve> curveLk)
        {
            if (curveLk.HasComponent(lane))
            {
                var b = curveLk[lane].m_Bezier; // Bezier4x3 (a,b,c,d)
                // 5-point polyline approx
                float3 p0 = b.a;
                float3 p1 = math.lerp(b.a, b.b, 1f / 3f);
                float3 p2 = math.lerp(b.b, b.c, 0.5f);
                float3 p3 = math.lerp(b.c, b.d, 2f / 3f);
                float3 p4 = b.d;
                return math.distance(p0, p1) + math.distance(p1, p2) + math.distance(p2, p3) + math.distance(p3, p4);
            }
            return 30f; // fallback if geometry missing
        }
    }
}
