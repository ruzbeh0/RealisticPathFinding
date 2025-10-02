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
    /// Multiplies ONLY m_WalkingCost by a user factor.
    /// - Prefab side: PathfindPedestrianData.m_WalkingCost.x *= factor
    /// - Live graph: multiply pedestrian edges' time-per-meter by (newFactor / prevFactor)
    /// </summary>
    public sealed partial class PedestrianWalkCostFactorSystem : GameSystemBase
    {
        private EntityQuery _pedPrefabQ;  // PathfindPedestrianData
        private EntityQuery _pedLaneQ;    // PedestrianLane owners

        // per-lane previous factor applied in the live graph (to apply deltas, not stack)
        private NativeParallelHashMap<Entity, float> _prevFactorByLane;

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
        }

        protected override void OnDestroy()
        {
            if (_prevFactorByLane.IsCreated) _prevFactorByLane.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Doesn’t need to run often; adjust if you want instant reaction to slider changes
            return 262144/32; // ~once per in-game day
        }

        protected override void OnUpdate()
        {
            // Read your setting (fallback to 1.0 = no change)
            float factor = Mod.m_Setting?.ped_walk_time_factor ?? 1.0f;
            factor = math.clamp(factor, 0.1f, 50f);
            if (math.abs(factor - 1f) < 1e-4f && _prevFactorByLane.IsCreated && _prevFactorByLane.Count() == 0)
                return; // nothing to do (first run and factor is 1)

            // --- 1) Prefab side: set PathfindPedestrianData.m_WalkingCost time to original * factor
            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
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

                    // Multiply ONLY the TIME channel:
                    //walk.m_Value.x = orig.Walk.m_Value.x * factor;

                    // If you want to multiply ALL channels instead, replace the line above with:
                    //walk.m_Value *= factor;
                    if(factor > 1f)
                    {
                        walk.m_Value.y = factor;
                        //walk.m_Value.x = factor;
                    }

                    data.m_WalkingCost = walk;
                    //data.m_SpawnCost.m_Value.x += factor;

                    EntityManager.SetComponentData(e, data);
                }
            }

            // --- 2) Live graph: multiply existing pedestrian edges’ time-per-meter by ratio
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

                    // TIME channel (x) *= ratio:
                    //costs.m_Value.x *= ratio;

                    // For full-vector scaling instead:
                    costs.m_Value *= ratio;
                    if (factor > 1f)
                    {
                        costs.m_Value.y = factor;
                        costs.m_Value.x = factor;
                    }

                    _prevFactorByLane[lane] = factor;
                }
            }

            // Let pathfinding pick up changes
            pqs.AddDataReader(default);
        }

        private static bool TryGetEdge(NativePathfindData data, Entity owner, out EdgeID id)
        {
            if (data.GetEdge(owner, out id)) return true;
            if (data.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }
    }
}
