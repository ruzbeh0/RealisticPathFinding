
using Colossal.Collections;
using Colossal.Entities;
using Colossal.IO;
using Colossal.Mathematics;
using Game;
using Game.Simulation;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Creatures;
using Game.Debug;
using Game.Economy;
using Game.Events;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Reflection;
using Game.Rendering;
using Game.Routes;
using Game.Tools;
using Game.Vehicles;
using RealisticPathFinding.Utils;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Internal;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#nullable disable
namespace RealisticPathFinding.Systems;

//[CompilerGenerated]
public partial class RPFResidentAISystem : GameSystemBase
{
    private EndFrameBarrier m_EndFrameBarrier;
    private SimulationSystem m_SimulationSystem;
    private PathfindSetupSystem m_PathfindSetupSystem;
    private TimeSystem m_TimeSystem;
    private CityConfigurationSystem m_CityConfigurationSystem;
    private ResidentAISystem.Actions m_Actions;
    private PersonalCarSelectData m_PersonalCarSelectData;
    private EntityQuery m_CreatureQuery;
    private EntityQuery m_GroupCreatureQuery;
    private EntityQuery m_CarPrefabQuery;
    private EntityArchetype m_ResetTripArchetype;
    private ComponentTypeSet m_ParkedToMovingCarRemoveTypes;
    private ComponentTypeSet m_ParkedToMovingCarAddTypes;
    private ComponentTypeSet m_ParkedToMovingTrailerAddTypes;
    [EnumArray(typeof(RPFResidentAISystem.DeletedResidentType))]
    [DebugWatchValue]
    private NativeArray<int> m_DeletedResidents;
    private RPFResidentAISystem.TypeHandle __TypeHandle;
    private bool _weAllocatedQueues;

    [UnityEngine.Scripting.Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        this.m_EndFrameBarrier = this.World.GetOrCreateSystemManaged<EndFrameBarrier>();
        this.m_SimulationSystem = this.World.GetOrCreateSystemManaged<SimulationSystem>();
        this.m_PathfindSetupSystem = this.World.GetOrCreateSystemManaged<PathfindSetupSystem>();
        this.m_TimeSystem = this.World.GetOrCreateSystemManaged<TimeSystem>();
        this.m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        this.m_Actions = this.World.GetOrCreateSystemManaged<ResidentAISystem.Actions>();
        this.m_PersonalCarSelectData = new PersonalCarSelectData((SystemBase)this);
        this.m_CreatureQuery = this.GetEntityQuery(ComponentType.ReadWrite<Game.Creatures.Resident>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.Exclude<GroupMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Stumbling>());
        this.m_GroupCreatureQuery = this.GetEntityQuery(ComponentType.ReadWrite<Game.Creatures.Resident>(), ComponentType.ReadOnly<GroupMember>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Stumbling>());
        this.m_CarPrefabQuery = this.GetEntityQuery(PersonalCarSelectData.GetEntityQueryDesc());
        this.m_ResetTripArchetype = this.EntityManager.CreateArchetype(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<ResetTrip>());
        this.m_ParkedToMovingCarRemoveTypes = new ComponentTypeSet(ComponentType.ReadWrite<ParkedCar>(), ComponentType.ReadWrite<Stopped>());
        this.m_ParkedToMovingCarAddTypes = new ComponentTypeSet(new ComponentType[12]
        {
      ComponentType.ReadWrite<Moving>(),
      ComponentType.ReadWrite<TransformFrame>(),
      ComponentType.ReadWrite<InterpolatedTransform>(),
      ComponentType.ReadWrite<CarNavigation>(),
      ComponentType.ReadWrite<CarNavigationLane>(),
      ComponentType.ReadWrite<CarCurrentLane>(),
      ComponentType.ReadWrite<PathOwner>(),
      ComponentType.ReadWrite<Game.Common.Target>(),
      ComponentType.ReadWrite<Blocker>(),
      ComponentType.ReadWrite<PathElement>(),
      ComponentType.ReadWrite<Swaying>(),
      ComponentType.ReadWrite<Updated>()
        });
        // ISSUE: reference to a compiler-generated field
        this.m_ParkedToMovingTrailerAddTypes = new ComponentTypeSet(new ComponentType[6]
        {
      ComponentType.ReadWrite<Moving>(),
      ComponentType.ReadWrite<TransformFrame>(),
      ComponentType.ReadWrite<InterpolatedTransform>(),
      ComponentType.ReadWrite<CarTrailerLane>(),
      ComponentType.ReadWrite<Swaying>(),
      ComponentType.ReadWrite<Updated>()
        });
        // ISSUE: reference to a compiler-generated field
        this.m_DeletedResidents = new NativeArray<int>(7, Allocator.Persistent);
    }

    [UnityEngine.Scripting.Preserve]
    protected override void OnDestroy()
    {
        // ISSUE: reference to a compiler-generated field
        this.m_DeletedResidents.Dispose();
        if (_weAllocatedQueues)
        {
            if (m_Actions.m_BoardingQueue.IsCreated) m_Actions.m_BoardingQueue.Dispose();
            if (m_Actions.m_ActionQueue.IsCreated) m_Actions.m_ActionQueue.Dispose();
        }
        base.OnDestroy();
    }

