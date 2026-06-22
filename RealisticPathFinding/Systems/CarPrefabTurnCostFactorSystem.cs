using Game;
using Game.Pathfind;
using Game.Prefabs;
using RealisticPathFinding.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    // Stores the prefab baseline captured before any RPF vanilla turn multiplier is applied.
    // Re-runs always derive from this snapshot so setting changes do not stack.
    public struct CarTurnCostOrig : IComponentData
    {
        public PathfindCosts Turning;
        public PathfindCosts UnsafeTurning;
        public PathfindCosts CurveAngle;
        public PathfindCosts UTurn;
        public PathfindCosts UnsafeUTurn;
    }

    /// <summary>
    /// Applies the game's built-in turn and U-turn multipliers on the prefab side.
    ///
    /// This intentionally avoids Harmony prefixes on PathUtils.GetCarDriveSpecification, because
    /// those methods are called from the game's Burst-compiled lane/path graph rebuild jobs.
    /// </summary>
    public sealed partial class CarPrefabTurnCostFactorSystem : GameSystemBase
    {
        private EntityQuery _carPrefabQ;
        private float _lastTurnMultiplier;
        private float _lastUTurnMultiplier;
        private bool _settingsInitialized;

        protected override void OnCreate()
        {
            base.OnCreate();

            _carPrefabQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<PathfindCarData>() }
            });

            RequireForUpdate(_carPrefabQ);
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
            float newTurnMultiplier = GetTurnMultiplier();
            float newUTurnMultiplier = GetUTurnMultiplier();

            if (_settingsInitialized &&
                PathfindCostUtils.AlmostEqual(newTurnMultiplier, _lastTurnMultiplier) &&
                PathfindCostUtils.AlmostEqual(newUTurnMultiplier, _lastUTurnMultiplier)) return;

            _lastTurnMultiplier = newTurnMultiplier;
            _lastUTurnMultiplier = newUTurnMultiplier;
            _settingsInitialized = true;
            Enabled = true;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven after startup: run on the next ModificationEnd tick after settings change.
            return 1;
        }

        protected override void OnUpdate()
        {
            float turnMultiplier = GetTurnMultiplier();
            float uTurnMultiplier = GetUTurnMultiplier();
            bool baseline =
                PathfindCostUtils.AlmostEqual(turnMultiplier, 1f) &&
                PathfindCostUtils.AlmostEqual(uTurnMultiplier, 1f);

            int prefabCount = 0;
            int prefabUpdates = 0;
            bool sawCachedOrig = false;

            using (var ents = _carPrefabQ.ToEntityArray(Allocator.Temp))
            {
                prefabCount = ents.Length;
                foreach (var e in ents)
                {
                    var data = EntityManager.GetComponentData<PathfindCarData>(e);

                    CarTurnCostOrig orig;
                    if (EntityManager.HasComponent<CarTurnCostOrig>(e))
                    {
                        orig = EntityManager.GetComponentData<CarTurnCostOrig>(e);
                        sawCachedOrig = true;
                    }
                    else
                    {
                        if (baseline)
                            continue;

                        orig = new CarTurnCostOrig
                        {
                            Turning = data.m_TurningCost,
                            UnsafeTurning = data.m_UnsafeTurningCost,
                            CurveAngle = data.m_CurveAngleCost,
                            UTurn = data.m_UTurnCost,
                            UnsafeUTurn = data.m_UnsafeUTurnCost
                        };
                        EntityManager.AddComponentData(e, orig);
                    }

                    var turning = orig.Turning; turning.m_Value *= turnMultiplier;
                    var unsafeTurning = orig.UnsafeTurning; unsafeTurning.m_Value *= turnMultiplier;
                    var curveAngle = orig.CurveAngle; curveAngle.m_Value *= turnMultiplier;
                    var uTurn = orig.UTurn; uTurn.m_Value *= uTurnMultiplier;
                    var unsafeUTurn = orig.UnsafeUTurn; unsafeUTurn.m_Value *= uTurnMultiplier;

                    if (PathfindCostUtils.AlmostEqual(data.m_TurningCost, turning) &&
                        PathfindCostUtils.AlmostEqual(data.m_UnsafeTurningCost, unsafeTurning) &&
                        PathfindCostUtils.AlmostEqual(data.m_CurveAngleCost, curveAngle) &&
                        PathfindCostUtils.AlmostEqual(data.m_UTurnCost, uTurn) &&
                        PathfindCostUtils.AlmostEqual(data.m_UnsafeUTurnCost, unsafeUTurn))
                        continue;

                    data.m_TurningCost = turning;
                    data.m_UnsafeTurningCost = unsafeTurning;
                    data.m_CurveAngleCost = curveAngle;
                    data.m_UTurnCost = uTurn;
                    data.m_UnsafeUTurnCost = unsafeUTurn;

                    EntityManager.SetComponentData(e, data);
                    prefabUpdates++;
                }
            }

            if (prefabUpdates > 0)
                Mod.log.Info($"[RPF] CarPrefabTurnCostFactorSystem: turn={turnMultiplier:F3}, uturn={uTurnMultiplier:F3}, prefabs_total={prefabCount}, prefab_updates={prefabUpdates}");

            // Baseline is not a no-op if cached originals exist: it may still need to restore costs.
            if (baseline && !sawCachedOrig)
            {
                Enabled = false;
                return;
            }

            Enabled = false;
        }

        private static float GetTurnMultiplier()
        {
            return math.clamp(Mod.m_Setting?.game_turn_cost_multiplier ?? 1f, 0.05f, 2f);
        }

        private static float GetUTurnMultiplier()
        {
            return math.clamp(Mod.m_Setting?.game_uturn_cost_multiplier ?? 1f, 0.05f, 2f);
        }
    }
}
