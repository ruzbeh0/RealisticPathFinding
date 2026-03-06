// RealisticPathFinding.Systems/PedestrianCrosswalkCostFactorSystem.cs
using Game;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using RealisticPathFinding.Utils;

namespace RealisticPathFinding.Systems
{
    // Stores the prefab baseline captured before any safe/unsafe crosswalk multiplier is applied.
    // Re-runs always derive from this snapshot so setting changes do not stack.
    public struct PedCrosswalkCostOrig : IComponentData
    {
        public PathfindCosts UnsafeCrosswalk;
        public PathfindCosts Crosswalk;
    }

    /// <summary>
    /// Applies configurable safe and unsafe crosswalk cost multipliers to pedestrian prefabs.
    ///
    /// The system is event-driven: OnSettingsApplied decides whether another pass is needed and
    /// Enabled only schedules that pass. Disabling pedestrian-cost adjustments still runs one
    /// restoration pass so cached crosswalk overrides return to the 1x baseline.
    /// </summary>
    public sealed partial class PedestrianCrosswalkCostFactorSystem : GameSystemBase
    {
        private EntityQuery _pedPrefabQ;
        // These fields are used only to decide whether an Apply action needs another run.
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
            float newCross = math.clamp(Mod.m_Setting?.ped_crosswalk_factor ?? 1f, 0.1f, 50f);
            float newUnsafe = math.clamp(Mod.m_Setting?.ped_unsafe_crosswalk_factor ?? 1f, 0.1f, 50f);
            bool newDisable = Mod.m_Setting?.disable_ped_cost == true;

            // Enabled is only the scheduling flag for the next simulation tick. Skip the rerun when
            // the effective crosswalk settings are unchanged since the last apply event.
            if (_crossSettingsInitialized &&
                PathfindCostUtils.AlmostEqual(newCross, _lastCross) &&
                PathfindCostUtils.AlmostEqual(newUnsafe, _lastUnsafe) &&
                newDisable == _lastDisable) return;

            _lastCross = newCross;
            _lastUnsafe = newUnsafe;
            _lastDisable = newDisable;
            _crossSettingsInitialized = true;
            Enabled = true;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven: run on next simulation tick after Enabled is set by OnSettingsApplied.
            return 1;
        }

        protected override void OnUpdate()
        {
            // Re-read settings when the pass executes. "Disable" means restore baseline 1x costs
            // from cached originals; it is not interchangeable with the system's Enabled flag.
            bool disable = Mod.m_Setting?.disable_ped_cost == true;
            float cross = disable ? 1f : math.clamp(Mod.m_Setting?.ped_crosswalk_factor ?? 1f, 0.1f, 50f);
            float unsafeCross = disable ? 1f : math.clamp(Mod.m_Setting?.ped_unsafe_crosswalk_factor ?? 1f, 0.1f, 50f);
            bool baseline = PathfindCostUtils.AlmostEqual(cross, 1f) && PathfindCostUtils.AlmostEqual(unsafeCross, 1f);

            int prefabUpdates = 0;
            // True once we have found at least one cached original that could need restoration.
            bool sawCachedOrig = false;
            using (var ents = _pedPrefabQ.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindPedestrianData>(e);

                    PedCrosswalkCostOrig orig;
                    if (EntityManager.HasComponent<PedCrosswalkCostOrig>(e))
                    {
                        orig = EntityManager.GetComponentData<PedCrosswalkCostOrig>(e);
                        sawCachedOrig = true;
                    }
                    else
                    {
                        // Do not capture a baseline during a no-op 1x pass. Prefabs only gain an
                        // origin component once this system actually needs to own a crosswalk change.
                        if (baseline)
                            continue;

                        orig = new PedCrosswalkCostOrig
                        {
                            UnsafeCrosswalk = data.m_UnsafeCrosswalkCost,
                            Crosswalk = data.m_CrosswalkCost
                        };
                        EntityManager.AddComponentData(e, orig);
                    }

                    var unsafeCw = orig.UnsafeCrosswalk; unsafeCw.m_Value *= unsafeCross;
                    var cw = orig.Crosswalk; cw.m_Value *= cross;

                    // Avoid redundant writes and log noise when both effective crosswalk costs match.
                    if (PathfindCostUtils.AlmostEqual(data.m_UnsafeCrosswalkCost, unsafeCw) &&
                        PathfindCostUtils.AlmostEqual(data.m_CrosswalkCost, cw))
                        continue;

                    data.m_UnsafeCrosswalkCost = unsafeCw;
                    data.m_CrosswalkCost = cw;

                    EntityManager.SetComponentData(e, data);
                    prefabUpdates++;
                }
            }

            if (prefabUpdates > 0)
                Mod.log.Info($"[RPF] PedestrianCrosswalkCostFactorSystem: crosswalk={cross:F3}, unsafe={unsafeCross:F3}, prefab_updates={prefabUpdates}");

            // Baseline is not the same thing as "no work": if cached originals exist, this pass may
            // still need to restore them. Only exit early when nothing was ever cached.
            if (baseline && !sawCachedOrig)
            {
                this.Enabled = false;
                return;
            }

            this.Enabled = false; // Run once; re-enabled by OnSettingsApplied when settings change.
        }

    }
}
