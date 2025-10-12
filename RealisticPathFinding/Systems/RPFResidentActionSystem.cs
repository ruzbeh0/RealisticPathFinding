using Colossal.Collections;
using Colossal.Entities;
using Game;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Creatures;
using Game.Economy;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.Simulation;
using Game.Vehicles;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace RealisticPathFinding.Systems
{
    /// <summary>
    /// One-to-one replacement for Game.Simulation.ResidentAISystem.Actions.
    /// Owns the queues for this frame, schedules the boarding + resident action consumers,
    /// and plays back ECB at EndFrame (like vanilla).
    /// </summary>
    //[UpdateInGroup(typeof(GameSimulationSystemGroup))]
    [BurstCompile]
    public partial class RPFResidentActionsSystem : GameSystemBase
    {
        // Queues provided by the producer (RPFResidentAISystem) each frame
        public NativeQueue<Game.Simulation.ResidentAISystem.Boarding> m_BoardingQueue;
        public NativeQueue<Game.Simulation.ResidentAISystem.ResidentAction> m_ActionQueue;

        // The producer writes its scheduling handle here; we combine with our own
        public JobHandle m_Dependency;

        // Systems used exactly like vanilla
        private EndFrameBarrier m_EndFrameBarrier;
        private Game.Objects.SearchSystem m_ObjectSearchSystem;
        private CityStatisticsSystem m_CityStatisticsSystem;
        private CitySystem m_CitySystem;
        private ServiceFeeSystem m_ServiceFeeSystem;

        // Same type-sets vanilla passes into the boarding job
        private ComponentTypeSet m_CurrentLaneTypes;
        private ComponentTypeSet m_CurrentLaneTypesRelative;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_ObjectSearchSystem = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_ServiceFeeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();

            m_CurrentLaneTypes = new ComponentTypeSet(new ComponentType[]
{
    ComponentType.ReadWrite<Moving>(),
    ComponentType.ReadWrite<TransformFrame>(),
    ComponentType.ReadWrite<InterpolatedTransform>(),
    ComponentType.ReadWrite<HumanNavigation>(),
    ComponentType.ReadWrite<HumanCurrentLane>(),
    ComponentType.ReadWrite<Blocker>()
});

            m_CurrentLaneTypesRelative = new ComponentTypeSet(new ComponentType[]
            {
    ComponentType.ReadWrite<Moving>(),
    ComponentType.ReadWrite<TransformFrame>(),
    ComponentType.ReadWrite<HumanNavigation>(),
    ComponentType.ReadWrite<HumanCurrentLane>(),
    ComponentType.ReadWrite<Blocker>()
            });

        }

        [Preserve]
        protected override void OnUpdate()
        {
            // 1) Combine producer deps (from RPFResidentAISystem) with our own
            var dep = JobHandle.CombineDependencies(Dependency, m_Dependency);

            // 2) Fetch moving search tree and service writers ONCE and capture their JobHandles
            var searchTree = m_ObjectSearchSystem.GetMovingSearchTree(false, out JobHandle treeWriter);
            var statsPW = m_CityStatisticsSystem.GetStatisticsEventQueue(out JobHandle statsWriter).AsParallelWriter();
            var feeQueue = m_ServiceFeeSystem.GetFeeQueue(out JobHandle feeWriter);

            var addedThisFrame = new NativeParallelHashSet<Entity>(1024, Allocator.TempJob);

            // 3) Build BoardingJob with ALL lookups it needs (you already wired these in previous step)
            var boardingJob = new RPFResidentAISystem.BoardingJob
            {
                m_Citizens = GetComponentLookup<Game.Citizens.Citizen>(true),
                m_Transforms = GetComponentLookup<Game.Objects.Transform>(true),
                m_PrefabRefData = GetComponentLookup<Game.Prefabs.PrefabRef>(true),
                m_ObjectGeometryData = GetComponentLookup<Game.Prefabs.ObjectGeometryData>(true),

                m_TaxiData = GetComponentLookup<Game.Prefabs.TaxiData>(true),
                m_PublicTransportVehicleData = GetComponentLookup<Game.Prefabs.PublicTransportVehicleData>(true),
                m_PrefabPersonalCarData = GetComponentLookup<Game.Prefabs.PersonalCarData>(true),

                m_GroupCreatures = GetBufferLookup<Game.Creatures.GroupCreature>(true),
                m_VehicleLayouts = GetBufferLookup<Game.Vehicles.LayoutElement>(true),
                m_ActivityLocations = GetBufferLookup<Game.Prefabs.ActivityLocationElement>(true),

                m_Residents = GetComponentLookup<Game.Creatures.Resident>(false),
                m_Creatures = GetComponentLookup<Game.Creatures.Creature>(false),
                m_Taxis = GetComponentLookup<Game.Vehicles.Taxi>(false),
                m_PublicTransports = GetComponentLookup<Game.Vehicles.PublicTransport>(false),
                m_WaitingPassengers = GetComponentLookup<Game.Routes.WaitingPassengers>(false),

                m_Queues = GetBufferLookup<Queue>(false),
                m_Passengers = GetBufferLookup<Game.Vehicles.Passenger>(false),
                m_LaneObjects = GetBufferLookup<Game.Net.LaneObject>(false),

                m_Resources = GetBufferLookup<Game.Economy.Resources>(false),
                m_PlayerMoney = GetComponentLookup<PlayerMoney>(false),

                m_City = m_CitySystem.City,

                m_CurrentLaneTypes = m_CurrentLaneTypes,
                m_CurrentLaneTypesRelative = m_CurrentLaneTypesRelative,

                m_BoardingQueue = m_BoardingQueue,
                m_SearchTree = searchTree,

                // ECB plays back at EndFrame like vanilla
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer(),
                m_StatisticsEventQueue = statsPW,
                m_FeeQueue = feeQueue,
                m_AddedThisFrame = addedThisFrame
            };

            // 4) Combine the producer dep with the three writer deps (tree/stats/fee)
            var boardingDeps = JobUtils.CombineDependencies(dep, treeWriter, statsWriter, feeWriter);
            var boardingHandle = boardingJob.Schedule(boardingDeps);
            addedThisFrame.Dispose(boardingHandle);

            // 5) Resident action (mail) job (small; same fields as vanilla)
            var actionJob = new RPFResidentAISystem.ResidentActionJob
            {
                m_PrefabRefData = GetComponentLookup<Game.Prefabs.PrefabRef>(true),
                m_PrefabMailBoxData = GetComponentLookup<Game.Prefabs.MailBoxData>(true),
                m_HouseholdNeedData = GetComponentLookup<Game.Citizens.HouseholdNeed>(false),
                m_MailBoxData = GetComponentLookup<Game.Routes.MailBox>(false),
                m_MailSenderData = GetComponentLookup<Game.Citizens.MailSender>(false),
                m_ActionQueue = m_ActionQueue,
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer()
            };
            var actionHandle = actionJob.Schedule(dep);

            // 6) Dispose per-frame queues (vanilla pattern)
            m_BoardingQueue.Dispose(boardingHandle);
            m_ActionQueue.Dispose(actionHandle);

            // 7) Register writers + ECB playback at EndFrame (vanilla pattern)
            m_CityStatisticsSystem.AddWriter(boardingHandle);
            m_ObjectSearchSystem.AddMovingSearchTreeWriter(boardingHandle);
            m_ServiceFeeSystem.AddQueueWriter(boardingHandle);

            m_EndFrameBarrier.AddJobHandleForProducer(boardingHandle);
            m_EndFrameBarrier.AddJobHandleForProducer(actionHandle);

            // 8) Publish combined dependency
            Dependency = JobHandle.CombineDependencies(boardingHandle, actionHandle);


        }

        [Preserve] public RPFResidentActionsSystem() { }
    }
}
