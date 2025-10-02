using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Prefabs.Climate;
using Game.Routes;
using Game.Simulation;
using Game.UI.InGame;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.PlayerLoop;
using static Game.Rendering.Debug.RenderPrefabRenderer;
using static Game.Simulation.ClimateSystem;
using static RealisticPathFinding.Utils.RPFRouteUtils;

namespace RealisticPathFinding.Patches
{

    [HarmonyPatch]
    public class RPFPatches
    {
        // --- walking speed settings ---
        //const float OLD_WALK_MPS = 5.555556f;          // 20 km/h used by the game here
        //const float MPH_TO_MPS = 0.44704f;
        //public static float DesiredWalkMps = 3f * MPH_TO_MPS;  // 3 mph => 1.34112 m/s
        //
        //[HarmonyPatch("Game.Pathfind.PathfindTargetSeeker`1", "CalculatePedestrianTargetCost")]
        //[HarmonyPostfix]
        //public static void CalculatePedestrianTargetCost_Postfix(ref float __result, float distance)
        //{
        //    // 1) Adjust for desired walking speed: cost ∝ 1/speed
        //    float scale = OLD_WALK_MPS / math.max(0.1f, DesiredWalkMps);
        //    __result *= scale;
        //}

        [HarmonyPatch(typeof(TransportLineSystem), "OnUpdate")]
        [HarmonyPostfix]
        public static void TLS_OnUpdate_Postfix(TransportLineSystem __instance)
        {
            //Mod.log.Info("TLS_OnUpdate_Postfix");
            var em = __instance.EntityManager;

            // Query all routes that actually have segments
            using var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<Route>(),
                ComponentType.ReadOnly<RouteSegment>());

            using var routes = q.ToEntityArray(Allocator.Temp);
            for (int r = 0; r < routes.Length; r++)
            {
                var routeEnt = routes[r];

                // Resolve TransportType from first RouteVehicle prefab (if any)
                var ttype = TransportType.None;
                if (em.HasBuffer<RouteVehicle>(routeEnt))
                {
                    var rv = em.GetBuffer<RouteVehicle>(routeEnt);
                    for (int i = 0; i < rv.Length; i++)
                    {
                        var veh = rv[i].m_Vehicle;
                        if (veh == Entity.Null) continue;
                        if (!em.HasComponent<PrefabRef>(veh)) continue;

                        var prefab = em.GetComponentData<PrefabRef>(veh).m_Prefab;
                        if (prefab == Entity.Null) continue;

                        if (em.HasComponent<PublicTransportVehicleData>(prefab))
                        {
                            var vdata = em.GetComponentData<PublicTransportVehicleData>(prefab);
                            ttype = vdata.m_TransportType;
                            break;
                        }
                    }
                }

                float k = ModeCoefficients.Get(ttype);
                if (math.abs(k - 1f) < 1e-4f) continue; // no-op for this mode

                if (!em.HasBuffer<RouteSegment>(routeEnt)) continue;
                var segs = em.GetBuffer<RouteSegment>(routeEnt);

                for (int i = 0; i < segs.Length; i++)
                {
                    var segEnt = segs[i].m_Segment;
                    if (segEnt == Entity.Null) continue;
                    if (!em.HasComponent<RouteInfo>(segEnt)) continue;

                    var ri = em.GetComponentData<RouteInfo>(segEnt);
                    float newDur = ri.m_Duration * k;

                    // avoid churn for tiny diffs
                    if (math.abs(newDur - ri.m_Duration) >= 1f)
                    {
                        ri.m_Duration = newDur;
                        em.SetComponentData(segEnt, ri);

                        if (!em.HasComponent<PathfindUpdated>(segEnt))
                            em.AddComponent<PathfindUpdated>(segEnt);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ResidentAISystem.Actions), "OnUpdate")]
        [HarmonyPrefix]
        public static void Patch_Actions_OnUpdate_GuardQueues_Prefix(ResidentAISystem.Actions __instance)
        {

            if (!__instance.m_BoardingQueue.IsCreated)
                __instance.m_BoardingQueue =
                    new NativeQueue<ResidentAISystem.Boarding>(Allocator.TempJob);

            if (!__instance.m_ActionQueue.IsCreated)
                __instance.m_ActionQueue =
                    new NativeQueue<ResidentAISystem.ResidentAction>(Allocator.TempJob);
        }
    }
}
