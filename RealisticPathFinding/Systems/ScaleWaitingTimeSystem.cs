using Game;
using Game.Routes;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    // Runs on main thread; cheap pass over WaitingPassengers
    public partial class ScaleWaitingTimesSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            // Pull your factor from settings or your Time2WorkInterop (managed ok here)
            float factor = Time2WorkInterop.GetFactor(); // return 1f if Time2Work absent

            if (factor == 1f) return;

            var query = GetEntityQuery(ComponentType.ReadWrite<WaitingPassengers>());
            using var stops = query.ToEntityArray(Allocator.Temp);
            var wpLookup = GetComponentLookup<WaitingPassengers>(false);

            foreach (var e in stops)
            {
                var wp = wpLookup[e];
                // m_AverageWaitingTime is ushort seconds; scale & clamp
                int scaled = (int)math.round(wp.m_AverageWaitingTime / factor);
                wp.m_AverageWaitingTime = (ushort)math.clamp(scaled, 0, ushort.MaxValue);
                wpLookup[e] = wp;
            }
        }
    }
}
