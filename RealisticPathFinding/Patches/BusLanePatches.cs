// RPFPatches_BusLanePenalty.cs
using Game.Pathfind;
using HarmonyLib;
using Unity.Mathematics;

using NetCarLane = Game.Net.CarLane;
using NetCurve = Game.Net.Curve;
using NetMasterLane = Game.Net.MasterLane;
using NetTrackLane = Game.Net.TrackLane;
using PrefabCarLaneData = Game.Prefabs.CarLaneData;
using PrefabPathfindCarData = Game.Prefabs.PathfindCarData;
using PrefabPathfindTransportData = Game.Prefabs.PathfindTransportData;

namespace RealisticPathFinding.Patches
{
    static class BusLanePenaltyConfig
    {
        // Use your existing seconds slider; clamp so 0 disables
        public static float Seconds =>
            math.max(0f, Mod.m_Setting?.nonbus_buslane_penalty_sec ?? 0f);

        public static void AddPenalty(ref PathSpecification specification, in NetCarLane carLane)
        {
            float sec = Seconds;
            if (sec <= 0f) return;

            if ((carLane.m_Flags & Game.Net.CarLaneFlags.PublicOnly) != 0)
            {
                PathUtils.TryAddCosts(ref specification.m_Costs,
                    new PathfindCosts { m_Value = new float4(sec, 0f, 0f, 0f) });
            }
        }
    }

    // Overload 1:
    // public static PathSpecification GetCarDriveSpecification(
    //   Curve curve,
    //   Game.Net.CarLane carLane,
    //   CarLaneData carLaneData,
    //   PathfindCarData carPathfindData,
    //   float density)
    [HarmonyPatch(typeof(PathUtils), nameof(PathUtils.GetCarDriveSpecification),
        new[] { typeof(NetCurve),
        typeof(NetCarLane),
        typeof(NetMasterLane),
        typeof(PrefabCarLaneData),
        typeof(PrefabPathfindCarData),
        typeof(RuleFlags),
        typeof(float)})]
    static class Patch_GetCarDriveSpecification_Road
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLane) =>
            BusLanePenaltyConfig.AddPenalty(ref __result, in carLane);
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
        new[] { typeof(NetCurve),
        typeof(NetCarLane),
        typeof(NetMasterLane),
        typeof(NetTrackLane),
        typeof(PrefabCarLaneData),
        typeof(PrefabPathfindCarData),
        typeof(RuleFlags),
        typeof(float) })]
    static class Patch_GetCarDriveSpecification_RoadWithTrack
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLane) =>
            BusLanePenaltyConfig.AddPenalty(ref __result, in carLane);
    }

    // Taxis use a separate spec path, so apply the same non-bus lane penalty there.
    // public static PathSpecification GetTaxiDriveSpecification(
    //   Curve curveData,
    //   Game.Net.CarLane carLaneData,
    //   PathfindCarData carPathfindData,
    //   PathfindTransportData transportPathfindData,
    //   float density)
    [HarmonyPatch(typeof(PathUtils), nameof(PathUtils.GetTaxiDriveSpecification),
        new[] { typeof(NetCurve),
        typeof(NetCarLane),
        typeof(PrefabPathfindCarData),
        typeof(PrefabPathfindTransportData),
        typeof(RuleFlags),
        typeof(float) })]
    static class Patch_GetTaxiDriveSpecification
    {
        static void Postfix(ref PathSpecification __result, ref NetCarLane carLaneData) =>
            BusLanePenaltyConfig.AddPenalty(ref __result, in carLaneData);
    }
}
