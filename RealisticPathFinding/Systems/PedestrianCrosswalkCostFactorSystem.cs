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
        private float _lastCross, _lastUnsafe;
        private bool _lastDisable;
        private bool _crossSettingsInitialized;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pedPrefabQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<PathfindPedestrianData>() }
            });
            RequireForUpdate(_pedPrefabQ);
            Mod.m_Setting.onSettingsApplied += OnSettingsApplied;
        }

        protected override void OnDestroy()
        {
            if (Mod.m_Setting != null)
                Mod.m_Setting.onSettingsApplied -= OnSettingsApplied;
            base.OnDestroy();
        }

        private void OnSettingsApplied(Game.Settings.Setting _)
        {
            float newCross   = math.clamp(Mod.m_Setting?.ped_crosswalk_factor        ?? 1f, 0.1f, 50f);
            float newUnsafe  = math.clamp(Mod.m_Setting?.ped_unsafe_crosswalk_factor ?? 1f, 0.1f, 50f);
            bool  newDisable = Mod.m_Setting?.disable_ped_cost == true;

            if (_crossSettingsInitialized &&
                math.abs(newCross  - _lastCross)  < 1e-4f &&
                math.abs(newUnsafe - _lastUnsafe) < 1e-4f &&
                newDisable == _lastDisable) return;

            _lastCross  = newCross;
            _lastUnsafe = newUnsafe;
            _lastDisable = newDisable;
            _crossSettingsInitialized = true;
            Enabled = true;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven: run on next simulation tick after Enabled is set by onSettingsApplied
            return 1;
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting?.disable_ped_cost == true)
            {
                this.Enabled = false;
                return;
            }

            float cross = math.clamp(Mod.m_Setting?.ped_crosswalk_factor ?? 1f, 0.1f, 50f);
            float unsafeCross = math.clamp(Mod.m_Setting?.ped_unsafe_crosswalk_factor ?? 1f, 0.1f, 50f);

            // Early out if both are 1 and this is the first run (optional)
            if (math.abs(cross - 1f) < 1e-4f && math.abs(unsafeCross - 1f) < 1e-4f)
            {
                this.Enabled = false;
                return;
            }

            Mod.log.Info($"[RPF] PedestrianCrosswalkCostFactorSystem: crosswalk={cross:F3} unsafe={unsafeCross:F3}");

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

            this.Enabled = false; // run once; re-enabled by onSettingsApplied when settings change
        }
    }
}
