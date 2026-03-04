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
    /// <summary>
    /// Car cost shaping:
    ///  • Turn penalties based on turn angle (+ optional U-turn bonus seconds)
    ///  • Road hierarchy bias via density adders (collector/local/very local)
    ///  • Bus-only lane penalty: extra behavior-time seconds for non-bus vehicles
    ///
    /// Uses delta-apply (previous vs desired) so repeated runs don't stack.
    /// </summary>
    public sealed partial class CarTurnAndHierarchyBiasSystem : GameSystemBase
    {
        // ----------------------- Queries -----------------------
        EntityQuery _connQ; // connection lanes (for angle sampling if needed)
        EntityQuery _laneQ; // normal lanes

        // ----------------------- Lookups -----------------------
        ComponentLookup<Curve> _curveLk;
        ComponentLookup<PrefabRef> _prefabLk;
        ComponentLookup<RoadData> _roadDataLk;
        ComponentLookup<NetLaneData> _netLaneLk; // lane prefab flags (PublicOnly)

        // ----------------------- Caches ------------------------
        NativeParallelHashMap<Entity, float> _prevTurnPenaltySec;
        NativeParallelHashMap<Entity, float> _prevDensityAdd;
        CarPathBiasConfig _lastCfg;
        bool _cfgInitialized;
        bool _syncingTurnAngleSettings;

        // ----------------------- Config ------------------------
        struct CarPathBiasConfig : IComponentData
        {
            // TURN PENALTY
            public float BaseTurnPenaltySec; // max penalty for a sharp turn
            public float MinTurnAngleDeg;    // below this, ~0 penalty
            public float MaxTurnAngleDeg;    // at/above this, full penalty

            // HIERARCHY BIAS (density adders; larger = slower perceived)
            public float BiasCollector;
            public float BiasLocal;
            public float BiasVeryLocal;

            // U-turn
            public float UTurnThresholdDeg;  // e.g., 150
            public float UTurnBonusSec;      // e.g., +5s
        }

        // ----------------------- Results -----------------------
        struct TurnResult { public Entity Owner; public float Seconds; }
        struct DensityResult { public Entity Owner; public float AddDensity; }

        // ----------------------- Lifecycle ---------------------
        protected override void OnCreate()
        {
            base.OnCreate();

            _laneQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Lane>(),
                    ComponentType.ReadOnly<PrefabRef>()
                }
            });

            _prevTurnPenaltySec = new NativeParallelHashMap<Entity, float>(256, Allocator.Persistent);
            _prevDensityAdd = new NativeParallelHashMap<Entity, float>(256, Allocator.Persistent);

            RequireForUpdate(_laneQ);

            Mod.m_Setting.onSettingsApplied += OnSettingsApplied;
        }

        private void OnSettingsApplied(Game.Settings.Setting _)
        {
            var s = Mod.m_Setting;
            float minTurn = s?.min_turn_agle_deg ?? 20f;
            float maxTurn = s?.max_turn_agle_deg ?? 100f;

            if (minTurn > maxTurn)
            {
                float temp = minTurn;
                minTurn = maxTurn;
                maxTurn = temp;

                if (s != null && !_syncingTurnAngleSettings)
                {
                    _syncingTurnAngleSettings = true;
                    try
                    {
                        s.min_turn_agle_deg = minTurn;
                        s.max_turn_agle_deg = maxTurn;
                        s.ApplyAndSave();
                    }
                    finally
                    {
                        _syncingTurnAngleSettings = false;
                    }

                    Mod.log.Info($"[RPF] CarTurnAndHierarchyBiasSystem: normalized turn angle range for UI sync -> min_ang={minTurn:F1}°, max_ang={maxTurn:F1}°");
                }
            }

            var newCfg = new CarPathBiasConfig
            {
                BaseTurnPenaltySec = s?.base_turn_penalty ?? 3f,
                MinTurnAngleDeg    = minTurn,
                MaxTurnAngleDeg    = maxTurn,
                BiasCollector      = s?.collector_bias ?? 0.05f,
                BiasLocal          = s?.local_bias ?? 0.10f,
                BiasVeryLocal      = s?.alleyway_bias ?? 0.15f,
                UTurnThresholdDeg  = s?.uturn_threshold_deg ?? 150f,
                UTurnBonusSec      = s?.uturn_sec_penalty ?? 5f,
            };

            if (_cfgInitialized && ConfigEquals(_lastCfg, newCfg)) return;

            _lastCfg = newCfg;
            _cfgInitialized = true;
            Enabled = true;
        }

        protected override void OnDestroy()
        {
            if (Mod.m_Setting != null)
                Mod.m_Setting.onSettingsApplied -= OnSettingsApplied;
            if (_prevTurnPenaltySec.IsCreated) _prevTurnPenaltySec.Dispose();
            if (_prevDensityAdd.IsCreated) _prevDensityAdd.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven: run on next simulation tick after Enabled is set by onSettingsApplied
            return 1;
        }

        // ----------------------- Update ------------------------
        protected override void OnUpdate()
        {
            // Refresh lookups
            _curveLk = GetComponentLookup<Curve>(true);
            _prefabLk = GetComponentLookup<PrefabRef>(true);
            _roadDataLk = GetComponentLookup<RoadData>(true);
            _netLaneLk = GetComponentLookup<NetLaneData>(true);

            _curveLk.Update(this);
            _prefabLk.Update(this);
            _roadDataLk.Update(this);
            _netLaneLk.Update(this);

            // Build config from settings on each run so changes apply as soon as the system is enabled
            var s = Mod.m_Setting;
            float minTurn = s?.min_turn_agle_deg ?? 20f;
            float maxTurn = s?.max_turn_agle_deg ?? 100f;
            if (minTurn > maxTurn)
            {
                float temp = minTurn;
                minTurn = maxTurn;
                maxTurn = temp;
            }

            var cfg = new CarPathBiasConfig
            {
                BaseTurnPenaltySec = s?.base_turn_penalty ?? 3f,
                MinTurnAngleDeg = minTurn,
                MaxTurnAngleDeg = maxTurn,
                BiasCollector = s?.collector_bias ?? 0.05f,
                BiasLocal = s?.local_bias ?? 0.10f,
                BiasVeryLocal = s?.alleyway_bias ?? 0.15f,
                UTurnThresholdDeg = s?.uturn_threshold_deg ?? 150f,
                UTurnBonusSec = s?.uturn_sec_penalty ?? 5f,
            };

            int laneN = _laneQ.CalculateEntityCount();

            var turnResults = new NativeList<TurnResult>(math.max(1, laneN), Allocator.TempJob);
            var densResults = new NativeList<DensityResult>(math.max(1, laneN), Allocator.TempJob);

            // 1) Hierarchy bias + Bus-only detection
            new LaneScanJob
            {
                PrefabLk = _prefabLk,
                RoadDataLk = _roadDataLk,
                NetLaneLk = _netLaneLk,

                BiasCollector = cfg.BiasCollector,
                BiasLocal = cfg.BiasLocal,
                BiasVeryLocal = cfg.BiasVeryLocal,

                Results = densResults.AsParallelWriter(),
            }.ScheduleParallel(_laneQ, default).Complete();

            // 2) Turn scan (angle → seconds)
            new TurnScanJob
            {
                CurveLk = _curveLk,
                BaseTurnPenaltySec = cfg.BaseTurnPenaltySec,
                MinTurnDeg = cfg.MinTurnAngleDeg,
                MaxTurnDeg = cfg.MaxTurnAngleDeg,
                UTurnThresholdDeg = cfg.UTurnThresholdDeg,
                UTurnBonusSec = cfg.UTurnBonusSec,
                TurnResults = turnResults.AsParallelWriter()
            }.ScheduleParallel(_laneQ, default).Complete();

            // 3) MAIN THREAD: commit deltas to pathfind data
            var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            var data = pqs.GetDataContainer(out var dep); dep.Complete();

            // A) Turn seconds (behavior-time channel)
            int turnApplied = 0;
            int turnEdgeMiss = 0;
            for (int i = 0; i < turnResults.Length; i++)
            {
                var r = turnResults[i];
                if (!TryGetEdge(data, r.Owner, out var eid))
                {
                    turnEdgeMiss++;
                    continue;
                }

                float prev = _prevTurnPenaltySec.TryGetValue(r.Owner, out var p) ? p : 0f;
                float delta = r.Seconds - prev;
                if (math.abs(delta) < 0.01f) continue;

                ref var costs = ref data.SetCosts(eid);
                costs.m_Value.y += delta; // behavior-time
                _prevTurnPenaltySec[r.Owner] = r.Seconds;
                turnApplied++;
            }

            // C) Hierarchy density adders
            int densApplied = 0;
            int densEdgeMiss = 0;
            for (int i = 0; i < densResults.Length; i++)
            {
                var r = densResults[i];
                if (!TryGetEdge(data, r.Owner, out var eid))
                {
                    densEdgeMiss++;
                    continue;
                }

                float prev = _prevDensityAdd.TryGetValue(r.Owner, out var p) ? p : 0f;
                float delta = r.AddDensity - prev;
                if (math.abs(delta) < 1e-4f) continue;

                ref float density = ref data.SetDensity(eid);
                density += delta;
                _prevDensityAdd[r.Owner] = r.AddDensity;
                densApplied++;
            }

            if (turnApplied > 0 || densApplied > 0)
            {
                Mod.log.Info(
                    $"[RPF] CarTurnAndHierarchyBiasSystem: applied → " +
                    $"base_turn={cfg.BaseTurnPenaltySec:F2}s, min_ang={cfg.MinTurnAngleDeg:F1}°, max_ang={cfg.MaxTurnAngleDeg:F1}°, " +
                    $"collector={cfg.BiasCollector:F3}, local={cfg.BiasLocal:F3}, verylocal={cfg.BiasVeryLocal:F3} | " +
                    $"turns_updated={turnApplied}, density_updated={densApplied}");
            }

            // Let pathfinding pick up the changes
            pqs.AddDataReader(default);

            // Dispose temps
            turnResults.Dispose();
            densResults.Dispose();

            this.Enabled = false; // run once; re-enabled by onSettingsApplied when settings change
        }

        // ----------------------- Helpers -----------------------
        static bool TryGetEdge(NativePathfindData d, Entity owner, out EdgeID id)
        {
            if (d.GetEdge(owner, out id)) return true;
            if (d.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }

        static bool ConfigEquals(CarPathBiasConfig a, CarPathBiasConfig b) =>
            math.abs(a.BaseTurnPenaltySec - b.BaseTurnPenaltySec) < 1e-4f &&
            math.abs(a.MinTurnAngleDeg    - b.MinTurnAngleDeg)    < 1e-4f &&
            math.abs(a.MaxTurnAngleDeg    - b.MaxTurnAngleDeg)    < 1e-4f &&
            math.abs(a.BiasCollector      - b.BiasCollector)      < 1e-4f &&
            math.abs(a.BiasLocal          - b.BiasLocal)          < 1e-4f &&
            math.abs(a.BiasVeryLocal      - b.BiasVeryLocal)      < 1e-4f &&
            math.abs(a.UTurnThresholdDeg  - b.UTurnThresholdDeg)  < 1e-4f &&
            math.abs(a.UTurnBonusSec      - b.UTurnBonusSec)      < 1e-4f;

        // ----------------------- Jobs --------------------------
        [BurstCompile]
        partial struct TurnScanJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Curve> CurveLk;

            public float BaseTurnPenaltySec;
            public float MinTurnDeg;
            public float MaxTurnDeg;

            public float UTurnThresholdDeg;
            public float UTurnBonusSec;

            [WriteOnly] public NativeList<TurnResult>.ParallelWriter TurnResults;

            public void Execute(Entity e, in Lane lane, in PrefabRef pr)
            {
                float angleDeg = 0f;

                if (CurveLk.HasComponent(e))
                {
                    // Approximate turn angle from Bezier tangents (matches other systems using m_Bezier)
                    var b = CurveLk[e].m_Bezier;
                    float3 t0 = math.normalize(b.b - b.a);
                    float3 t1 = math.normalize(b.c - b.d);
                    float dot = math.clamp(math.dot(t0, -t1), -1f, 1f);
                    angleDeg = math.degrees(math.acos(dot));
                }

                float secs = 0f;
                if (angleDeg > MinTurnDeg)
                {
                    float t = math.saturate((angleDeg - MinTurnDeg) / math.max(1e-3f, (MaxTurnDeg - MinTurnDeg)));
                    secs = t * BaseTurnPenaltySec;
                }

                if (angleDeg >= UTurnThresholdDeg && UTurnBonusSec > 0f)
                    secs += UTurnBonusSec;

                TurnResults.AddNoResize(new TurnResult { Owner = e, Seconds = secs });
            }
        }

        [BurstCompile]
        partial struct LaneScanJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<PrefabRef> PrefabLk;
            [ReadOnly] public ComponentLookup<RoadData> RoadDataLk;
            [ReadOnly] public ComponentLookup<NetLaneData> NetLaneLk;

            public float BiasCollector, BiasLocal, BiasVeryLocal;

            [WriteOnly] public NativeList<DensityResult>.ParallelWriter Results;

            public void Execute(Entity e, in Lane lane, in PrefabRef pr)
            {
                // ----- Hierarchy bias (skip highways) -----
                float add = 0f;
                var prefab = pr.m_Prefab;
                if (prefab != Entity.Null && RoadDataLk.HasComponent(prefab))
                {
                    var rd = RoadDataLk[prefab];
                    bool isHighway = (rd.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;

                    if (!isHighway)
                    {
                        // Speed thresholds in m/s (≈60km/h, 40km/h)
                        if (rd.m_SpeedLimit >= 16.7f) add = BiasCollector;
                        else if (rd.m_SpeedLimit >= 11.1f) add = BiasLocal;
                        else add = BiasVeryLocal;
                    }
                }

                Results.AddNoResize(new DensityResult { Owner = e, AddDensity = add });
            }
        }
    }
}
