using Game.Common;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    [UpdateAfter(typeof(TaxiStandSystem))]
    public partial class TaxiStandCrowdingSystem : SystemBase
    {
        // Holds the non-crowded fee so we can restore it
        public struct TaxiStandCrowdingBase : IComponentData
        {
            public ushort BaseFee;
        }

        // Tunables
        public int Threshold = (int)Mod.m_Setting.taxi_passengers_waiting_threashold;     // “more than 7 waiting”
        public float Increase = Mod.m_Setting.taxi_fare_increase; // +20% surcharge

        private EntityQuery _q;


        protected override void OnCreate()
        {
            _q = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<TaxiStand>(),
                    ComponentType.ReadOnly<WaitingPassengers>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            int thr = Threshold;
            float factor = math.max(0f, Increase);

            using NativeArray<Entity> ents = _q.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var stand = em.GetComponentData<TaxiStand>(e);
                var waiting = em.GetComponentData<WaitingPassengers>(e);

                // Ensure we have a baseline component
                TaxiStandCrowdingBase baseData;
                if (!em.HasComponent<TaxiStandCrowdingBase>(e))
                {
                    baseData = new TaxiStandCrowdingBase { BaseFee = stand.m_StartingFee };
                    em.AddComponentData(e, baseData);
                }
                else
                {
                    baseData = em.GetComponentData<TaxiStandCrowdingBase>(e);
                }

                int w = math.max(0, waiting.m_Count); // adjust field name if different
                bool crowded = w > thr;

                // If not crowded, keep baseline in sync with any external fee changes
                if (!crowded && baseData.BaseFee != stand.m_StartingFee)
                {
                    baseData.BaseFee = stand.m_StartingFee;
                    em.SetComponentData(e, baseData);
                }

                // Compute desired fee from *baseline* (never compound)
                ushort desired = stand.m_StartingFee;
                ushort baseFee = baseData.BaseFee;

                if (crowded)
                {
                    // new = round(base * (1 + factor)), at least +1 if base > 0
                    uint inflated = (uint)math.round(baseFee * (1f + factor));
                    inflated = math.min(inflated, ushort.MaxValue);
                    desired = (ushort)inflated;
                    if (desired == baseFee && baseFee > 0) desired = (ushort)math.min(baseFee + 1, ushort.MaxValue);
                }
                else
                {
                    desired = baseFee; // restore baseline when not crowded
                }

                if (desired != stand.m_StartingFee)
                {
                    stand.m_StartingFee = desired;
                    em.SetComponentData(e, stand);

                    if (!em.HasComponent<PathfindUpdated>(e))
                        em.AddComponent<PathfindUpdated>(e);
                }
            }
        }
    }
}
