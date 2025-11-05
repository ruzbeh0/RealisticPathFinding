// RealisticPathFinding.Systems/PedestrianCrosswalkCostFactorSystem.cs
using Game;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    // Cache originals so repeated updates don’t stack
    public struct PedCrosswalkCostOrig : IComponentData
    {
        public PathfindCosts UnsafeCrosswalk;
        public PathfindCosts Crosswalk;
    }

    public sealed partial class PedestrianCrosswalkCostFactorSystem : GameSystemBase
    {
        private EntityQuery _pedPrefabQ;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pedPrefabQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<PathfindPedestrianData>() }
            });
            RequireForUpdate(_pedPrefabQ);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / 32; // similar cadence as your walk factor system
        }

        protected override void OnUpdate()
        {
            float cross = math.clamp(Mod.m_Setting?.ped_crosswalk_factor ?? 1f, 0.1f, 50f);
            float unsafeCross = math.clamp(Mod.m_Setting?.ped_unsafe_crosswalk_factor ?? 1f, 0.1f, 50f);

            // Early out if both are 1 and this is the first run (optional)
            if (math.abs(cross - 1f) < 1e-4f && math.abs(unsafeCross - 1f) < 1e-4f)
                return;

            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindPedestrianData>(e);

                    PedCrosswalkCostOrig orig;
                    if (EntityManager.HasComponent<PedCrosswalkCostOrig>(e))
                        orig = EntityManager.GetComponentData<PedCrosswalkCostOrig>(e);
                    else
                    {
                        orig = new PedCrosswalkCostOrig
                        {
                            UnsafeCrosswalk = data.m_UnsafeCrosswalkCost,
                            Crosswalk = data.m_CrosswalkCost
                        };
                        EntityManager.AddComponentData(e, orig);
                    }

                    var unsafeCw = orig.UnsafeCrosswalk; unsafeCw.m_Value *= unsafeCross;
                    var cw = orig.Crosswalk; cw.m_Value *= cross;

                    data.m_UnsafeCrosswalkCost = unsafeCw;
                    data.m_CrosswalkCost = cw;

                    EntityManager.SetComponentData(e, data);
                }
            }
        }
    }
}
