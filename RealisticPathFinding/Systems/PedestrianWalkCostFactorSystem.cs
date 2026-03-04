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
        private float _lastLoggedFactor = float.MinValue;
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
            if (Mod.m_Setting?.disable_ped_cost == true)
                return;

            // Read your setting (fallback to 1.0 = no change)
            float factor = Mod.m_Setting?.ped_walk_time_factor ?? 1.0f;
            factor = math.clamp(factor, 0.1f, 50f);
            if (math.abs(factor - 1f) < 1e-4f && _prevFactorByLane.IsCreated && _prevFactorByLane.Count() == 0)
                return; // nothing to do (first run and factor is 1)

            // --- 1) Prefab side: set PathfindPedestrianData.m_WalkingCost time to original * factor
            int prefabCount = 0;
            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
                prefabCount = ents.Length;
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindPedestrianData>(e);

                    PedWalkCostOrig orig;
                    bool hasOrig = EntityManager.HasComponent<PedWalkCostOrig>(e);
                    if (hasOrig) orig = EntityManager.GetComponentData<PedWalkCostOrig>(e);
                    else
                    {
                        orig = new PedWalkCostOrig { Walk = data.m_WalkingCost };
                        EntityManager.AddComponentData(e, orig);
                    }

                    var walk = orig.Walk;
                    walk.m_Value.x = orig.Walk.m_Value.x * factor;

                    data.m_WalkingCost = walk;

                    EntityManager.SetComponentData(e, data);
                }
            }

            // --- 2) Live graph: multiply existing pedestrian edges’ time-per-meter by ratio
            var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            var graph = pqs.GetDataContainer(out var dep); dep.Complete();

            int laneUpdateCount = 0;
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
                }
            }

            if (math.abs(factor - _lastLoggedFactor) > 1e-4f || laneUpdateCount > 0)
            {
                Mod.log.Info($"[RPF] PedestrianWalkCostFactorSystem: factor={factor:F3}, prefabs={prefabCount}, lanes updated={laneUpdateCount}");
                _lastLoggedFactor = factor;
            }

            // Let pathfinding pick up changes
            pqs.AddDataReader(default);

            this.Enabled = false; // run once; re-enabled by onSettingsApplied when settings change
        }

        private static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }
    }
}
