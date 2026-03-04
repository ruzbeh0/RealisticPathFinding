using Game;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RealisticPathFinding.Systems
{
    /// <summary>
    /// Single-pass: sets HumanData.m_WalkSpeed (m/s) by age using assumed proportions,
    /// normalized so the *population-weighted* mean equals TargetMeanMph.
    /// </summary>
    public partial class WalkSpeedUpdaterSystem : GameSystemBase
    {
        private EntityQuery _q;


        // Target overall mean
        private const float TargetMeanMph = 3.0f;
        private const float MPH_TO_MPS = 0.44704f;

        // Cached per-age speeds (computed once per update from the constants above)
        private float _mpsChild, _mpsTeen, _mpsAdult, _mpsElderly;
        private float _lastChild, _lastTeen, _lastAdult, _lastElderly;
        private bool _walkSettingsInitialized;

        protected override void OnCreate()
        {
            base.OnCreate();

            _q = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ResidentData>(),  // has m_Age (AgeMask)
                    ComponentType.ReadWrite<HumanData>()     // has m_WalkSpeed (float)
                }
            });

            RequireForUpdate(_q);
            Mod.m_Setting.onSettingsApplied += OnSettingsApplied;
        }

        protected override void OnDestroy()
        {
            if (Mod.m_Setting != null)
                Mod.m_Setting.onSettingsApplied -= OnSettingsApplied;
            base.OnDestroy();
        }

        private void OnSettingsApplied(Game.Settings.Setting _)
        {
            var s = Mod.m_Setting;
            float newChild   = s?.average_walk_speed_child   ?? 2.8f;
            float newTeen    = s?.average_walk_speed_teen    ?? 3.3f;
            float newAdult   = s?.average_walk_speed_adult   ?? 3.1f;
            float newElderly = s?.average_walk_speed_elderly ?? 2.6f;

            if (_walkSettingsInitialized &&
                math.abs(newChild   - _lastChild)   < 1e-4f &&
                math.abs(newTeen    - _lastTeen)    < 1e-4f &&
                math.abs(newAdult   - _lastAdult)   < 1e-4f &&
                math.abs(newElderly - _lastElderly) < 1e-4f) return;

            _lastChild   = newChild;
            _lastTeen    = newTeen;
            _lastAdult   = newAdult;
            _lastElderly = newElderly;
            _walkSettingsInitialized = true;
            Enabled = true;
        }

        protected override void OnUpdate()
        {
            // Final per-age mph, then convert to m/s
            _mpsChild = Mod.m_Setting.average_walk_speed_child * MPH_TO_MPS;
            _mpsTeen = Mod.m_Setting.average_walk_speed_teen * MPH_TO_MPS;
            _mpsAdult = Mod.m_Setting.average_walk_speed_adult * MPH_TO_MPS;
            _mpsElderly = Mod.m_Setting.average_walk_speed_elderly * MPH_TO_MPS;

            Mod.log.Info($"[RPF] WalkSpeedUpdaterSystem: child={_mpsChild:F3} teen={_mpsTeen:F3} adult={_mpsAdult:F3} elderly={_mpsElderly:F3} m/s");

            // 2) Single pass over entities: assign by age bucket
            using var entities = _q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var age = EntityManager.GetComponentData<ResidentData>(e).m_Age;  // AgeMask flags :contentReference[oaicite:3]{index=3}
                var hd = EntityManager.GetComponentData<HumanData>(e);           // has m_WalkSpeed  :contentReference[oaicite:4]{index=4}
                hd.m_WalkSpeed = SelectSpeedByAge(age);
                EntityManager.SetComponentData(e, hd);
            }

            this.Enabled = false; // run once; re-enabled by onSettingsApplied when settings change
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Event-driven system: run on next simulation tick after it is enabled.
            return 1;
        }

        private float SelectSpeedByAge(AgeMask mask)
        {
            // AgeMask is [Flags]; pick a single bucket by precedence. :contentReference[oaicite:5]{index=5}
            if ((mask & AgeMask.Child) != 0) return _mpsChild;
            if ((mask & AgeMask.Teen) != 0) return _mpsTeen;
            if ((mask & AgeMask.Adult) != 0) return _mpsAdult;
            if ((mask & AgeMask.Elderly) != 0) return _mpsElderly;
            return _mpsAdult; // default
        }
    }
}

