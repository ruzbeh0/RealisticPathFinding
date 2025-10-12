using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static Game.Routes.RouteUtils;

namespace RealisticPathFinding.Utils
{
    public static class RPFRouteUtils
    {
        // Transit > Transfers
        public static float feeder_transfer_mult { get; set; } = 0.8f;  // < 1 lowers penalty

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsFeeder(TransportType t) => t == TransportType.Bus || t == TransportType.Tram;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTrunk(TransportType t) =>
            t == TransportType.Subway || t == TransportType.Train || t == TransportType.Ship || t == TransportType.Airplane;

        public static void StripTransportSegments<TTransportEstimateBuffer>(ref Unity.Mathematics.Random random, int length, DynamicBuffer<PathElement> path, ComponentLookup<Connected> connectedData, ComponentLookup<BoardingVehicle> boardingVehicleData, ComponentLookup<Owner> ownerData, ComponentLookup<Lane> laneData, ComponentLookup<Game.Net.ConnectionLane> connectionLaneData, ComponentLookup<Curve> curveData, ComponentLookup<PrefabRef> prefabRefData, ComponentLookup<TransportStopData> prefabTransportStopData, BufferLookup<Game.Net.SubLane> subLanes, BufferLookup<Game.Areas.Node> areaNodes, BufferLookup<Triangle> areaTriangles, ComponentLookup<Game.Routes.WaitingPassengers> waitingPassengersData, ComponentLookup<Game.Routes.CurrentRoute> currentRouteData, ComponentLookup<Game.Prefabs.PublicTransportVehicleData> publicTransportVehicleData, float kCrowd, float schedule_factor, float transfer_penalty, float feeder_trunk_transfer_penalty, float t2w_timefactor, float waiting_weight, float crowdness_stop_threashold, TTransportEstimateBuffer transportEstimateBuffer) where TTransportEstimateBuffer : unmanaged, ITransportEstimateBuffer
        {
            int num = 0;
            Entity lastBoardedRoute = Entity.Null;
            TransportType lastTransportType = TransportType.None;
            while (num < length)
            {
                PathElement pathElement = path[num++];
                Entity entity = Entity.Null;
                int num2 = -1;
                int capacity = 0; // capacity of the vehicle type last boarded at this stop; used for crowding penalty
                if (connectedData.HasComponent(pathElement.m_Target))
                {
                    Connected connected = connectedData[pathElement.m_Target];
                    if (boardingVehicleData.HasComponent(connected.m_Connected))
                    {
                        entity = connected.m_Connected;
                        num2 = num - 2;

                        var bv = boardingVehicleData[connected.m_Connected];
                        var veh = bv.m_Vehicle != Entity.Null ? bv.m_Vehicle : bv.m_Testing;
                        if (veh != Entity.Null && prefabRefData.HasComponent(veh))
                        {
                            var prefab = prefabRefData[veh].m_Prefab;
                            Game.Prefabs.PublicTransportVehicleData vdata;
                            if (publicTransportVehicleData.TryGetComponent(prefab, out vdata))
                            {
                                capacity = vdata.m_PassengerCapacity;   // <-- the number we want
                            }
                        }
                    }

                    int i;
                    for (i = num; i < length && !connectedData.HasComponent(path[i].m_Target); i++)
                    {
                    }

                    if (i > num)
                    {
                        path.RemoveRange(num, i - num);
                        length -= i - num;
                    }

                    num = i;
                }
                else if (boardingVehicleData.HasComponent(pathElement.m_Target))
                {
                    entity = pathElement.m_Target;
                    num2 = num - 2;
                }

                if (!(entity != Entity.Null) || !prefabTransportStopData.TryGetComponent(prefabRefData[entity].m_Prefab, out var componentData))
                {
                    continue;
                }

                if (num2 >= 0 && componentData.m_AccessDistance > 0f)
                {
                    PathElement pathElement2 = path[num2];
                    int length2 = path.Length;
                    if (connectionLaneData.TryGetComponent(pathElement2.m_Target, out var componentData2))
                    {
                        if ((componentData2.m_Flags & ConnectionLaneFlags.Area) != 0)
                        {
                            OffsetPathTarget_AreaLane(ref random, componentData.m_AccessDistance, num2, path, ownerData, curveData, laneData, connectionLaneData, subLanes, areaNodes, areaTriangles);
                        }
                    }
                    else if (curveData.HasComponent(pathElement2.m_Target))
                    {
                        OffsetPathTarget_EdgeLane(ref random, componentData.m_AccessDistance, num2, path, ownerData, laneData, curveData, subLanes);
                    }

                    num += path.Length - length2;
                    length += path.Length - length2;
                }

                // ... inside the loop, when you’ve identified a boarding waypoint (pathElement1.m_Target)
                Entity currentRoute = ResolveRouteForWaypoint(
                    pathElement.m_Target,
                    ref connectedData,
                    ref boardingVehicleData,
                    ref currentRouteData);

                int seconds = MathUtils.RoundToIntRandom(ref random, componentData.m_BoardingTime);
                var wp = waitingPassengersData[pathElement.m_Target];
                if (waitingPassengersData.HasComponent(pathElement.m_Target))
                {
                    seconds += wp.m_AverageWaitingTime; // already in seconds

                    // --- crowding factor using capacity normalization ---
                    float crowdingFactor = 1f;
                    if (capacity > 0)
                    {
                        if(wp.m_Count > (float)capacity* crowdness_stop_threashold)
                        {
                            // signal ~ 0 when few waiting; ~1 when roughly one vehicle-load of people wait
                            float signal = math.saturate(wp.m_Count / (float)capacity);
                            // pick your kCrowd (e.g., 0.3 = up to +30% perceived wait at one full vehicle queue)
                            crowdingFactor = 1f + kCrowd * signal;
                        } 
                    }

                    seconds = (int)math.ceil(seconds * crowdingFactor* waiting_weight);

                }

                if (seconds > 0)
                {
                    //If realistic trips exists, adjust for its time factor
                    //seconds = (int)((float)seconds/t2w_timefactor);
                    if (componentData.m_TransportType == TransportType.Train || componentData.m_TransportType == TransportType.Ship || componentData.m_TransportType == TransportType.Airplane)
                    {
                        // Boarding time is halved for trains, ships and airplanes since it is assumed that those modes run less frequently and with a set schedule that is known to the passenger
                        seconds = (int)((float)seconds*schedule_factor);
                    }

                    // ----- APPLY TRANSFER PENALTY (wait only) -----
                    bool isTransfer = (lastBoardedRoute != Entity.Null) && (currentRoute != Entity.Null) && (currentRoute != lastBoardedRoute);

                    if (isTransfer)
                    {
                        if (IsFeeder(lastTransportType) && IsTrunk(componentData.m_TransportType))
                        {
                            seconds = (int)math.ceil(seconds * feeder_trunk_transfer_penalty);
                        } else
                        {
                            seconds = (int)math.ceil(seconds * transfer_penalty);
                        }    
                    }
                        
                    if (currentRoute != Entity.Null)
                    {
                        lastBoardedRoute = currentRoute;   // first boarding sets the baseline route; later changes are transfers
                        lastTransportType = componentData.m_TransportType;
                    }

                    seconds -= wp.m_AverageWaitingTime; // subtract wating time already counted by the game in PathUtils

                    if(seconds < 0)
                    {
                        continue;
                    }

                    //Mod.log.Info($"Adding boarding time estimate of {seconds} seconds for transport type {componentData.m_TransportType}. IsTransfer:{isTransfer}");
                    transportEstimateBuffer.AddWaitEstimate(pathElement.m_Target, seconds);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool TryGetRoadSpeedMps(Entity laneOrConn,
    ref ComponentLookup<PrefabRef> prefabRefLk,
    ref ComponentLookup<RoadData> roadDataLk,
    out float speedMps)
        {
            speedMps = 0f;
            if (!prefabRefLk.HasComponent(laneOrConn)) return false;
            var prefab = prefabRefLk[laneOrConn].m_Prefab;
            if (prefab == Entity.Null || !roadDataLk.HasComponent(prefab)) return false;

            // RoadData.m_SpeedLimit is in m/s
            var rd = roadDataLk[prefab];
            speedMps = math.max(0.1f, rd.m_SpeedLimit); // guard tiny values
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float CurveLength(Entity laneOrConn,
            ref ComponentLookup<Curve> curveLk)
        {
            // lanes and connection lanes hold Curve
            if (!curveLk.HasComponent(laneOrConn)) return 0f;
            return math.max(0f, curveLk[laneOrConn].m_Length);
        }

        /// <summary>
        /// Central place to configure mode coefficients.
        /// Multiply ONLY in-vehicle time between stops by these factors.
        /// 1.0 = no change; <1.0 “feels” faster; >1.0 “feels” slower.
        /// </summary>
        public static class ModeCoefficients
        {
            // Tweak these to taste. Example defaults:
            public static readonly Dictionary<TransportType, float> Values = new()
        {
            { TransportType.Bus,      Mod.m_Setting.bus_mode_weight },
            { TransportType.Tram,     Mod.m_Setting.tram_mode_weight },
            { TransportType.Train,    Mod.m_Setting.train_mode_weight },
            { TransportType.Subway,   Mod.m_Setting.subway_mode_weight },
            { TransportType.Ship,     1.00f },
            { TransportType.Airplane, 1.00f },
            { TransportType.Helicopter, 1.00f },
            { TransportType.Taxi,     1.00f },
            { TransportType.None,     1.00f },
            { TransportType.Post,     1.00f },
            { TransportType.Rocket,   1.00f },
            { TransportType.Work,     1.00f },
        };

            public static float Get(TransportType t)
                => Values.TryGetValue(t, out var k) ? k : 1.0f;
        }

        private static void OffsetPathTarget_EdgeLane(
  ref Unity.Mathematics.Random random,
  float distance,
  int elementIndex,
  DynamicBuffer<PathElement> path,
  ComponentLookup<Owner> ownerData,
  ComponentLookup<Lane> laneData,
  ComponentLookup<Curve> curveData,
  BufferLookup<Game.Net.SubLane> subLanes)
        {
            PathElement pathElement1 = path[elementIndex];
            Curve curve = curveData[pathElement1.m_Target];
            float num1 = random.NextFloat(-distance, distance);
            if ((double)num1 >= 0.0)
            {
                Bounds1 t = new Bounds1(pathElement1.m_TargetDelta.y, 1f);
                float length1 = num1;
                if (MathUtils.ClampLength(curve.m_Bezier.xz, ref t, ref length1))
                {
                    pathElement1.m_TargetDelta.y = t.max;
                    path[elementIndex] = pathElement1;
                }
                else
                {
                    Entity target = pathElement1.m_Target;
                    if (NetUtils.FindNextLane(ref target, ref ownerData, ref laneData, ref subLanes))
                    {
                        float length2 = math.max(0.0f, num1 - length1);
                        t = new Bounds1(0.0f, 1f);
                        curve = curveData[target];
                        MathUtils.ClampLength(curve.m_Bezier.xz, ref t, length2);
                        if (elementIndex > 0 && path[elementIndex - 1].m_Target == target)
                        {
                            path.RemoveAt(elementIndex--);
                            PathElement pathElement2 = path[elementIndex];
                            pathElement2.m_TargetDelta.y = t.max;
                            path[elementIndex] = pathElement2;
                        }
                        else
                        {
                            path.Insert(elementIndex++, new PathElement()
                            {
                                m_Target = pathElement1.m_Target,
                                m_TargetDelta = new float2(pathElement1.m_TargetDelta.x, 1f)
                            });
                            pathElement1.m_Target = target;
                            pathElement1.m_TargetDelta = new float2(0.0f, t.max);
                            path[elementIndex] = pathElement1;
                        }
                    }
                    else
                    {
                        pathElement1.m_TargetDelta.y = math.saturate(pathElement1.m_TargetDelta.y + (1f - pathElement1.m_TargetDelta.y) * num1 / distance);
                        path[elementIndex] = pathElement1;
                    }
                }
            }
            else
            {
                float num2 = -num1;
                Bounds1 t = new Bounds1(0.0f, pathElement1.m_TargetDelta.y);
                float length3 = num2;
                if (MathUtils.ClampLengthInverse(curve.m_Bezier.xz, ref t, ref length3))
                {
                    pathElement1.m_TargetDelta.y = t.min;
                    path[elementIndex] = pathElement1;
                }
                else
                {
                    Entity target = pathElement1.m_Target;
                    if (NetUtils.FindPrevLane(ref target, ref ownerData, ref laneData, ref subLanes))
                    {
                        float length4 = math.max(0.0f, num2 - length3);
                        t = new Bounds1(0.0f, 1f);
                        MathUtils.ClampLengthInverse(curveData[target].m_Bezier.xz, ref t, length4);
                        if (elementIndex > 0 && path[elementIndex - 1].m_Target == target)
                        {
                            path.RemoveAt(elementIndex--);
                            PathElement pathElement3 = path[elementIndex];
                            pathElement3.m_TargetDelta.y = t.min;
                            path[elementIndex] = pathElement3;
                        }
                        else
                        {
                            path.Insert(elementIndex++, new PathElement()
                            {
                                m_Target = pathElement1.m_Target,
                                m_TargetDelta = new float2(pathElement1.m_TargetDelta.x, 0.0f)
                            });
                            pathElement1.m_Target = target;
                            pathElement1.m_TargetDelta = new float2(1f, t.min);
                            path[elementIndex] = pathElement1;
                        }
                    }
                    else
                    {
                        pathElement1.m_TargetDelta.y = math.saturate(pathElement1.m_TargetDelta.y - pathElement1.m_TargetDelta.y * num2 / distance);
                        path[elementIndex] = pathElement1;
                    }
                }
            }
        }

        private static void OffsetPathTarget_AreaLane(
    ref Unity.Mathematics.Random random,
    float distance,
    int elementIndex,
    DynamicBuffer<PathElement> path,
    ComponentLookup<Owner> ownerData,
    ComponentLookup<Curve> curveData,
    ComponentLookup<Lane> laneData,
    ComponentLookup<Game.Net.ConnectionLane> connectionLaneData,
    BufferLookup<Game.Net.SubLane> subLanes,
    BufferLookup<Game.Areas.Node> areaNodes,
    BufferLookup<Triangle> areaTriangles)
        {
            PathElement pathElement1 = path[elementIndex];
            Curve curve1 = curveData[pathElement1.m_Target];
            Entity owner = ownerData[pathElement1.m_Target].m_Owner;
            float3 position1 = MathUtils.Position(curve1.m_Bezier, pathElement1.m_TargetDelta.y);
            DynamicBuffer<Game.Areas.Node> areaNode = areaNodes[owner];
            DynamicBuffer<Triangle> areaTriangle = areaTriangles[owner];
            int index1 = -1;
            float max = 0.0f;
            float2 t1;
            for (int index2 = 0; index2 < areaTriangle.Length; ++index2)
            {
                Triangle3 triangle3 = AreaUtils.GetTriangle3(areaNode, areaTriangle[index2]);
                if ((double)MathUtils.Distance(triangle3, position1, out t1) < (double)distance)
                {
                    float num = MathUtils.Area(triangle3.xz);
                    max += num;
                    if ((double)random.NextFloat(max) < (double)num)
                        index1 = index2;
                }
            }
            if (index1 == -1)
                return;
            DynamicBuffer<Game.Net.SubLane> subLane1 = subLanes[owner];
            float2 float2_1 = random.NextFloat2((float2)1f);
            float2 t2 = math.select(float2_1, 1f - float2_1, (double)math.csum(float2_1) > 1.0);
            Triangle3 triangle3_1 = AreaUtils.GetTriangle3(areaNode, areaTriangle[index1]);
            float3 position2 = MathUtils.Position(triangle3_1, t2);
            float num1 = float.MaxValue;
            Entity endEntity = Entity.Null;
            float endCurvePos = 0.0f;
            for (int index3 = 0; index3 < subLane1.Length; ++index3)
            {
                Entity subLane2 = subLane1[index3].m_SubLane;
                if (connectionLaneData.HasComponent(subLane2) && (connectionLaneData[subLane2].m_Flags & ConnectionLaneFlags.Pedestrian) != (ConnectionLaneFlags)0)
                {
                    Curve curve2 = curveData[subLane2];
                    bool2 x = new bool2(MathUtils.Intersect(triangle3_1.xz, curve2.m_Bezier.a.xz, out t1), MathUtils.Intersect(triangle3_1.xz, curve2.m_Bezier.d.xz, out t1));
                    if (math.any(x))
                    {
                        float num2 = MathUtils.Distance(curve2.m_Bezier, position2, out float _);
                        if ((double)num2 < (double)num1)
                        {
                            float2 float2_2 = math.select(new float2(0.0f, 0.49f), math.select(new float2(0.51f, 1f), new float2(0.0f, 1f), x.x), x.y);
                            num1 = num2;
                            endEntity = subLane2;
                            endCurvePos = random.NextFloat(float2_2.x, float2_2.y);
                        }
                    }
                }
            }
            if (endEntity == Entity.Null)
            {
                //Debug.Log((object)$"Target path lane not found ({position2.x}, {position2.y}, {position2.z})");
            }
            else
            {
                int index4;
                for (index4 = elementIndex; index4 > 0; --index4)
                {
                    PathElement pathElement2 = path[index4 - 1];
                    Owner componentData;
                    if (!ownerData.TryGetComponent(pathElement2.m_Target, out componentData) || componentData.m_Owner != owner)
                        break;
                }
                NativeList<PathElement> path1 = new NativeList<PathElement>(subLane1.Length, (AllocatorManager.AllocatorHandle)Allocator.Temp);
                PathElement pathElement3 = path[index4];
                AreaUtils.FindAreaPath(ref random, path1, subLane1, pathElement3.m_Target, pathElement3.m_TargetDelta.x, endEntity, endCurvePos, laneData, curveData);
                if (path1.Length != 0)
                {
                    int x = elementIndex - index4 + 1;
                    int num3 = math.min(x, path1.Length);
                    for (int index5 = 0; index5 < num3; ++index5)
                        path[index4 + index5] = path1[index5];
                    if (path1.Length < x)
                    {
                        path.RemoveRange(index4 + path1.Length, x - path1.Length);
                    }
                    else
                    {
                        for (int index6 = x; index6 < path1.Length; ++index6)
                            path.Insert(index4 + index6, path1[index6]);
                    }
                }
                path1.Dispose();
            }
        }


        private static Entity ResolveRouteForWaypoint(
    Entity waypoint,
    ref ComponentLookup<Connected> connectedData,
    ref ComponentLookup<Game.Routes.BoardingVehicle> boardingVehicleData,
    ref ComponentLookup<Game.Routes.CurrentRoute> currentRouteData)
        {
            // Waypoint -> Connected hub with BoardingVehicle -> vehicle -> CurrentRoute.m_Route
            if (connectedData.HasComponent(waypoint))
            {
                var hub = connectedData[waypoint].m_Connected;
                if (hub != Entity.Null && boardingVehicleData.HasComponent(hub))
                {
                    var bv = boardingVehicleData[hub];
                    // prefer real vehicle; fall back to testing if needed
                    var veh = bv.m_Vehicle != Entity.Null ? bv.m_Vehicle : bv.m_Testing;
                    if (veh != Entity.Null && currentRouteData.HasComponent(veh))
                        return currentRouteData[veh].m_Route;
                }
            }
            // Some maps may tag the waypoint itself with BoardingVehicle
            if (boardingVehicleData.HasComponent(waypoint))
            {
                var bv = boardingVehicleData[waypoint];
                var veh = bv.m_Vehicle != Entity.Null ? bv.m_Vehicle : bv.m_Testing;
                if (veh != Entity.Null && currentRouteData.HasComponent(veh))
                    return currentRouteData[veh].m_Route;
            }
            return Entity.Null;
        }

    }
}
