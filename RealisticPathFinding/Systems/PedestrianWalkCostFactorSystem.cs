using Game;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using RealisticPathFinding.Utils;
using PedestrianLane = Game.Net.PedestrianLane;

namespace RealisticPathFinding.Systems
{
    // Stores the prefab baseline captured before any RPF multiplier is applied.
    // Re-runs always derive from this snapshot so setting changes do not stack.
    public struct PedWalkCostOrig : IComponentData
    {
        public PathfindCosts Walk;
    }

    /// <summary>
    /// Applies the pedestrian walking-cost multiplier in two places:
    /// - Prefab side: derive PathfindPedestrianData.m_WalkingCost from the cached original value.
    /// - Live graph side: apply only the delta ratio (new factor / previous factor) to pedestrian edges.
    ///
    /// The system is event-driven: OnSettingsApplied decides whether another pass is needed and
    /// Enabled only schedules that pass. Disabling pedestrian-cost adjustments still runs one
    /// restoration pass so old overrides return to the 1x baseline instead of being left behind.
    ///
    /// x = time, y = behavior-time, z/w = other cost channels.
    /// </summary>
    public sealed partial class PedestrianWalkCostFactorSystem : GameSystemBase
    {
        private EntityQuery _pedPrefabQ;  // PathfindPedestrianData
        private EntityQuery _pedLaneQ;    // PedestrianLane owners

        // Live graph edges do not keep their own original walking factor, so track the factor that
        // RPF last applied per lane and write only the delta on the next settings pass.
        private NativeParallelHashMap<Entity, float> _prevFactorByLane;
        // These fields are used only to decide whether an Apply action needs another run.
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

            // Enabled is just the scheduling flag for the next simulation tick. If the effective
            // pedestrian settings did not change, skip the rerun entirely.
            if (_pedSettingsInitialized &&
                PathfindCostUtils.AlmostEqual(newFactor, _lastKnownFactor) &&
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
            // Event-driven: run on next simulation tick after Enabled is set by OnSettingsApplied.
            return 1;
        }

        protected override void OnUpdate()
        {
            // Re-read settings when the pass executes. "Disable" means restore baseline walking costs
            // from cached originals; it is not the same thing as disabling the system scheduler.
            bool disable = Mod.m_Setting?.disable_ped_cost == true;
            float factor = disable
                ? 1f
                : math.clamp(Mod.m_Setting?.ped_walk_time_factor ?? 1.0f, 0.1f, 50f);
            bool baselineFactor = PathfindCostUtils.AlmostEqual(factor, 1f);

            // 1) Prefab side: derive the current walking cost from the cached original value.
            int prefabCount = 0;
            int prefabUpdateCount = 0;
            // True once we have found at least one cached original that could need restoration.
            bool sawCachedOrig = false;
            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
                prefabCount = ents.Length;
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindPedestrianData>(e);

                    bool hasOrig = EntityManager.HasComponent<PedWalkCostOrig>(e);
                    // A baseline pass should stay a no-op for prefabs this system never modified.
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

                    // Avoid redundant writes and log noise when the effective walking cost is unchanged.
                    if (PathfindCostUtils.AlmostEqual(data.m_WalkingCost, walk))
                        continue;

                    data.m_WalkingCost = walk;
                    EntityManager.SetComponentData(e, data);
                    prefabUpdateCount++;
                }
            }

            // If the graph does not remember any previous per-lane override, factor 1x means there is
            // nothing left to restore outside prefabs and the pass can exit quietly.
            bool hadLaneOverrides = _prevFactorByLane.IsCreated && _prevFactorByLane.Count() > 0;
            if (baselineFactor && !sawCachedOrig && !hadLaneOverrides)
            {
                this.Enabled = false;
                return;
            }

            // 2) Live graph: apply only the ratio between the new factor and the factor previously
            // applied to each lane. This keeps repeated runs non-stacking without taking a full graph
            // snapshot. Known limitation: another mod changing the same edge after capture can still be
            // overwritten on a later RPF reapply because this map only tracks RPF's own history.
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

                        // Skip lanes that already reflect the desired factor.
                        if (PathfindCostUtils.AlmostEqual(ratio, 1f))
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

            // Once every lane is back at 1x, forget the live-graph overrides so future baseline passes
            // can treat the graph as clean again.
            if (baselineFactor)
            {
                _prevFactorByLane.Clear();
            }

            if (prefabUpdateCount > 0 || laneUpdateCount > 0)
            {
                Mod.log.Info($"[RPF] PedestrianWalkCostFactorSystem: factor={factor:F3}, prefabs_total={prefabCount}, prefab_updates={prefabUpdateCount}, lanes_updated={laneUpdateCount}");
            }

            this.Enabled = false; // Run once; re-enabled by OnSettingsApplied when settings change.
        }

        private static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }

    }
}
