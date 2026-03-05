using Game;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using PedestrianLane = Game.Net.PedestrianLane;

namespace RealisticPathFinding.Systems
{
    // Caches original prefab costs so repeated updates don’t stack
    public struct PedWalkCostOrig : IComponentData
    {
        public PathfindCosts Walk;
    }

    /// <summary>
    /// Scales the time channel (x) of m_WalkingCost by a user factor.
    /// - Prefab side: m_WalkingCost.m_Value.x = orig.x * factor (from cached original)
    /// - Live graph: multiply pedestrian edges' time-per-meter by (newFactor / prevFactor) delta ratio
    /// Consistent with BusLanePatches / CarTurnAndHierarchyBiasSystem channel semantics:
    ///   x = time,  y = behavior-time,  z/w = other.
    /// </summary>
    public sealed partial class PedestrianWalkCostFactorSystem : GameSystemBase
    {
        private EntityQuery _pedPrefabQ;  // PathfindPedestrianData
        private EntityQuery _pedLaneQ;    // PedestrianLane owners

        // per-lane previous factor applied in the live graph (to apply deltas, not stack)
        private NativeParallelHashMap<Entity, float> _prevFactorByLane;
        private float _lastKnownFactor;
        private bool _lastKnownDisable;
        private bool _pedSettingsInitialized;

        protected override void OnCreate()
        {
            base.OnCreate();

            _pedPrefabQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<PathfindPedestrianData>() }
            });

            _pedLaneQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PedestrianLane>() }
            });

            _prevFactorByLane = new NativeParallelHashMap<Entity, float>(2048, Allocator.Persistent);

            RequireForUpdate(_pedPrefabQ);
            RequireForUpdate(_pedLaneQ);

            Mod.m_Setting.onSettingsApplied += OnSettingsApplied;
        }

        private void OnSettingsApplied(Game.Settings.Setting _)
        {
            float newFactor = math.clamp(Mod.m_Setting?.ped_walk_time_factor ?? 1.0f, 0.1f, 50f);
            bool newDisable = Mod.m_Setting?.disable_ped_cost == true;

            if (_pedSettingsInitialized &&
                math.abs(newFactor - _lastKnownFactor) < 1e-4f &&
                newDisable == _lastKnownDisable) return;

            _lastKnownFactor = newFactor;
            _lastKnownDisable = newDisable;
            _pedSettingsInitialized = true;
            Enabled = true;
        }

        protected override void OnDestroy()
        {
            if (Mod.m_Setting != null)
                Mod.m_Setting.onSettingsApplied -= OnSettingsApplied;
            if (_prevFactorByLane.IsCreated) _prevFactorByLane.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven: run on next simulation tick after Enabled is set by onSettingsApplied
            return 1;
        }

        protected override void OnUpdate()
        {
            bool disable = Mod.m_Setting?.disable_ped_cost == true;
            float factor = disable
                ? 1f
                : math.clamp(Mod.m_Setting?.ped_walk_time_factor ?? 1.0f, 0.1f, 50f);
            bool baselineFactor = math.abs(factor - 1f) < 1e-4f;

            // --- 1) Prefab side: set PathfindPedestrianData.m_WalkingCost time to original * factor
            int prefabCount = 0;
            int prefabUpdateCount = 0;
            bool sawCachedOrig = false;
            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
                prefabCount = ents.Length;
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindPedestrianData>(e);

                    bool hasOrig = EntityManager.HasComponent<PedWalkCostOrig>(e);
                    if (!hasOrig && baselineFactor)
                        continue;

                    PedWalkCostOrig orig;
                    if (hasOrig)
                    {
                        sawCachedOrig = true;
                        orig = EntityManager.GetComponentData<PedWalkCostOrig>(e);
                    }
                    else
                    {
                        orig = new PedWalkCostOrig { Walk = data.m_WalkingCost };
                        EntityManager.AddComponentData(e, orig);
                    }

                    var walk = orig.Walk;
                    walk.m_Value.x = orig.Walk.m_Value.x * factor;

                    if (CostAlmostEqual(data.m_WalkingCost, walk))
                        continue;

                    data.m_WalkingCost = walk;
                    EntityManager.SetComponentData(e, data);
                    prefabUpdateCount++;
                }
            }

            bool hadLaneOverrides = _prevFactorByLane.IsCreated && _prevFactorByLane.Count() > 0;
            if (baselineFactor && !sawCachedOrig && !hadLaneOverrides)
            {
                this.Enabled = false;
                return;
            }

            // --- 2) Live graph: multiply existing pedestrian edges’ time-per-meter by ratio
            int laneUpdateCount = 0;
            bool touchedGraph = false;
            if (!baselineFactor || hadLaneOverrides)
            {
                int laneCount = _pedLaneQ.CalculateEntityCount();
                if (_prevFactorByLane.Capacity < laneCount)
                    _prevFactorByLane.Capacity = laneCount;

                var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
                var graph = pqs.GetDataContainer(out var dep); dep.Complete();

                using (var lanes = _pedLaneQ.ToEntityArray(Allocator.Temp))
                {
                    foreach (var lane in lanes)
                    {
                        if (!TryGetEdge(graph, lane, out var eid))
                            continue;

                        float prev = _prevFactorByLane.TryGetValue(lane, out var p) ? p : 1f;
                        float ratio = (prev > 0f) ? factor / prev : factor;

                        if (math.abs(ratio - 1f) < 1e-4f)
                            continue;

                        ref var costs = ref graph.SetCosts(eid);
                        costs.m_Value.x *= ratio;
                        _prevFactorByLane[lane] = factor;
                        laneUpdateCount++;
                        touchedGraph = true;
                    }
                }

                if (touchedGraph)
                    pqs.AddDataReader(default);
            }

            if (baselineFactor)
            {
                _prevFactorByLane.Clear();
            }

            if (prefabUpdateCount > 0 || laneUpdateCount > 0)
            {
                Mod.log.Info($"[RPF] PedestrianWalkCostFactorSystem: factor={factor:F3}, prefabs={prefabCount}, prefab_updates={prefabUpdateCount}, lanes_updated={laneUpdateCount}");
            }

            this.Enabled = false; // run once; re-enabled by onSettingsApplied when settings change
        }

        private static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }

        private static bool CostAlmostEqual(PathfindCosts a, PathfindCosts b)
        {
            float4 d = math.abs(a.m_Value - b.m_Value);
            return d.x < 1e-4f && d.y < 1e-4f && d.z < 1e-4f && d.w < 1e-4f;
        }
    }
}
