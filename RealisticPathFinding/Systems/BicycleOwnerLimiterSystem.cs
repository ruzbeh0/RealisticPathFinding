using Game;
using Game.Citizens;
using Game.Common;
using Game.Serialization.DataMigration;
using Game.Simulation;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    public partial class BicycleOwnerLimiterSystem : GameSystemBase
    {
        private EntityQuery m_CitizenQuery;
        private EndFrameBarrier m_EndFrameBarrier;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // light but effective cadence; tweak as desired
            return 262144 / 32; // ~every 7.5 in-game minutes
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            // All citizens that have a BicycleOwner component (enabled or disabled)
            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadWrite<BicycleOwner>());

            RequireForUpdate(m_CitizenQuery);
        }

        protected override void OnUpdate()
        {
            var settings = Mod.m_Setting;
            if (settings == null)
                return;

            // Clamp all thresholds to [0, 100]
            int teenPercent = math.clamp(settings.bike_teen_percent, 0, 100);
            int adultPercent = math.clamp(settings.bike_adult_percent, 0, 100);
            int seniorPercent = math.clamp(settings.bike_senior_percent, 0, 100);

            // If all groups are at 100%, we don't need to touch anything.
            if (teenPercent >= 100 && adultPercent >= 100 && seniorPercent >= 100)
                return;

            var job = new LimitBicycleOwnersJob
            {
                EntityType = GetEntityTypeHandle(),
                CitizenType = GetComponentTypeHandle<Citizen>(true),
                BicycleOwnerType = GetComponentTypeHandle<BicycleOwner>(false),

                TeenPercent = teenPercent,
                AdultPercent = adultPercent,
                SeniorPercent = seniorPercent,

                CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter()
            };

            Dependency = job.ScheduleParallel(m_CitizenQuery, Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
        }

        [BurstCompile]
        private struct LimitBicycleOwnersJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Citizen> CitizenType;
            public ComponentTypeHandle<BicycleOwner> BicycleOwnerType;

            public int TeenPercent;
            public int AdultPercent;
            public int SeniorPercent;

            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var citizens = chunk.GetNativeArray(CitizenType);
                var owners = chunk.GetNativeArray(ref BicycleOwnerType);

                int count = chunk.Count;

                for (int i = 0; i < count; i++)
                {
                    Entity e = entities[i];
                    Citizen citizen = citizens[i];
                    BicycleOwner bo = owners[i];

                    // Robustness: don't touch citizens that already have a concrete
                    // bicycle entity assigned (they're in the middle of a trip or have
                    // a bike parked somewhere). We'll let vanilla / PersonalCarOwnerSystem
                    // clean those up naturally.
                    if (bo.m_Bicycle != Entity.Null)
                        continue;

                    // Determine threshold based on age group.
                    var age = citizen.GetAge();
                    int threshold = 0;

                    switch (age)
                    {
                        case CitizenAge.Teen:
                            threshold = TeenPercent;
                            break;
                        case CitizenAge.Adult:
                            threshold = AdultPercent;
                            break;
                        case CitizenAge.Elderly:
                            threshold = SeniorPercent;
                            break;
                        default:
                            // Children / undefined ages: treat as 0% bikes.
                            threshold = 0;
                            break;
                    }

                    // 100% -> we don't change anything for that age group
                    if (threshold >= 100)
                        continue;

                    // 0% -> disable BicycleOwner for everyone in this age group
                    if (threshold <= 0)
                    {
                        CommandBuffer.SetComponentEnabled<BicycleOwner>(unfilteredChunkIndex, e, false);
                        continue;
                    }

                    // Deterministic "random" 0..99 from entity index
                    uint idx = (uint)e.Index;
                    uint hash = idx * 1103515245u + 12345u;
                    uint value = hash % 100u;

                    // If this citizen falls OUTSIDE the allowed share, disable ownership.
                    // If they fall inside, we do nothing and keep whatever vanilla decided
                    // (enabled or disabled).
                    if (value >= (uint)threshold)
                    {
                        CommandBuffer.SetComponentEnabled<BicycleOwner>(unfilteredChunkIndex, e, false);
                    }
                }
            }
        }
    }
}
