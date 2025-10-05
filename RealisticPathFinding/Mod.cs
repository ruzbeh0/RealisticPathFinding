using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Environment;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using RealisticPathFinding.Systems;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace RealisticPathFinding
{
    public class Mod : IMod
    {
        public static readonly string harmonyID = "RealisticPathFinding";
        public static ILog log = LogManager.GetLogger($"{nameof(RealisticPathFinding)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting m_Setting;
        // Mods Settings Folder
        public static string SettingsFolder = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", nameof(RealisticPathFinding));

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));


            AssetDatabase.global.LoadSettings(nameof(RealisticPathFinding), m_Setting, new Setting(this));

            foreach (var modInfo in GameManager.instance.modManager)
            {
                if (modInfo.asset.name.Equals("Time2Work"))
                {
                    Mod.log.Info($"Loaded Realistic Trips Mod with time factor: {Time2WorkInterop.GetFactor()}");
                }
            }

            // Disable original systems
            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<Game.Simulation.ResidentAISystem>().Enabled = false;

            updateSystem.UpdateAfter<RealisticPathFinding.Systems.ScaleWaitingTimesSystem, Game.Simulation.WaitingPassengersSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealisticPathFinding.Systems.RPFResidentAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<Game.Simulation.ResidentAISystem.Actions,
                         RealisticPathFinding.Systems.RPFResidentAISystem>(SystemUpdatePhase.GameSimulation);
            //updateSystem.UpdateAfter<Game.Simulation.ResidentAISystem.Actions,RealisticPathFinding.Systems.RPFResidentAISystem>(SystemUpdatePhase.GameSimulation);
            //updateSystem.UpdateAfter<RealisticPathFinding.Systems.RPFResidentAISystem, Game.Simulation.ResidentAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealisticPathFinding.Systems.WalkSpeedUpdaterSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealisticPathFinding.Systems.CarTurnAndHierarchyBiasSystem>(SystemUpdatePhase.GameSimulation);
            if(!m_Setting.disable_ped_cost)
            {
                updateSystem.UpdateAt<RealisticPathFinding.Systems.PedestrianWalkCostFactorSystem>(SystemUpdatePhase.GameSimulation);
            }
            
            //updateSystem.UpdateAt<RealisticPathFinding.Systems.PedestrianDensityPenaltySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealisticPathFinding.Systems.CarCongestionEwmaSystem>(SystemUpdatePhase.GameSimulation);

            //Harmony
            var harmony = new Harmony(harmonyID);
            //Harmony.DEBUG = true;
            harmony.PatchAll(typeof(Mod).Assembly);
            var patchedMethods = harmony.GetPatchedMethods().ToArray();
            log.Info($"Plugin {harmonyID} made patches! Patched methods: " + patchedMethods);
            foreach (var patchedMethod in patchedMethods)
            {
                log.Info($"Patched: {patchedMethod.DeclaringType?.FullName}.{patchedMethod.Name}");
            }


        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
