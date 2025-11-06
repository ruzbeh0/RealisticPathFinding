// RPFPatches_BusLanePenalty.cs
using Game.Pathfind;
using Game.Prefabs;
using HarmonyLib;
using Unity.Mathematics;
// Disambiguation aliases (optional but keeps the attributes tidy)
using NetCarLane = Game.Net.CarLane;
using NetTrackLane = Game.Net.TrackLane;
using PrefabCarLaneData = Game.Prefabs.CarLaneData;

namespace RealisticPathFinding.Patches
{
    static class BusLanePenaltyConfig
    {
        // Use your existing seconds slider; clamp so 0 disables
        public static float Seconds =>
            math.max(0f, Mod.m_Setting?.nonbus_buslane_penalty_sec ?? 0f);
    }

    // Overload 1:
    // public static PathSpecification GetCarDriveSpecification(
    //   Curve curve,
    //   Game.Net.CarLane carLane,
    //   CarLaneData carLaneData,
    //   PathfindCarData carPathfindData,
    //   float density)
    [HarmonyPatch(typeof(PathUtils), nameof(PathUtils.GetCarDriveSpecification),
        new[] { typeof(Game.Net.Curve), typeof(NetCarLane), typeof(PrefabCarLaneData), typeof(PathfindCarData), typeof(float) })]
    static class Patch_GetCarDriveSpecification_Road
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLane)
        {
            float sec = BusLanePenaltyConfig.Seconds;
            if (sec <= 0f) return;

            if ((carLane.m_Flags & Game.Net.CarLaneFlags.PublicOnly) != 0)
            {
                // Add time-like cost to the TIME channel (x)
                PathUtils.TryAddCosts(ref __result.m_Costs,
                    new PathfindCosts { m_Value = new float4(sec, 0f, 0f, 0f) });
            }
        }
    }

    // Overload 2:
    // public static PathSpecification GetCarDriveSpecification(
    //   Curve curve,
    //   Game.Net.CarLane carLane,
    //   Game.Net.TrackLane trackLaneData,
    //   CarLaneData carLaneData,
    //   PathfindCarData carPathfindData,
    //   float density)
    [HarmonyPatch(typeof(PathUtils), nameof(PathUtils.GetCarDriveSpecification),
        new[] { typeof(Game.Net.Curve), typeof(NetCarLane), typeof(NetTrackLane), typeof(PrefabCarLaneData), typeof(PathfindCarData), typeof(float) })]
    static class Patch_GetCarDriveSpecification_RoadWithTrack
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLane)
        {
            float sec = BusLanePenaltyConfig.Seconds;
            if (sec <= 0f) return;

            if ((carLane.m_Flags & Game.Net.CarLaneFlags.PublicOnly) != 0)
            {
                PathUtils.TryAddCosts(ref __result.m_Costs,
                    new PathfindCosts { m_Value = new float4(sec, 0f, 0f, 0f) });
            }
        }
    }

    // Taxis: remove the same amount so taxis are not discouraged
    // public static PathSpecification GetTaxiDriveSpecification(
    //   Curve curveData,
    //   Game.Net.CarLane carLaneData,
    //   PathfindCarData carPathfindData,
    //   PathfindTransportData transportPathfindData,
    //   float density)
    [HarmonyPatch(typeof(PathUtils), nameof(PathUtils.GetTaxiDriveSpecification),
        new[] { typeof(Game.Net.Curve), typeof(NetCarLane), typeof(PathfindCarData), typeof(PathfindTransportData), typeof(float) })]
    static class Patch_GetTaxiDriveSpecification
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLaneData)
        {
            float sec = BusLanePenaltyConfig.Seconds;
            if (sec <= 0f) return;

            if ((carLaneData.m_Flags & Game.Net.CarLaneFlags.PublicOnly) != 0)
            {
                PathUtils.TryAddCosts(ref __result.m_Costs,
                    new PathfindCosts { m_Value = new float4(-sec, 0f, 0f, 0f) });
            }
        }
    }
}