    [UnityEngine.Scripting.Preserve]
    protected override void OnUpdate()
    {
        uint index = this.m_SimulationSystem.frameIndex % 16U /*0x10*/;
        this.m_CreatureQuery.SetSharedComponentFilter<UpdateFrame>(new UpdateFrame(index));
        this.m_GroupCreatureQuery.SetSharedComponentFilter<UpdateFrame>(new UpdateFrame(index));
        if (!m_Actions.m_BoardingQueue.IsCreated || !m_Actions.m_ActionQueue.IsCreated)
        {
            m_Actions.m_BoardingQueue = new NativeQueue<ResidentAISystem.Boarding>(Allocator.Persistent);
            m_Actions.m_ActionQueue   = new NativeQueue<ResidentAISystem.ResidentAction>(Allocator.Persistent);
            _weAllocatedQueues = true;
        }

        JobHandle jobHandle1;
        this.m_PersonalCarSelectData.PreUpdate((SystemBase)this, this.m_CityConfigurationSystem, this.m_CarPrefabQuery, Allocator.TempJob, out jobHandle1);

        // Example defaults if settings aren’t available yet
        float comfort = 600f;
        float ramp = 800f;
        float minMult = 0.60f;

        // If you have a settings object:
        if (Mod.m_Setting != null)
        {
            comfort = Mod.m_Setting.walk_long_comfort_m;
            ramp = Mod.m_Setting.walk_long_ramp_m;
            minMult = Mod.m_Setting.walk_long_min_mult;
        }

        RPFResidentAISystem.ResidentTickJob jobData = new RPFResidentAISystem.ResidentTickJob()
        {
            m_EntityType = InternalCompilerInterface.GetEntityTypeHandle(ref this.__TypeHandle.__Unity_Entities_Entity_TypeHandle, ref this.CheckedStateRef),
            m_CurrentVehicleType = InternalCompilerInterface.GetComponentTypeHandle<CurrentVehicle>(ref this.__TypeHandle.__Game_Creatures_CurrentVehicle_RO_ComponentTypeHandle, ref this.CheckedStateRef),
            m_GroupMemberType = InternalCompilerInterface.GetComponentTypeHandle<GroupMember>(ref this.__TypeHandle.__Game_Creatures_GroupMember_RO_ComponentTypeHandle, ref this.CheckedStateRef),
            m_UnspawnedType = InternalCompilerInterface.GetComponentTypeHandle<Unspawned>(ref this.__TypeHandle.__Game_Objects_Unspawned_RO_ComponentTypeHandle, ref this.CheckedStateRef),
            m_HumanNavigationType = InternalCompilerInterface.GetComponentTypeHandle<HumanNavigation>(ref this.__TypeHandle.__Game_Creatures_HumanNavigation_RO_ComponentTypeHandle, ref this.CheckedStateRef),
            m_PrefabRefType = InternalCompilerInterface.GetComponentTypeHandle<PrefabRef>(ref this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentTypeHandle, ref this.CheckedStateRef),
            m_GroupCreatureType = InternalCompilerInterface.GetBufferTypeHandle<GroupCreature>(ref this.__TypeHandle.__Game_Creatures_GroupCreature_RO_BufferTypeHandle, ref this.CheckedStateRef),
            m_CreatureType = InternalCompilerInterface.GetComponentTypeHandle<Creature>(ref this.__TypeHandle.__Game_Creatures_Creature_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_HumanType = InternalCompilerInterface.GetComponentTypeHandle<Human>(ref this.__TypeHandle.__Game_Creatures_Human_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_ResidentType = InternalCompilerInterface.GetComponentTypeHandle<Game.Creatures.Resident>(ref this.__TypeHandle.__Game_Creatures_Resident_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_CurrentLaneType = InternalCompilerInterface.GetComponentTypeHandle<HumanCurrentLane>(ref this.__TypeHandle.__Game_Creatures_HumanCurrentLane_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_TargetType = InternalCompilerInterface.GetComponentTypeHandle<Game.Common.Target>(ref this.__TypeHandle.__Game_Common_Target_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_DivertType = InternalCompilerInterface.GetComponentTypeHandle<Divert>(ref this.__TypeHandle.__Game_Creatures_Divert_RW_ComponentTypeHandle, ref this.CheckedStateRef),
            m_EntityLookup = InternalCompilerInterface.GetEntityStorageInfoLookup(ref this.__TypeHandle.__EntityStorageInfoLookup, ref this.CheckedStateRef),
            m_HumanData = InternalCompilerInterface.GetComponentLookup<Human>(ref this.__TypeHandle.__Game_Creatures_Human_RW_ComponentLookup, ref this.CheckedStateRef),
            m_TransformData = InternalCompilerInterface.GetComponentLookup<Game.Objects.Transform>(ref this.__TypeHandle.__Game_Objects_Transform_RO_ComponentLookup, ref this.CheckedStateRef),
            m_OwnerData = InternalCompilerInterface.GetComponentLookup<Owner>(ref this.__TypeHandle.__Game_Common_Owner_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CurrentVehicleData = InternalCompilerInterface.GetComponentLookup<CurrentVehicle>(ref this.__TypeHandle.__Game_Creatures_CurrentVehicle_RO_ComponentLookup, ref this.CheckedStateRef),
            m_DestroyedData = InternalCompilerInterface.GetComponentLookup<Destroyed>(ref this.__TypeHandle.__Game_Common_Destroyed_RO_ComponentLookup, ref this.CheckedStateRef),
            m_DeletedData = InternalCompilerInterface.GetComponentLookup<Deleted>(ref this.__TypeHandle.__Game_Common_Deleted_RO_ComponentLookup, ref this.CheckedStateRef),
            m_UnspawnedData = InternalCompilerInterface.GetComponentLookup<Unspawned>(ref this.__TypeHandle.__Game_Objects_Unspawned_RO_ComponentLookup, ref this.CheckedStateRef),
            m_RideNeederData = InternalCompilerInterface.GetComponentLookup<RideNeeder>(ref this.__TypeHandle.__Game_Creatures_RideNeeder_RO_ComponentLookup, ref this.CheckedStateRef),
            m_MovingData = InternalCompilerInterface.GetComponentLookup<Moving>(ref this.__TypeHandle.__Game_Objects_Moving_RO_ComponentLookup, ref this.CheckedStateRef),
            m_SpawnLocation = InternalCompilerInterface.GetComponentLookup<Game.Objects.SpawnLocation>(ref this.__TypeHandle.__Game_Objects_SpawnLocation_RO_ComponentLookup, ref this.CheckedStateRef),
            m_AnimalData = InternalCompilerInterface.GetComponentLookup<Animal>(ref this.__TypeHandle.__Game_Creatures_Animal_RO_ComponentLookup, ref this.CheckedStateRef),
            m_Dispatched = InternalCompilerInterface.GetComponentLookup<Dispatched>(ref this.__TypeHandle.__Game_Simulation_Dispatched_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ServiceRequestData = InternalCompilerInterface.GetComponentLookup<ServiceRequest>(ref this.__TypeHandle.__Game_Simulation_ServiceRequest_RO_ComponentLookup, ref this.CheckedStateRef),
            m_OnFireData = InternalCompilerInterface.GetComponentLookup<OnFire>(ref this.__TypeHandle.__Game_Events_OnFire_RO_ComponentLookup, ref this.CheckedStateRef),
            m_EdgeData = InternalCompilerInterface.GetComponentLookup<Game.Net.Edge>(ref this.__TypeHandle.__Game_Net_Edge_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CurveData = InternalCompilerInterface.GetComponentLookup<Curve>(ref this.__TypeHandle.__Game_Net_Curve_RO_ComponentLookup, ref this.CheckedStateRef),
            m_LaneData = InternalCompilerInterface.GetComponentLookup<Lane>(ref this.__TypeHandle.__Game_Net_Lane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_EdgeLaneData = InternalCompilerInterface.GetComponentLookup<EdgeLane>(ref this.__TypeHandle.__Game_Net_EdgeLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ParkingLaneData = InternalCompilerInterface.GetComponentLookup<Game.Net.ParkingLane>(ref this.__TypeHandle.__Game_Net_ParkingLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_GarageLaneData = InternalCompilerInterface.GetComponentLookup<GarageLane>(ref this.__TypeHandle.__Game_Net_GarageLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PedestrianLaneData = InternalCompilerInterface.GetComponentLookup<Game.Net.PedestrianLane>(ref this.__TypeHandle.__Game_Net_PedestrianLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ConnectionLaneData = InternalCompilerInterface.GetComponentLookup<Game.Net.ConnectionLane>(ref this.__TypeHandle.__Game_Net_ConnectionLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HangaroundLocationData = InternalCompilerInterface.GetComponentLookup<HangaroundLocation>(ref this.__TypeHandle.__Game_Areas_HangaroundLocation_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CitizenData = InternalCompilerInterface.GetComponentLookup<Citizen>(ref this.__TypeHandle.__Game_Citizens_Citizen_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HouseholdMembers = InternalCompilerInterface.GetComponentLookup<HouseholdMember>(ref this.__TypeHandle.__Game_Citizens_HouseholdMember_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HouseholdData = InternalCompilerInterface.GetComponentLookup<Household>(ref this.__TypeHandle.__Game_Citizens_Household_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CurrentBuildingData = InternalCompilerInterface.GetComponentLookup<CurrentBuilding>(ref this.__TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CurrentTransportData = InternalCompilerInterface.GetComponentLookup<CurrentTransport>(ref this.__TypeHandle.__Game_Citizens_CurrentTransport_RO_ComponentLookup, ref this.CheckedStateRef),
            m_WorkerData = InternalCompilerInterface.GetComponentLookup<Worker>(ref this.__TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CarKeeperData = InternalCompilerInterface.GetComponentLookup<CarKeeper>(ref this.__TypeHandle.__Game_Citizens_CarKeeper_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HealthProblemData = InternalCompilerInterface.GetComponentLookup<HealthProblem>(ref this.__TypeHandle.__Game_Citizens_HealthProblem_RO_ComponentLookup, ref this.CheckedStateRef),
            m_TravelPurposeData = InternalCompilerInterface.GetComponentLookup<TravelPurpose>(ref this.__TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HomelessHouseholdData = InternalCompilerInterface.GetComponentLookup<HomelessHousehold>(ref this.__TypeHandle.__Game_Citizens_HomelessHousehold_RO_ComponentLookup, ref this.CheckedStateRef),
            m_TouristHouseholds = InternalCompilerInterface.GetComponentLookup<TouristHousehold>(ref this.__TypeHandle.__Game_Citizens_TouristHousehold_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HouseholdNeedData = InternalCompilerInterface.GetComponentLookup<HouseholdNeed>(ref this.__TypeHandle.__Game_Citizens_HouseholdNeed_RO_ComponentLookup, ref this.CheckedStateRef),
            m_AttendingMeetingData = InternalCompilerInterface.GetComponentLookup<AttendingMeeting>(ref this.__TypeHandle.__Game_Citizens_AttendingMeeting_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CoordinatedMeetingData = InternalCompilerInterface.GetComponentLookup<CoordinatedMeeting>(ref this.__TypeHandle.__Game_Citizens_CoordinatedMeeting_RO_ComponentLookup, ref this.CheckedStateRef),
            m_MovingAwayData = InternalCompilerInterface.GetComponentLookup<MovingAway>(ref this.__TypeHandle.__Game_Agents_MovingAway_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ServiceAvailableData = InternalCompilerInterface.GetComponentLookup<ServiceAvailable>(ref this.__TypeHandle.__Game_Companies_ServiceAvailable_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ParkedCarData = InternalCompilerInterface.GetComponentLookup<ParkedCar>(ref this.__TypeHandle.__Game_Vehicles_ParkedCar_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PersonalCarData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.PersonalCar>(ref this.__TypeHandle.__Game_Vehicles_PersonalCar_RO_ComponentLookup, ref this.CheckedStateRef),
            m_TaxiData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.Taxi>(ref this.__TypeHandle.__Game_Vehicles_Taxi_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PublicTransportData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.PublicTransport>(ref this.__TypeHandle.__Game_Vehicles_PublicTransport_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PoliceCarData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.PoliceCar>(ref this.__TypeHandle.__Game_Vehicles_PoliceCar_RO_ComponentLookup, ref this.CheckedStateRef),
            m_AmbulanceData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.Ambulance>(ref this.__TypeHandle.__Game_Vehicles_Ambulance_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HearseData = InternalCompilerInterface.GetComponentLookup<Game.Vehicles.Hearse>(ref this.__TypeHandle.__Game_Vehicles_Hearse_RO_ComponentLookup, ref this.CheckedStateRef),
            m_ControllerData = InternalCompilerInterface.GetComponentLookup<Controller>(ref this.__TypeHandle.__Game_Vehicles_Controller_RO_ComponentLookup, ref this.CheckedStateRef),
            m_VehicleData = InternalCompilerInterface.GetComponentLookup<Vehicle>(ref this.__TypeHandle.__Game_Vehicles_Vehicle_RO_ComponentLookup, ref this.CheckedStateRef),
            m_TrainData = InternalCompilerInterface.GetComponentLookup<Train>(ref this.__TypeHandle.__Game_Vehicles_Train_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PropertyRenters = InternalCompilerInterface.GetComponentLookup<PropertyRenter>(ref this.__TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup, ref this.CheckedStateRef),
            m_AttractivenessProviderData = InternalCompilerInterface.GetComponentLookup<AttractivenessProvider>(ref this.__TypeHandle.__Game_Buildings_AttractivenessProvider_RO_ComponentLookup, ref this.CheckedStateRef),
            m_RouteConnectedData = InternalCompilerInterface.GetComponentLookup<Connected>(ref this.__TypeHandle.__Game_Routes_Connected_RO_ComponentLookup, ref this.CheckedStateRef),
            m_BoardingVehicleData = InternalCompilerInterface.GetComponentLookup<BoardingVehicle>(ref this.__TypeHandle.__Game_Routes_BoardingVehicle_RO_ComponentLookup, ref this.CheckedStateRef),
            m_CurrentRouteData = InternalCompilerInterface.GetComponentLookup<CurrentRoute>(ref this.__TypeHandle.__Game_Routes_CurrentRoute_RO_ComponentLookup, ref this.CheckedStateRef),
            m_TransportLineData = InternalCompilerInterface.GetComponentLookup<TransportLine>(ref this.__TypeHandle.__Game_Routes_TransportLine_RO_ComponentLookup, ref this.CheckedStateRef),
            m_AccessLaneLaneData = InternalCompilerInterface.GetComponentLookup<AccessLane>(ref this.__TypeHandle.__Game_Routes_AccessLane_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabRefData = InternalCompilerInterface.GetComponentLookup<PrefabRef>(ref this.__TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabCreatureData = InternalCompilerInterface.GetComponentLookup<CreatureData>(ref this.__TypeHandle.__Game_Prefabs_CreatureData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabHumanData = InternalCompilerInterface.GetComponentLookup<HumanData>(ref this.__TypeHandle.__Game_Prefabs_HumanData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabObjectGeometryData = InternalCompilerInterface.GetComponentLookup<ObjectGeometryData>(ref this.__TypeHandle.__Game_Prefabs_ObjectGeometryData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabCarData = InternalCompilerInterface.GetComponentLookup<CarData>(ref this.__TypeHandle.__Game_Prefabs_CarData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabIndustrialProcessData = InternalCompilerInterface.GetComponentLookup<IndustrialProcessData>(ref this.__TypeHandle.__Game_Prefabs_IndustrialProcessData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabTransportStopData = InternalCompilerInterface.GetComponentLookup<TransportStopData>(ref this.__TypeHandle.__Game_Prefabs_TransportStopData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PrefabSpawnLocationData = InternalCompilerInterface.GetComponentLookup<Game.Prefabs.SpawnLocationData>(ref this.__TypeHandle.__Game_Prefabs_SpawnLocationData_RO_ComponentLookup, ref this.CheckedStateRef),
            m_HouseholdAnimals = InternalCompilerInterface.GetBufferLookup<HouseholdAnimal>(ref this.__TypeHandle.__Game_Citizens_HouseholdAnimal_RO_BufferLookup, ref this.CheckedStateRef),
            m_HouseholdCitizens = InternalCompilerInterface.GetBufferLookup<HouseholdCitizen>(ref this.__TypeHandle.__Game_Citizens_HouseholdCitizen_RO_BufferLookup, ref this.CheckedStateRef),
            m_ConnectedRoutes = InternalCompilerInterface.GetBufferLookup<ConnectedRoute>(ref this.__TypeHandle.__Game_Routes_ConnectedRoute_RO_BufferLookup, ref this.CheckedStateRef),
            m_VehicleLayouts = InternalCompilerInterface.GetBufferLookup<LayoutElement>(ref this.__TypeHandle.__Game_Vehicles_LayoutElement_RO_BufferLookup, ref this.CheckedStateRef),
            m_CarNavigationLanes = InternalCompilerInterface.GetBufferLookup<CarNavigationLane>(ref this.__TypeHandle.__Game_Vehicles_CarNavigationLane_RO_BufferLookup, ref this.CheckedStateRef),
            m_ConnectedEdges = InternalCompilerInterface.GetBufferLookup<ConnectedEdge>(ref this.__TypeHandle.__Game_Net_ConnectedEdge_RO_BufferLookup, ref this.CheckedStateRef),
            m_SubLanes = InternalCompilerInterface.GetBufferLookup<Game.Net.SubLane>(ref this.__TypeHandle.__Game_Net_SubLane_RO_BufferLookup, ref this.CheckedStateRef),
            m_AreaNodes = InternalCompilerInterface.GetBufferLookup<Game.Areas.Node>(ref this.__TypeHandle.__Game_Areas_Node_RO_BufferLookup, ref this.CheckedStateRef),
            m_AreaTriangles = InternalCompilerInterface.GetBufferLookup<Triangle>(ref this.__TypeHandle.__Game_Areas_Triangle_RO_BufferLookup, ref this.CheckedStateRef),
            m_ConnectedBuildings = InternalCompilerInterface.GetBufferLookup<ConnectedBuilding>(ref this.__TypeHandle.__Game_Buildings_ConnectedBuilding_RO_BufferLookup, ref this.CheckedStateRef),
            m_Renters = InternalCompilerInterface.GetBufferLookup<Renter>(ref this.__TypeHandle.__Game_Buildings_Renter_RO_BufferLookup, ref this.CheckedStateRef),
            m_SpawnLocationElements = InternalCompilerInterface.GetBufferLookup<SpawnLocationElement>(ref this.__TypeHandle.__Game_Buildings_SpawnLocationElement_RO_BufferLookup, ref this.CheckedStateRef),
            m_Resources = InternalCompilerInterface.GetBufferLookup<Game.Economy.Resources>(ref this.__TypeHandle.__Game_Economy_Resources_RO_BufferLookup, ref this.CheckedStateRef),
            m_ServiceDispatches = InternalCompilerInterface.GetBufferLookup<ServiceDispatch>(ref this.__TypeHandle.__Game_Simulation_ServiceDispatch_RO_BufferLookup, ref this.CheckedStateRef),
            m_PrefabActivityLocationElements = InternalCompilerInterface.GetBufferLookup<ActivityLocationElement>(ref this.__TypeHandle.__Game_Prefabs_ActivityLocationElement_RO_BufferLookup, ref this.CheckedStateRef),
            m_PathOwnerData = InternalCompilerInterface.GetComponentLookup<PathOwner>(ref this.__TypeHandle.__Game_Pathfind_PathOwner_RW_ComponentLookup, ref this.CheckedStateRef),
            m_PathElements = InternalCompilerInterface.GetBufferLookup<PathElement>(ref this.__TypeHandle.__Game_Pathfind_PathElement_RW_BufferLookup, ref this.CheckedStateRef),
            m_WaitingPassengers = InternalCompilerInterface.GetComponentLookup<WaitingPassengers>(ref this.__TypeHandle.__Game_Prefabs_WaitingPassengers_RO_ComponentLookup, ref this.CheckedStateRef),
            m_PublicTransportVehicleData = InternalCompilerInterface.GetComponentLookup<PublicTransportVehicleData>(ref this.__TypeHandle.__Game_Vehicles_PublicTransportVehicle_RO_ComponentLookup, ref this.CheckedStateRef),
            m_RandomSeed = RandomSeed.Next(),
            m_SimulationFrameIndex = this.m_SimulationSystem.frameIndex,
            m_LefthandTraffic = this.m_CityConfigurationSystem.leftHandTraffic,
            m_GroupMember = false,
            m_PersonalCarSelectData = this.m_PersonalCarSelectData,
            m_ResetTripArchetype = this.m_ResetTripArchetype,
            m_ParkedToMovingCarRemoveTypes = this.m_ParkedToMovingCarRemoveTypes,
            m_ParkedToMovingCarAddTypes = this.m_ParkedToMovingCarAddTypes,
            m_ParkedToMovingTrailerAddTypes = this.m_ParkedToMovingTrailerAddTypes,
            m_DeletedResidents = this.m_DeletedResidents,
            m_TimeOfDay = this.m_TimeSystem.normalizedTime,
            kCrowd = Mod.m_Setting.crowdness_factor,
            scheduled_factor = Mod.m_Setting.scheduled_wt_factor,
            transfer_penalty = Mod.m_Setting.transfer_penalty,
            feeder_trunk_transfer_penalty = Mod.m_Setting.feeder_trunk_transfer_penalty,
            ComfortMeters = comfort,
            RampMeters = ramp,
            MinSpeedMult = minMult,
            t2w_timefactor = Time2WorkInterop.GetFactor(),
            waiting_weight = Mod.m_Setting.waiting_time_factor,
            m_PathfindQueue = this.m_PathfindSetupSystem.GetQueue((object)this, 64 /*0x40*/).AsParallelWriter(),
            m_BoardingQueue = this.m_Actions.m_BoardingQueue.AsParallelWriter(),
            m_ActionQueue = this.m_Actions.m_ActionQueue.AsParallelWriter(),
            m_CommandBuffer = this.m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter()
        };
        // ISSUE: reference to a compiler-generated field
        JobHandle dependsOn = jobData.ScheduleParallel<RPFResidentAISystem.ResidentTickJob>(this.m_CreatureQuery, JobHandle.CombineDependencies(this.Dependency, jobHandle1));
        // ISSUE: reference to a compiler-generated field
        jobData.m_GroupMember = true;
        // ISSUE: reference to a compiler-generated field
        JobHandle jobHandle2 = jobData.ScheduleParallel<RPFResidentAISystem.ResidentTickJob>(this.m_GroupCreatureQuery, dependsOn);
        // ISSUE: reference to a compiler-generated field
        this.m_PersonalCarSelectData.PostUpdate(jobHandle2);
        // ISSUE: reference to a compiler-generated field
        // ISSUE: reference to a compiler-generated method
        this.m_PathfindSetupSystem.AddQueueWriter(jobHandle2);
        // ISSUE: reference to a compiler-generated field
        this.m_EndFrameBarrier.AddJobHandleForProducer(jobHandle2);
        // ISSUE: reference to a compiler-generated field
        // ISSUE: reference to a compiler-generated field
        this.m_Actions.m_Dependency = jobHandle2;
        this.Dependency = jobHandle2;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float LongWalkSpeedMultiplier(float odMeters, float comfortMeters, float rampMeters, float minSpeedMult)
    {
        float t = math.saturate((odMeters - comfortMeters) / math.max(1f, rampMeters));
        t = t * t * (3f - 2f * t); // smoothstep
        return math.lerp(1f, minSpeedMult, t);
    }

    // Utility to get XZ (meters) from a Transform position
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float2 XZ(in float3 p) => new float2(p.x, p.z);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void __AssignQueries(ref SystemState state)
    {
        new EntityQueryBuilder((AllocatorManager.AllocatorHandle)Allocator.Temp).Dispose();
    }

    protected override void OnCreateForCompiler()
    {
        base.OnCreateForCompiler();
        // ISSUE: reference to a compiler-generated method
        this.__AssignQueries(ref this.CheckedStateRef);
        // ISSUE: reference to a compiler-generated field
        // ISSUE: reference to a compiler-generated method
        this.__TypeHandle.__AssignHandles(ref this.CheckedStateRef);
    }

    [UnityEngine.Scripting.Preserve]
    public RPFResidentAISystem()
    {
    }

    public struct Boarding
    {
        public Entity m_Passenger;
        public Entity m_Leader;
        public Entity m_Household;
        public Entity m_Vehicle;
        public Entity m_LeaderVehicle;
        public Entity m_Waypoint;
        public HumanCurrentLane m_CurrentLane;
        public CreatureVehicleFlags m_Flags;
        public float3 m_Position;
        public quaternion m_Rotation;
        public int m_TicketPrice;
        public RPFResidentAISystem.BoardingType m_Type;

        public static RPFResidentAISystem.Boarding ExitVehicle(
          Entity passenger,
          Entity household,
          Entity vehicle,
          HumanCurrentLane newCurrentLane,
          float3 position,
          quaternion rotation,
          int ticketPrice)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Household = household,
                m_Vehicle = vehicle,
                m_CurrentLane = newCurrentLane,
                m_Position = position,
                m_Rotation = rotation,
                m_TicketPrice = ticketPrice,
                m_Type = RPFResidentAISystem.BoardingType.Exit
            };
        }

        public static RPFResidentAISystem.Boarding TryEnterVehicle(
          Entity passenger,
          Entity leader,
          Entity vehicle,
          Entity leaderVehicle,
          Entity waypoint,
          float3 position,
          CreatureVehicleFlags flags)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Leader = leader,
                m_Vehicle = vehicle,
                m_LeaderVehicle = leaderVehicle,
                m_Waypoint = waypoint,
                m_Position = position,
                m_Flags = flags,
                m_Type = RPFResidentAISystem.BoardingType.TryEnter
            };
        }

        public static RPFResidentAISystem.Boarding FinishEnterVehicle(
          Entity passenger,
          Entity household,
          Entity vehicle,
          Entity controllerVehicle,
          HumanCurrentLane oldCurrentLane,
          int ticketPrice)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Household = household,
                m_Vehicle = vehicle,
                m_LeaderVehicle = controllerVehicle,
                m_CurrentLane = oldCurrentLane,
                m_TicketPrice = ticketPrice,
                m_Type = RPFResidentAISystem.BoardingType.FinishEnter
            };
        }

        public static RPFResidentAISystem.Boarding CancelEnterVehicle(Entity passenger, Entity vehicle)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Vehicle = vehicle,
                m_Type = RPFResidentAISystem.BoardingType.CancelEnter
            };
        }

        public static RPFResidentAISystem.Boarding RequireStop(
          Entity passenger,
          Entity vehicle,
          float3 position)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Vehicle = vehicle,
                m_Position = position,
                m_Type = RPFResidentAISystem.BoardingType.RequireStop
            };
        }

        public static RPFResidentAISystem.Boarding WaitTimeExceeded(Entity passenger, Entity waypoint)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Waypoint = waypoint,
                m_Type = RPFResidentAISystem.BoardingType.WaitTimeExceeded
            };
        }

        public static ResidentAISystem.Boarding WaitTimeEstimate(Entity waypoint, int seconds)
        {
            return new ResidentAISystem.Boarding()
            {
                m_Waypoint = waypoint,
                m_TicketPrice = seconds,
                m_Type = ResidentAISystem.BoardingType.WaitTimeEstimate
            };
        }

        public static RPFResidentAISystem.Boarding FinishExitVehicle(Entity passenger, Entity vehicle)
        {
            // ISSUE: object of a compiler-generated type is created
            return new RPFResidentAISystem.Boarding()
            {
                m_Passenger = passenger,
                m_Vehicle = vehicle,
                m_Type = RPFResidentAISystem.BoardingType.FinishExit
            };
        }
    }

    public struct ResidentAction
    {
        public Entity m_Citizen;
        public Entity m_Target;
        public Entity m_Household;
        public Resource m_Resource;
        public RPFResidentAISystem.ResidentActionType m_Type;
        public int m_Amount;
        public float m_Distance;
    }

    public enum BoardingType
    {
        Exit,
        TryEnter,
        FinishEnter,
        CancelEnter,
        RequireStop,
        WaitTimeExceeded,
        WaitTimeEstimate,
        FinishExit,
    }

    public enum ResidentActionType
    {
        SendMail,
        GoShopping,
    }

    private enum DeletedResidentType
    {
        StuckLoop,
        NoPathToHome,
        NoPathToHome_AlreadyOutside,
        WaitingHome_AlreadyOutside,
        NoPath_AlreadyMovingAway,
        InvalidVehicleTarget,
        Dead,
        Count,
    }

    [BurstCompile]
    private struct ResidentTickJob : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle m_EntityType;
        [ReadOnly]
        public ComponentTypeHandle<CurrentVehicle> m_CurrentVehicleType;
        [ReadOnly]
        public ComponentTypeHandle<GroupMember> m_GroupMemberType;
        [ReadOnly]
        public ComponentTypeHandle<Unspawned> m_UnspawnedType;
        [ReadOnly]
        public ComponentTypeHandle<HumanNavigation> m_HumanNavigationType;
        [ReadOnly]
        public ComponentTypeHandle<PrefabRef> m_PrefabRefType;
        [ReadOnly]
        public BufferTypeHandle<GroupCreature> m_GroupCreatureType;
        public ComponentTypeHandle<Game.Creatures.Resident> m_ResidentType;
        public ComponentTypeHandle<Creature> m_CreatureType;
        [NativeDisableContainerSafetyRestriction]
        public ComponentTypeHandle<Human> m_HumanType;
        public ComponentTypeHandle<HumanCurrentLane> m_CurrentLaneType;
        public ComponentTypeHandle<Game.Common.Target> m_TargetType;
        public ComponentTypeHandle<Divert> m_DivertType;
        [ReadOnly]
        public EntityStorageInfoLookup m_EntityLookup;
        [ReadOnly]
        public ComponentLookup<Game.Objects.Transform> m_TransformData;
        [ReadOnly]
        public ComponentLookup<Owner> m_OwnerData;
        [ReadOnly]
        public ComponentLookup<CurrentVehicle> m_CurrentVehicleData;
        [ReadOnly]
        public ComponentLookup<Destroyed> m_DestroyedData;
        [ReadOnly]
        public ComponentLookup<Deleted> m_DeletedData;
        [ReadOnly]
        public ComponentLookup<Unspawned> m_UnspawnedData;
        [ReadOnly]
        public ComponentLookup<RideNeeder> m_RideNeederData;
        [ReadOnly]
        public ComponentLookup<Dispatched> m_Dispatched;
        [ReadOnly]
        public ComponentLookup<ServiceRequest> m_ServiceRequestData;
        [ReadOnly]
        public ComponentLookup<Moving> m_MovingData;
        [ReadOnly]
        public ComponentLookup<Game.Objects.SpawnLocation> m_SpawnLocation;
        [ReadOnly]
        public ComponentLookup<Animal> m_AnimalData;
        [ReadOnly]
        public ComponentLookup<OnFire> m_OnFireData;
        [ReadOnly]
        public ComponentLookup<Game.Net.Edge> m_EdgeData;
        [ReadOnly]
        public ComponentLookup<Curve> m_CurveData;
        [ReadOnly]
        public ComponentLookup<Lane> m_LaneData;
        [ReadOnly]
        public ComponentLookup<EdgeLane> m_EdgeLaneData;
        [ReadOnly]
        public ComponentLookup<Game.Net.ParkingLane> m_ParkingLaneData;
        [ReadOnly]
        public ComponentLookup<GarageLane> m_GarageLaneData;
        [ReadOnly]
        public ComponentLookup<Game.Net.PedestrianLane> m_PedestrianLaneData;
        [ReadOnly]
        public ComponentLookup<Game.Net.ConnectionLane> m_ConnectionLaneData;
        [ReadOnly]
        public ComponentLookup<HangaroundLocation> m_HangaroundLocationData;
        [ReadOnly]
        public ComponentLookup<Citizen> m_CitizenData;
        [ReadOnly]
        public ComponentLookup<HouseholdMember> m_HouseholdMembers;
        [ReadOnly]
        public ComponentLookup<Household> m_HouseholdData;
        [ReadOnly]
        public ComponentLookup<CurrentBuilding> m_CurrentBuildingData;
        [ReadOnly]
        public ComponentLookup<CurrentTransport> m_CurrentTransportData;
        [ReadOnly]
        public ComponentLookup<Worker> m_WorkerData;
        [ReadOnly]
        public ComponentLookup<CarKeeper> m_CarKeeperData;
        [ReadOnly]
        public ComponentLookup<HealthProblem> m_HealthProblemData;
        [ReadOnly]
        public ComponentLookup<TravelPurpose> m_TravelPurposeData;
        [ReadOnly]
        public ComponentLookup<TouristHousehold> m_TouristHouseholds;
        [ReadOnly]
        public ComponentLookup<HomelessHousehold> m_HomelessHouseholdData;
        [ReadOnly]
        public ComponentLookup<HouseholdNeed> m_HouseholdNeedData;
        [ReadOnly]
        public ComponentLookup<AttendingMeeting> m_AttendingMeetingData;
        [ReadOnly]
        public ComponentLookup<CoordinatedMeeting> m_CoordinatedMeetingData;
        [ReadOnly]
        public ComponentLookup<MovingAway> m_MovingAwayData;
        [ReadOnly]
        public ComponentLookup<ServiceAvailable> m_ServiceAvailableData;
        [ReadOnly]
        public ComponentLookup<ParkedCar> m_ParkedCarData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PersonalCar> m_PersonalCarData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Taxi> m_TaxiData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PublicTransport> m_PublicTransportData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PoliceCar> m_PoliceCarData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Ambulance> m_AmbulanceData;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Hearse> m_HearseData;
        [ReadOnly]
        public ComponentLookup<Controller> m_ControllerData;
        [ReadOnly]
        public ComponentLookup<Vehicle> m_VehicleData;
        [ReadOnly]
        public ComponentLookup<Train> m_TrainData;
        [ReadOnly]
        public ComponentLookup<PropertyRenter> m_PropertyRenters;
        [ReadOnly]
        public ComponentLookup<AttractivenessProvider> m_AttractivenessProviderData;
        [ReadOnly]
        public ComponentLookup<Connected> m_RouteConnectedData;
        [ReadOnly]
        public ComponentLookup<BoardingVehicle> m_BoardingVehicleData;
        [ReadOnly]
        public ComponentLookup<CurrentRoute> m_CurrentRouteData;
        [ReadOnly]
        public ComponentLookup<TransportLine> m_TransportLineData;
        [ReadOnly]
        public ComponentLookup<AccessLane> m_AccessLaneLaneData;
        [ReadOnly]
        public ComponentLookup<PrefabRef> m_PrefabRefData;
        [ReadOnly]
        public ComponentLookup<CreatureData> m_PrefabCreatureData;
        [ReadOnly]
        public ComponentLookup<HumanData> m_PrefabHumanData;
        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> m_PrefabObjectGeometryData;
        [ReadOnly]
        public ComponentLookup<CarData> m_PrefabCarData;
        [ReadOnly]
        public ComponentLookup<IndustrialProcessData> m_PrefabIndustrialProcessData;
        [ReadOnly]
        public ComponentLookup<TransportStopData> m_PrefabTransportStopData;
        [ReadOnly]
        public ComponentLookup<Game.Prefabs.SpawnLocationData> m_PrefabSpawnLocationData;
        [ReadOnly]
        public BufferLookup<HouseholdAnimal> m_HouseholdAnimals;
        [ReadOnly]
        public BufferLookup<HouseholdCitizen> m_HouseholdCitizens;
        [ReadOnly]
        public BufferLookup<ConnectedRoute> m_ConnectedRoutes;
        [ReadOnly]
        public BufferLookup<LayoutElement> m_VehicleLayouts;
        [ReadOnly]
        public BufferLookup<CarNavigationLane> m_CarNavigationLanes;
        [ReadOnly]
        public BufferLookup<ConnectedEdge> m_ConnectedEdges;
        [ReadOnly]
        public BufferLookup<Game.Net.SubLane> m_SubLanes;
        [ReadOnly]
        public BufferLookup<Game.Areas.Node> m_AreaNodes;
        [ReadOnly]
        public BufferLookup<Triangle> m_AreaTriangles;
        [ReadOnly]
        public BufferLookup<ConnectedBuilding> m_ConnectedBuildings;
        [ReadOnly]
        public BufferLookup<Renter> m_Renters;
        [ReadOnly]
        public BufferLookup<SpawnLocationElement> m_SpawnLocationElements;
        [ReadOnly]
        public BufferLookup<Game.Economy.Resources> m_Resources;
        [ReadOnly]
        public BufferLookup<ServiceDispatch> m_ServiceDispatches;
        [ReadOnly]
        public BufferLookup<ActivityLocationElement> m_PrefabActivityLocationElements;
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Human> m_HumanData;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<PathOwner> m_PathOwnerData;
        [NativeDisableParallelForRestriction]
        public BufferLookup<PathElement> m_PathElements;
        [ReadOnly]
        public float m_TimeOfDay;
        [ReadOnly]
        public RandomSeed m_RandomSeed;
        [ReadOnly]
        public uint m_SimulationFrameIndex;
        [ReadOnly]
        public bool m_LefthandTraffic;
        [ReadOnly]
        public bool m_GroupMember;
        [ReadOnly]
        public PersonalCarSelectData m_PersonalCarSelectData;
        [ReadOnly]
        public EntityArchetype m_ResetTripArchetype;
        [ReadOnly]
        public ComponentTypeSet m_ParkedToMovingCarRemoveTypes;
        [ReadOnly]
        public ComponentTypeSet m_ParkedToMovingCarAddTypes;
        [ReadOnly]
        public ComponentTypeSet m_ParkedToMovingTrailerAddTypes;
        [ReadOnly]
        public NativeArray<int> m_DeletedResidents;
        [ReadOnly] public ComponentLookup<Game.Routes.WaitingPassengers> m_WaitingPassengers;
        [ReadOnly] public ComponentLookup<Game.Prefabs.PublicTransportVehicleData> m_PublicTransportVehicleData;
        [ReadOnly] public float t2w_timefactor;
        [ReadOnly] public float waiting_weight;
        public NativeQueue<SetupQueueItem>.ParallelWriter m_PathfindQueue;
        public NativeQueue<ResidentAISystem.Boarding>.ParallelWriter m_BoardingQueue;
        public NativeQueue<ResidentAISystem.ResidentAction>.ParallelWriter m_ActionQueue;
        public EntityCommandBuffer.ParallelWriter m_CommandBuffer;
        public float kCrowd;
        public float scheduled_factor;
        public float transfer_penalty;
        public float feeder_trunk_transfer_penalty;
        public float ComfortMeters;
        public float RampMeters;
        public float MinSpeedMult;

        public void Execute(
          in ArchetypeChunk chunk,
          int unfilteredChunkIndex,
          bool useEnabledMask,
          in v128 chunkEnabledMask)
        {
            // ISSUE: reference to a compiler-generated field
            NativeArray<Entity> nativeArray1 = chunk.GetNativeArray(this.m_EntityType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<PrefabRef> nativeArray2 = chunk.GetNativeArray<PrefabRef>(ref this.m_PrefabRefType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<Creature> nativeArray3 = chunk.GetNativeArray<Creature>(ref this.m_CreatureType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<Game.Creatures.Resident> nativeArray4 = chunk.GetNativeArray<Game.Creatures.Resident>(ref this.m_ResidentType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<Game.Common.Target> nativeArray5 = chunk.GetNativeArray<Game.Common.Target>(ref this.m_TargetType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<CurrentVehicle> nativeArray6 = chunk.GetNativeArray<CurrentVehicle>(ref this.m_CurrentVehicleType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<HumanCurrentLane> nativeArray7 = chunk.GetNativeArray<HumanCurrentLane>(ref this.m_CurrentLaneType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<HumanNavigation> nativeArray8 = chunk.GetNativeArray<HumanNavigation>(ref this.m_HumanNavigationType);
            // ISSUE: reference to a compiler-generated field
            NativeArray<Divert> nativeArray9 = chunk.GetNativeArray<Divert>(ref this.m_DivertType);
            // ISSUE: reference to a compiler-generated field
            Unity.Mathematics.Random random = this.m_RandomSeed.GetRandom(unfilteredChunkIndex);
            // ISSUE: reference to a compiler-generated field
            if (this.m_GroupMember)
            {
                // ISSUE: reference to a compiler-generated field
                NativeArray<GroupMember> nativeArray10 = chunk.GetNativeArray<GroupMember>(ref this.m_GroupMemberType);
                if (nativeArray6.Length != 0)
                {
                    for (int index = 0; index < nativeArray1.Length; ++index)
                    {
                        Entity entity = nativeArray1[index];
                        PrefabRef prefabRef = nativeArray2[index];
                        Game.Creatures.Resident resident = nativeArray4[index];
                        Creature creature = nativeArray3[index];
                        CurrentVehicle currentVehicle = nativeArray6[index];
                        Game.Common.Target target = nativeArray5[index];
                        GroupMember groupMember = nativeArray10[index];
                        HumanCurrentLane currentLane;
                        CollectionUtils.TryGet<HumanCurrentLane>(nativeArray7, index, out currentLane);
                        HumanNavigation navigation;
                        CollectionUtils.TryGet<HumanNavigation>(nativeArray8, index, out navigation);
                        Divert divert;
                        CollectionUtils.TryGet<Divert>(nativeArray9, index, out divert);
                        // ISSUE: reference to a compiler-generated field
                        bool hasPathOwner = this.m_PathOwnerData.HasComponent(entity);
                        PathOwner pathOwner = default;
                        if (hasPathOwner)
                            pathOwner = this.m_PathOwnerData[entity];
                        else
                        {
                            // Give the resident a default PathOwner so downstream code works
                            // Use the jobIndex overload of ECB since you’re inside a parallel job
                            this.m_CommandBuffer.AddComponent<PathOwner>(unfilteredChunkIndex, entity, default);
                        }
                        // ISSUE: reference to a compiler-generated field
                        ref Human local = ref this.m_HumanData.GetRefRW(entity).ValueRW;
                        // ISSUE: reference to a compiler-generated method
                        this.TickGroupMemberInVehicle(unfilteredChunkIndex, ref random, entity, prefabRef, navigation, groupMember, currentVehicle, nativeArray7.Length != 0, ref resident, ref local, ref currentLane, ref pathOwner, ref target, ref divert);
                        // ISSUE: reference to a compiler-generated method
                        this.TickQueue(ref random, ref resident, ref creature, ref currentLane);
                        // ISSUE: reference to a compiler-generated field
                        this.m_PathOwnerData[entity] = pathOwner;
                        nativeArray4[index] = resident;
                        nativeArray3[index] = creature;
                        nativeArray5[index] = target;
                        CollectionUtils.TrySet<HumanCurrentLane>(nativeArray7, index, currentLane);
                        CollectionUtils.TrySet<Divert>(nativeArray9, index, divert);
                    }
                }
                else
                {
                    // ISSUE: reference to a compiler-generated field
                    bool isUnspawned = chunk.Has<Unspawned>(ref this.m_UnspawnedType);
                    for (int index = 0; index < nativeArray1.Length; ++index)
                    {
                        Entity entity = nativeArray1[index];
                        PrefabRef prefabRef = nativeArray2[index];
                        Game.Creatures.Resident resident = nativeArray4[index];
                        Creature creature = nativeArray3[index];
                        HumanNavigation navigation = nativeArray8[index];
                        Game.Common.Target target = nativeArray5[index];
                        GroupMember groupMember = nativeArray10[index];
                        HumanCurrentLane currentLane;
                        CollectionUtils.TryGet<HumanCurrentLane>(nativeArray7, index, out currentLane);
                        Divert divert;
                        CollectionUtils.TryGet<Divert>(nativeArray9, index, out divert);
                        // ISSUE: reference to a compiler-generated field
                        bool hasPathOwner = this.m_PathOwnerData.HasComponent(entity);
                        PathOwner pathOwner = default;
                        if (hasPathOwner)
                            pathOwner = this.m_PathOwnerData[entity];
                        else
                        {
                            // Give the resident a default PathOwner so downstream code works
                            // Use the jobIndex overload of ECB since you’re inside a parallel job
                            this.m_CommandBuffer.AddComponent<PathOwner>(unfilteredChunkIndex, entity, default);
                        }
                        // ISSUE: reference to a compiler-generated field
                        ref Human local = ref this.m_HumanData.GetRefRW(entity).ValueRW;
                        // ISSUE: reference to a compiler-generated field
                        CreatureUtils.CheckUnspawned(unfilteredChunkIndex, entity, currentLane, local, isUnspawned, this.m_CommandBuffer);
                        // ISSUE: reference to a compiler-generated method
                        this.TickGroupMemberWalking(unfilteredChunkIndex, ref random, entity, prefabRef, navigation, groupMember, ref resident, ref creature, ref local, ref currentLane, ref pathOwner, ref target, ref divert);
                        // ISSUE: reference to a compiler-generated method
                        this.TickQueue(ref random, ref resident, ref creature, ref currentLane);
                        // ISSUE: reference to a compiler-generated field
                        this.m_PathOwnerData[entity] = pathOwner;
                        nativeArray4[index] = resident;
                        nativeArray3[index] = creature;
                        nativeArray5[index] = target;
                        CollectionUtils.TrySet<HumanCurrentLane>(nativeArray7, index, currentLane);
                        CollectionUtils.TrySet<Divert>(nativeArray9, index, divert);
                    }
                }
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                NativeArray<Human> nativeArray11 = chunk.GetNativeArray<Human>(ref this.m_HumanType);
                // ISSUE: reference to a compiler-generated field
                BufferAccessor<GroupCreature> bufferAccessor = chunk.GetBufferAccessor<GroupCreature>(ref this.m_GroupCreatureType);
                if (nativeArray6.Length != 0)
                {
                    for (int index = 0; index < nativeArray1.Length; ++index)
                    {
                        Entity entity = nativeArray1[index];
                        PrefabRef prefabRef = nativeArray2[index];
                        Game.Creatures.Resident resident = nativeArray4[index];
                        Creature creature = nativeArray3[index];
                        Human human = nativeArray11[index];
                        CurrentVehicle currentVehicle = nativeArray6[index];
                        Game.Common.Target target = nativeArray5[index];
                        HumanCurrentLane currentLane;
                        CollectionUtils.TryGet<HumanCurrentLane>(nativeArray7, index, out currentLane);
                        HumanNavigation navigation;
                        CollectionUtils.TryGet<HumanNavigation>(nativeArray8, index, out navigation);
                        Divert divert;
                        CollectionUtils.TryGet<Divert>(nativeArray9, index, out divert);
                        DynamicBuffer<GroupCreature> groupCreatures;
                        CollectionUtils.TryGet<GroupCreature>(bufferAccessor, index, out groupCreatures);
                        // ISSUE: reference to a compiler-generated field
                        bool hasPathOwner = this.m_PathOwnerData.HasComponent(entity);
                        PathOwner pathOwner = default;
                        if (hasPathOwner)
                            pathOwner = this.m_PathOwnerData[entity];
                        else
                        {
                            // Give the resident a default PathOwner so downstream code works
                            // Use the jobIndex overload of ECB since you’re inside a parallel job
                            this.m_CommandBuffer.AddComponent<PathOwner>(unfilteredChunkIndex, entity, default);
                        }
                        // ISSUE: reference to a compiler-generated method
                        this.TickInVehicle(unfilteredChunkIndex, ref random, entity, prefabRef, navigation, currentVehicle, nativeArray7.Length != 0, ref resident, ref creature, ref human, ref currentLane, ref pathOwner, ref target, ref divert, groupCreatures);
                        // ISSUE: reference to a compiler-generated method
                        this.TickQueue(ref random, ref resident, ref creature, ref currentLane);
                        // ISSUE: reference to a compiler-generated field
                        this.m_PathOwnerData[entity] = pathOwner;
                        nativeArray4[index] = resident;
                        nativeArray3[index] = creature;
                        nativeArray11[index] = human;
                        nativeArray5[index] = target;
                        CollectionUtils.TrySet<HumanCurrentLane>(nativeArray7, index, currentLane);
                        CollectionUtils.TrySet<Divert>(nativeArray9, index, divert);
                    }
                }
                else
                {
                    // ISSUE: reference to a compiler-generated field
                    bool isUnspawned = chunk.Has<Unspawned>(ref this.m_UnspawnedType);
                    for (int index = 0; index < nativeArray1.Length; ++index)
                    {
                        Entity entity = nativeArray1[index];
                        PrefabRef prefabRef = nativeArray2[index];
                        Game.Creatures.Resident resident = nativeArray4[index];
                        Creature creature = nativeArray3[index];
                        Human human = nativeArray11[index];
                        HumanNavigation navigation = nativeArray8[index];
                        Game.Common.Target target = nativeArray5[index];
                        HumanCurrentLane currentLane;
                        CollectionUtils.TryGet<HumanCurrentLane>(nativeArray7, index, out currentLane);
                        Divert divert;
                        CollectionUtils.TryGet<Divert>(nativeArray9, index, out divert);
                        DynamicBuffer<GroupCreature> groupCreatures;
                        CollectionUtils.TryGet<GroupCreature>(bufferAccessor, index, out groupCreatures);
                        // ISSUE: reference to a compiler-generated field
                        bool hasPathOwner = this.m_PathOwnerData.HasComponent(entity);
                        PathOwner pathOwner = default;
                        if (hasPathOwner)
                            pathOwner = this.m_PathOwnerData[entity];
                        else
                        {
                            // Give the resident a default PathOwner so downstream code works
                            // Use the jobIndex overload of ECB since you’re inside a parallel job
                            this.m_CommandBuffer.AddComponent<PathOwner>(unfilteredChunkIndex, entity, default);
                        }
                        // ISSUE: reference to a compiler-generated field
                        CreatureUtils.CheckUnspawned(unfilteredChunkIndex, entity, currentLane, human, isUnspawned, this.m_CommandBuffer);
                        // ISSUE: reference to a compiler-generated method
                        this.TickWalking(unfilteredChunkIndex, ref random, entity, prefabRef, navigation, isUnspawned, ref resident, ref creature, ref human, ref currentLane, ref pathOwner, ref target, ref divert, groupCreatures);
                        // ISSUE: reference to a compiler-generated method
                        this.TickQueue(ref random, ref resident, ref creature, ref currentLane);
                        // ISSUE: reference to a compiler-generated field
                        this.m_PathOwnerData[entity] = pathOwner;
                        nativeArray4[index] = resident;
                        nativeArray3[index] = creature;
                        nativeArray11[index] = human;
                        nativeArray5[index] = target;
                        CollectionUtils.TrySet<HumanCurrentLane>(nativeArray7, index, currentLane);
                        CollectionUtils.TrySet<Divert>(nativeArray9, index, divert);
                    }
                }
            }
        }

        private void TickGroupMemberInVehicle(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          PrefabRef prefabRef,
          HumanNavigation navigation,
          GroupMember groupMember,
          CurrentVehicle currentVehicle,
          bool hasCurrentLane,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          ref Divert divert)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_EntityLookup.Exists(currentVehicle.m_Vehicle))
            {
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.InvalidVehicleTarget);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
            }
            else
            {
                Entity entity1 = currentVehicle.m_Vehicle;
                Controller componentData1;
                // ISSUE: reference to a compiler-generated field
                if (this.m_ControllerData.TryGetComponent(currentVehicle.m_Vehicle, out componentData1) && componentData1.m_Controller != Entity.Null)
                    entity1 = componentData1.m_Controller;
                if ((currentVehicle.m_Flags & CreatureVehicleFlags.Ready) == (CreatureVehicleFlags)0)
                {
                    if (hasCurrentLane)
                    {
                        if (CreatureUtils.IsStuck(pathOwner))
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.StuckLoop);
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                            return;
                        }
                        // ISSUE: reference to a compiler-generated field
                        if (!this.m_CurrentVehicleData.HasComponent(groupMember.m_Leader))
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.CancelEnterVehicle(entity, currentVehicle.m_Vehicle, ref resident, ref human, ref currentLane, ref pathOwner);
                            return;
                        }
                        Game.Vehicles.PublicTransport componentData2;
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_PublicTransportData.TryGetComponent(entity1, out componentData2))
                        {
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_SimulationFrameIndex >= componentData2.m_DepartureFrame)
                                human.m_Flags |= HumanFlags.Run;
                            if ((componentData2.m_State & PublicTransportFlags.Boarding) == (PublicTransportFlags)0 && currentLane.m_Lane == currentVehicle.m_Vehicle)
                                currentLane.m_Flags |= CreatureLaneFlags.EndReached;
                        }
                        if (CreatureUtils.ParkingSpaceReached(currentLane) || CreatureUtils.TransportStopReached(currentLane))
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.SetEnterVehiclePath(entity, currentVehicle.m_Vehicle, groupMember, ref random, ref currentLane, ref pathOwner);
                        }
                        else if (CreatureUtils.PathEndReached(currentLane) || CreatureUtils.RequireNewPath(pathOwner) || resident.m_Timer >= 250)
                        {
                            // ISSUE: reference to a compiler-generated method
                            if (RPFResidentAISystem.ResidentTickJob.ShouldFinishEnterVehicle(navigation))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.FinishEnterVehicle(entity, currentVehicle.m_Vehicle, entity1, ref resident, ref human, ref currentLane);
                                hasCurrentLane = false;
                            }
                            else if ((currentVehicle.m_Flags & CreatureVehicleFlags.Entering) == (CreatureVehicleFlags)0)
                            {
                                currentVehicle.m_Flags |= CreatureVehicleFlags.Entering;
                                // ISSUE: reference to a compiler-generated field
                                this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
                                // ISSUE: reference to a compiler-generated field
                                this.m_CommandBuffer.AddComponent<BatchesUpdated>(jobIndex, entity, new BatchesUpdated());
                            }
                        }
                        else
                        {
                            // ISSUE: reference to a compiler-generated method
                            if (CreatureUtils.ActionLocationReached(currentLane) && this.ActionLocationReached(entity, ref resident, ref human, ref currentLane, ref pathOwner))
                                return;
                        }
                    }
                    if (!hasCurrentLane)
                    {
                        currentVehicle.m_Flags &= ~CreatureVehicleFlags.Entering;
                        currentVehicle.m_Flags |= CreatureVehicleFlags.Ready;
                        // ISSUE: reference to a compiler-generated field
                        this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
                    }
                }
                else if ((currentVehicle.m_Flags & CreatureVehicleFlags.Exiting) != (CreatureVehicleFlags)0)
                {
                    // ISSUE: reference to a compiler-generated method
                    if (RPFResidentAISystem.ResidentTickJob.ShouldFinishExitVehicle(navigation))
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.FinishExitVehicle(entity, currentVehicle.m_Vehicle, ref currentLane);
                    }
                }
                else
                {
                    // ISSUE: reference to a compiler-generated field
                    if ((resident.m_Flags & ResidentFlags.Disembarking) == ResidentFlags.None && !this.m_CurrentVehicleData.HasComponent(groupMember.m_Leader))
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.GroupLeaderDisembarking(entity, ref resident, ref pathOwner);
                    }
                    if ((resident.m_Flags & ResidentFlags.Disembarking) != ResidentFlags.None)
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.ExitVehicle(entity, jobIndex, ref random, entity1, prefabRef, currentVehicle, ref resident, ref human, ref divert, ref pathOwner);
                    }
                }
                // ISSUE: reference to a compiler-generated method
                this.UpdateMoodFlags(ref random, navigation, hasCurrentLane, ref resident, ref human, ref divert);
            }
        }

        private void TickInVehicle(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          PrefabRef prefabRef,
          HumanNavigation navigation,
          CurrentVehicle currentVehicle,
          bool hasCurrentLane,
          ref Game.Creatures.Resident resident,
          ref Creature creature,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          ref Divert divert,
          DynamicBuffer<GroupCreature> groupCreatures)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_PrefabRefData.HasComponent(currentVehicle.m_Vehicle))
            {
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.InvalidVehicleTarget);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
            }
            else
            {
                // ISSUE: reference to a compiler-generated method
                if (CreatureUtils.ResetUpdatedPath(ref pathOwner) && this.CheckPath(jobIndex, entity, prefabRef, ref random, ref creature, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident))
                {
                    // ISSUE: reference to a compiler-generated method
                    this.FindNewPath(entity, prefabRef, ref resident, ref human, ref currentLane, ref pathOwner, ref target, ref divert);
                }
                else
                {
                    Entity entity1 = currentVehicle.m_Vehicle;
                    Controller componentData1;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_ControllerData.TryGetComponent(currentVehicle.m_Vehicle, out componentData1) && componentData1.m_Controller != Entity.Null)
                        entity1 = componentData1.m_Controller;
                    if ((currentVehicle.m_Flags & CreatureVehicleFlags.Ready) == (CreatureVehicleFlags)0)
                    {
                        if (hasCurrentLane)
                        {
                            if (CreatureUtils.IsStuck(pathOwner))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.StuckLoop);
                                // ISSUE: reference to a compiler-generated field
                                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                                return;
                            }
                            Game.Vehicles.PublicTransport componentData2;
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_PublicTransportData.TryGetComponent(entity1, out componentData2))
                            {
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_SimulationFrameIndex >= componentData2.m_DepartureFrame)
                                    human.m_Flags |= HumanFlags.Run;
                                if ((componentData2.m_State & PublicTransportFlags.Boarding) == (PublicTransportFlags)0)
                                {
                                    if (currentLane.m_Lane == currentVehicle.m_Vehicle)
                                    {
                                        currentLane.m_Flags |= CreatureLaneFlags.EndReached;
                                    }
                                    else
                                    {
                                        // ISSUE: reference to a compiler-generated method
                                        this.CancelEnterVehicle(entity, currentVehicle.m_Vehicle, ref resident, ref human, ref currentLane, ref pathOwner);
                                        return;
                                    }
                                }
                            }
                            if (CreatureUtils.ParkingSpaceReached(currentLane) || CreatureUtils.TransportStopReached(currentLane))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.SetEnterVehiclePath(entity, currentVehicle.m_Vehicle, new GroupMember(), ref random, ref currentLane, ref pathOwner);
                            }
                            else if (CreatureUtils.PathEndReached(currentLane) || CreatureUtils.RequireNewPath(pathOwner) || resident.m_Timer >= 250)
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (RPFResidentAISystem.ResidentTickJob.ShouldFinishEnterVehicle(navigation))
                                {
                                    // ISSUE: reference to a compiler-generated method
                                    this.FinishEnterVehicle(entity, currentVehicle.m_Vehicle, entity1, ref resident, ref human, ref currentLane);
                                    hasCurrentLane = false;
                                }
                                else if ((currentVehicle.m_Flags & CreatureVehicleFlags.Entering) == (CreatureVehicleFlags)0)
                                {
                                    currentVehicle.m_Flags |= CreatureVehicleFlags.Entering;
                                    // ISSUE: reference to a compiler-generated field
                                    this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
                                    // ISSUE: reference to a compiler-generated field
                                    this.m_CommandBuffer.AddComponent<BatchesUpdated>(jobIndex, entity, new BatchesUpdated());
                                }
                            }
                            else
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (CreatureUtils.ActionLocationReached(currentLane) && this.ActionLocationReached(entity, ref resident, ref human, ref currentLane, ref pathOwner))
                                    return;
                            }
                        }
                        // ISSUE: reference to a compiler-generated method
                        if (!hasCurrentLane && this.HasEveryoneBoarded(groupCreatures))
                        {
                            currentVehicle.m_Flags &= ~CreatureVehicleFlags.Entering;
                            currentVehicle.m_Flags |= CreatureVehicleFlags.Ready;
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
                        }
                    }
                    else if ((currentVehicle.m_Flags & CreatureVehicleFlags.Exiting) != (CreatureVehicleFlags)0)
                    {
                        // ISSUE: reference to a compiler-generated method
                        if (RPFResidentAISystem.ResidentTickJob.ShouldFinishExitVehicle(navigation))
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.FinishExitVehicle(entity, currentVehicle.m_Vehicle, ref currentLane);
                        }
                    }
                    else
                    {
                        if ((resident.m_Flags & ResidentFlags.Disembarking) == ResidentFlags.None)
                        {
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_DestroyedData.HasComponent(entity1))
                            {
                                // ISSUE: reference to a compiler-generated field
                                if (!this.m_MovingData.HasComponent(entity1))
                                {
                                    resident.m_Flags |= ResidentFlags.Disembarking;
                                    pathOwner.m_State &= ~PathFlags.Failed;
                                    pathOwner.m_State |= PathFlags.Obsolete;
                                }
                            }
                            else
                            {
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_PersonalCarData.HasComponent(entity1))
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    Game.Vehicles.PersonalCar personalCar = this.m_PersonalCarData[entity1];
                                    if ((personalCar.m_State & PersonalCarFlags.Disembarking) != (PersonalCarFlags)0)
                                    {
                                        // ISSUE: reference to a compiler-generated method
                                        this.CurrentVehicleDisembarking(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                    }
                                    else if ((personalCar.m_State & PersonalCarFlags.Transporting) != (PersonalCarFlags)0)
                                    {
                                        // ISSUE: reference to a compiler-generated method
                                        this.CurrentVehicleTransporting(entity, entity1, ref pathOwner);
                                    }
                                }
                                else
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    if (this.m_PublicTransportData.HasComponent(entity1))
                                    {
                                        // ISSUE: reference to a compiler-generated field
                                        Game.Vehicles.PublicTransport publicTransport = this.m_PublicTransportData[entity1];
                                        if ((publicTransport.m_State & PublicTransportFlags.Boarding) != (PublicTransportFlags)0)
                                        {
                                            // ISSUE: reference to a compiler-generated method
                                            this.CurrentVehicleBoarding(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                        }
                                        else if ((publicTransport.m_State & (PublicTransportFlags.Testing | PublicTransportFlags.RequireStop)) == PublicTransportFlags.Testing)
                                        {
                                            // ISSUE: reference to a compiler-generated method
                                            this.CurrentVehicleTesting(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                        }
                                    }
                                    else
                                    {
                                        // ISSUE: reference to a compiler-generated field
                                        if (this.m_TaxiData.HasComponent(entity1))
                                        {
                                            // ISSUE: reference to a compiler-generated field
                                            Game.Vehicles.Taxi taxi = this.m_TaxiData[entity1];
                                            if ((taxi.m_State & TaxiFlags.Disembarking) != (TaxiFlags)0)
                                            {
                                                // ISSUE: reference to a compiler-generated method
                                                this.CurrentVehicleDisembarking(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                            }
                                            else if ((taxi.m_State & TaxiFlags.Transporting) != (TaxiFlags)0)
                                            {
                                                // ISSUE: reference to a compiler-generated method
                                                this.CurrentVehicleTransporting(entity, entity1, ref pathOwner);
                                            }
                                        }
                                        else
                                        {
                                            // ISSUE: reference to a compiler-generated field
                                            if (this.m_PoliceCarData.HasComponent(entity1))
                                            {
                                                // ISSUE: reference to a compiler-generated field
                                                if ((this.m_PoliceCarData[entity1].m_State & PoliceCarFlags.Disembarking) != (PoliceCarFlags)0)
                                                {
                                                    // ISSUE: reference to a compiler-generated method
                                                    this.CurrentVehicleDisembarking(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                                }
                                                else
                                                {
                                                    // ISSUE: reference to a compiler-generated method
                                                    this.CurrentVehicleTransporting(entity, entity1, ref pathOwner);
                                                }
                                            }
                                            else
                                            {
                                                // ISSUE: reference to a compiler-generated field
                                                if (this.m_AmbulanceData.HasComponent(entity1))
                                                {
                                                    // ISSUE: reference to a compiler-generated field
                                                    if ((this.m_AmbulanceData[entity1].m_State & AmbulanceFlags.Disembarking) != (AmbulanceFlags)0)
                                                    {
                                                        // ISSUE: reference to a compiler-generated method
                                                        this.CurrentVehicleDisembarking(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                                    }
                                                    else
                                                    {
                                                        // ISSUE: reference to a compiler-generated method
                                                        this.CurrentVehicleTransporting(entity, entity1, ref pathOwner);
                                                    }
                                                }
                                                else
                                                {
                                                    // ISSUE: reference to a compiler-generated field
                                                    if (this.m_HearseData.HasComponent(entity1))
                                                    {
                                                        // ISSUE: reference to a compiler-generated field
                                                        if ((this.m_HearseData[entity1].m_State & HearseFlags.Disembarking) != (HearseFlags)0)
                                                        {
                                                            // ISSUE: reference to a compiler-generated method
                                                            this.CurrentVehicleDisembarking(jobIndex, entity, entity1, ref resident, ref pathOwner, ref target);
                                                        }
                                                        else
                                                        {
                                                            // ISSUE: reference to a compiler-generated method
                                                            this.CurrentVehicleTransporting(entity, entity1, ref pathOwner);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if ((resident.m_Flags & ResidentFlags.Disembarking) != ResidentFlags.None)
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.ExitVehicle(entity, jobIndex, ref random, entity1, prefabRef, currentVehicle, ref resident, ref human, ref divert, ref pathOwner);
                        }
                        else if ((currentVehicle.m_Flags & CreatureVehicleFlags.Leader) == (CreatureVehicleFlags)0)
                        {
                            currentVehicle.m_Flags |= CreatureVehicleFlags.Leader;
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
                        }
                    }
                    // ISSUE: reference to a compiler-generated method
                    this.UpdateMoodFlags(ref random, navigation, hasCurrentLane, ref resident, ref human, ref divert);
                }
            }
        }

        private static bool ShouldFinishEnterVehicle(HumanNavigation humanNavigation)
        {
            if (humanNavigation.m_TargetActivity != (byte)10)
                return true;
            return (int)humanNavigation.m_LastActivity == (int)humanNavigation.m_TargetActivity && humanNavigation.m_TransformState != TransformState.Action;
        }

        private static bool ShouldFinishExitVehicle(HumanNavigation humanNavigation)
        {
            if (humanNavigation.m_TargetActivity != (byte)11)
                return true;
            return (int)humanNavigation.m_LastActivity == (int)humanNavigation.m_TargetActivity && humanNavigation.m_TransformState != TransformState.Action;
        }

        private void TickGroupMemberWalking(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          PrefabRef prefabRef,
          HumanNavigation navigation,
          GroupMember groupMember,
          ref Game.Creatures.Resident resident,
          ref Creature creature,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          ref Divert divert)
        {
            if ((resident.m_Flags & ResidentFlags.Disembarking) != ResidentFlags.None)
            {
                resident.m_Flags &= ~ResidentFlags.Disembarking;
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                if (divert.m_Purpose == Game.Citizens.Purpose.None && !this.m_EntityLookup.Exists(target.m_Target))
                {
                    HealthProblem componentData;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_HealthProblemData.TryGetComponent(resident.m_Citizen, out componentData) && (componentData.m_Flags & HealthProblemFlags.RequireTransport) != HealthProblemFlags.None && (componentData.m_Flags & (HealthProblemFlags.Dead | HealthProblemFlags.Injured)) != HealthProblemFlags.None)
                    {
                        if ((componentData.m_Flags & HealthProblemFlags.Dead) != HealthProblemFlags.None)
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.Dead);
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                            return;
                        }
                        // ISSUE: reference to a compiler-generated method
                        this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, Game.Citizens.Purpose.None, Entity.Null);
                        // ISSUE: reference to a compiler-generated method
                        this.WaitHere(entity, ref currentLane, ref pathOwner);
                        return;
                    }
                    // ISSUE: reference to a compiler-generated method
                    if (this.ReturnHome(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner))
                        return;
                }
                else
                {
                    if (CreatureUtils.IsStuck(pathOwner))
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.StuckLoop);
                        // ISSUE: reference to a compiler-generated field
                        this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                        return;
                    }
                    // ISSUE: reference to a compiler-generated method
                    if (CreatureUtils.ActionLocationReached(currentLane) && this.ActionLocationReached(entity, ref resident, ref human, ref currentLane, ref pathOwner))
                        return;
                }
            }
            Human componentData1;
            // ISSUE: reference to a compiler-generated field
            if ((resident.m_Flags & ResidentFlags.Arrived) == ResidentFlags.None && this.m_HumanData.TryGetComponent(groupMember.m_Leader, out componentData1))
                human.m_Flags |= componentData1.m_Flags & (HumanFlags.Run | HumanFlags.Emergency);
            // ISSUE: reference to a compiler-generated method
            this.UpdateMoodFlags(ref random, navigation, true, ref resident, ref human, ref divert);
            // ISSUE: reference to a compiler-generated field
            if (!this.m_CurrentVehicleData.HasComponent(groupMember.m_Leader) || (currentLane.m_Flags & CreatureLaneFlags.EndReached) == (CreatureLaneFlags)0)
                return;
            // ISSUE: reference to a compiler-generated field
            CurrentVehicle currentVehicle = this.m_CurrentVehicleData[groupMember.m_Leader];
            // ISSUE: reference to a compiler-generated field
            Game.Objects.Transform transform = this.m_TransformData[entity];
            Entity vehicle = currentVehicle.m_Vehicle;
            // ISSUE: reference to a compiler-generated field
            if (this.m_ControllerData.HasComponent(currentVehicle.m_Vehicle))
            {
                // ISSUE: reference to a compiler-generated field
                Controller controller = this.m_ControllerData[currentVehicle.m_Vehicle];
                if (controller.m_Controller != Entity.Null)
                    vehicle = controller.m_Controller;
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.TryEnterVehicle(entity, groupMember.m_Leader, vehicle, currentVehicle.m_Vehicle, Entity.Null, transform.m_Position, (CreatureVehicleFlags)0));
        }

        private void TickWalking(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          PrefabRef prefabRef,
          HumanNavigation navigation,
          bool isUnspawned,
          ref Game.Creatures.Resident resident,
          ref Creature creature,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          ref Divert divert,
          DynamicBuffer<GroupCreature> groupCreatures)
        {
            // ISSUE: reference to a compiler-generated method
            if (CreatureUtils.ResetUpdatedPath(ref pathOwner) && this.CheckPath(jobIndex, entity, prefabRef, ref random, ref creature, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident))
            {
                // ISSUE: reference to a compiler-generated method
                this.FindNewPath(entity, prefabRef, ref resident, ref human, ref currentLane, ref pathOwner, ref target, ref divert);
            }
            else
            {
                // ISSUE: reference to a compiler-generated method
                if (CreatureUtils.ResetUncheckedLane(ref currentLane) && this.CheckLane(jobIndex, entity, prefabRef, ref random, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident))
                {
                    // ISSUE: reference to a compiler-generated method
                    this.FindNewPath(entity, prefabRef, ref resident, ref human, ref currentLane, ref pathOwner, ref target, ref divert);
                }
                else
                {
                    // ISSUE: reference to a compiler-generated method
                    this.UpdateMoodFlags(ref random, navigation, true, ref resident, ref human, ref divert);
                    if ((resident.m_Flags & ResidentFlags.Disembarking) != ResidentFlags.None)
                    {
                        resident.m_Flags &= ~ResidentFlags.Disembarking;
                    }
                    else
                    {
                        // ISSUE: reference to a compiler-generated field
                        if (divert.m_Purpose == Game.Citizens.Purpose.None && !this.m_EntityLookup.Exists(target.m_Target))
                        {
                            // ISSUE: reference to a compiler-generated method
                            // ISSUE: reference to a compiler-generated method
                            if (this.HandleHealthProblem(jobIndex, entity, ref resident, ref currentLane, ref pathOwner) || this.ReturnHome(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner))
                                return;
                        }
                        else if (CreatureUtils.PathfindFailed(pathOwner))
                        {
                            if (CreatureUtils.IsStuck(pathOwner))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.StuckLoop);
                                // ISSUE: reference to a compiler-generated field
                                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                                return;
                            }
                            if (divert.m_Purpose != Game.Citizens.Purpose.None)
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.None, Entity.Null);
                            }
                            else
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (this.ReturnHome(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner))
                                    return;
                            }
                        }
                        else if (CreatureUtils.EndReached(currentLane))
                        {
                            if (CreatureUtils.PathEndReached(currentLane))
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (this.PathEndReached(jobIndex, entity, ref random, ref resident, ref human, ref currentLane, ref target, ref divert, ref pathOwner))
                                    return;
                            }
                            else if (CreatureUtils.ParkingSpaceReached(currentLane))
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (this.ParkingSpaceReached(jobIndex, ref random, entity, ref resident, ref currentLane, ref pathOwner, ref target, groupCreatures))
                                    return;
                            }
                            else if (CreatureUtils.TransportStopReached(currentLane))
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (this.TransportStopReached(jobIndex, ref random, entity, prefabRef, isUnspawned, ref resident, ref currentLane, ref pathOwner, ref target))
                                    return;
                            }
                            else
                            {
                                // ISSUE: reference to a compiler-generated method
                                if (CreatureUtils.ActionLocationReached(currentLane) && this.ActionLocationReached(entity, ref resident, ref human, ref currentLane, ref pathOwner))
                                    return;
                            }
                        }
                        else if ((double)currentLane.m_QueueArea.radius > 0.0)
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.QueueReached(entity, ref resident, ref currentLane, ref pathOwner);
                        }
                    }
                    if (!CreatureUtils.RequireNewPath(pathOwner))
                        return;
                    // ISSUE: reference to a compiler-generated method
                    this.FindNewPath(entity, prefabRef, ref resident, ref human, ref currentLane, ref pathOwner, ref target, ref divert);
                }
            }
        }

        private bool HandleHealthProblem(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner)
        {
            HealthProblem componentData1;
            // ISSUE: reference to a compiler-generated field
            if (!this.m_HealthProblemData.TryGetComponent(resident.m_Citizen, out componentData1) || (componentData1.m_Flags & HealthProblemFlags.RequireTransport) == HealthProblemFlags.None)
                return false;
            if ((componentData1.m_Flags & HealthProblemFlags.Dead) != HealthProblemFlags.None)
            {
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.Dead);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                return true;
            }
            if ((componentData1.m_Flags & HealthProblemFlags.Injured) != HealthProblemFlags.None)
            {
                // ISSUE: reference to a compiler-generated method
                this.WaitHere(entity, ref currentLane, ref pathOwner);
                return true;
            }
            CurrentBuilding componentData2;
            // ISSUE: reference to a compiler-generated field
            if ((componentData1.m_Flags & HealthProblemFlags.Sick) == HealthProblemFlags.None || !this.m_CurrentBuildingData.TryGetComponent(resident.m_Citizen, out componentData2) || !(componentData2.m_CurrentBuilding != Entity.Null))
                return false;
            // ISSUE: reference to a compiler-generated method
            this.WaitHere(entity, ref currentLane, ref pathOwner);
            return true;
        }

        private void UpdateMoodFlags(
          ref Unity.Mathematics.Random random,
          HumanNavigation navigation,
          bool hasCurrentLane,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref Divert divert)
        {
            if (hasCurrentLane && (resident.m_Flags & ResidentFlags.Arrived) == ResidentFlags.None && (double)navigation.m_MaxSpeed < 0.10000000149011612)
            {
                if ((human.m_Flags & HumanFlags.Waiting) == (HumanFlags)0 && random.NextInt(10) == 0)
                    human.m_Flags |= HumanFlags.Waiting;
            }
            else
                human.m_Flags &= ~HumanFlags.Waiting;
            if (divert.m_Purpose == Game.Citizens.Purpose.Safety)
                human.m_Flags |= HumanFlags.Angry;
            else if ((human.m_Flags & HumanFlags.Angry) != (HumanFlags)0 && random.NextInt(10) == 0)
                human.m_Flags &= ~HumanFlags.Angry;
            if (random.NextInt(100) != 0)
                return;
            int num1 = random.NextInt(20, 40);
            int num2 = random.NextInt(60, 80 /*0x50*/);
            Citizen componentData;
            // ISSUE: reference to a compiler-generated field
            if ((!this.m_CitizenData.TryGetComponent(resident.m_Citizen, out componentData) ? random.NextInt(101) : componentData.Happiness) < num1)
            {
                human.m_Flags &= ~HumanFlags.Happy;
                human.m_Flags |= HumanFlags.Sad;
            }
            else if (componentData.Happiness > num2)
            {
                human.m_Flags &= ~HumanFlags.Sad;
                human.m_Flags |= HumanFlags.Happy;
            }
            else
                human.m_Flags &= ~(HumanFlags.Sad | HumanFlags.Happy);
        }

        private void SetEnterVehiclePath(
          Entity entity,
          Entity vehicle,
          GroupMember groupMember,
          ref Unity.Mathematics.Random random,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner)
        {
            currentLane.m_Flags &= ~(CreatureLaneFlags.ParkingSpace | CreatureLaneFlags.Transport | CreatureLaneFlags.Taxi);
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
            if (groupMember.m_Leader != Entity.Null)
            {
                pathElement.Clear();
                pathElement.Add(new PathElement(vehicle, (float2)0.0f));
                pathOwner.m_ElementIndex = 0;
            }
            else
            {
                if (pathElement.Length > pathOwner.m_ElementIndex && pathElement[pathOwner.m_ElementIndex].m_Target == vehicle)
                    return;
                if (pathOwner.m_ElementIndex > 0)
                    pathElement[--pathOwner.m_ElementIndex] = new PathElement(vehicle, (float2)0.0f);
                else
                    pathElement.Insert(pathOwner.m_ElementIndex, new PathElement(vehicle, (float2)0.0f));
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (!this.m_TransformData.HasComponent(vehicle) || !this.m_LaneData.HasComponent(currentLane.m_Lane))
                return;
            // ISSUE: reference to a compiler-generated field
            float3 position = this.m_TransformData[vehicle].m_Position;
            if (pathOwner.m_ElementIndex > 0)
                pathElement[--pathOwner.m_ElementIndex] = new PathElement(currentLane.m_Lane, (float2)currentLane.m_CurvePosition.y);
            else
                pathElement.Insert(pathOwner.m_ElementIndex, new PathElement(currentLane.m_Lane, (float2)currentLane.m_CurvePosition.y));
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            CreatureUtils.FixEnterPath(ref random, position, pathOwner.m_ElementIndex, pathElement, ref this.m_OwnerData, ref this.m_LaneData, ref this.m_EdgeLaneData, ref this.m_ConnectionLaneData, ref this.m_CurveData, ref this.m_SubLanes, ref this.m_AreaNodes, ref this.m_AreaTriangles);
        }

        private unsafe void AddDeletedResident(RPFResidentAISystem.DeletedResidentType type)
        {
            // ISSUE: reference to a compiler-generated field
            Interlocked.Increment(ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<int>(this.m_DeletedResidents), (int)type));
        }

        private void TickQueue(
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref Creature creature,
          ref HumanCurrentLane currentLane)
        {
            resident.m_Timer += random.NextInt(1, 3);
            if ((double)currentLane.m_QueueArea.radius > 0.0)
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (((double)creature.m_QueueArea.radius == 0.0 || currentLane.m_QueueEntity != creature.m_QueueEntity) && (this.m_RouteConnectedData.HasComponent(currentLane.m_QueueEntity) || this.m_BoardingVehicleData.HasComponent(currentLane.m_QueueEntity)) && (resident.m_Flags & ResidentFlags.WaitingTransport) == ResidentFlags.None)
                {
                    resident.m_Flags |= ResidentFlags.WaitingTransport;
                    resident.m_Timer = 0;
                }
                creature.m_QueueEntity = currentLane.m_QueueEntity;
                creature.m_QueueArea = currentLane.m_QueueArea;
            }
            else
            {
                creature.m_QueueEntity = Entity.Null;
                creature.m_QueueArea = new Sphere3();
            }
        }

        private void QueueReached(
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner)
        {
            if ((resident.m_Flags & ResidentFlags.WaitingTransport) == ResidentFlags.None || resident.m_Timer < 5000)
                return;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.WaitTimeExceeded(entity, currentLane.m_QueueEntity));
            // ISSUE: reference to a compiler-generated field
            if (this.m_BoardingVehicleData.HasComponent(currentLane.m_QueueEntity))
                resident.m_Flags |= ResidentFlags.IgnoreTaxi;
            else
                resident.m_Flags |= ResidentFlags.IgnoreTransport;
            pathOwner.m_State &= ~PathFlags.Failed;
            pathOwner.m_State |= PathFlags.Obsolete;
        }

        private void WaitHere(Entity entity, ref HumanCurrentLane currentLane, ref PathOwner pathOwner)
        {
            currentLane.m_CurvePosition.y = currentLane.m_CurvePosition.x;
            pathOwner.m_ElementIndex = 0;
            // ISSUE: reference to a compiler-generated field
            this.m_PathElements[entity].Clear();
        }

        private void ExitVehicle(
          Entity entity,
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity controllerVehicle,
          PrefabRef prefabRef,
          CurrentVehicle currentVehicle,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated method
            Entity household = this.GetHousehold(resident);
            int ticketPrice = 0;
            // ISSUE: reference to a compiler-generated field
            if ((currentVehicle.m_Flags & CreatureVehicleFlags.Leader) != (CreatureVehicleFlags)0 && this.m_TaxiData.HasComponent(controllerVehicle))
            {
                // ISSUE: reference to a compiler-generated field
                Game.Vehicles.Taxi taxi = this.m_TaxiData[controllerVehicle];
                ticketPrice = math.select((int)taxi.m_CurrentFee, (int)-taxi.m_CurrentFee, (taxi.m_State & TaxiFlags.FromOutside) > (TaxiFlags)0);
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_TransformData.HasComponent(currentVehicle.m_Vehicle))
            {
                // ISSUE: reference to a compiler-generated field
                Game.Objects.Transform vehicleTransform = this.m_TransformData[currentVehicle.m_Vehicle];
                Game.Objects.Transform transform = vehicleTransform;
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
                HumanCurrentLane newCurrentLane = new HumanCurrentLane();
                float3 targetPosition = transform.m_Position;
                if (pathOwner.m_ElementIndex < pathElement1.Length && (pathOwner.m_State & PathFlags.Obsolete) == (PathFlags)0)
                {
                    PathElement pathElement2 = pathElement1[pathOwner.m_ElementIndex];
                    Curve componentData1;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_CurveData.TryGetComponent(pathElement2.m_Target, out componentData1))
                    {
                        targetPosition = MathUtils.Position(componentData1.m_Bezier, pathElement2.m_TargetDelta.x);
                    }
                    else
                    {
                        Game.Objects.Transform componentData2;
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_TransformData.TryGetComponent(pathElement2.m_Target, out componentData2))
                            targetPosition = componentData2.m_Position;
                    }
                }
                BufferLookup<SubMeshGroup> subMeshGroupBuffers = new BufferLookup<SubMeshGroup>();
                BufferLookup<CharacterElement> characterElementBuffers = new BufferLookup<CharacterElement>();
                BufferLookup<SubMesh> subMeshBuffers = new BufferLookup<SubMesh>();
                BufferLookup<Game.Prefabs.AnimationClip> animationClipBuffers = new BufferLookup<Game.Prefabs.AnimationClip>();
                BufferLookup<AnimationMotion> animationMotionBuffers = new BufferLookup<AnimationMotion>();
                bool isDriver = (currentVehicle.m_Flags & CreatureVehicleFlags.Driver) > (CreatureVehicleFlags)0;
                ActivityCondition conditions = CreatureUtils.GetConditions(human);
                ActivityMask activityMask;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                Game.Objects.Transform vehicleDoorPosition = CreatureUtils.GetVehicleDoorPosition(ref random, ActivityType.Exit, conditions, vehicleTransform, targetPosition, isDriver, this.m_LefthandTraffic, prefabRef.m_Prefab, currentVehicle.m_Vehicle, new DynamicBuffer<MeshGroup>(), ref this.m_PublicTransportData, ref this.m_TrainData, ref this.m_ControllerData, ref this.m_PrefabRefData, ref this.m_PrefabCarData, ref this.m_PrefabActivityLocationElements, ref subMeshGroupBuffers, ref characterElementBuffers, ref subMeshBuffers, ref animationClipBuffers, ref animationMotionBuffers, out activityMask, out AnimatedPropID _);
                if (pathOwner.m_ElementIndex < pathElement1.Length && (pathOwner.m_State & PathFlags.Obsolete) == (PathFlags)0)
                {
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    CreatureUtils.FixPathStart(ref random, vehicleDoorPosition.m_Position, pathOwner.m_ElementIndex, pathElement1, ref this.m_OwnerData, ref this.m_LaneData, ref this.m_EdgeLaneData, ref this.m_ConnectionLaneData, ref this.m_CurveData, ref this.m_SubLanes, ref this.m_AreaNodes, ref this.m_AreaTriangles);
                    PathElement pathElement3 = pathElement1[pathOwner.m_ElementIndex];
                    CreatureLaneFlags flags1 = (CreatureLaneFlags)0;
                    if ((double)pathElement3.m_TargetDelta.y < (double)pathElement3.m_TargetDelta.x)
                        flags1 |= CreatureLaneFlags.Backward;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_PedestrianLaneData.HasComponent(pathElement3.m_Target))
                    {
                        newCurrentLane = new HumanCurrentLane(pathElement3, flags1);
                    }
                    else
                    {
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_ConnectionLaneData.HasComponent(pathElement3.m_Target))
                        {
                            // ISSUE: reference to a compiler-generated field
                            Game.Net.ConnectionLane connectionLane = this.m_ConnectionLaneData[pathElement3.m_Target];
                            CreatureLaneFlags flags2;
                            if ((connectionLane.m_Flags & ConnectionLaneFlags.Area) != (ConnectionLaneFlags)0)
                            {
                                flags2 = flags1 | CreatureLaneFlags.Area;
                                // ISSUE: reference to a compiler-generated field
                                // ISSUE: reference to a compiler-generated field
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_OwnerData.HasComponent(pathElement3.m_Target) && this.m_HangaroundLocationData.HasComponent(this.m_OwnerData[pathElement3.m_Target].m_Owner))
                                    flags2 |= CreatureLaneFlags.Hangaround;
                            }
                            else
                                flags2 = flags1 | CreatureLaneFlags.Connection;
                            if ((connectionLane.m_Flags & ConnectionLaneFlags.Parking) == (ConnectionLaneFlags)0)
                                newCurrentLane = new HumanCurrentLane(pathElement3, flags2);
                        }
                        else
                        {
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_SpawnLocation.HasComponent(pathElement3.m_Target))
                            {
                                CreatureLaneFlags flags3 = flags1 | CreatureLaneFlags.TransformTarget;
                                if (++pathOwner.m_ElementIndex >= pathElement1.Length)
                                    flags3 |= CreatureLaneFlags.EndOfPath;
                                newCurrentLane = new HumanCurrentLane(pathElement3, flags3);
                            }
                            else
                            {
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_PrefabRefData.HasComponent(pathElement3.m_Target))
                                    newCurrentLane = new HumanCurrentLane(flags1 | CreatureLaneFlags.FindLane);
                            }
                        }
                    }
                }
                if (newCurrentLane.m_Lane == Entity.Null)
                {
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_UnspawnedData.HasComponent(currentVehicle.m_Vehicle))
                        newCurrentLane.m_Flags |= CreatureLaneFlags.EmergeUnspawned;
                    PathOwner componentData;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_PathOwnerData.TryGetComponent(controllerVehicle, out componentData) && VehicleUtils.PathfindFailed(componentData))
                    {
                        newCurrentLane.m_Flags |= CreatureLaneFlags.EmergeUnspawned;
                        pathOwner.m_State |= PathFlags.Stuck;
                    }
                }
                if (((int)activityMask.m_Mask & (int)new ActivityMask(ActivityType.Driving).m_Mask) != 0)
                    newCurrentLane.m_Flags |= CreatureLaneFlags.EndOfPath | CreatureLaneFlags.EndReached;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated method
                this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.ExitVehicle(entity, household, currentVehicle.m_Vehicle, newCurrentLane, vehicleDoorPosition.m_Position, vehicleDoorPosition.m_Rotation, ticketPrice));
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                Game.Objects.Transform transform = this.m_TransformData[entity];
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated method
                this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.ExitVehicle(entity, household, currentVehicle.m_Vehicle, new HumanCurrentLane(), transform.m_Position, transform.m_Rotation, ticketPrice));
                pathOwner.m_State &= ~PathFlags.Failed;
                pathOwner.m_State |= PathFlags.Obsolete;
            }
            currentVehicle.m_Flags |= CreatureVehicleFlags.Exiting;
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<CurrentVehicle>(jobIndex, entity, currentVehicle);
            switch (divert.m_Purpose)
            {
                case Game.Citizens.Purpose.None:
                    TravelPurpose componentData3;
                    // ISSUE: reference to a compiler-generated field
                    if (!this.m_TravelPurposeData.TryGetComponent(resident.m_Citizen, out componentData3) || componentData3.m_Purpose != Game.Citizens.Purpose.EmergencyShelter)
                        break;
                    human.m_Flags |= HumanFlags.Run | HumanFlags.Emergency;
                    break;
                case Game.Citizens.Purpose.Safety:
                case Game.Citizens.Purpose.Escape:
                    human.m_Flags |= HumanFlags.Run;
                    break;
            }
        }

        private bool HasEveryoneBoarded(DynamicBuffer<GroupCreature> group)
        {
            if (group.IsCreated)
            {
                for (int index = 0; index < group.Length; ++index)
                {
                    Entity creature = group[index].m_Creature;
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    if (!this.m_CurrentVehicleData.HasComponent(creature) || (this.m_CurrentVehicleData[creature].m_Flags & CreatureVehicleFlags.Ready) == (CreatureVehicleFlags)0)
                        return false;
                }
            }
            return true;
        }

        private bool CheckLane(
          int jobIndex,
          Entity entity,
          PrefabRef prefabRef,
          ref Unity.Mathematics.Random random,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner,
          ref Game.Creatures.Resident resident)
        {
            Entity owner1 = Entity.Null;
            // ISSUE: reference to a compiler-generated field
            if (this.m_OwnerData.HasComponent(currentLane.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                owner1 = this.m_OwnerData[currentLane.m_Lane].m_Owner;
            }
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
            if (pathElement1.Length > pathOwner.m_ElementIndex)
            {
                PathElement pathElement2 = pathElement1[pathOwner.m_ElementIndex];
                // ISSUE: reference to a compiler-generated field
                if (this.m_OwnerData.HasComponent(pathElement2.m_Target))
                {
                    // ISSUE: reference to a compiler-generated field
                    Entity owner2 = this.m_OwnerData[pathElement2.m_Target].m_Owner;
                    if (owner2 != owner1)
                    {
                        // ISSUE: reference to a compiler-generated method
                        return this.FindDivertTargets(jobIndex, entity, prefabRef, ref random, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident, owner2, owner1);
                    }
                }
            }
            return false;
        }

        private bool GetDivertNeeds(
          PrefabRef prefabRef,
          ref Unity.Mathematics.Random random,
          ref Human human,
          ref Game.Creatures.Resident resident,
          ref Divert divert,
          out ActivityMask actionMask,
          out HouseholdNeed householdNeed)
        {
            // ISSUE: reference to a compiler-generated field
            CreatureData creatureData = this.m_PrefabCreatureData[prefabRef.m_Prefab];
            householdNeed = new HouseholdNeed();
            actionMask = new ActivityMask();
            if ((human.m_Flags & HumanFlags.Selfies) == (HumanFlags)0)
                actionMask.m_Mask |= creatureData.m_SupportedActivities.m_Mask & new ActivityMask(ActivityType.Selfies).m_Mask;
            if ((resident.m_Flags & ResidentFlags.ActivityDone) != ResidentFlags.None)
            {
                if (random.NextInt(3) != 0)
                    actionMask = new ActivityMask();
                resident.m_Flags &= ~ResidentFlags.ActivityDone;
            }
            bool divertNeeds = actionMask.m_Mask > 0U;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (divert.m_Purpose != Game.Citizens.Purpose.None || this.m_AttendingMeetingData.HasComponent(resident.m_Citizen) && this.m_PrefabRefData.HasComponent(this.m_AttendingMeetingData[resident.m_Citizen].m_Meeting) || !this.m_CitizenData.HasComponent(resident.m_Citizen))
                return divertNeeds;
            // ISSUE: reference to a compiler-generated field
            switch (this.m_CitizenData[resident.m_Citizen].GetAge())
            {
                case CitizenAge.Adult:
                case CitizenAge.Elderly:
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_HouseholdMembers.HasComponent(resident.m_Citizen))
                    {
                        // ISSUE: reference to a compiler-generated field
                        HouseholdMember householdMember = this.m_HouseholdMembers[resident.m_Citizen];
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_HouseholdNeedData.HasComponent(householdMember.m_Household))
                        {
                            // ISSUE: reference to a compiler-generated field
                            householdNeed = this.m_HouseholdNeedData[householdMember.m_Household];
                            divertNeeds |= householdNeed.m_Resource > Resource.NoResource;
                            break;
                        }
                        break;
                    }
                    break;
            }
            return divertNeeds;
        }

        private bool FindDivertTargets(
          int jobIndex,
          Entity entity,
          PrefabRef prefabRef,
          ref Unity.Mathematics.Random random,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner,
          ref Game.Creatures.Resident resident,
          Entity element,
          Entity ignoreElement)
        {
            ActivityMask actionMask;
            HouseholdNeed householdNeed;
            // ISSUE: reference to a compiler-generated method
            if (!this.GetDivertNeeds(prefabRef, ref random, ref human, ref resident, ref divert, out actionMask, out householdNeed))
                return false;
            // ISSUE: reference to a compiler-generated field
            if (this.m_ConnectedEdges.HasBuffer(element))
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<ConnectedEdge> connectedEdge = this.m_ConnectedEdges[element];
                for (int index = 0; index < connectedEdge.Length; ++index)
                {
                    Entity edge = connectedEdge[index].m_Edge;
                    // ISSUE: reference to a compiler-generated method
                    if (!(edge == ignoreElement) && this.FindDivertTargets(jobIndex, entity, ref random, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident, edge, ref actionMask, householdNeed))
                        return true;
                }
                return false;
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_ConnectedEdges.HasBuffer(ignoreElement))
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<ConnectedEdge> connectedEdge = this.m_ConnectedEdges[ignoreElement];
                for (int index = 0; index < connectedEdge.Length; ++index)
                {
                    if (connectedEdge[index].m_Edge == element)
                        return false;
                }
            }
            // ISSUE: reference to a compiler-generated method
            return this.FindDivertTargets(jobIndex, entity, ref random, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident, element, ref actionMask, householdNeed);
        }

        private HumanFlags SelectAttractionFlags(ref Unity.Mathematics.Random random, ActivityMask actionMask)
        {
            HumanFlags result = (HumanFlags)0;
            int count = 0;
            // ISSUE: reference to a compiler-generated method
            this.CheckActionFlags(ref result, ref count, ref random, actionMask, ActivityType.Selfies, HumanFlags.Selfies);
            return result;
        }

        private void CheckActionFlags(
          ref HumanFlags result,
          ref int count,
          ref Unity.Mathematics.Random random,
          ActivityMask actionMask,
          ActivityType activityType,
          HumanFlags flags)
        {
            if (((int)actionMask.m_Mask & (int)new ActivityMask(activityType).m_Mask) == 0 || random.NextInt(++count) != 0)
                return;
            result = flags;
        }

        private bool FindDivertTargets(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner,
          ref Game.Creatures.Resident resident,
          Entity element,
          ref ActivityMask actionMask,
          HouseholdNeed householdNeed)
        {
            // ISSUE: reference to a compiler-generated field
            if (this.m_ConnectedBuildings.HasBuffer(element))
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<ConnectedBuilding> connectedBuilding = this.m_ConnectedBuildings[element];
                int num1 = random.NextInt(connectedBuilding.Length);
                bool flag1 = actionMask.m_Mask > 0U;
                bool flag2 = householdNeed.m_Resource > Resource.NoResource;
                for (int index1 = 0; index1 < connectedBuilding.Length; ++index1)
                {
                    int falseValue = num1 + index1;
                    int index2 = math.select(falseValue, falseValue - connectedBuilding.Length, falseValue >= connectedBuilding.Length);
                    Entity building = connectedBuilding[index2].m_Building;
                    if (flag1)
                    {
                        int num2 = 0;
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_AttractivenessProviderData.HasComponent(building))
                        {
                            // ISSUE: reference to a compiler-generated field
                            AttractivenessProvider attractivenessProvider = this.m_AttractivenessProviderData[building];
                            num2 += attractivenessProvider.m_Attractiveness;
                        }
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_OnFireData.HasComponent(building))
                        {
                            // ISSUE: reference to a compiler-generated field
                            OnFire onFire = this.m_OnFireData[building];
                            num2 += Mathf.RoundToInt(onFire.m_Intensity);
                        }
                        // ISSUE: reference to a compiler-generated method
                        if (random.NextInt(10) < num2 && this.AddPathAction(entity, ref random, ref currentLane, ref pathOwner, building))
                        {
                            // ISSUE: reference to a compiler-generated method
                            human.m_Flags |= this.SelectAttractionFlags(ref random, actionMask);
                            actionMask = new ActivityMask();
                            flag1 = false;
                        }
                    }
                    // ISSUE: reference to a compiler-generated field
                    if (!(building == target.m_Target) && flag2 && this.m_Renters.HasBuffer(building))
                    {
                        // ISSUE: reference to a compiler-generated field
                        DynamicBuffer<Renter> renter1 = this.m_Renters[building];
                        for (int index3 = 0; index3 < renter1.Length; ++index3)
                        {
                            Entity renter2 = renter1[index3].m_Renter;
                            // ISSUE: reference to a compiler-generated field
                            if (!(renter2 == target.m_Target) && this.m_ServiceAvailableData.HasComponent(renter2))
                            {
                                // ISSUE: reference to a compiler-generated field
                                PrefabRef prefabRef = this.m_PrefabRefData[renter2];
                                // ISSUE: reference to a compiler-generated field
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_PrefabIndustrialProcessData.HasComponent(prefabRef.m_Prefab) && (this.m_PrefabIndustrialProcessData[prefabRef.m_Prefab].m_Output.m_Resource & householdNeed.m_Resource) != Resource.NoResource)
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    ServiceAvailable serviceAvailable = this.m_ServiceAvailableData[renter2];
                                    // ISSUE: reference to a compiler-generated field
                                    DynamicBuffer<Game.Economy.Resources> resource = this.m_Resources[renter2];
                                    if (math.min(EconomyUtils.GetResources(householdNeed.m_Resource, resource), serviceAvailable.m_ServiceAvailable) >= householdNeed.m_Amount)
                                    {
                                        // ISSUE: reference to a compiler-generated method
                                        this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.Shopping, renter2, householdNeed.m_Amount, householdNeed.m_Resource);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private bool AddPathAction(
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          Entity actionTarget)
        {
            // ISSUE: reference to a compiler-generated field
            Game.Objects.Transform transform = this.m_TransformData[actionTarget];
            // ISSUE: reference to a compiler-generated field
            PrefabRef prefabRef = this.m_PrefabRefData[actionTarget];
            float3 position;
            // ISSUE: reference to a compiler-generated field
            if (this.m_PrefabObjectGeometryData.HasComponent(prefabRef.m_Prefab))
            {
                // ISSUE: reference to a compiler-generated field
                ObjectGeometryData objectGeometryData = this.m_PrefabObjectGeometryData[prefabRef.m_Prefab];
                position = ObjectUtils.LocalToWorld(transform, random.NextFloat3(objectGeometryData.m_Bounds.min, objectGeometryData.m_Bounds.max));
            }
            else
                position = transform.m_Position + random.NextFloat3((float3) (- 10f), (float3)10f);
            float num1 = float.MaxValue;
            float t1 = 0.0f;
            int index1 = -2;
            // ISSUE: reference to a compiler-generated field
            if ((double)currentLane.m_CurvePosition.y != (double)currentLane.m_CurvePosition.x && this.m_PedestrianLaneData.HasComponent(currentLane.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                num1 = MathUtils.Distance(this.m_CurveData[currentLane.m_Lane].m_Bezier, position, currentLane.m_CurvePosition, out t1);
                index1 = -1;
            }
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
            int num2 = math.min(pathElement1.Length, pathOwner.m_ElementIndex + 8);
            for (int elementIndex = pathOwner.m_ElementIndex; elementIndex < num2; ++elementIndex)
            {
                PathElement pathElement2 = pathElement1[elementIndex];
                // ISSUE: reference to a compiler-generated field
                if ((double)pathElement2.m_TargetDelta.y != (double)pathElement2.m_TargetDelta.x && this.m_PedestrianLaneData.HasComponent(pathElement2.m_Target))
                {
                    float t2;
                    // ISSUE: reference to a compiler-generated field
                    float num3 = MathUtils.Distance(this.m_CurveData[pathElement2.m_Target].m_Bezier, position, pathElement2.m_TargetDelta, out t2);
                    if ((double)num3 < (double)num1)
                    {
                        num1 = num3;
                        t1 = t2;
                        index1 = elementIndex;
                    }
                }
            }
            Entity entity1;
            float y;
            int num4;
            switch (index1)
            {
                case -2:
                    return false;
                case -1:
                    entity1 = currentLane.m_Lane;
                    y = currentLane.m_CurvePosition.y;
                    currentLane.m_CurvePosition.y = t1;
                    num4 = pathOwner.m_ElementIndex;
                    break;
                default:
                    PathElement pathElement3 = pathElement1[index1];
                    entity1 = pathElement3.m_Target;
                    y = pathElement3.m_TargetDelta.y;
                    pathElement3.m_TargetDelta.y = t1;
                    ref DynamicBuffer<PathElement> local1 = ref pathElement1;
                    int index2 = index1;
                    num4 = index2 + 1;
                    PathElement pathElement4 = pathElement3;
                    local1[index2] = pathElement4;
                    break;
            }
            ref DynamicBuffer<PathElement> local2 = ref pathElement1;
            int index3 = num4;
            PathElement pathElement5 = new PathElement();
            pathElement5.m_Target = entity1;
            pathElement5.m_TargetDelta = new float2(t1, y);
            PathElement elem1 = pathElement5;
            local2.Insert(index3, elem1);
            ref DynamicBuffer<PathElement> local3 = ref pathElement1;
            int index4 = num4;
            pathElement5 = new PathElement();
            pathElement5.m_Target = actionTarget;
            pathElement5.m_Flags = PathElementFlags.Action;
            PathElement elem2 = pathElement5;
            local3.Insert(index4, elem2);
            return true;
        }

        private bool CheckPath(
          int jobIndex,
          Entity entity,
          PrefabRef prefabRef,
          ref Unity.Mathematics.Random random,
          ref Creature creature,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner,
          ref Game.Creatures.Resident resident)
        {
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
            human.m_Flags &= ~(HumanFlags.Selfies | HumanFlags.Carried);
            resident.m_Flags &= ~(ResidentFlags.WaitingTransport | ResidentFlags.NoLateDeparture);
            resident.m_Timer = 0;
            creature.m_QueueEntity = Entity.Null;
            creature.m_QueueArea = new Sphere3();
            switch (divert.m_Purpose)
            {
                case Game.Citizens.Purpose.None:
                    TravelPurpose componentData1;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_TravelPurposeData.TryGetComponent(resident.m_Citizen, out componentData1) && componentData1.m_Purpose == Game.Citizens.Purpose.EmergencyShelter)
                    {
                        human.m_Flags |= HumanFlags.Run | HumanFlags.Emergency;
                        break;
                    }
                    break;
                case Game.Citizens.Purpose.Shopping:
                    // ISSUE: reference to a compiler-generated field
                    if (divert.m_Data != 0 && this.m_HouseholdMembers.HasComponent(resident.m_Citizen))
                    {
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: object of a compiler-generated type is created
                        this.m_ActionQueue.Enqueue(new ResidentAISystem.ResidentAction()
                        {
                            m_Type = ResidentAISystem.ResidentActionType.GoShopping,
                            m_Citizen = resident.m_Citizen,
                            m_Household = this.m_HouseholdMembers[resident.m_Citizen].m_Household,
                            m_Resource = divert.m_Resource,
                            m_Target = divert.m_Target,
                            m_Amount = divert.m_Data,
                            m_Distance = 100f
                        });
                        divert.m_Data = 0;
                        break;
                    }
                    break;
                case Game.Citizens.Purpose.Safety:
                case Game.Citizens.Purpose.Escape:
                    human.m_Flags |= HumanFlags.Run;
                    break;
                case Game.Citizens.Purpose.Disappear:
                    if (divert.m_Target == Entity.Null && pathElement1.Length >= 1)
                    {
                        divert.m_Target = pathElement1[pathElement1.Length - 1].m_Target;
                        pathElement1.RemoveAt(pathElement1.Length - 1);
                        break;
                    }
                    break;
            }
            ParkedCar componentData2 = new ParkedCar();
            CarKeeper component;

            if (this.m_CarKeeperData.TryGetEnabledComponent<CarKeeper>(resident.m_Citizen, out component))
            {
                this.m_ParkedCarData.TryGetComponent(component.m_Car, out componentData2);
            }
            int length = pathElement1.Length;
            for (int index = 0; index < pathElement1.Length; ++index)
            {
                PathElement pathElement2 = pathElement1[index];
                if (pathElement2.m_Target == componentData2.m_Lane)
                {
                    VehicleUtils.SetParkingCurvePos(pathElement1, pathOwner, index, currentLane.m_Lane, componentData2.m_CurvePosition, ref this.m_CurveData);
                    length = index;
                    break;
                }
                if (this.m_ParkingLaneData.HasComponent(pathElement2.m_Target))
                {
                    float curvePos = random.NextFloat(0.05f, 0.95f);
                    VehicleUtils.SetParkingCurvePos(pathElement1, pathOwner, index, currentLane.m_Lane, curvePos, ref this.m_CurveData);
                    length = index;
                    break;
                }
                if (this.m_ConnectionLaneData.HasComponent(pathElement2.m_Target))
                {
                    Game.Net.ConnectionLane connectionLane = this.m_ConnectionLaneData[pathElement2.m_Target];
                    if ((connectionLane.m_Flags & ConnectionLaneFlags.Parking) != (ConnectionLaneFlags)0)
                    {
                        float curvePos = random.NextFloat(0.05f, 0.95f);
                        VehicleUtils.SetParkingCurvePos(pathElement1, pathOwner, index, currentLane.m_Lane, curvePos, ref this.m_CurveData);
                        length = index;
                        break;
                    }
                    if ((connectionLane.m_Flags & ConnectionLaneFlags.Area) != (ConnectionLaneFlags)0 && index == pathElement1.Length - 1)
                    {
                        CreatureUtils.SetRandomAreaTarget(ref random, index, pathElement1, this.m_OwnerData, this.m_CurveData, this.m_LaneData, this.m_ConnectionLaneData, this.m_SubLanes, this.m_AreaNodes, this.m_AreaTriangles);
                        length = pathElement1.Length;
                        break;
                    }
                }
                else
                {
                    if (index == pathElement1.Length - 1 && this.m_SpawnLocation.HasComponent(pathElement2.m_Target))
                    {
                        PrefabRef prefabRef1 = this.m_PrefabRefData[pathElement2.m_Target];
                        Game.Prefabs.SpawnLocationData componentData3;
                        if (index != 0 && this.m_PrefabSpawnLocationData.TryGetComponent((Entity)prefabRef1, out componentData3) && componentData3.m_HangaroundOnLane)
                        {
                            ref PathElement local = ref pathElement1.ElementAt(index - 1);
                            local.m_TargetDelta.y = random.NextFloat();
                            local.m_Flags |= PathElementFlags.Hangaround;
                            pathElement1.RemoveAt(index);
                            length = pathElement1.Length;
                            break;
                        }
                    }
                }
            }

            RPFResidentAISystem.ResidentTickJob.TransportEstimateBuffer transportEstimateBuffer = new RPFResidentAISystem.ResidentTickJob.TransportEstimateBuffer()
            {
                m_BoardingQueue = this.m_BoardingQueue
            };

            RPFRouteUtils.StripTransportSegments<RPFResidentAISystem.ResidentTickJob.TransportEstimateBuffer>(ref random, length, pathElement1, this.m_RouteConnectedData, this.m_BoardingVehicleData, this.m_OwnerData, this.m_LaneData, this.m_ConnectionLaneData, this.m_CurveData, this.m_PrefabRefData, this.m_PrefabTransportStopData, this.m_SubLanes, this.m_AreaNodes, this.m_AreaTriangles, this.m_WaitingPassengers, this.m_CurrentRouteData, this.m_PublicTransportVehicleData, kCrowd, scheduled_factor, transfer_penalty, feeder_trunk_transfer_penalty, t2w_timefactor, waiting_weight, transportEstimateBuffer);
            if (!this.m_OwnerData.HasComponent(currentLane.m_Lane))
                return false;
            Entity owner = this.m_OwnerData[currentLane.m_Lane].m_Owner;
            return this.FindDivertTargets(jobIndex, entity, prefabRef, ref random, ref human, ref currentLane, ref target, ref divert, ref pathOwner, ref resident, owner, Entity.Null);
        }

        private void CurrentVehicleBoarding(
          int jobIndex,
          Entity entity,
          Entity controllerVehicle,
          ref Game.Creatures.Resident resident,
          ref PathOwner pathOwner,
          ref Game.Common.Target target)
        {
            // ISSUE: reference to a compiler-generated field
            Game.Vehicles.PublicTransport publicTransport = this.m_PublicTransportData[controllerVehicle];
            if ((publicTransport.m_State & (PublicTransportFlags.Evacuating | PublicTransportFlags.PrisonerTransport)) != (PublicTransportFlags)0)
            {
                if ((publicTransport.m_State & PublicTransportFlags.Returning) == (PublicTransportFlags)0)
                    return;
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
                if (pathElement.Length >= pathOwner.m_ElementIndex + 2)
                {
                    Entity target1 = pathElement[pathOwner.m_ElementIndex + 1].m_Target;
                    Entity target2 = Entity.Null;
                    if (pathElement.Length >= pathOwner.m_ElementIndex + 3)
                        target2 = pathElement[pathOwner.m_ElementIndex + 2].m_Target;
                    bool obsolete;
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    if (!RouteUtils.ShouldExitVehicle(target2, target1, controllerVehicle, ref this.m_OwnerData, ref this.m_RouteConnectedData, ref this.m_BoardingVehicleData, ref this.m_CurrentRouteData, ref this.m_AccessLaneLaneData, ref this.m_PublicTransportData, ref this.m_ConnectedRoutes, false, out obsolete))
                        return;
                    pathOwner.m_ElementIndex += 2;
                    if (!obsolete)
                    {
                        resident.m_Flags |= ResidentFlags.Disembarking;
                        return;
                    }
                }
            }
            pathOwner.m_State &= ~PathFlags.Failed;
            pathOwner.m_State |= PathFlags.Obsolete;
            resident.m_Flags |= ResidentFlags.Disembarking;
        }

        private void CurrentVehicleTesting(
          int jobIndex,
          Entity entity,
          Entity controllerVehicle,
          ref Game.Creatures.Resident resident,
          ref PathOwner pathOwner,
          ref Game.Common.Target target)
        {
            // ISSUE: reference to a compiler-generated field
            Game.Vehicles.PublicTransport publicTransport = this.m_PublicTransportData[controllerVehicle];
            if ((publicTransport.m_State & (PublicTransportFlags.Evacuating | PublicTransportFlags.PrisonerTransport)) != (PublicTransportFlags)0)
            {
                if ((publicTransport.m_State & PublicTransportFlags.Returning) == (PublicTransportFlags)0)
                    return;
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
                if (pathElement.Length >= pathOwner.m_ElementIndex + 2)
                {
                    Entity target1 = pathElement[pathOwner.m_ElementIndex + 1].m_Target;
                    Entity target2 = Entity.Null;
                    if (pathElement.Length >= pathOwner.m_ElementIndex + 3)
                        target2 = pathElement[pathOwner.m_ElementIndex + 2].m_Target;

                    if (!RouteUtils.ShouldExitVehicle(target2, target1, controllerVehicle, ref this.m_OwnerData, ref this.m_RouteConnectedData, ref this.m_BoardingVehicleData, ref this.m_CurrentRouteData, ref this.m_AccessLaneLaneData, ref this.m_PublicTransportData, ref this.m_ConnectedRoutes, true, out bool _))
                        return;
                }
            }

            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.RequireStop(Entity.Null, controllerVehicle, new float3()));
        }

        private void CurrentVehicleDisembarking(
          int jobIndex,
          Entity entity,
          Entity controllerVehicle,
          ref Game.Creatures.Resident resident,
          ref PathOwner pathOwner,
          ref Game.Common.Target target)
        {
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement2 = this.m_PathElements[controllerVehicle];
            // ISSUE: reference to a compiler-generated field
            PathOwner sourceOwner = this.m_PathOwnerData[controllerVehicle];
            if (pathElement2.Length > sourceOwner.m_ElementIndex)
            {
                Game.Pathfind.PathUtils.CopyPath(pathElement2, sourceOwner, 0, pathElement1);
                pathOwner.m_ElementIndex = 0;
                pathOwner.m_State |= PathFlags.Updated;
            }
            else
            {
                pathOwner.m_State &= ~PathFlags.Failed;
                pathOwner.m_State |= PathFlags.Obsolete;
            }
            resident.m_Flags |= ResidentFlags.Disembarking;
        }

        private void CurrentVehicleTransporting(
          Entity entity,
          Entity controllerVehicle,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            this.m_PathElements[entity].Clear();
            pathOwner.m_ElementIndex = 0;
        }

        private void GroupLeaderDisembarking(
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            this.m_PathElements[entity].Clear();
            pathOwner.m_ElementIndex = 0;
            resident.m_Flags |= ResidentFlags.Disembarking;
        }

        private bool PathEndReached(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            Game.Citizens.Purpose purpose = divert.m_Purpose;
            if ((uint)purpose <= 15U)
            {
                if (purpose != Game.Citizens.Purpose.None)
                {
                    if (purpose == Game.Citizens.Purpose.Safety)
                    {
                        // ISSUE: reference to a compiler-generated method
                        return this.ReachSafety(jobIndex, entity, ref random, ref resident, ref human, ref currentLane, ref target, ref divert, ref pathOwner);
                    }
                }
                else
                {
                    // ISSUE: reference to a compiler-generated method
                    return this.ReachTarget(jobIndex, entity, ref random, ref resident, ref human, ref currentLane, ref target, ref divert, ref pathOwner, new ResetTrip()
                    {
                        m_Target = target.m_Target
                    });
                }
            }
            else
            {
                switch (purpose)
                {
                    case Game.Citizens.Purpose.Escape:
                        // ISSUE: reference to a compiler-generated method
                        return this.ReachEscape(jobIndex, entity, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                    case Game.Citizens.Purpose.SendMail:
                        // ISSUE: reference to a compiler-generated method
                        return this.ReachSendMail(jobIndex, entity, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                    case Game.Citizens.Purpose.WaitingHome:
                        // ISSUE: reference to a compiler-generated method
                        return this.ReachWaitingHome(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                    case Game.Citizens.Purpose.PathFailed:
                        // ISSUE: reference to a compiler-generated method
                        return this.ReachPathFailed(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                }
            }
            // ISSUE: reference to a compiler-generated method
            return this.ReachDivert(jobIndex, entity, ref random, ref resident, ref human, ref currentLane, ref target, ref divert, ref pathOwner);
        }

        private bool ActionLocationReached(
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner)
        {
            if ((currentLane.m_Flags & CreatureLaneFlags.ActivityDone) == (CreatureLaneFlags)0)
                return true;
            resident.m_Flags |= ResidentFlags.ActivityDone;
            human.m_Flags &= ~HumanFlags.Selfies;
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
            pathOwner.m_ElementIndex += math.select(0, 1, pathOwner.m_ElementIndex < pathElement.Length);
            return false;
        }

        private bool ReachTarget(
          int jobIndex,
          Entity creatureEntity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner,
          ResetTrip resetTrip)
        {
            // ISSUE: reference to a compiler-generated field
            if (this.m_VehicleData.HasComponent(target.m_Target))
            {
                // ISSUE: reference to a compiler-generated method
                return this.ReachVehicle(jobIndex, creatureEntity, ref resident, ref currentLane, ref target, ref pathOwner);
            }
            Entity entity1 = target.m_Target;
            // ISSUE: reference to a compiler-generated field
            if (this.m_PropertyRenters.HasComponent(entity1))
            {
                // ISSUE: reference to a compiler-generated field
                entity1 = this.m_PropertyRenters[entity1].m_Property;
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_OnFireData.HasComponent(entity1) || this.m_DestroyedData.HasComponent(entity1))
            {
                // ISSUE: reference to a compiler-generated method
                this.SetDivert(jobIndex, creatureEntity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.Safety, Entity.Null);
                return false;
            }
            if ((currentLane.m_Flags & CreatureLaneFlags.Hangaround) != (CreatureLaneFlags)0)
            {
                if ((currentLane.m_Flags & (CreatureLaneFlags.TransformTarget | CreatureLaneFlags.ActivityDone)) == (CreatureLaneFlags.TransformTarget | CreatureLaneFlags.ActivityDone) || random.NextInt(2500) == 0)
                {
                    // ISSUE: reference to a compiler-generated method
                    ResidentFlags ignoreFlags = this.GetIgnoreFlags(entity1, ref resident, ref currentLane);
                    if (ignoreFlags != ResidentFlags.None)
                    {
                        if ((ignoreFlags & ~resident.m_Flags) != ResidentFlags.None)
                        {
                            resident.m_Flags |= ignoreFlags;
                            pathOwner.m_State &= ~PathFlags.Failed;
                            pathOwner.m_State |= PathFlags.Obsolete;
                            resident.m_Flags &= ~ResidentFlags.Hangaround;
                            if ((resident.m_Flags & ResidentFlags.Arrived) != ResidentFlags.None)
                            {
                                resetTrip.m_Source = target.m_Target;
                                resetTrip.m_Target = target.m_Target;
                                bool flag = false;
                                HouseholdMember componentData1;
                                DynamicBuffer<HouseholdAnimal> bufferData;
                                // ISSUE: reference to a compiler-generated field
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_HouseholdMembers.TryGetComponent(resident.m_Citizen, out componentData1) && this.m_HouseholdAnimals.TryGetBuffer(componentData1.m_Household, out bufferData))
                                {
                                    for (int index = 0; index < bufferData.Length; ++index)
                                    {
                                        HouseholdAnimal householdAnimal = bufferData[index];
                                        CurrentBuilding componentData2;
                                        CurrentTransport componentData3;
                                        // ISSUE: reference to a compiler-generated field
                                        // ISSUE: reference to a compiler-generated field
                                        if (this.m_CurrentBuildingData.TryGetComponent(householdAnimal.m_HouseholdPet, out componentData2) && this.m_CurrentTransportData.TryGetComponent(householdAnimal.m_HouseholdPet, out componentData3) && componentData2.m_CurrentBuilding == entity1)
                                        {
                                            // ISSUE: reference to a compiler-generated field
                                            // ISSUE: reference to a compiler-generated field
                                            Entity entity2 = this.m_CommandBuffer.CreateEntity(jobIndex, this.m_ResetTripArchetype);
                                            resetTrip.m_Creature = componentData3.m_CurrentTransport;
                                            // ISSUE: reference to a compiler-generated field
                                            this.m_CommandBuffer.SetComponent<ResetTrip>(jobIndex, entity2, resetTrip);
                                            flag = true;
                                        }
                                    }
                                }
                                if (flag)
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    // ISSUE: reference to a compiler-generated field
                                    Entity entity3 = this.m_CommandBuffer.CreateEntity(jobIndex, this.m_ResetTripArchetype);
                                    resetTrip.m_Creature = creatureEntity;
                                    // ISSUE: reference to a compiler-generated field
                                    this.m_CommandBuffer.SetComponent<ResetTrip>(jobIndex, entity3, resetTrip);
                                }
                            }
                        }
                        return false;
                    }
                }
                resident.m_Flags |= ResidentFlags.Hangaround;
            }
            human.m_Flags &= ~(HumanFlags.Run | HumanFlags.Emergency);
            resident.m_Flags &= ~(ResidentFlags.IgnoreTaxi | ResidentFlags.IgnoreTransport);
            if ((resident.m_Flags & ResidentFlags.Arrived) == ResidentFlags.None)
            {
                resetTrip.m_Creature = creatureEntity;
                resetTrip.m_Arrived = entity1;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                Entity entity4 = this.m_CommandBuffer.CreateEntity(jobIndex, this.m_ResetTripArchetype);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.SetComponent<ResetTrip>(jobIndex, entity4, resetTrip);
                resident.m_Flags |= ResidentFlags.Arrived;
                return false;
            }
            if ((resident.m_Flags & ResidentFlags.Hangaround) != ResidentFlags.None)
                return false;
            // ISSUE: reference to a compiler-generated field
            if (this.m_EntityLookup.Exists(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.RemoveComponent<CurrentTransport>(jobIndex, resident.m_Citizen);
            }
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, creatureEntity, new Deleted());
            return true;
        }

        private ResidentFlags GetIgnoreFlags(
          Entity building,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane)
        {
            if ((resident.m_Flags & ResidentFlags.CannotIgnore) != ResidentFlags.None)
                return ResidentFlags.None;
            ResidentFlags ignoreFlags = ResidentFlags.None;
            if ((currentLane.m_Flags & CreatureLaneFlags.TransformTarget) != (CreatureLaneFlags)0)
            {
                PrefabRef componentData1;
                Game.Prefabs.SpawnLocationData componentData2;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (this.m_PrefabRefData.TryGetComponent(currentLane.m_Lane, out componentData1) && this.m_PrefabSpawnLocationData.TryGetComponent(componentData1.m_Prefab, out componentData2) && (((int)componentData2.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.BenchSitting).m_Mask) != 0 || ((int)componentData2.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.PullUps).m_Mask) != 0))
                    ignoreFlags |= ResidentFlags.IgnoreBenches;
            }
            else
            {
                Owner componentData3;
                PrefabRef componentData4;
                Game.Prefabs.SpawnLocationData componentData5;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if ((currentLane.m_Flags & CreatureLaneFlags.Area) != (CreatureLaneFlags)0 && this.m_OwnerData.TryGetComponent(currentLane.m_Lane, out componentData3) && this.m_PrefabRefData.TryGetComponent(componentData3.m_Owner, out componentData4) && this.m_PrefabSpawnLocationData.TryGetComponent(componentData4.m_Prefab, out componentData5) && (((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Standing).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.GroundLaying).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.GroundSitting).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.PushUps).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.SitUps).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.JumpingJacks).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.JumpingLunges).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Squats).m_Mask) != 0 || ((int)componentData5.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Yoga).m_Mask) != 0))
                    ignoreFlags |= ResidentFlags.IgnoreAreas;
            }
            if ((ignoreFlags & ~resident.m_Flags) != ResidentFlags.None)
            {
                ResidentFlags residentFlags1 = ~(resident.m_Flags | ignoreFlags);
                bool flag = false;
                DynamicBuffer<SpawnLocationElement> bufferData;
                // ISSUE: reference to a compiler-generated field
                if (this.m_SpawnLocationElements.TryGetBuffer(building, out bufferData))
                {
                    for (int index = 0; index < bufferData.Length; ++index)
                    {
                        SpawnLocationElement spawnLocationElement = bufferData[index];
                        if (spawnLocationElement.m_Type == SpawnLocationType.SpawnLocation || spawnLocationElement.m_Type == SpawnLocationType.HangaroundLocation)
                        {
                            // ISSUE: reference to a compiler-generated field
                            // ISSUE: reference to a compiler-generated field
                            Game.Prefabs.SpawnLocationData spawnLocationData = this.m_PrefabSpawnLocationData[this.m_PrefabRefData[spawnLocationElement.m_SpawnLocation].m_Prefab];
                            ResidentFlags residentFlags2 = ResidentFlags.None;
                            if (((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.BenchSitting).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.PullUps).m_Mask) != 0)
                                residentFlags2 |= ResidentFlags.IgnoreBenches;
                            if (((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Standing).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.GroundLaying).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.GroundSitting).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.PushUps).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.SitUps).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.JumpingJacks).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.JumpingLunges).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Squats).m_Mask) != 0 || ((int)spawnLocationData.m_ActivityMask.m_Mask & (int)new ActivityMask(ActivityType.Yoga).m_Mask) != 0)
                                residentFlags2 |= ResidentFlags.IgnoreAreas;
                            if (residentFlags2 == ResidentFlags.None || (residentFlags2 & residentFlags1) != ResidentFlags.None)
                                return ignoreFlags;
                            flag |= (residentFlags2 & ~ignoreFlags) > ResidentFlags.None;
                        }
                    }
                }
                if (flag)
                {
                    resident.m_Flags &= ~(ResidentFlags.IgnoreBenches | ResidentFlags.IgnoreAreas) | ignoreFlags;
                }
                else
                {
                    resident.m_Flags |= ResidentFlags.CannotIgnore;
                    return ResidentFlags.None;
                }
            }
            return ignoreFlags;
        }

        private bool ReachVehicle(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref PathOwner pathOwner)
        {
            Entity entity1 = target.m_Target;
            // ISSUE: reference to a compiler-generated field
            if (this.m_ControllerData.HasComponent(target.m_Target))
            {
                // ISSUE: reference to a compiler-generated field
                Controller controller = this.m_ControllerData[target.m_Target];
                if (controller.m_Controller != Entity.Null)
                    entity1 = controller.m_Controller;
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_PublicTransportData.HasComponent(entity1))
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if ((this.m_PublicTransportData[entity1].m_State & PublicTransportFlags.Boarding) != (PublicTransportFlags)0 && this.m_OwnerData.HasComponent(entity1))
                {
                    // ISSUE: reference to a compiler-generated field
                    Owner owner = this.m_OwnerData[entity1];
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_PrefabRefData.HasComponent(owner.m_Owner))
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.TryEnterVehicle(entity, target.m_Target, Entity.Null, ref resident, ref currentLane);
                        target.m_Target = owner.m_Owner;
                        pathOwner.m_State &= ~PathFlags.Failed;
                        pathOwner.m_State |= PathFlags.Obsolete;
                        return true;
                    }
                }
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_PoliceCarData.HasComponent(entity1))
                {
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    if ((this.m_PoliceCarData[entity1].m_State & PoliceCarFlags.AtTarget) != (PoliceCarFlags)0 && this.m_OwnerData.HasComponent(entity1))
                    {
                        // ISSUE: reference to a compiler-generated field
                        Owner owner = this.m_OwnerData[entity1];
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_PrefabRefData.HasComponent(owner.m_Owner))
                        {
                            // ISSUE: reference to a compiler-generated method
                            this.TryEnterVehicle(entity, target.m_Target, Entity.Null, ref resident, ref currentLane);
                            target.m_Target = owner.m_Owner;
                            pathOwner.m_State &= ~PathFlags.Failed;
                            pathOwner.m_State |= PathFlags.Obsolete;
                            return true;
                        }
                    }
                }
                else
                {
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_AmbulanceData.HasComponent(entity1))
                    {
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        if ((this.m_AmbulanceData[entity1].m_State & AmbulanceFlags.AtTarget) != (AmbulanceFlags)0 && this.m_OwnerData.HasComponent(entity1))
                        {
                            // ISSUE: reference to a compiler-generated field
                            Owner owner = this.m_OwnerData[entity1];
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_PrefabRefData.HasComponent(owner.m_Owner))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.TryEnterVehicle(entity, target.m_Target, Entity.Null, ref resident, ref currentLane);
                                target.m_Target = owner.m_Owner;
                                pathOwner.m_State &= ~PathFlags.Failed;
                                pathOwner.m_State |= PathFlags.Obsolete;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_HearseData.HasComponent(entity1) && (this.m_HearseData[entity1].m_State & HearseFlags.AtTarget) != (HearseFlags)0 && this.m_OwnerData.HasComponent(entity1))
                        {
                            // ISSUE: reference to a compiler-generated field
                            Owner owner = this.m_OwnerData[entity1];
                            // ISSUE: reference to a compiler-generated field
                            if (this.m_PrefabRefData.HasComponent(owner.m_Owner))
                            {
                                // ISSUE: reference to a compiler-generated method
                                this.TryEnterVehicle(entity, target.m_Target, Entity.Null, ref resident, ref currentLane);
                                target.m_Target = owner.m_Owner;
                                pathOwner.m_State &= ~PathFlags.Failed;
                                pathOwner.m_State |= PathFlags.Obsolete;
                                return true;
                            }
                        }
                    }
                }
            }
            // ISSUE: reference to a compiler-generated method
            this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.InvalidVehicleTarget);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
            return true;
        }

        private bool ReachDivert(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            ResetTrip resetTrip = new ResetTrip()
            {
                m_Target = divert.m_Target,
                m_TravelPurpose = divert.m_Purpose,
                m_TravelData = divert.m_Data,
                m_TravelResource = divert.m_Resource
            };
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_TravelPurposeData.HasComponent(resident.m_Citizen) && this.m_PrefabRefData.HasComponent(target.m_Target))
            {
                // ISSUE: reference to a compiler-generated field
                TravelPurpose travelPurpose = this.m_TravelPurposeData[resident.m_Citizen];
                resetTrip.m_NextTarget = target.m_Target;
                resetTrip.m_NextPurpose = travelPurpose.m_Purpose;
                resetTrip.m_NextData = travelPurpose.m_Data;
                resetTrip.m_NextResource = travelPurpose.m_Resource;
            }
            target.m_Target = divert.m_Target;
            divert = new Divert();
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.RemoveComponent<Divert>(jobIndex, entity);
            pathOwner.m_State &= ~PathFlags.CachedObsolete;
            // ISSUE: reference to a compiler-generated method
            return this.ReachTarget(jobIndex, entity, ref random, ref resident, ref human, ref currentLane, ref target, ref divert, ref pathOwner, resetTrip);
        }

        private bool ReachSendMail(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: object of a compiler-generated type is created
            this.m_ActionQueue.Enqueue(new ResidentAISystem.ResidentAction()
            {
                m_Type = ResidentAISystem.ResidentActionType.SendMail,
                m_Citizen = resident.m_Citizen,
                m_Target = currentLane.m_Lane
            });
            // ISSUE: reference to a compiler-generated method
            this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.None, Entity.Null);
            return false;
        }

        private bool ReachSafety(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            human.m_Flags &= ~(HumanFlags.Run | HumanFlags.Emergency);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (!this.m_PrefabRefData.HasComponent(target.m_Target) || this.m_DestroyedData.HasComponent(target.m_Target))
            {
                bool movingAway;
                // ISSUE: reference to a compiler-generated method
                Entity homeBuilding = this.GetHomeBuilding(ref resident, out movingAway);
                // ISSUE: reference to a compiler-generated field
                if (homeBuilding != Entity.Null && !this.m_DestroyedData.HasComponent(homeBuilding))
                {
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_OnFireData.HasComponent(homeBuilding))
                    {
                        // ISSUE: reference to a compiler-generated method
                        this.FindWaitingPosition(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                        return false;
                    }
                    // ISSUE: reference to a compiler-generated method
                    this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, Game.Citizens.Purpose.GoingHome, homeBuilding);
                    return false;
                }
                if (movingAway)
                {
                    target.m_Target = Entity.Null;
                    // ISSUE: reference to a compiler-generated method
                    this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.Disappear, Entity.Null);
                    return false;
                }
                // ISSUE: reference to a compiler-generated method
                this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.WaitingHome, Entity.Null);
                return false;
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_OnFireData.HasComponent(target.m_Target))
            {
                // ISSUE: reference to a compiler-generated method
                this.FindWaitingPosition(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                return false;
            }
            Game.Citizens.Purpose purpose = Game.Citizens.Purpose.None;
            // ISSUE: reference to a compiler-generated field
            if (this.m_TravelPurposeData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                purpose = this.m_TravelPurposeData[resident.m_Citizen].m_Purpose;
            }
            // ISSUE: reference to a compiler-generated method
            this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, purpose, target.m_Target);
            return false;
        }

        private bool ReachEscape(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated method
            Entity homeBuilding = this.GetHomeBuilding(ref resident, out bool _);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (homeBuilding == Entity.Null || this.m_OnFireData.HasComponent(homeBuilding) || this.m_DestroyedData.HasComponent(homeBuilding))
            {
                // ISSUE: reference to a compiler-generated method
                this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.Disappear, Entity.Null);
                return false;
            }
            // ISSUE: reference to a compiler-generated method
            this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, Game.Citizens.Purpose.GoingHome, homeBuilding);
            return false;
        }

        private bool ReachWaitingHome(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            bool movingAway;
            // ISSUE: reference to a compiler-generated method
            Entity homeBuilding = this.GetHomeBuilding(ref resident, out movingAway);
            // ISSUE: reference to a compiler-generated field
            if (homeBuilding == Entity.Null || this.m_DestroyedData.HasComponent(homeBuilding))
            {
                if (movingAway)
                {
                    // ISSUE: reference to a compiler-generated method
                    this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.Disappear, Entity.Null);
                    return false;
                }
                divert.m_Data += random.NextInt(1, 3);
                bool flag = divert.m_Data <= 2500;
                if (!flag)
                {
                    Game.Net.ConnectionLane componentData;
                    // ISSUE: reference to a compiler-generated field
                    flag = (currentLane.m_Flags & CreatureLaneFlags.Connection) == (CreatureLaneFlags)0 || !this.m_ConnectionLaneData.TryGetComponent(currentLane.m_Lane, out componentData) || (componentData.m_Flags & ConnectionLaneFlags.Outside) == (ConnectionLaneFlags)0;
                }
                if (flag)
                {
                    // ISSUE: reference to a compiler-generated method
                    this.FindWaitingPosition(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                    return false;
                }
                // ISSUE: reference to a compiler-generated field
                if (this.m_CitizenData.HasComponent(resident.m_Citizen))
                {
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, resident.m_Citizen, new Deleted());
                }
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.WaitingHome_AlreadyOutside);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                return true;
            }
            // ISSUE: reference to a compiler-generated method
            this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, Game.Citizens.Purpose.GoingHome, homeBuilding);
            return false;
        }

        private bool ReachPathFailed(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            divert.m_Data += random.NextInt(1, 3);
            if (divert.m_Data <= 2500)
            {
                // ISSUE: reference to a compiler-generated method
                this.FindWaitingPosition(jobIndex, entity, ref random, ref resident, ref currentLane, ref target, ref divert, ref pathOwner);
                return false;
            }
            // ISSUE: reference to a compiler-generated method
            this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.None, Entity.Null);
            return false;
        }

        private bool ReturnHome(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            if (this.m_AttendingMeetingData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                AttendingMeeting attendingMeeting = this.m_AttendingMeetingData[resident.m_Citizen];
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (this.m_CoordinatedMeetingData.HasComponent(attendingMeeting.m_Meeting) && this.m_CoordinatedMeetingData[attendingMeeting.m_Meeting].m_Target == target.m_Target)
                {
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.RemoveComponent<AttendingMeeting>(jobIndex, resident.m_Citizen);
                }
            }
            bool movingAway;
            // ISSUE: reference to a compiler-generated method
            Entity homeBuilding = this.GetHomeBuilding(ref resident, out movingAway);
            if (homeBuilding != Entity.Null && homeBuilding != target.m_Target)
            {
                // ISSUE: reference to a compiler-generated method
                this.SetTarget(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, ref target, Game.Citizens.Purpose.GoingHome, homeBuilding);
                return false;
            }
            if (homeBuilding == Entity.Null && currentLane.m_Lane != Entity.Null)
            {
                if (movingAway)
                {
                    // ISSUE: reference to a compiler-generated method
                    this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.NoPath_AlreadyMovingAway);
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_CitizenData.HasComponent(resident.m_Citizen))
                    {
                        // ISSUE: reference to a compiler-generated field
                        this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, resident.m_Citizen, new Deleted());
                    }
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
                    return true;
                }
                // ISSUE: reference to a compiler-generated method
                this.SetDivert(jobIndex, entity, ref resident, ref currentLane, ref divert, ref pathOwner, Game.Citizens.Purpose.WaitingHome, Entity.Null);
                return false;
            }
            Game.Net.ConnectionLane componentData;
            // ISSUE: reference to a compiler-generated field
            if ((currentLane.m_Flags & CreatureLaneFlags.Connection) != (CreatureLaneFlags)0 && this.m_ConnectionLaneData.TryGetComponent(currentLane.m_Lane, out componentData) && (componentData.m_Flags & ConnectionLaneFlags.Outside) != (ConnectionLaneFlags)0)
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_CitizenData.HasComponent(resident.m_Citizen))
                {
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, resident.m_Citizen, new Deleted());
                }
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.NoPathToHome_AlreadyOutside);
            }
            else
            {
                // ISSUE: reference to a compiler-generated method
                this.AddDeletedResident(RPFResidentAISystem.DeletedResidentType.NoPathToHome);
            }
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<Deleted>(jobIndex, entity, new Deleted());
            return true;
        }

        private void FindWaitingPosition(
          int jobIndex,
          Entity entity,
          ref Unity.Mathematics.Random random,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Game.Common.Target target,
          ref Divert divert,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            if (this.m_PedestrianLaneData.HasComponent(currentLane.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                if ((this.m_PedestrianLaneData[currentLane.m_Lane].m_Flags & PedestrianLaneFlags.Crosswalk) == (PedestrianLaneFlags)0)
                    return;
                pathOwner.m_ElementIndex = 0;
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
                pathElement.Clear();
                NativeParallelHashSet<Entity> ignoreLanes = new NativeParallelHashSet<Entity>(16 /*0x10*/, (AllocatorManager.AllocatorHandle)Allocator.Temp);
                ignoreLanes.Add(currentLane.m_Lane);
                Entity lane = currentLane.m_Lane;
                float2 yy = currentLane.m_CurvePosition.yy;
                if ((double)yy.y >= 0.5)
                {
                    if ((double)yy.y != 1.0)
                    {
                        yy.y = 1f;
                        pathElement.Add(new PathElement(currentLane.m_Lane, yy));
                    }
                }
                else if ((double)yy.y != 0.0)
                {
                    yy.y = 0.0f;
                    pathElement.Add(new PathElement(currentLane.m_Lane, yy));
                }
                // ISSUE: reference to a compiler-generated method
                while (this.TryFindNextLane(ignoreLanes, ref lane, ref yy.y))
                {
                    ignoreLanes.Add(lane);
                    yy.x = yy.y;
                    // ISSUE: reference to a compiler-generated field
                    if ((this.m_PedestrianLaneData[lane].m_Flags & PedestrianLaneFlags.Crosswalk) == (PedestrianLaneFlags)0)
                    {
                        yy.y = random.NextFloat(0.0f, 1f);
                        pathElement.Add(new PathElement(lane, yy));
                        break;
                    }
                    yy.y = math.select(0.0f, 1f, (double)yy.x < 0.5);
                    pathElement.Add(new PathElement(lane, yy));
                }
                ignoreLanes.Dispose();
                if (pathElement.Length == 0)
                    return;
                currentLane.m_Flags &= ~(CreatureLaneFlags.EndOfPath | CreatureLaneFlags.ParkingSpace | CreatureLaneFlags.Transport | CreatureLaneFlags.Taxi | CreatureLaneFlags.Action);
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (!this.m_ConnectionLaneData.HasComponent(currentLane.m_Lane) || (currentLane.m_Flags & CreatureLaneFlags.WaitPosition) != (CreatureLaneFlags)0 || (this.m_ConnectionLaneData[currentLane.m_Lane].m_Flags & ConnectionLaneFlags.Outside) == (ConnectionLaneFlags)0)
                    return;
                currentLane.m_Flags &= ~CreatureLaneFlags.EndReached;
                currentLane.m_Flags |= CreatureLaneFlags.WaitPosition;
                currentLane.m_CurvePosition.y = random.NextFloat(0.0f, 1f);
            }
        }

        private bool TryFindNextLane(
          NativeParallelHashSet<Entity> ignoreLanes,
          ref Entity lane,
          ref float curveDelta)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_OwnerData.HasComponent(lane))
                return false;
            // ISSUE: reference to a compiler-generated field
            Owner owner = this.m_OwnerData[lane];
            // ISSUE: reference to a compiler-generated method
            if (this.TryFindNextLane(ignoreLanes, owner.m_Owner, ref lane, ref curveDelta))
                return true;
            // ISSUE: reference to a compiler-generated field
            if (this.m_ConnectedEdges.HasBuffer(owner.m_Owner))
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<ConnectedEdge> connectedEdge = this.m_ConnectedEdges[owner.m_Owner];
                for (int index = 0; index < connectedEdge.Length; ++index)
                {
                    // ISSUE: reference to a compiler-generated method
                    if (this.TryFindNextLane(ignoreLanes, connectedEdge[index].m_Edge, ref lane, ref curveDelta))
                        return true;
                }
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_EdgeData.HasComponent(owner.m_Owner))
                {
                    // ISSUE: reference to a compiler-generated field
                    Game.Net.Edge edge = this.m_EdgeData[owner.m_Owner];
                    // ISSUE: reference to a compiler-generated method
                    // ISSUE: reference to a compiler-generated method
                    if (this.TryFindNextLane(ignoreLanes, edge.m_Start, ref lane, ref curveDelta) || this.TryFindNextLane(ignoreLanes, edge.m_End, ref lane, ref curveDelta))
                        return true;
                }
            }
            return false;
        }

        private bool TryFindNextLane(
          NativeParallelHashSet<Entity> ignoreLanes,
          Entity owner,
          ref Entity lane,
          ref float curveDelta)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_SubLanes.HasBuffer(owner))
                return false;
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<Game.Net.SubLane> subLane1 = this.m_SubLanes[owner];
            // ISSUE: reference to a compiler-generated field
            Lane lane1 = this.m_LaneData[lane];
            PathNode other = (double)curveDelta != 0.0 ? ((double)curveDelta != 1.0 ? lane1.m_MiddleNode : lane1.m_EndNode) : lane1.m_StartNode;
            for (int index = 0; index < subLane1.Length; ++index)
            {
                Entity subLane2 = subLane1[index].m_SubLane;
                // ISSUE: reference to a compiler-generated field
                if (!ignoreLanes.Contains(subLane2) && this.m_PedestrianLaneData.HasComponent(subLane2))
                {
                    // ISSUE: reference to a compiler-generated field
                    Lane lane2 = this.m_LaneData[subLane2];
                    if (lane2.m_StartNode.EqualsIgnoreCurvePos(other))
                    {
                        lane = subLane2;
                        curveDelta = 0.0f;
                        return true;
                    }
                    if (lane2.m_EndNode.EqualsIgnoreCurvePos(other))
                    {
                        lane = subLane2;
                        curveDelta = 1f;
                        return true;
                    }
                    if (lane2.m_MiddleNode.EqualsIgnoreCurvePos(other))
                    {
                        lane = subLane2;
                        curveDelta = lane2.m_MiddleNode.GetCurvePos();
                        return true;
                    }
                }
            }
            return false;
        }

        private void SetDivert(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Divert divert,
          ref PathOwner pathOwner,
          Game.Citizens.Purpose purpose,
          Entity targetEntity,
          int data = 0,
          Resource resource = Resource.NoResource)
        {
            if (purpose != Game.Citizens.Purpose.None)
            {
                int num = divert.m_Purpose == Game.Citizens.Purpose.None ? 1 : 0;
                divert = new Divert()
                {
                    m_Purpose = purpose,
                    m_Target = targetEntity,
                    m_Data = data,
                    m_Resource = resource
                };
                if (num != 0)
                {
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.AddComponent<Divert>(jobIndex, entity, divert);
                }
                pathOwner.m_State |= PathFlags.DivertObsolete;
            }
            else if (divert.m_Purpose != Game.Citizens.Purpose.None)
            {
                divert = new Divert();
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.RemoveComponent<Divert>(jobIndex, entity);
                if ((pathOwner.m_State & PathFlags.CachedObsolete) != (PathFlags)0)
                {
                    pathOwner.m_State &= ~PathFlags.CachedObsolete;
                    pathOwner.m_State |= PathFlags.Obsolete;
                }
            }
            // ISSUE: reference to a compiler-generated field
            if ((resident.m_Flags & ResidentFlags.Arrived) != ResidentFlags.None && this.m_PrefabRefData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.RemoveComponent<CurrentBuilding>(jobIndex, resident.m_Citizen);
            }
            currentLane.m_Flags &= ~CreatureLaneFlags.EndOfPath;
            resident.m_Flags &= ~(ResidentFlags.Arrived | ResidentFlags.Hangaround | ResidentFlags.IgnoreBenches | ResidentFlags.IgnoreAreas | ResidentFlags.CannotIgnore);
            pathOwner.m_State &= ~PathFlags.Failed;
        }

        private void SetTarget(
          int jobIndex,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref Divert divert,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          Game.Citizens.Purpose purpose,
          Entity targetEntity)
        {
            Entity target1 = Entity.Null;
            if ((resident.m_Flags & ResidentFlags.Arrived) != ResidentFlags.None)
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_PrefabRefData.HasComponent(resident.m_Citizen))
                {
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.RemoveComponent<CurrentBuilding>(jobIndex, resident.m_Citizen);
                }
                target1 = target.m_Target;
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            Entity entity1 = this.m_CommandBuffer.CreateEntity(jobIndex, this.m_ResetTripArchetype);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<ResetTrip>(jobIndex, entity1, new ResetTrip()
            {
                m_Creature = entity,
                m_Source = target1,
                m_Target = targetEntity,
                m_TravelPurpose = purpose
            });
        }

        private bool ParkingSpaceReached(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          DynamicBuffer<GroupCreature> groupCreatures)
        {
            if ((currentLane.m_Flags & CreatureLaneFlags.Taxi) != (CreatureLaneFlags)0)
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_RideNeederData.HasComponent(entity))
                {
                    // ISSUE: reference to a compiler-generated field
                    RideNeeder rideNeeder = this.m_RideNeederData[entity];
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_Dispatched.HasComponent(rideNeeder.m_RideRequest))
                    {
                        // ISSUE: reference to a compiler-generated field
                        Dispatched dispatched = this.m_Dispatched[rideNeeder.m_RideRequest];
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_TaxiData.HasComponent(dispatched.m_Handler))
                        {
                            // ISSUE: reference to a compiler-generated field
                            Game.Vehicles.Taxi taxi = this.m_TaxiData[dispatched.m_Handler];
                            // ISSUE: reference to a compiler-generated field
                            DynamicBuffer<ServiceDispatch> serviceDispatch = this.m_ServiceDispatches[dispatched.m_Handler];
                            if ((taxi.m_State & TaxiFlags.Dispatched) != (TaxiFlags)0 && serviceDispatch.Length >= 1 && serviceDispatch[0].m_Request == rideNeeder.m_RideRequest)
                            {
                                // ISSUE: reference to a compiler-generated field
                                if (this.m_CarNavigationLanes.HasBuffer(dispatched.m_Handler))
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
                                    // ISSUE: reference to a compiler-generated field
                                    DynamicBuffer<CarNavigationLane> carNavigationLane1 = this.m_CarNavigationLanes[dispatched.m_Handler];
                                    if (carNavigationLane1.Length > 0 && pathElement1.Length > pathOwner.m_ElementIndex)
                                    {
                                        PathElement pathElement2 = pathElement1[pathOwner.m_ElementIndex];
                                        CarNavigationLane carNavigationLane2 = carNavigationLane1[carNavigationLane1.Length - 1];
                                        // ISSUE: reference to a compiler-generated field
                                        // ISSUE: reference to a compiler-generated field
                                        if (carNavigationLane2.m_Lane == pathElement2.m_Target && (double)carNavigationLane2.m_CurvePosition.y != (double)pathElement2.m_TargetDelta.y && this.m_CurveData.HasComponent(currentLane.m_Lane) && this.m_CurveData.HasComponent(pathElement2.m_Target))
                                        {
                                            pathElement2.m_TargetDelta = (float2)carNavigationLane2.m_CurvePosition.y;
                                            pathElement1[pathOwner.m_ElementIndex] = pathElement2;
                                            // ISSUE: reference to a compiler-generated field
                                            float3 position = MathUtils.Position(this.m_CurveData[pathElement2.m_Target].m_Bezier, pathElement2.m_TargetDelta.y);
                                            float t;
                                            // ISSUE: reference to a compiler-generated field
                                            double num = (double)MathUtils.Distance(this.m_CurveData[currentLane.m_Lane].m_Bezier, position, out t);
                                            if ((double)t != (double)currentLane.m_CurvePosition.y)
                                            {
                                                currentLane.m_CurvePosition.y = t;
                                                currentLane.m_Flags &= ~CreatureLaneFlags.EndReached;
                                                return true;
                                            }
                                        }
                                    }
                                }
                                if ((taxi.m_State & TaxiFlags.Boarding) != (TaxiFlags)0)
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    Game.Objects.Transform transform = this.m_TransformData[entity];
                                    // ISSUE: reference to a compiler-generated field
                                    // ISSUE: reference to a compiler-generated method
                                    this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.TryEnterVehicle(entity, Entity.Null, dispatched.m_Handler, Entity.Null, Entity.Null, transform.m_Position, CreatureVehicleFlags.Leader));
                                }
                            }
                        }
                    }
                    else
                    {
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_ServiceRequestData.HasComponent(rideNeeder.m_RideRequest) && this.m_ServiceRequestData[rideNeeder.m_RideRequest].m_FailCount >= (byte)3)
                        {
                            resident.m_Flags |= ResidentFlags.IgnoreTaxi;
                            currentLane.m_Flags &= ~(CreatureLaneFlags.ParkingSpace | CreatureLaneFlags.Taxi);
                            pathOwner.m_State &= ~PathFlags.Failed;
                            pathOwner.m_State |= PathFlags.Obsolete;
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.RemoveComponent<RideNeeder>(jobIndex, entity);
                            return false;
                        }
                    }
                    return true;
                }
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<RideNeeder>(jobIndex, entity, new RideNeeder());
                return true;
            }
            CarKeeper component;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_CarKeeperData.TryGetEnabledComponent<CarKeeper>(resident.m_Citizen, out component) && this.m_ParkedCarData.HasComponent(component.m_Car))
            {
                // ISSUE: reference to a compiler-generated method
                this.ActivateParkedCar(jobIndex, ref random, entity, component.m_Car, ref resident, ref pathOwner, ref target, groupCreatures);
                // ISSUE: reference to a compiler-generated field
                Game.Objects.Transform transform = this.m_TransformData[entity];
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated method
                this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.TryEnterVehicle(entity, Entity.Null, component.m_Car, Entity.Null, Entity.Null, transform.m_Position, CreatureVehicleFlags.Leader | CreatureVehicleFlags.Driver));
                return true;
            }
            currentLane.m_Flags &= ~(CreatureLaneFlags.ParkingSpace | CreatureLaneFlags.Taxi);
            pathOwner.m_State &= ~PathFlags.Failed;
            pathOwner.m_State |= PathFlags.Obsolete;
            return false;
        }

        private bool TransportStopReached(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          PrefabRef prefabRef,
          bool isUnspawned,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target)
        {
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
            if (pathElement.Length >= pathOwner.m_ElementIndex + 2)
            {
                Entity target1 = pathElement[pathOwner.m_ElementIndex].m_Target;
                Entity target2 = pathElement[pathOwner.m_ElementIndex + 1].m_Target;
                if ((resident.m_Flags & ResidentFlags.WaitingTransport) == ResidentFlags.None)
                    resident.m_Flags |= ResidentFlags.NoLateDeparture;
                // ISSUE: reference to a compiler-generated field
                uint minDeparture = math.select(0U, this.m_SimulationFrameIndex, (resident.m_Flags & ResidentFlags.NoLateDeparture) > ResidentFlags.None);
                Entity vehicle;
                bool testing;
                bool obsolete;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (RouteUtils.GetBoardingVehicle(currentLane.m_Lane, target1, target2, minDeparture, ref this.m_OwnerData, ref this.m_RouteConnectedData, ref this.m_BoardingVehicleData, ref this.m_CurrentRouteData, ref this.m_AccessLaneLaneData, ref this.m_PublicTransportData, ref this.m_TaxiData, ref this.m_ConnectedRoutes, out vehicle, out testing, out obsolete))
                {
                    // ISSUE: reference to a compiler-generated method
                    this.TryEnterVehicle(entity, vehicle, target1, ref resident, ref currentLane);
                    // ISSUE: reference to a compiler-generated method
                    this.SetQueuePosition(entity, prefabRef, target1, ref currentLane);
                    return true;
                }
                if (!obsolete)
                {
                    if ((resident.m_Flags & ResidentFlags.WaitingTransport) != ResidentFlags.None && resident.m_Timer >= 5000)
                    {
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.WaitTimeExceeded(entity, target1));
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_BoardingVehicleData.HasComponent(target1))
                            resident.m_Flags |= ResidentFlags.IgnoreTaxi;
                        else
                            resident.m_Flags |= ResidentFlags.IgnoreTransport;
                    }
                    else
                    {
                        if (testing)
                        {
                            // ISSUE: reference to a compiler-generated field
                            Game.Objects.Transform transform = this.m_TransformData[entity];
                            // ISSUE: reference to a compiler-generated field
                            // ISSUE: reference to a compiler-generated method
                            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.RequireStop(entity, vehicle, transform.m_Position));
                        }
                        if (isUnspawned && (currentLane.m_Flags & (CreatureLaneFlags.TransformTarget | CreatureLaneFlags.Connection)) != (CreatureLaneFlags)0 && (currentLane.m_Flags & CreatureLaneFlags.WaitPosition) == (CreatureLaneFlags)0)
                        {
                            currentLane.m_Flags &= ~CreatureLaneFlags.EndReached;
                            currentLane.m_Flags |= CreatureLaneFlags.WaitPosition;
                            currentLane.m_CurvePosition.y = random.NextFloat(0.0f, 1f);
                        }
                        // ISSUE: reference to a compiler-generated method
                        this.SetQueuePosition(entity, prefabRef, target1, ref currentLane);
                        return true;
                    }
                }
            }
            currentLane.m_Flags &= ~CreatureLaneFlags.Transport;
            pathOwner.m_State &= ~PathFlags.Failed;
            pathOwner.m_State |= PathFlags.Obsolete;
            return false;
        }

        private void SetQueuePosition(
          Entity entity,
          PrefabRef prefabRef,
          Entity targetEntity,
          ref HumanCurrentLane currentLane)
        {
            // ISSUE: reference to a compiler-generated field
            Game.Objects.Transform transform = this.m_TransformData[entity];
            // ISSUE: reference to a compiler-generated field
            Sphere3 queueArea = CreatureUtils.GetQueueArea(this.m_PrefabObjectGeometryData[prefabRef.m_Prefab], transform.m_Position);
            CreatureUtils.SetQueue(ref currentLane.m_QueueEntity, ref currentLane.m_QueueArea, targetEntity, queueArea);
        }

        private Entity GetHomeBuilding(ref Game.Creatures.Resident resident, out bool movingAway)
        {
            movingAway = false;
            HouseholdMember componentData1;
            // ISSUE: reference to a compiler-generated field
            if (this.m_HouseholdMembers.TryGetComponent(resident.m_Citizen, out componentData1))
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_MovingAwayData.HasComponent(componentData1.m_Household))
                {
                    movingAway = true;
                    return Entity.Null;
                }
                PropertyRenter componentData2;
                TouristHousehold componentData3;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (this.m_PropertyRenters.TryGetComponent(componentData1.m_Household, out componentData2) && this.m_EntityLookup.Exists(componentData2.m_Property) && !this.m_DeletedData.HasComponent(componentData2.m_Property) || this.m_TouristHouseholds.TryGetComponent(componentData1.m_Household, out componentData3) && this.m_PropertyRenters.TryGetComponent(componentData3.m_Hotel, out componentData2) && this.m_EntityLookup.Exists(componentData2.m_Property) && !this.m_DeletedData.HasComponent(componentData2.m_Property))
                    return componentData2.m_Property;
                HomelessHousehold componentData4;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                if (this.m_HomelessHouseholdData.TryGetComponent(componentData1.m_Household, out componentData4) && this.m_EntityLookup.Exists(componentData4.m_TempHome) && !this.m_DeletedData.HasComponent(componentData4.m_TempHome))
                    return componentData4.m_TempHome;
                Citizen componentData5;
                // ISSUE: reference to a compiler-generated field
                if (this.m_CitizenData.TryGetComponent(resident.m_Citizen, out componentData5))
                {
                    // ISSUE: reference to a compiler-generated field
                    movingAway = (componentData5.m_State & CitizenFlags.Commuter) != CitizenFlags.None || !this.m_EntityLookup.Exists(componentData1.m_Household);
                    return Entity.Null;
                }
            }
            movingAway = true;
            return Entity.Null;
        }

        private void TryEnterVehicle(
          Entity entity,
          Entity vehicle,
          Entity waypoint,
          ref Game.Creatures.Resident resident,
          ref HumanCurrentLane currentLane)
        {
            // ISSUE: reference to a compiler-generated field
            Game.Objects.Transform transform = this.m_TransformData[entity];
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.TryEnterVehicle(entity, Entity.Null, vehicle, Entity.Null, waypoint, transform.m_Position, CreatureVehicleFlags.Leader));
        }

        private void FinishEnterVehicle(
          Entity entity,
          Entity vehicle,
          Entity controllerVehicle,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane)
        {
            // ISSUE: reference to a compiler-generated method
            Entity household = this.GetHousehold(resident);
            // ISSUE: reference to a compiler-generated method
            int ticketPrice = this.GetTicketPrice(vehicle);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.FinishEnterVehicle(entity, household, vehicle, controllerVehicle, currentLane, ticketPrice));
            human.m_Flags &= ~(HumanFlags.Run | HumanFlags.Emergency);
        }

        private void FinishExitVehicle(Entity entity, Entity vehicle, ref HumanCurrentLane currentLane)
        {
            currentLane.m_Flags &= ~CreatureLaneFlags.EndOfPath;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.FinishExitVehicle(entity, vehicle));
        }

        private void CancelEnterVehicle(
          Entity entity,
          Entity vehicle,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated method
            this.m_BoardingQueue.Enqueue(ResidentAISystem.Boarding.CancelEnterVehicle(entity, vehicle));
            human.m_Flags &= ~(HumanFlags.Run | HumanFlags.Emergency);
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<PathElement> pathElement = this.m_PathElements[entity];
            for (int elementIndex = pathOwner.m_ElementIndex; elementIndex < pathElement.Length; ++elementIndex)
            {
                if (pathElement[elementIndex].m_Target == vehicle)
                {
                    pathElement.RemoveRange(0, elementIndex + 1);
                    pathOwner.m_ElementIndex = 0;
                    return;
                }
            }
            pathElement.Clear();
            pathOwner.m_ElementIndex = 0;
            pathOwner.m_State &= ~PathFlags.Failed;
            pathOwner.m_State |= PathFlags.Obsolete;
        }

        private Entity GetHousehold(Game.Creatures.Resident resident)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            return this.m_HouseholdMembers.HasComponent(resident.m_Citizen) ? this.m_HouseholdMembers[resident.m_Citizen].m_Household : Entity.Null;
        }

        private int GetTicketPrice(Entity vehicle)
        {
            // ISSUE: reference to a compiler-generated field
            if (this.m_CurrentRouteData.HasComponent(vehicle))
            {
                // ISSUE: reference to a compiler-generated field
                CurrentRoute currentRoute = this.m_CurrentRouteData[vehicle];
                // ISSUE: reference to a compiler-generated field
                if (this.m_TransportLineData.HasComponent(currentRoute.m_Route))
                {
                    // ISSUE: reference to a compiler-generated field
                    return (int)this.m_TransportLineData[currentRoute.m_Route].m_TicketPrice;
                }
            }
            return 0;
        }

        private void FindNewPath(
          Entity entity,
          PrefabRef prefabRef,
          ref Game.Creatures.Resident resident,
          ref Human human,
          ref HumanCurrentLane currentLane,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          ref Divert divert)
        {
            // ISSUE: reference to a compiler-generated field
            CreatureData creatureData = this.m_PrefabCreatureData[prefabRef.m_Prefab];
            // ISSUE: reference to a compiler-generated field
            HumanData humanData = this.m_PrefabHumanData[prefabRef.m_Prefab];
            pathOwner.m_State &= ~(PathFlags.AddDestination | PathFlags.Divert);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            PathfindParameters parameters = new PathfindParameters()
            {
                m_MaxSpeed = (float2)277.777771f,
                m_WalkSpeed = (float2)humanData.m_WalkSpeed,
                m_Weights = new PathfindWeights(1f, 1f, 1f, 1f),
                m_Methods = PathMethod.Pedestrian | RouteUtils.GetTaxiMethods(resident) | RouteUtils.GetPublicTransportMethods(resident, this.m_TimeOfDay),
                m_SecondaryIgnoredRules = VehicleUtils.GetIgnoredPathfindRulesTaxiDefaults(),
                m_MaxCost = CitizenBehaviorSystem.kMaxPathfindCost
            };
            SetupQueueTarget origin = new SetupQueueTarget()
            {
                m_Type = SetupTargetType.CurrentLocation,
                m_Methods = PathMethod.Pedestrian,
                m_RandomCost = 30f
            };
            SetupQueueTarget destination = new SetupQueueTarget()
            {
                m_Type = SetupTargetType.CurrentLocation,
                m_Methods = PathMethod.Pedestrian,
                m_Entity = target.m_Target,
                m_RandomCost = 30f,
                m_ActivityMask = creatureData.m_SupportedActivities
            };
            // 1) Figure out origin & destination in XZ (meters)
            float2 originXZ = default, destXZ = default;

            // origin: the resident’s current world position
            if (m_TransformData.HasComponent(entity))
            {
                var tr = m_TransformData[entity];
                originXZ = XZ(tr.m_Position);
            }

            // destination: from Game.Common.Target (prefer transform; fall back to stop/building center)
            if (m_TransformData.HasComponent(target.m_Target))
            {
                var trT = m_TransformData[target.m_Target];
                destXZ = XZ(trT.m_Position);
            }
            else
            {
                // Fallbacks are fine; use whatever you already have available:
                //  - currentLane end point
                //  - building center
                //  - target waypoint
                // (replace the next line with your own helper)
                destXZ = originXZ; // TODO: replace with your real dest XZ
            }

            // 2) Compute straight-line OD (meters)
            float odMeters = math.distance(originXZ, destXZ);

            // 4) Apply the long-walk multiplier to pedestrian speed
            float mult = LongWalkSpeedMultiplier(odMeters, ComfortMeters, RampMeters, MinSpeedMult);
            float baseWalk = parameters.m_WalkSpeed.x;
            float newWalk = baseWalk * mult;
            parameters.m_WalkSpeed = new float2(newWalk, newWalk);
            // ISSUE: reference to a compiler-generated field
            if (this.m_CitizenData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                Citizen citizen = this.m_CitizenData[resident.m_Citizen];
                // ISSUE: reference to a compiler-generated field
                Entity household1 = this.m_HouseholdMembers[resident.m_Citizen].m_Household;
                // ISSUE: reference to a compiler-generated field
                Household household2 = this.m_HouseholdData[household1];
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<HouseholdCitizen> householdCitizen = this.m_HouseholdCitizens[household1];
                parameters.m_Weights = CitizenUtils.GetPathfindWeights(citizen, household2, householdCitizen.Length);
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_HouseholdMembers.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                Entity household = this.m_HouseholdMembers[resident.m_Citizen].m_Household;
                // ISSUE: reference to a compiler-generated field
                if (this.m_PropertyRenters.HasComponent(household))
                {
                    // ISSUE: reference to a compiler-generated field
                    parameters.m_Authorization1 = this.m_PropertyRenters[household].m_Property;
                }
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_WorkerData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                Worker worker = this.m_WorkerData[resident.m_Citizen];
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                parameters.m_Authorization2 = !this.m_PropertyRenters.HasComponent(worker.m_Workplace) ? worker.m_Workplace : this.m_PropertyRenters[worker.m_Workplace].m_Property;
            }
            CarKeeper component;
            ParkedCar componentData1;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_CarKeeperData.TryGetEnabledComponent<CarKeeper>(resident.m_Citizen, out component) && this.m_ParkedCarData.TryGetComponent(component.m_Car, out componentData1))
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                CarData carData = this.m_PrefabCarData[this.m_PrefabRefData[component.m_Car].m_Prefab];
                parameters.m_MaxSpeed.x = carData.m_MaxSpeed;
                parameters.m_ParkingTarget = componentData1.m_Lane;
                parameters.m_ParkingDelta = componentData1.m_CurvePosition;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                parameters.m_ParkingSize = VehicleUtils.GetParkingSize(component.m_Car, ref this.m_PrefabRefData, ref this.m_PrefabObjectGeometryData);
                parameters.m_Methods |= VehicleUtils.GetPathMethods(carData) | PathMethod.Parking;
                parameters.m_IgnoredRules = VehicleUtils.GetIgnoredPathfindRules(carData);
                Game.Vehicles.PersonalCar componentData2;
                // ISSUE: reference to a compiler-generated field
                if (this.m_PersonalCarData.TryGetComponent(component.m_Car, out componentData2) && (componentData2.m_State & PersonalCarFlags.HomeTarget) == (PersonalCarFlags)0)
                    parameters.m_PathfindFlags |= PathfindFlags.ParkingReset;
            }
            bool flag = false;
            TravelPurpose componentData3;
            // ISSUE: reference to a compiler-generated field
            if (this.m_TravelPurposeData.TryGetComponent(resident.m_Citizen, out componentData3))
            {
                Game.Citizens.Purpose purpose = componentData3.m_Purpose;
                if ((uint)purpose <= 12U)
                {
                    switch (purpose)
                    {
                        case Game.Citizens.Purpose.MovingAway:
                            // ISSUE: reference to a compiler-generated field
                            parameters.m_MaxCost = CitizenBehaviorSystem.kMaxMovingAwayCost;
                            goto label_17;
                        case Game.Citizens.Purpose.Hospital:
                            break;
                        default:
                            goto label_17;
                    }
                }
                else
                {
                    switch (purpose)
                    {
                        case Game.Citizens.Purpose.EmergencyShelter:
                            parameters.m_Weights = new PathfindWeights(1f, 0.2f, 0.0f, 0.1f);
                            goto label_17;
                        case Game.Citizens.Purpose.Deathcare:
                            break;
                        default:
                            goto label_17;
                    }
                }
                HealthProblem componentData4;
                // ISSUE: reference to a compiler-generated field
                flag = this.m_HealthProblemData.TryGetComponent(resident.m_Citizen, out componentData4) && (componentData4.m_Flags & HealthProblemFlags.RequireTransport) != 0;
            }
        label_17:
            if ((resident.m_Flags & ResidentFlags.IgnoreBenches) != ResidentFlags.None)
            {
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.BenchSitting).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.PullUps).m_Mask;
            }
            if ((resident.m_Flags & ResidentFlags.IgnoreAreas) != ResidentFlags.None)
            {
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.Standing).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.GroundLaying).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.GroundSitting).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.PushUps).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.SitUps).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.JumpingJacks).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.JumpingLunges).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.Squats).m_Mask;
                destination.m_ActivityMask.m_Mask &= ~new ActivityMask(ActivityType.Yoga).m_Mask;
            }
            if (flag)
            {
                human.m_Flags |= HumanFlags.Carried;
                currentLane.m_CurvePosition.y = currentLane.m_CurvePosition.x;
                pathOwner.m_ElementIndex = 0;
                pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Obsolete | PathFlags.DivertObsolete | PathFlags.CachedObsolete);
                // ISSUE: reference to a compiler-generated field
                this.m_PathElements[entity].Clear();
            }
            else if (CreatureUtils.DivertDestination(ref destination, ref pathOwner, divert))
            {
                SetupQueueItem setupQueueItem = new SetupQueueItem(entity, parameters, origin, destination);
                // ISSUE: reference to a compiler-generated field
                CreatureUtils.SetupPathfind(ref currentLane, ref pathOwner, this.m_PathfindQueue, setupQueueItem);
            }
            else
            {
                currentLane.m_CurvePosition.y = currentLane.m_CurvePosition.x;
                pathOwner.m_ElementIndex = 0;
                pathOwner.m_State |= PathFlags.CachedObsolete;
                pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Obsolete | PathFlags.DivertObsolete);
                // ISSUE: reference to a compiler-generated field
                this.m_PathElements[entity].Clear();
            }
        }

        private void ActivateParkedCar(
          int jobIndex,
          ref Unity.Mathematics.Random random,
          Entity entity,
          Entity carEntity,
          ref Game.Creatures.Resident resident,
          ref PathOwner pathOwner,
          ref Game.Common.Target target,
          DynamicBuffer<GroupCreature> groupCreatures)
        {
            // ISSUE: reference to a compiler-generated field
            ParkedCar parkedCar1 = this.m_ParkedCarData[carEntity];
            Game.Vehicles.CarLaneFlags flags = Game.Vehicles.CarLaneFlags.EndReached | Game.Vehicles.CarLaneFlags.ParkingSpace | Game.Vehicles.CarLaneFlags.FixedLane;
            DynamicBuffer<LayoutElement> dynamicBuffer1 = new DynamicBuffer<LayoutElement>();
            // ISSUE: reference to a compiler-generated field
            if (this.m_VehicleLayouts.HasBuffer(carEntity))
            {
                // ISSUE: reference to a compiler-generated field
                dynamicBuffer1 = this.m_VehicleLayouts[carEntity];
            }
            if (parkedCar1.m_Lane == Entity.Null)
            {
                // ISSUE: reference to a compiler-generated field
                DynamicBuffer<PathElement> pathElement1 = this.m_PathElements[entity];
                if (pathElement1.Length > pathOwner.m_ElementIndex)
                {
                    PathElement pathElement2 = pathElement1[pathOwner.m_ElementIndex];
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_CurveData.HasComponent(pathElement2.m_Target))
                    {
                        parkedCar1.m_Lane = pathElement2.m_Target;
                        // ISSUE: reference to a compiler-generated field
                        Curve curve = this.m_CurveData[parkedCar1.m_Lane];
                        // ISSUE: reference to a compiler-generated field
                        Game.Objects.Transform transform1 = this.m_TransformData[entity];
                        double num = (double)MathUtils.Distance(curve.m_Bezier, transform1.m_Position, out parkedCar1.m_CurvePosition);
                        Game.Objects.Transform transform2 = VehicleUtils.CalculateTransform(curve, parkedCar1.m_CurvePosition);
                        bool flag = false;
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_ConnectionLaneData.HasComponent(parkedCar1.m_Lane))
                        {
                            // ISSUE: reference to a compiler-generated field
                            Game.Net.ConnectionLane connectionLane = this.m_ConnectionLaneData[parkedCar1.m_Lane];
                            if ((connectionLane.m_Flags & ConnectionLaneFlags.Parking) != (ConnectionLaneFlags)0)
                            {
                                parkedCar1.m_CurvePosition = random.NextFloat(0.0f, 1f);
                                transform2.m_Position = VehicleUtils.GetConnectionParkingPosition(connectionLane, curve.m_Bezier, parkedCar1.m_CurvePosition);
                            }
                            flag = true;
                        }
                        // ISSUE: reference to a compiler-generated field
                        this.m_CommandBuffer.SetComponent<Game.Objects.Transform>(jobIndex, carEntity, transform2);
                        if (flag)
                        {
                            // ISSUE: reference to a compiler-generated field
                            this.m_CommandBuffer.AddComponent<Unspawned>(jobIndex, carEntity, new Unspawned());
                        }
                        if (dynamicBuffer1.IsCreated)
                        {
                            for (int index = 1; index < dynamicBuffer1.Length; ++index)
                            {
                                Entity vehicle = dynamicBuffer1[index].m_Vehicle;
                                // ISSUE: reference to a compiler-generated field
                                this.m_CommandBuffer.SetComponent<Game.Objects.Transform>(jobIndex, vehicle, transform2);
                                if (flag)
                                {
                                    // ISSUE: reference to a compiler-generated field
                                    this.m_CommandBuffer.AddComponent<Unspawned>(jobIndex, vehicle, new Unspawned());
                                }
                            }
                        }
                    }
                }
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_ConnectionLaneData.HasComponent(parkedCar1.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                if ((this.m_ConnectionLaneData[parkedCar1.m_Lane].m_Flags & ConnectionLaneFlags.Area) != (ConnectionLaneFlags)0)
                    flags |= Game.Vehicles.CarLaneFlags.Area;
                else
                    flags |= Game.Vehicles.CarLaneFlags.Connection;
            }
            // ISSUE: reference to a compiler-generated field
            Game.Vehicles.PersonalCar component = this.m_PersonalCarData[carEntity];
            component.m_State |= PersonalCarFlags.Boarding;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.RemoveComponent(jobIndex, carEntity, in this.m_ParkedToMovingCarRemoveTypes);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent(jobIndex, carEntity, in this.m_ParkedToMovingCarAddTypes);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<Game.Vehicles.PersonalCar>(jobIndex, carEntity, component);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<CarCurrentLane>(jobIndex, carEntity, new CarCurrentLane(parkedCar1, flags));
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_ParkingLaneData.HasComponent(parkedCar1.m_Lane) || this.m_GarageLaneData.HasComponent(parkedCar1.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<PathfindUpdated>(jobIndex, parkedCar1.m_Lane);
            }
            if (dynamicBuffer1.IsCreated)
            {
                for (int index = 1; index < dynamicBuffer1.Length; ++index)
                {
                    Entity vehicle = dynamicBuffer1[index].m_Vehicle;
                    // ISSUE: reference to a compiler-generated field
                    ParkedCar parkedCar2 = this.m_ParkedCarData[vehicle];
                    if (parkedCar2.m_Lane == Entity.Null)
                    {
                        parkedCar2.m_Lane = parkedCar1.m_Lane;
                        parkedCar2.m_CurvePosition = parkedCar1.m_CurvePosition;
                    }
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.RemoveComponent(jobIndex, vehicle, in this.m_ParkedToMovingCarRemoveTypes);
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.AddComponent(jobIndex, vehicle, in this.m_ParkedToMovingTrailerAddTypes);
                    // ISSUE: reference to a compiler-generated field
                    this.m_CommandBuffer.SetComponent<CarTrailerLane>(jobIndex, vehicle, new CarTrailerLane(parkedCar2));
                    // ISSUE: reference to a compiler-generated field
                    // ISSUE: reference to a compiler-generated field
                    if ((this.m_ParkingLaneData.HasComponent(parkedCar2.m_Lane) || this.m_GarageLaneData.HasComponent(parkedCar2.m_Lane)) && parkedCar2.m_Lane != parkedCar1.m_Lane)
                    {
                        // ISSUE: reference to a compiler-generated field
                        this.m_CommandBuffer.AddComponent<PathfindUpdated>(jobIndex, parkedCar2.m_Lane);
                    }
                }
            }
            if (dynamicBuffer1.IsCreated && dynamicBuffer1.Length > 1)
                return;
            int num1 = 1;
            int num2 = 0;
            if (groupCreatures.IsCreated)
            {
                for (int index = 0; index < groupCreatures.Length; ++index)
                {
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_AnimalData.HasComponent(groupCreatures[index].m_Creature))
                        ++num2;
                    else
                        ++num1;
                }
            }
            int passengerAmount = num1;
            int baggageAmount = 1 + num2;
            // ISSUE: reference to a compiler-generated field
            if (this.m_TravelPurposeData.HasComponent(resident.m_Citizen))
            {
                // ISSUE: reference to a compiler-generated field
                switch (this.m_TravelPurposeData[resident.m_Citizen].m_Purpose)
                {
                    case Game.Citizens.Purpose.Shopping:
                        if (random.NextInt(10) == 0)
                        {
                            baggageAmount += 5;
                            if (random.NextInt(10) == 0)
                            {
                                baggageAmount += 5;
                                break;
                            }
                            break;
                        }
                        break;
                    case Game.Citizens.Purpose.Leisure:
                        if (random.NextInt(20) == 0)
                        {
                            passengerAmount += 5;
                            baggageAmount += 5;
                            break;
                        }
                        break;
                    case Game.Citizens.Purpose.MovingAway:
                        if (random.NextInt(20) == 0)
                        {
                            passengerAmount += 5;
                            baggageAmount += 5;
                            break;
                        }
                        if (random.NextInt(10) == 0)
                        {
                            baggageAmount += 5;
                            if (random.NextInt(10) == 0)
                            {
                                baggageAmount += 5;
                                break;
                            }
                            break;
                        }
                        break;
                }
            }
            // ISSUE: reference to a compiler-generated field
            Game.Objects.Transform tractorTransform = this.m_TransformData[carEntity];
            // ISSUE: reference to a compiler-generated field
            PrefabRef prefabRef = this.m_PrefabRefData[carEntity];
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            Entity trailer = this.m_PersonalCarSelectData.CreateTrailer(this.m_CommandBuffer, jobIndex, ref random, passengerAmount, baggageAmount, false, prefabRef.m_Prefab, tractorTransform, (PersonalCarFlags)0, false);
            if (!(trailer != Entity.Null))
                return;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<LayoutElement> dynamicBuffer2 = !dynamicBuffer1.IsCreated ? this.m_CommandBuffer.AddBuffer<LayoutElement>(jobIndex, carEntity) : this.m_CommandBuffer.SetBuffer<LayoutElement>(jobIndex, carEntity);
            dynamicBuffer2.Add(new LayoutElement(carEntity));
            dynamicBuffer2.Add(new LayoutElement(trailer));
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<Controller>(jobIndex, trailer, new Controller(carEntity));
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<CarTrailerLane>(jobIndex, trailer, new CarTrailerLane(parkedCar1));
        }

        void IJobChunk.Execute(
          in ArchetypeChunk chunk,
          int unfilteredChunkIndex,
          bool useEnabledMask,
          in v128 chunkEnabledMask)
        {
            // ISSUE: reference to a compiler-generated method
            this.Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
        }

        private struct TransportEstimateBuffer : RouteUtils.ITransportEstimateBuffer
        {
            public NativeQueue<ResidentAISystem.Boarding>.ParallelWriter m_BoardingQueue;

            public void AddWaitEstimate(Entity waypoint, int seconds)
            {
                this.m_BoardingQueue.Enqueue(RPFResidentAISystem.Boarding.WaitTimeEstimate(waypoint, seconds));
            }
        }
    }

    [BurstCompile]
    private struct BoardingJob : IJob
    {
        [ReadOnly]
        public ComponentLookup<Citizen> m_Citizens;
        [ReadOnly]
        public ComponentLookup<Game.Objects.Transform> m_Transforms;
        [ReadOnly]
        public ComponentLookup<PrefabRef> m_PrefabRefData;
        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> m_ObjectGeometryData;
        [ReadOnly]
        public ComponentLookup<TaxiData> m_TaxiData;
        [ReadOnly]
        public ComponentLookup<PublicTransportVehicleData> m_PublicTransportVehicleData;
        [ReadOnly]
        public ComponentLookup<PersonalCarData> m_PrefabPersonalCarData;
        [ReadOnly]
        public BufferLookup<GroupCreature> m_GroupCreatures;
        [ReadOnly]
        public BufferLookup<LayoutElement> m_VehicleLayouts;
        [ReadOnly]
        public BufferLookup<ActivityLocationElement> m_ActivityLocations;
        public ComponentLookup<Game.Creatures.Resident> m_Residents;
        public ComponentLookup<Creature> m_Creatures;
        public ComponentLookup<Game.Vehicles.Taxi> m_Taxis;
        public ComponentLookup<Game.Vehicles.PublicTransport> m_PublicTransports;
        public ComponentLookup<WaitingPassengers> m_WaitingPassengers;
        public BufferLookup<Queue> m_Queues;
        public BufferLookup<Passenger> m_Passengers;
        public BufferLookup<LaneObject> m_LaneObjects;
        public BufferLookup<Game.Economy.Resources> m_Resources;
        public ComponentLookup<PlayerMoney> m_PlayerMoney;
        [ReadOnly]
        public Entity m_City;
        [ReadOnly]
        public ComponentTypeSet m_CurrentLaneTypes;
        [ReadOnly]
        public ComponentTypeSet m_CurrentLaneTypesRelative;
        public NativeQueue<RPFResidentAISystem.Boarding> m_BoardingQueue;
        public NativeQuadTree<Entity, QuadTreeBoundsXZ> m_SearchTree;
        public EntityCommandBuffer m_CommandBuffer;
        public NativeQueue<StatisticsEvent>.ParallelWriter m_StatisticsEventQueue;
        public NativeQueue<ServiceFeeSystem.FeeEvent> m_FeeQueue;

        public void Execute()
        {
            NativeParallelHashMap<Entity, int3> freeSpaceMap = new NativeParallelHashMap<Entity, int3>();
            // ISSUE: variable of a compiler-generated type
            RPFResidentAISystem.Boarding boarding;
            // ISSUE: reference to a compiler-generated field
            while (this.m_BoardingQueue.TryDequeue(out boarding))
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: variable of a compiler-generated type
                RPFResidentAISystem.BoardingType type = boarding.m_Type;
                switch (type)
                {
                    case RPFResidentAISystem.BoardingType.Exit:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.ExitVehicle(ref freeSpaceMap, boarding.m_Passenger, boarding.m_Household, boarding.m_Vehicle, boarding.m_CurrentLane, boarding.m_Position, boarding.m_Rotation, boarding.m_TicketPrice);
                        continue;
                    case RPFResidentAISystem.BoardingType.TryEnter:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.TryEnterVehicle(ref freeSpaceMap, boarding.m_Passenger, boarding.m_Leader, boarding.m_Vehicle, boarding.m_LeaderVehicle, boarding.m_Waypoint, boarding.m_Position, boarding.m_Flags);
                        continue;
                    case RPFResidentAISystem.BoardingType.FinishEnter:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.FinishEnterVehicle(boarding.m_Passenger, boarding.m_Household, boarding.m_Vehicle, boarding.m_LeaderVehicle, boarding.m_CurrentLane, boarding.m_TicketPrice);
                        continue;
                    case RPFResidentAISystem.BoardingType.CancelEnter:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.CancelEnterVehicle(ref freeSpaceMap, boarding.m_Passenger, boarding.m_Vehicle);
                        continue;
                    case RPFResidentAISystem.BoardingType.RequireStop:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.RequireStop(ref freeSpaceMap, boarding.m_Passenger, boarding.m_Vehicle, boarding.m_Position);
                        continue;
                    case RPFResidentAISystem.BoardingType.WaitTimeExceeded:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.WaitTimeExceeded(boarding.m_Passenger, boarding.m_Waypoint);
                        continue;
                    case RPFResidentAISystem.BoardingType.WaitTimeEstimate:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.WaitTimeEstimate(boarding.m_Waypoint, boarding.m_TicketPrice);
                        continue;
                    case RPFResidentAISystem.BoardingType.FinishExit:
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated field
                        // ISSUE: reference to a compiler-generated method
                        this.FinishExitVehicle(ref freeSpaceMap, boarding.m_Passenger, boarding.m_Vehicle);
                        continue;
                    default:
                        continue;
                }
            }
            if (!freeSpaceMap.IsCreated)
                return;
            freeSpaceMap.Dispose();
        }

        private void ExitVehicle(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity passenger,
          Entity household,
          Entity vehicle,
          HumanCurrentLane newCurrentLane,
          float3 position,
          quaternion rotation,
          int ticketPrice)
        {
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.RemoveComponent<Relative>(passenger);
            // ISSUE: reference to a compiler-generated field
            Game.Creatures.Resident resident = this.m_Residents[passenger];
            resident.m_Flags &= ~ResidentFlags.InVehicle;
            resident.m_Timer = 0;
            // ISSUE: reference to a compiler-generated field
            this.m_Residents[passenger] = resident;
            // ISSUE: reference to a compiler-generated field
            if (this.m_LaneObjects.HasBuffer(newCurrentLane.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                NetUtils.AddLaneObject(this.m_LaneObjects[newCurrentLane.m_Lane], passenger, (float2)newCurrentLane.m_CurvePosition.x);
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                ObjectGeometryData geometryData = this.m_ObjectGeometryData[this.m_PrefabRefData[passenger].m_Prefab];
                Bounds3 bounds = ObjectUtils.CalculateBounds(position, quaternion.identity, geometryData);
                // ISSUE: reference to a compiler-generated field
                this.m_SearchTree.Add(passenger, new QuadTreeBoundsXZ(bounds));
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent(passenger, in this.m_CurrentLaneTypes);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<Updated>(passenger, new Updated());
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<HumanCurrentLane>(passenger, newCurrentLane);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponent<Game.Objects.Transform>(passenger, new Game.Objects.Transform(position, rotation));
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (ticketPrice == 0 || !this.m_Resources.HasBuffer(household) || !this.m_PlayerMoney.HasComponent(this.m_City))
                return;
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<Game.Economy.Resources> resource = this.m_Resources[household];
            if (ticketPrice > 0)
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                PlayerMoney playerMoney = this.m_PlayerMoney[this.m_City];
                playerMoney.Add(ticketPrice);
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                this.m_PlayerMoney[this.m_City] = playerMoney;
                // ISSUE: reference to a compiler-generated field
                // ISSUE: object of a compiler-generated type is created
                this.m_FeeQueue.Enqueue(new ServiceFeeSystem.FeeEvent()
                {
                    m_Amount = 1f,
                    m_Cost = (float)ticketPrice,
                    m_Resource = PlayerResource.PublicTransport,
                    m_Outside = false
                });
                ticketPrice = -ticketPrice;
            }
            EconomyUtils.AddResources(Resource.Money, ticketPrice, resource);
        }

        private void FinishExitVehicle(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity passenger,
          Entity vehicle)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_Passengers.HasBuffer(vehicle) && CollectionUtils.RemoveValue<Passenger>(this.m_Passengers[vehicle], new Passenger(passenger)))
            {
                // ISSUE: reference to a compiler-generated method
                int3 freeSpace = this.GetFreeSpace(ref freeSpaceMap, vehicle);
                ++freeSpace.x;
                freeSpaceMap[vehicle] = freeSpace;
            }
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.RemoveComponent<CurrentVehicle>(passenger);
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<BatchesUpdated>(passenger, new BatchesUpdated());
        }

        private Entity TryFindVehicle(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity vehicle,
          Entity leaderVehicle,
          float3 position,
          bool isLeader,
          int requiredSpace,
          out float distance)
        {
            Entity entity = vehicle;
            int3 int3 = (int3)0;
            DynamicBuffer<LayoutElement> bufferData;
            // ISSUE: reference to a compiler-generated field
            if (this.m_VehicleLayouts.TryGetBuffer(vehicle, out bufferData))
            {
                distance = float.MaxValue;
                int num1 = 0;
                for (int index = 0; index < bufferData.Length; ++index)
                {
                    Entity vehicle1 = bufferData[index].m_Vehicle;
                    // ISSUE: reference to a compiler-generated field
                    Game.Objects.Transform transform = this.m_Transforms[vehicle1];
                    float num2 = math.distancesq(position, transform.m_Position);
                    // ISSUE: reference to a compiler-generated method
                    int3 freeSpace = this.GetFreeSpace(ref freeSpaceMap, vehicle1);
                    if (isLeader)
                    {
                        int3.xy += freeSpace.xy;
                        int3.z |= freeSpace.z;
                        freeSpace.x = math.min(freeSpace.x, requiredSpace);
                    }
                    else
                    {
                        freeSpace.x += math.select(0, requiredSpace, vehicle1 == leaderVehicle);
                        int3.xy += freeSpace.xy;
                        int3.z |= freeSpace.z;
                        freeSpace.x = math.min(freeSpace.x, 1) * 2;
                        freeSpace.x += math.select(0, 1, vehicle1 == leaderVehicle);
                    }
                    if (freeSpace.x > num1 | freeSpace.x == num1 & (double)num2 < (double)distance)
                    {
                        distance = num2;
                        num1 = freeSpace.x;
                        entity = vehicle1;
                        if ((freeSpace.z & 4) != 0 & isLeader)
                            break;
                    }
                }
                distance = math.sqrt(distance);
            }
            else
            {
                // ISSUE: reference to a compiler-generated method
                int3 = this.GetFreeSpace(ref freeSpaceMap, vehicle);
                // ISSUE: reference to a compiler-generated field
                Game.Objects.Transform transform = this.m_Transforms[vehicle];
                distance = math.distance(position, transform.m_Position);
            }
            return !isLeader || (int3.z & 1) != 0 && int3.x >= requiredSpace || (int3.z & 6) != 0 && int3.x == int3.y ? entity : Entity.Null;
        }

        private int3 GetFreeSpace(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity vehicle)
        {
            if (!freeSpaceMap.IsCreated)
                freeSpaceMap = new NativeParallelHashMap<Entity, int3>(20, (AllocatorManager.AllocatorHandle)Allocator.Temp);
            int3 freeSpace;
            if (freeSpaceMap.TryGetValue(vehicle, out freeSpace))
                return freeSpace;
            DynamicBuffer<Passenger> bufferData;
            // ISSUE: reference to a compiler-generated field
            if (this.m_Passengers.TryGetBuffer(vehicle, out bufferData))
            {
                freeSpace = (int3)0;
                for (int index = 0; index < bufferData.Length; ++index)
                {
                    Passenger passenger = bufferData[index];
                    Game.Creatures.Resident componentData;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_Residents.TryGetComponent(passenger.m_Passenger, out componentData) && (componentData.m_Flags & ResidentFlags.InVehicle) != ResidentFlags.None)
                    {
                        // ISSUE: reference to a compiler-generated method
                        freeSpace.x -= 1 + this.GetPendingGroupMemberCount(passenger.m_Passenger);
                    }
                }
                // ISSUE: reference to a compiler-generated field
                PrefabRef prefabRef = this.m_PrefabRefData[vehicle];
                PublicTransportVehicleData componentData1;
                // ISSUE: reference to a compiler-generated field
                if (this.m_PublicTransportVehicleData.TryGetComponent(prefabRef.m_Prefab, out componentData1))
                {
                    freeSpace.xy += componentData1.m_PassengerCapacity;
                    freeSpace.z |= 1;
                }
                else
                {
                    TaxiData componentData2;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_TaxiData.TryGetComponent(prefabRef.m_Prefab, out componentData2))
                    {
                        freeSpace.xy += componentData2.m_PassengerCapacity;
                        freeSpace.z |= 2;
                    }
                    else
                    {
                        PersonalCarData componentData3;
                        // ISSUE: reference to a compiler-generated field
                        if (this.m_PrefabPersonalCarData.TryGetComponent(prefabRef.m_Prefab, out componentData3))
                        {
                            freeSpace.xy += componentData3.m_PassengerCapacity;
                            freeSpace.z |= 4;
                        }
                        else
                        {
                            freeSpace.xy += 1000000;
                            freeSpace.z |= 1;
                        }
                    }
                }
                freeSpaceMap.Add(vehicle, freeSpace);
                return freeSpace;
            }
            freeSpaceMap.Add(vehicle, (int3)0);
            return (int3)0;
        }

        private int GetPendingGroupMemberCount(Entity leader)
        {
            int groupMemberCount = 0;
            DynamicBuffer<GroupCreature> bufferData;
            // ISSUE: reference to a compiler-generated field
            if (this.m_GroupCreatures.TryGetBuffer(leader, out bufferData))
            {
                for (int index = 0; index < bufferData.Length; ++index)
                {
                    Game.Creatures.Resident componentData;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_Residents.TryGetComponent(bufferData[index].m_Creature, out componentData) && (componentData.m_Flags & ResidentFlags.InVehicle) == ResidentFlags.None)
                        ++groupMemberCount;
                }
            }
            return groupMemberCount;
        }

        private void TryEnterVehicle(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity passenger,
          Entity leader,
          Entity vehicle,
          Entity leaderVehicle,
          Entity waypoint,
          float3 position,
          CreatureVehicleFlags flags)
        {
            int requiredSpace;
            if ((flags & CreatureVehicleFlags.Leader) != (CreatureVehicleFlags)0)
            {
                Entity entity = vehicle;
                // ISSUE: reference to a compiler-generated method
                requiredSpace = 1 + this.GetPendingGroupMemberCount(passenger);
                float distance;
                // ISSUE: reference to a compiler-generated method
                vehicle = this.TryFindVehicle(ref freeSpaceMap, vehicle, Entity.Null, position, true, requiredSpace, out distance);
                if (vehicle == Entity.Null)
                    return;
                Game.Vehicles.Taxi componentData1;
                // ISSUE: reference to a compiler-generated field
                if (this.m_Taxis.TryGetComponent(entity, out componentData1) && (double)distance > (double)componentData1.m_MaxBoardingDistance)
                {
                    componentData1.m_MinWaitingDistance = math.min(componentData1.m_MinWaitingDistance, distance);
                    // ISSUE: reference to a compiler-generated field
                    this.m_Taxis[entity] = componentData1;
                    return;
                }
                Game.Vehicles.PublicTransport componentData2;
                // ISSUE: reference to a compiler-generated field
                if (this.m_PublicTransports.TryGetComponent(entity, out componentData2) && (double)distance > (double)componentData2.m_MaxBoardingDistance)
                {
                    componentData2.m_MinWaitingDistance = math.min(componentData2.m_MinWaitingDistance, distance);
                    // ISSUE: reference to a compiler-generated field
                    this.m_PublicTransports[entity] = componentData2;
                    return;
                }
                int3 int3 = freeSpaceMap[vehicle];
                int3.x -= requiredSpace;
                freeSpaceMap[vehicle] = int3;
            }
            else
            {
                // ISSUE: reference to a compiler-generated method
                requiredSpace = this.GetPendingGroupMemberCount(leader);
                // ISSUE: reference to a compiler-generated method
                vehicle = this.TryFindVehicle(ref freeSpaceMap, vehicle, leaderVehicle, position, false, requiredSpace, out float _);
                if (vehicle == Entity.Null)
                    return;
                if (vehicle != leaderVehicle)
                {
                    int3 int3_1 = freeSpaceMap[leaderVehicle];
                    ++int3_1.x;
                    freeSpaceMap[leaderVehicle] = int3_1;
                    int3 int3_2 = freeSpaceMap[vehicle];
                    --int3_2.x;
                    freeSpaceMap[vehicle] = int3_2;
                }
            }
            // ISSUE: reference to a compiler-generated field
            this.m_Passengers[vehicle].Add(new Passenger(passenger));
            // ISSUE: reference to a compiler-generated field
            ref Game.Creatures.Resident local1 = ref this.m_Residents.GetRefRW(passenger).ValueRW;
            // ISSUE: reference to a compiler-generated field
            if ((flags & CreatureVehicleFlags.Leader) != (CreatureVehicleFlags)0 && this.m_WaitingPassengers.HasComponent(waypoint))
            {
                // ISSUE: reference to a compiler-generated field
                ref WaitingPassengers local2 = ref this.m_WaitingPassengers.GetRefRW(waypoint).ValueRW;
                int num = (int)((double)(local1.m_Timer * requiredSpace) * 0.13333334028720856);
                local2.m_ConcludedAccumulation += num;
                local2.m_SuccessAccumulation = (ushort)math.min((int)ushort.MaxValue, (int)local2.m_SuccessAccumulation + requiredSpace);
            }
            local1.m_Flags &= ~(ResidentFlags.WaitingTransport | ResidentFlags.NoLateDeparture);
            local1.m_Flags |= ResidentFlags.InVehicle;
            local1.m_Timer = 0;
            // ISSUE: reference to a compiler-generated field
            this.m_Queues[passenger].Clear();
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<CurrentVehicle>(passenger, new CurrentVehicle(vehicle, flags));
        }

        private void CancelEnterVehicle(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity passenger,
          Entity vehicle)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_Passengers.HasBuffer(vehicle) && CollectionUtils.RemoveValue<Passenger>(this.m_Passengers[vehicle], new Passenger(passenger)))
            {
                // ISSUE: reference to a compiler-generated method
                int3 freeSpace = this.GetFreeSpace(ref freeSpaceMap, vehicle);
                ++freeSpace.x;
                freeSpaceMap[vehicle] = freeSpace;
            }
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.RemoveComponent<CurrentVehicle>(passenger);
            // ISSUE: reference to a compiler-generated field
            ref Game.Creatures.Resident local = ref this.m_Residents.GetRefRW(passenger).ValueRW;
            local.m_Flags &= ~ResidentFlags.InVehicle;
            local.m_Timer = 0;
        }

        private void FinishEnterVehicle(
          Entity passenger,
          Entity household,
          Entity vehicle,
          Entity controllerVehicle,
          HumanCurrentLane oldCurrentLane,
          int ticketPrice)
        {
            TransportType transportType = TransportType.None;
            // ISSUE: reference to a compiler-generated field
            PrefabRef prefabRef = this.m_PrefabRefData[vehicle];
            // ISSUE: reference to a compiler-generated field
            if (this.m_TaxiData.HasComponent(prefabRef.m_Prefab))
            {
                transportType = TransportType.Taxi;
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                if (this.m_PublicTransportVehicleData.HasComponent(prefabRef.m_Prefab))
                {
                    // ISSUE: reference to a compiler-generated field
                    transportType = this.m_PublicTransportVehicleData[prefabRef.m_Prefab].m_TransportType;
                    Game.Vehicles.PublicTransport componentData;
                    // ISSUE: reference to a compiler-generated field
                    if (this.m_PublicTransports.TryGetComponent(controllerVehicle, out componentData) && (componentData.m_State & (PublicTransportFlags.Evacuating | PublicTransportFlags.PrisonerTransport)) != (PublicTransportFlags)0)
                        transportType = TransportType.None;
                }
            }
            // ISSUE: reference to a compiler-generated field
            Creature creature = this.m_Creatures[passenger] with
            {
                m_QueueEntity = Entity.Null,
                m_QueueArea = new Sphere3()
            };
            // ISSUE: reference to a compiler-generated field
            this.m_Creatures[passenger] = creature;
            // ISSUE: reference to a compiler-generated field
            this.m_Queues[passenger].Clear();
            Citizen componentData1;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_Citizens.TryGetComponent(this.m_Residents[passenger].m_Citizen, out componentData1))
            {
                PassengerType parameter = (componentData1.m_State & CitizenFlags.Tourist) != CitizenFlags.None ? PassengerType.Tourist : PassengerType.Citizen;
                switch (transportType)
                {
                    case TransportType.Bus:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountBus, 1, (int)parameter);
                        break;
                    case TransportType.Train:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountTrain, 1, (int)parameter);
                        break;
                    case TransportType.Taxi:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountTaxi, 1, (int)parameter);
                        break;
                    case TransportType.Tram:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountTram, 1, (int)parameter);
                        break;
                    case TransportType.Ship:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountShip, 1, (int)parameter);
                        break;
                    case TransportType.Airplane:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountAirplane, 1, (int)parameter);
                        break;
                    case TransportType.Subway:
                        // ISSUE: reference to a compiler-generated method
                        this.EnqueueStat(StatisticType.PassengerCountSubway, 1, (int)parameter);
                        break;
                }
            }
            // ISSUE: reference to a compiler-generated field
            if (this.m_LaneObjects.HasBuffer(oldCurrentLane.m_Lane))
            {
                // ISSUE: reference to a compiler-generated field
                NetUtils.RemoveLaneObject(this.m_LaneObjects[oldCurrentLane.m_Lane], passenger);
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                this.m_SearchTree.TryRemove(passenger);
            }
            Relative relative;
            // ISSUE: reference to a compiler-generated method
            if (this.TryGetRelativeLocation(prefabRef.m_Prefab, out relative))
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.RemoveComponent(passenger, in this.m_CurrentLaneTypesRelative);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Relative>(passenger, relative);
            }
            else
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.RemoveComponent(passenger, in this.m_CurrentLaneTypes);
                // ISSUE: reference to a compiler-generated field
                this.m_CommandBuffer.AddComponent<Unspawned>(passenger, new Unspawned());
            }
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<Updated>(passenger, new Updated());
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (ticketPrice == 0 || !this.m_Resources.HasBuffer(household) || !this.m_PlayerMoney.HasComponent(this.m_City))
                return;
            // ISSUE: reference to a compiler-generated field
            DynamicBuffer<Game.Economy.Resources> resource = this.m_Resources[household];
            EconomyUtils.AddResources(Resource.Money, -ticketPrice, resource);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            PlayerMoney playerMoney = this.m_PlayerMoney[this.m_City];
            playerMoney.Add(ticketPrice);
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_PlayerMoney[this.m_City] = playerMoney;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: object of a compiler-generated type is created
            this.m_FeeQueue.Enqueue(new ServiceFeeSystem.FeeEvent()
            {
                m_Amount = 1f,
                m_Cost = (float)ticketPrice,
                m_Resource = PlayerResource.PublicTransport,
                m_Outside = false
            });
        }

        private bool TryGetRelativeLocation(Entity prefab, out Relative relative)
        {
            relative = new Relative();
            DynamicBuffer<ActivityLocationElement> bufferData;
            // ISSUE: reference to a compiler-generated field
            if (this.m_ActivityLocations.TryGetBuffer(prefab, out bufferData))
            {
                ActivityMask activityMask = new ActivityMask(ActivityType.Driving);
                for (int index = 0; index < bufferData.Length; ++index)
                {
                    ActivityLocationElement activityLocationElement = bufferData[index];
                    if (((int)activityLocationElement.m_ActivityMask.m_Mask & (int)activityMask.m_Mask) != 0)
                    {
                        relative.m_Position = activityLocationElement.m_Position;
                        relative.m_Rotation = activityLocationElement.m_Rotation;
                        relative.m_BoneIndex = new int3(0, -1, -1);
                        return true;
                    }
                }
            }
            return false;
        }

        private void RequireStop(
          ref NativeParallelHashMap<Entity, int3> freeSpaceMap,
          Entity passenger,
          Entity vehicle,
          float3 position)
        {
            if (passenger != Entity.Null)
            {
                // ISSUE: reference to a compiler-generated method
                int requiredSpace = 1 + this.GetPendingGroupMemberCount(passenger);
                // ISSUE: reference to a compiler-generated method
                if (this.TryFindVehicle(ref freeSpaceMap, vehicle, Entity.Null, position, true, requiredSpace, out float _) == Entity.Null)
                    return;
            }
            Game.Vehicles.PublicTransport componentData;
            // ISSUE: reference to a compiler-generated field
            if (!this.m_PublicTransports.TryGetComponent(vehicle, out componentData))
                return;
            componentData.m_State |= PublicTransportFlags.RequireStop;
            // ISSUE: reference to a compiler-generated field
            this.m_PublicTransports[vehicle] = componentData;
        }

        private void WaitTimeExceeded(Entity passenger, Entity waypoint)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_WaitingPassengers.HasComponent(waypoint))
                return;
            // ISSUE: reference to a compiler-generated method
            int num = 1 + this.GetPendingGroupMemberCount(passenger);
            // ISSUE: reference to a compiler-generated field
            this.m_WaitingPassengers.GetRefRW(waypoint).ValueRW.m_ConcludedAccumulation += (int)((double)(5000 * num) * 0.13333334028720856);
        }

        private void WaitTimeEstimate(Entity waypoint, int seconds)
        {
            // ISSUE: reference to a compiler-generated field
            if (!this.m_WaitingPassengers.HasComponent(waypoint))
                return;
            // ISSUE: reference to a compiler-generated field
            this.m_WaitingPassengers.GetRefRW(waypoint).ValueRW.m_ConcludedAccumulation += seconds;
        }

        private void EnqueueStat(StatisticType statisticType, int change, int parameter)
        {
            // ISSUE: reference to a compiler-generated field
            this.m_StatisticsEventQueue.Enqueue(new StatisticsEvent()
            {
                m_Statistic = statisticType,
                m_Change = (float)change,
                m_Parameter = parameter
            });
        }
    }

    [BurstCompile]
    private struct ResidentActionJob : IJob
    {
        [ReadOnly]
        public ComponentLookup<PrefabRef> m_PrefabRefData;
        [ReadOnly]
        public ComponentLookup<MailBoxData> m_PrefabMailBoxData;
        public ComponentLookup<MailSender> m_MailSenderData;
        public ComponentLookup<HouseholdNeed> m_HouseholdNeedData;
        public ComponentLookup<Game.Routes.MailBox> m_MailBoxData;
        public NativeQueue<RPFResidentAISystem.ResidentAction> m_ActionQueue;
        public EntityCommandBuffer m_CommandBuffer;

        public void Execute()
        {
            // ISSUE: reference to a compiler-generated field
            int count = this.m_ActionQueue.Count;
            for (int index = 0; index < count; ++index)
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: variable of a compiler-generated type
                RPFResidentAISystem.ResidentAction action = this.m_ActionQueue.Dequeue();
                // ISSUE: reference to a compiler-generated field
                // ISSUE: variable of a compiler-generated type
                RPFResidentAISystem.ResidentActionType type = action.m_Type;
                switch (type)
                {
                    case RPFResidentAISystem.ResidentActionType.SendMail:
                        // ISSUE: reference to a compiler-generated method
                        this.SendMail(action);
                        break;
                    case RPFResidentAISystem.ResidentActionType.GoShopping:
                        // ISSUE: reference to a compiler-generated method
                        this.GoShopping(action);
                        break;
                }
            }
        }

        private void SendMail(RPFResidentAISystem.ResidentAction action)
        {
            MailSender component;
            Game.Routes.MailBox componentData1;
            PrefabRef componentData2;
            MailBoxData componentData3;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (!this.m_MailSenderData.TryGetEnabledComponent<MailSender>(action.m_Citizen, out component) || !this.m_MailBoxData.TryGetComponent(action.m_Target, out componentData1) || !this.m_PrefabRefData.TryGetComponent(action.m_Target, out componentData2) || !this.m_PrefabMailBoxData.TryGetComponent(componentData2.m_Prefab, out componentData3))
                return;
            int num = math.min((int)component.m_Amount, componentData3.m_MailCapacity - componentData1.m_MailAmount);
            if (num <= 0)
                return;
            component.m_Amount -= (ushort)num;
            componentData1.m_MailAmount += num;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_MailSenderData[action.m_Citizen] = component;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_MailBoxData[action.m_Target] = componentData1;
            if (component.m_Amount != (ushort)0)
                return;
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.SetComponentEnabled<MailSender>(action.m_Citizen, false);
        }

        private void GoShopping(RPFResidentAISystem.ResidentAction action)
        {
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            if (this.m_HouseholdNeedData.HasComponent(action.m_Household))
            {
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                HouseholdNeed householdNeed = this.m_HouseholdNeedData[action.m_Household] with
                {
                    m_Resource = Resource.NoResource
                };
                // ISSUE: reference to a compiler-generated field
                // ISSUE: reference to a compiler-generated field
                this.m_HouseholdNeedData[action.m_Household] = householdNeed;
            }
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            // ISSUE: reference to a compiler-generated field
            this.m_CommandBuffer.AddComponent<ResourceBought>(action.m_Citizen, new ResourceBought()
            {
                m_Seller = action.m_Target,
                m_Payer = action.m_Household,
                m_Resource = action.m_Resource,
                m_Amount = action.m_Amount,
                m_Distance = action.m_Distance
            });
        }
    }

    private struct TypeHandle
    {
        [ReadOnly]
        public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;
        [ReadOnly]
        public ComponentTypeHandle<CurrentVehicle> __Game_Creatures_CurrentVehicle_RO_ComponentTypeHandle;
        [ReadOnly]
        public ComponentTypeHandle<GroupMember> __Game_Creatures_GroupMember_RO_ComponentTypeHandle;
        [ReadOnly]
        public ComponentTypeHandle<Unspawned> __Game_Objects_Unspawned_RO_ComponentTypeHandle;
        [ReadOnly]
        public ComponentTypeHandle<HumanNavigation> __Game_Creatures_HumanNavigation_RO_ComponentTypeHandle;
        [ReadOnly]
        public ComponentTypeHandle<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentTypeHandle;
        [ReadOnly]
        public BufferTypeHandle<GroupCreature> __Game_Creatures_GroupCreature_RO_BufferTypeHandle;
        public ComponentTypeHandle<Creature> __Game_Creatures_Creature_RW_ComponentTypeHandle;
        public ComponentTypeHandle<Human> __Game_Creatures_Human_RW_ComponentTypeHandle;
        public ComponentTypeHandle<Game.Creatures.Resident> __Game_Creatures_Resident_RW_ComponentTypeHandle;
        public ComponentTypeHandle<HumanCurrentLane> __Game_Creatures_HumanCurrentLane_RW_ComponentTypeHandle;
        public ComponentTypeHandle<Game.Common.Target> __Game_Common_Target_RW_ComponentTypeHandle;
        public ComponentTypeHandle<Divert> __Game_Creatures_Divert_RW_ComponentTypeHandle;
        [ReadOnly]
        public EntityStorageInfoLookup __EntityStorageInfoLookup;
        public ComponentLookup<Human> __Game_Creatures_Human_RW_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Objects.Transform> __Game_Objects_Transform_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Owner> __Game_Common_Owner_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CurrentVehicle> __Game_Creatures_CurrentVehicle_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Destroyed> __Game_Common_Destroyed_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Deleted> __Game_Common_Deleted_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Unspawned> __Game_Objects_Unspawned_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<RideNeeder> __Game_Creatures_RideNeeder_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Moving> __Game_Objects_Moving_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Objects.SpawnLocation> __Game_Objects_SpawnLocation_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Animal> __Game_Creatures_Animal_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Dispatched> __Game_Simulation_Dispatched_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<ServiceRequest> __Game_Simulation_ServiceRequest_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<OnFire> __Game_Events_OnFire_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Net.Edge> __Game_Net_Edge_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Curve> __Game_Net_Curve_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Lane> __Game_Net_Lane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<EdgeLane> __Game_Net_EdgeLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Net.ParkingLane> __Game_Net_ParkingLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<GarageLane> __Game_Net_GarageLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Net.PedestrianLane> __Game_Net_PedestrianLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Net.ConnectionLane> __Game_Net_ConnectionLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HangaroundLocation> __Game_Areas_HangaroundLocation_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Citizen> __Game_Citizens_Citizen_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HouseholdMember> __Game_Citizens_HouseholdMember_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Household> __Game_Citizens_Household_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CurrentTransport> __Game_Citizens_CurrentTransport_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Worker> __Game_Citizens_Worker_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CarKeeper> __Game_Citizens_CarKeeper_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HealthProblem> __Game_Citizens_HealthProblem_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HomelessHousehold> __Game_Citizens_HomelessHousehold_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<TouristHousehold> __Game_Citizens_TouristHousehold_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HouseholdNeed> __Game_Citizens_HouseholdNeed_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<AttendingMeeting> __Game_Citizens_AttendingMeeting_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CoordinatedMeeting> __Game_Citizens_CoordinatedMeeting_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<MovingAway> __Game_Agents_MovingAway_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<ServiceAvailable> __Game_Companies_ServiceAvailable_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<ParkedCar> __Game_Vehicles_ParkedCar_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PersonalCar> __Game_Vehicles_PersonalCar_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Taxi> __Game_Vehicles_Taxi_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PublicTransport> __Game_Vehicles_PublicTransport_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.PoliceCar> __Game_Vehicles_PoliceCar_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Ambulance> __Game_Vehicles_Ambulance_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Vehicles.Hearse> __Game_Vehicles_Hearse_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Controller> __Game_Vehicles_Controller_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Vehicle> __Game_Vehicles_Vehicle_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Train> __Game_Vehicles_Train_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<AttractivenessProvider> __Game_Buildings_AttractivenessProvider_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Connected> __Game_Routes_Connected_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<BoardingVehicle> __Game_Routes_BoardingVehicle_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CurrentRoute> __Game_Routes_CurrentRoute_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<TransportLine> __Game_Routes_TransportLine_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<AccessLane> __Game_Routes_AccessLane_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CreatureData> __Game_Prefabs_CreatureData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<HumanData> __Game_Prefabs_HumanData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> __Game_Prefabs_ObjectGeometryData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<CarData> __Game_Prefabs_CarData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<IndustrialProcessData> __Game_Prefabs_IndustrialProcessData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<TransportStopData> __Game_Prefabs_TransportStopData_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<WaitingPassengers> __Game_Prefabs_WaitingPassengers_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<PublicTransportVehicleData> __Game_Vehicles_PublicTransportVehicle_RO_ComponentLookup;
        [ReadOnly]
        public ComponentLookup<Game.Prefabs.SpawnLocationData> __Game_Prefabs_SpawnLocationData_RO_ComponentLookup;
        [ReadOnly]
        public BufferLookup<HouseholdAnimal> __Game_Citizens_HouseholdAnimal_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<HouseholdCitizen> __Game_Citizens_HouseholdCitizen_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<ConnectedRoute> __Game_Routes_ConnectedRoute_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<LayoutElement> __Game_Vehicles_LayoutElement_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<CarNavigationLane> __Game_Vehicles_CarNavigationLane_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<ConnectedEdge> __Game_Net_ConnectedEdge_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<Game.Net.SubLane> __Game_Net_SubLane_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<Game.Areas.Node> __Game_Areas_Node_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<Triangle> __Game_Areas_Triangle_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<ConnectedBuilding> __Game_Buildings_ConnectedBuilding_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<Renter> __Game_Buildings_Renter_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<SpawnLocationElement> __Game_Buildings_SpawnLocationElement_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<Game.Economy.Resources> __Game_Economy_Resources_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<ServiceDispatch> __Game_Simulation_ServiceDispatch_RO_BufferLookup;
        [ReadOnly]
        public BufferLookup<ActivityLocationElement> __Game_Prefabs_ActivityLocationElement_RO_BufferLookup;
        public ComponentLookup<PathOwner> __Game_Pathfind_PathOwner_RW_ComponentLookup;
        public BufferLookup<PathElement> __Game_Pathfind_PathElement_RW_BufferLookup;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void __AssignHandles(ref SystemState state)
        {
            // ISSUE: reference to a compiler-generated field
            this.__Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_CurrentVehicle_RO_ComponentTypeHandle = state.GetComponentTypeHandle<CurrentVehicle>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_GroupMember_RO_ComponentTypeHandle = state.GetComponentTypeHandle<GroupMember>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Objects_Unspawned_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Unspawned>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_HumanNavigation_RO_ComponentTypeHandle = state.GetComponentTypeHandle<HumanNavigation>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_PrefabRef_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PrefabRef>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_GroupCreature_RO_BufferTypeHandle = state.GetBufferTypeHandle<GroupCreature>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Creature_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Creature>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Human_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Human>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Resident_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Game.Creatures.Resident>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_HumanCurrentLane_RW_ComponentTypeHandle = state.GetComponentTypeHandle<HumanCurrentLane>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Common_Target_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Game.Common.Target>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Divert_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Divert>();
            // ISSUE: reference to a compiler-generated field
            this.__EntityStorageInfoLookup = state.GetEntityStorageInfoLookup();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Human_RW_ComponentLookup = state.GetComponentLookup<Human>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Objects_Transform_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.Transform>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Common_Owner_RO_ComponentLookup = state.GetComponentLookup<Owner>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_CurrentVehicle_RO_ComponentLookup = state.GetComponentLookup<CurrentVehicle>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Common_Destroyed_RO_ComponentLookup = state.GetComponentLookup<Destroyed>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Common_Deleted_RO_ComponentLookup = state.GetComponentLookup<Deleted>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Objects_Unspawned_RO_ComponentLookup = state.GetComponentLookup<Unspawned>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_RideNeeder_RO_ComponentLookup = state.GetComponentLookup<RideNeeder>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Objects_Moving_RO_ComponentLookup = state.GetComponentLookup<Moving>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Objects_SpawnLocation_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.SpawnLocation>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Creatures_Animal_RO_ComponentLookup = state.GetComponentLookup<Animal>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Simulation_Dispatched_RO_ComponentLookup = state.GetComponentLookup<Dispatched>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Simulation_ServiceRequest_RO_ComponentLookup = state.GetComponentLookup<ServiceRequest>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Events_OnFire_RO_ComponentLookup = state.GetComponentLookup<OnFire>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_Edge_RO_ComponentLookup = state.GetComponentLookup<Game.Net.Edge>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_Curve_RO_ComponentLookup = state.GetComponentLookup<Curve>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_Lane_RO_ComponentLookup = state.GetComponentLookup<Lane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_EdgeLane_RO_ComponentLookup = state.GetComponentLookup<EdgeLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_ParkingLane_RO_ComponentLookup = state.GetComponentLookup<Game.Net.ParkingLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_GarageLane_RO_ComponentLookup = state.GetComponentLookup<GarageLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_PedestrianLane_RO_ComponentLookup = state.GetComponentLookup<Game.Net.PedestrianLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_ConnectionLane_RO_ComponentLookup = state.GetComponentLookup<Game.Net.ConnectionLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Areas_HangaroundLocation_RO_ComponentLookup = state.GetComponentLookup<HangaroundLocation>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_Citizen_RO_ComponentLookup = state.GetComponentLookup<Citizen>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HouseholdMember_RO_ComponentLookup = state.GetComponentLookup<HouseholdMember>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_Household_RO_ComponentLookup = state.GetComponentLookup<Household>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_CurrentBuilding_RO_ComponentLookup = state.GetComponentLookup<CurrentBuilding>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_CurrentTransport_RO_ComponentLookup = state.GetComponentLookup<CurrentTransport>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_Worker_RO_ComponentLookup = state.GetComponentLookup<Worker>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_CarKeeper_RO_ComponentLookup = state.GetComponentLookup<CarKeeper>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HealthProblem_RO_ComponentLookup = state.GetComponentLookup<HealthProblem>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_TravelPurpose_RO_ComponentLookup = state.GetComponentLookup<TravelPurpose>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HomelessHousehold_RO_ComponentLookup = state.GetComponentLookup<HomelessHousehold>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_TouristHousehold_RO_ComponentLookup = state.GetComponentLookup<TouristHousehold>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HouseholdNeed_RO_ComponentLookup = state.GetComponentLookup<HouseholdNeed>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_AttendingMeeting_RO_ComponentLookup = state.GetComponentLookup<AttendingMeeting>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_CoordinatedMeeting_RO_ComponentLookup = state.GetComponentLookup<CoordinatedMeeting>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Agents_MovingAway_RO_ComponentLookup = state.GetComponentLookup<MovingAway>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Companies_ServiceAvailable_RO_ComponentLookup = state.GetComponentLookup<ServiceAvailable>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_ParkedCar_RO_ComponentLookup = state.GetComponentLookup<ParkedCar>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_PersonalCar_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.PersonalCar>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Taxi_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.Taxi>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_PublicTransport_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.PublicTransport>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_PoliceCar_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.PoliceCar>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Ambulance_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.Ambulance>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Hearse_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.Hearse>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Controller_RO_ComponentLookup = state.GetComponentLookup<Controller>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Vehicle_RO_ComponentLookup = state.GetComponentLookup<Vehicle>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_Train_RO_ComponentLookup = state.GetComponentLookup<Train>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Buildings_AttractivenessProvider_RO_ComponentLookup = state.GetComponentLookup<AttractivenessProvider>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_Connected_RO_ComponentLookup = state.GetComponentLookup<Connected>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_BoardingVehicle_RO_ComponentLookup = state.GetComponentLookup<BoardingVehicle>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_CurrentRoute_RO_ComponentLookup = state.GetComponentLookup<CurrentRoute>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_TransportLine_RO_ComponentLookup = state.GetComponentLookup<TransportLine>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_AccessLane_RO_ComponentLookup = state.GetComponentLookup<AccessLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_PrefabRef_RO_ComponentLookup = state.GetComponentLookup<PrefabRef>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_CreatureData_RO_ComponentLookup = state.GetComponentLookup<CreatureData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_HumanData_RO_ComponentLookup = state.GetComponentLookup<HumanData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_ObjectGeometryData_RO_ComponentLookup = state.GetComponentLookup<ObjectGeometryData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_CarData_RO_ComponentLookup = state.GetComponentLookup<CarData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_IndustrialProcessData_RO_ComponentLookup = state.GetComponentLookup<IndustrialProcessData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_TransportStopData_RO_ComponentLookup = state.GetComponentLookup<TransportStopData>(true);
            this.__Game_Prefabs_WaitingPassengers_RO_ComponentLookup = state.GetComponentLookup<WaitingPassengers>(true);
            this.__Game_Vehicles_PublicTransportVehicle_RO_ComponentLookup = state.GetComponentLookup<PublicTransportVehicleData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_SpawnLocationData_RO_ComponentLookup = state.GetComponentLookup<Game.Prefabs.SpawnLocationData>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HouseholdAnimal_RO_BufferLookup = state.GetBufferLookup<HouseholdAnimal>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Citizens_HouseholdCitizen_RO_BufferLookup = state.GetBufferLookup<HouseholdCitizen>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Routes_ConnectedRoute_RO_BufferLookup = state.GetBufferLookup<ConnectedRoute>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_LayoutElement_RO_BufferLookup = state.GetBufferLookup<LayoutElement>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Vehicles_CarNavigationLane_RO_BufferLookup = state.GetBufferLookup<CarNavigationLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_ConnectedEdge_RO_BufferLookup = state.GetBufferLookup<ConnectedEdge>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Net_SubLane_RO_BufferLookup = state.GetBufferLookup<Game.Net.SubLane>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Areas_Node_RO_BufferLookup = state.GetBufferLookup<Game.Areas.Node>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Areas_Triangle_RO_BufferLookup = state.GetBufferLookup<Triangle>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Buildings_ConnectedBuilding_RO_BufferLookup = state.GetBufferLookup<ConnectedBuilding>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Buildings_Renter_RO_BufferLookup = state.GetBufferLookup<Renter>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Buildings_SpawnLocationElement_RO_BufferLookup = state.GetBufferLookup<SpawnLocationElement>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Economy_Resources_RO_BufferLookup = state.GetBufferLookup<Game.Economy.Resources>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Simulation_ServiceDispatch_RO_BufferLookup = state.GetBufferLookup<ServiceDispatch>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Prefabs_ActivityLocationElement_RO_BufferLookup = state.GetBufferLookup<ActivityLocationElement>(true);
            // ISSUE: reference to a compiler-generated field
            this.__Game_Pathfind_PathOwner_RW_ComponentLookup = state.GetComponentLookup<PathOwner>();
            // ISSUE: reference to a compiler-generated field
            this.__Game_Pathfind_PathElement_RW_BufferLookup = state.GetBufferLookup<PathElement>();
        }
    }
}
