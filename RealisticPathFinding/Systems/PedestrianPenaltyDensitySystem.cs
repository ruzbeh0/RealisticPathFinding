using Game;
using Game.Net;
using Game.Pathfind;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using NetPedestrianLane = Game.Net.PedestrianLane;

namespace RealisticPathFinding.Systems
{
    /// Adds a constant density to all pedestrian lanes, penalizing walking per meter.
    public sealed partial class PedestrianDensityPenaltySystem : GameSystemBase
    {
        private EntityQuery _laneQ;
        private NativeParallelHashMap<Entity, float> _prev; // remember what we added last time

        protected override void OnCreate()
        {
            base.OnCreate();
            _laneQ = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<NetPedestrianLane>() }
            });
            _prev = new NativeParallelHashMap<Entity, float>(2048, Allocator.Persistent);
            RequireForUpdate(_laneQ);
        }

        protected override void OnDestroy()
        {
            if (_prev.IsCreated) _prev.Dispose();
            base.OnDestroy();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 32;

        protected override void OnUpdate()
        {
            // Slider in your settings, e.g. 0.00 – 0.30; start with 0.05–0.10
            float pedDensityAdd = math.saturate(Mod.m_Setting?.ped_walk_time_factor ?? 0.08f);

            var pqs = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            var data = pqs.GetDataContainer(out var dep); dep.Complete();

            using var lanes = _laneQ.ToEntityArray(Allocator.Temp);
            foreach (var lane in lanes)
            {
                if (!TryGetEdge(data, lane, out var eid)) continue;

                float prev = _prev.TryGetValue(lane, out var p) ? p : 0f;
                float delta = pedDensityAdd - prev;
                if (math.abs(delta) < 1e-4f) continue;

                ref float density = ref data.SetDensity(eid);
                density += delta;

                _prev[lane] = pedDensityAdd;
            }

            pqs.AddDataReader(default);
        }

        static bool TryGetEdge(NativePathfindData d, Entity owner, out EdgeID id)
        {
            if (d.GetEdge(owner, out id)) return true;
            if (d.GetSecondaryEdge(owner, out id)) return true;
            id = default; return false;
        }
    }
}
