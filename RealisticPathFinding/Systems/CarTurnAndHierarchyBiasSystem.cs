using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    public partial class CarTurnAndHierarchyBiasSystem : GameSystemBase
    {
        // Queries
        EntityQuery _connQ;  // ConnectionLane + Curve
        EntityQuery _laneQ;  // Lane + PrefabRef

        // Component lookups (read-only for jobs)
        ComponentLookup<Curve> _curveLk;
        ComponentLookup<PrefabRef> _prefabLk;
        ComponentLookup<RoadData> _roadDataLk;

        // State caches so we can update deltas when settings change (no double-adding)
        NativeParallelHashMap<Entity, float> _prevTurnPenaltySec; // per owner entity
        NativeParallelHashMap<Entity, float> _prevDensityAdd;     // per owner entity

        public struct CarPathBiasConfig : IComponentData
        {
            // TURN PENALTY
            public float BaseTurnPenaltySec; // max penalty for a sharp turn
            public float MinTurnAngleDeg;    // below this, ~0 penalty
            public float MaxTurnAngleDeg;    // at/above this, full penalty

            // HIERARCHY BIAS (density adders; larger = slower perceived)
            public float BiasCollector;
            public float BiasLocal;
            public float BiasVeryLocal;

            // NEW
            public float UTurnThresholdDeg; // e.g., 150
            public float UTurnBonusSec;     // e.g., +5 s
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            _connQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.ConnectionLane>(), ComponentType.ReadOnly<Curve>() }
            });
            _laneQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<PrefabRef>() }
            });

            _prevTurnPenaltySec = new NativeParallelHashMap<Entity, float>(1024, Allocator.Persistent);
            _prevDensityAdd = new NativeParallelHashMap<Entity, float>(2048, Allocator.Persistent);

            RequireForUpdate(_connQ);
            RequireForUpdate(_laneQ);
        }

        protected override void OnDestroy()
        {
            if (_prevTurnPenaltySec.IsCreated) _prevTurnPenaltySec.Dispose();
            if (_prevDensityAdd.IsCreated) _prevDensityAdd.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Run infrequently; we compute deltas so repeated runs won’t accumulate
            return 262144/32; // once per in-game day
        }

        // Update pipeline: jobs compute desired values -> main thread applies deltas to the graph
        protected override void OnUpdate()
        {
            _curveLk = GetComponentLookup<Curve>(true);
            _prefabLk = GetComponentLookup<PrefabRef>(true);
            _roadDataLk = GetComponentLookup<RoadData>(true);

            var s = Mod.m_Setting;
            var cfg = SystemAPI.HasSingleton<CarPathBiasConfig>()
                ? SystemAPI.GetSingleton<CarPathBiasConfig>()
                : new CarPathBiasConfig
                {
                    BaseTurnPenaltySec = s?.base_turn_penalty ?? 3f,
                    MinTurnAngleDeg = s?.min_turn_agle_deg ?? 20f,
                    MaxTurnAngleDeg = s?.max_turn_agle_deg ?? 100f,
                    BiasCollector = s?.collector_bias ?? 0.05f,
                    BiasLocal = s?.local_bias ?? 0.10f,
                    BiasVeryLocal = s?.alleyway_bias ?? 0.15f,
                    UTurnThresholdDeg = s?.uturn_threshold_deg ?? 150f,
                    UTurnBonusSec = s?.uturn_sec_penalty ?? 5f,
                };


            // Allocate output buffers sized to queries
            int connN = _connQ.CalculateEntityCount();
            int laneN = _laneQ.CalculateEntityCount();
            var turnResults = new NativeList<TurnResult>(math.max(1, connN), Allocator.TempJob);
            var densResults = new NativeList<DensityResult>(math.max(1, laneN), Allocator.TempJob);

            // ensure no growth inside jobs
            turnResults.Capacity = math.max(turnResults.Capacity, connN);
            densResults.Capacity = math.max(densResults.Capacity, laneN);


            // Schedule Burst jobs
            new TurnScanJob
            {
                CurveLk = _curveLk,
                BaseTurnPenaltySec = cfg.BaseTurnPenaltySec,
                MinTurnAngleDeg = cfg.MinTurnAngleDeg,
                MaxTurnAngleDeg = cfg.MaxTurnAngleDeg,
                UTurnThresholdDeg = cfg.UTurnThresholdDeg,
                UTurnBonusSec = cfg.UTurnBonusSec,
                Results = turnResults.AsParallelWriter()
            }.ScheduleParallel(_connQ, Dependency).Complete();

            new LaneScanJob
            {
                PrefabLk = _prefabLk,
                RoadDataLk = _roadDataLk,
                BiasCollector = cfg.BiasCollector,
                BiasLocal = cfg.BiasLocal,
                BiasVeryLocal = cfg.BiasVeryLocal,
                Results = densResults.AsParallelWriter()
            }.ScheduleParallel(_laneQ, default).Complete();

            // MAIN THREAD: commit deltas to the path graph
            var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            var data = pqs.GetDataContainer(out var dep); dep.Complete();

            // 1) Turn penalties (behavior seconds)
            for (int i = 0; i < turnResults.Length; i++)
            {
                var r = turnResults[i];
                if (!TryGetEdge(data, r.Owner, out var eid))
                    continue;

                float prev = _prevTurnPenaltySec.TryGetValue(r.Owner, out var p) ? p : 0f;
                float delta = r.PenaltySec - prev;
                if (math.abs(delta) >= 0.01f)
                {
                    ref var costs = ref data.SetCosts(eid); // .m_Value.x/y/z layout is internal; y holds behavior time
                    costs.m_Value.y += delta;
                    _prevTurnPenaltySec[r.Owner] = r.PenaltySec;
                }
            }

            // 2) Hierarchy bias (density add)
            for (int i = 0; i < densResults.Length; i++)
            {
                var r = densResults[i];
                if (!TryGetEdge(data, r.Owner, out var eid))
                    continue;

                float prev = _prevDensityAdd.TryGetValue(r.Owner, out var p) ? p : 0f;
                float delta = r.AddDensity - prev;
                if (math.abs(delta) >= 1e-4f)
                {
                    ref float density = ref data.SetDensity(eid);
                    density += delta;
                    _prevDensityAdd[r.Owner] = r.AddDensity;
                }
            }

            // Let the queue know we touched the data this frame
            pqs.AddDataReader(default);

            // Dispose temp lists
            turnResults.Dispose();
            densResults.Dispose();
        }

        static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }

        // ---------- Burst jobs ----------

        struct TurnResult
        {
            public Entity Owner;
            public float PenaltySec;
        }

        [BurstCompile]
        partial struct TurnScanJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Curve> CurveLk;
            public float BaseTurnPenaltySec, MinTurnAngleDeg, MaxTurnAngleDeg, UTurnThresholdDeg, UTurnBonusSec;

            [WriteOnly] public NativeList<TurnResult>.ParallelWriter Results;

            void Execute(Entity e, in Game.Net.ConnectionLane conn)
            {
                // only road turns
                if ((conn.m_Flags & ConnectionLaneFlags.Road) == 0) return;
                if (!CurveLk.HasComponent(e)) return;

                var bz = CurveLk[e].m_Bezier; // Bezier4x3 (a,b,c,d)

                float3 p0 = Eval(bz, 0.00f);
                float3 p1 = Eval(bz, 0.10f);
                float3 q1 = Eval(bz, 0.90f);
                float3 q2 = Eval(bz, 1.00f);

                float3 dirIn = math.normalizesafe(p1 - p0);
                float3 dirOut = math.normalizesafe(q2 - q1);

                float dot = math.clamp(math.dot(dirIn, dirOut), -1f, 1f);
                float turnDeg = math.degrees(math.acos(dot));

                float t = math.saturate((turnDeg - MinTurnAngleDeg) / math.max(1f, (MaxTurnAngleDeg - MinTurnAngleDeg)));

                float penalty = BaseTurnPenaltySec * t;

                if (turnDeg >= UTurnThresholdDeg)
                    penalty += UTurnBonusSec;

                if (penalty > 0.001f)
                    Results.AddNoResize(new TurnResult { Owner = e, PenaltySec = penalty });
                else
                    Results.AddNoResize(new TurnResult { Owner = e, PenaltySec = 0f });
            }

            static float3 Eval(Bezier4x3 b, float t)
            {
                float u = 1f - t;
                return (u * u * u) * b.a + 3f * (u * u * t) * b.b + 3f * (u * t * t) * b.c + (t * t * t) * b.d;
            }
        }

        struct DensityResult
        {
            public Entity Owner;
            public float AddDensity;
        }

        [BurstCompile]
        partial struct LaneScanJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<PrefabRef> PrefabLk;
            [ReadOnly] public ComponentLookup<RoadData> RoadDataLk;

            public float BiasCollector, BiasLocal, BiasVeryLocal;

            [WriteOnly] public NativeList<DensityResult>.ParallelWriter Results;

            void Execute(Entity e, in Lane lane, in PrefabRef pr)
            {
                var prefab = pr.m_Prefab;
                if (prefab == Entity.Null || !RoadDataLk.HasComponent(prefab))
                    return;

                var rd = RoadDataLk[prefab];
                // Favor highways by not adding bias (UseHighwayRules flag)
                bool isHighway = (rd.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;

                float add = 0f;
                if (!isHighway)
                {
                    // Speed thresholds in m/s (≈60 km/h, 40 km/h)
                    if (rd.m_SpeedLimit >= 16.7f) add = BiasCollector;   // arterials/collectors
                    else if (rd.m_SpeedLimit >= 11.1f) add = BiasLocal;       // locals
                    else add = BiasVeryLocal;   // very local/alleys
                }

                if (add > 0f)
                    Results.AddNoResize(new DensityResult { Owner = e, AddDensity = add });
                else
                    Results.AddNoResize(new DensityResult { Owner = e, AddDensity = 0f });
            }
        }
    }
}
