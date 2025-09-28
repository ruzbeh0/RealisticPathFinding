using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System;
using System.Collections.Generic;

namespace RealisticPathFinding
{
    [FileLocation("ModsSettings\\" + nameof(RealisticPathFinding) + "\\" + nameof(RealisticPathFinding))]
    [SettingsUIGroupOrder(TurnPenaltyGroup, RoadBiasGroup, WaitingTimeGroup, ModeWeightGroup, PedestrianGroup, LongDistanceGroup)]
    [SettingsUIShowGroupName(TurnPenaltyGroup, RoadBiasGroup, WaitingTimeGroup, ModeWeightGroup, PedestrianGroup, LongDistanceGroup)]
    public class Setting : ModSetting
    {
        public const string PedestriansSection = "Pedestrians";
        public const string TransitSection = "Transit";
        public const string VehicleSection = "Vehicles";
        public const string WaitingTimeGroup = "WaitingTine";
        public const string ModeWeightGroup = "ModeWeight";
        public const string PedestrianGroup = "Pedestrians";
        public const string LongDistanceGroup = "LongDistanceGroup";
        public const string TurnPenaltyGroup = "TurnPenalty";
        public const string RoadBiasGroup = "RoadBias";

        public Setting(IMod mod) : base(mod)
        {
            if (transfer_penalty == 0) SetDefaults();
        }

        public override void SetDefaults()
        {
            SetParameters();
        }
        public void SetParameters()
        {
            base_turn_penalty = 3f;
            min_turn_agle_deg = 20f;
            max_turn_agle_deg = 100f;
            uturn_threshold_deg = 150f;
            collector_bias = 0.05f;
            local_bias = 0.1f;
            alleyway_bias = 0.15f;
            uturn_sec_penalty = 5f;
            transfer_penalty = 1.5f;
            scheduled_wt_factor = 0.5f;
            crowdness_factor = 0.15f;
            bus_mode_weight = 1.05f;
            tram_mode_weight = 0.95f;
            subway_mode_weight = 0.9f;
            train_mode_weight = 0.9f;
            average_walk_speed_child = 2.8f;
            average_walk_speed_teen = 3.3f;
            average_walk_speed_adult = 3.1f;
            average_walk_speed_elderly = 2.6f;
            crowdness_factor = 0.2f;
            walk_long_comfort_m = 500f;
            walk_long_ramp_m = 700f;
            walk_long_min_mult = 0.6f;
        }

        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float min_turn_agle_deg { get; set; }

        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float max_turn_agle_deg { get; set; }

        [SettingsUISlider(min = 0, max = 10, step = 1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float base_turn_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float uturn_threshold_deg { get; set; }

        [SettingsUISlider(min = 0, max = 10, step = 1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float uturn_sec_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 1, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, RoadBiasGroup)]
        public float collector_bias { get; set; }

        [SettingsUISlider(min = 0, max = 1, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, RoadBiasGroup)]
        public float local_bias { get; set; }

        [SettingsUISlider(min = 0, max = 1, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, RoadBiasGroup)]
        public float alleyway_bias { get; set; }

        [SettingsUISlider(min = 0, max = 3, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float transfer_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 1, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float scheduled_wt_factor { get; set; }

        [SettingsUISlider(min = 0, max = 0.5f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float crowdness_factor { get; set; }

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, ModeWeightGroup)]
        public float bus_mode_weight { get; set; }

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, ModeWeightGroup)]
        public float tram_mode_weight { get; set; }

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, ModeWeightGroup)]
        public float subway_mode_weight { get; set; }

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, ModeWeightGroup)]
        public float train_mode_weight { get; set; }

        [SettingsUISlider(min = 0.5f, max = 4f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        public float average_walk_speed_child { get; set; }

        [SettingsUISlider(min = 0.5f, max = 4f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        public float average_walk_speed_teen { get; set; }

        [SettingsUISlider(min = 0.5f, max = 4f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        public float average_walk_speed_adult { get; set; }

        [SettingsUISlider(min = 0.5f, max = 4f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        public float average_walk_speed_elderly { get; set; }

        [SettingsUISlider(min = 500f, max = 1000f, step = 50f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_comfort_m { get; set; }

        [SettingsUISlider(min = 500f, max = 1000f, step = 50f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_ramp_m { get; set; }

        [SettingsUISlider(min = 0, max = 1f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_min_mult { get; set; }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) => m_Setting = setting;

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var dict = new Dictionary<string, string>
        {
            // Settings title
            { m_Setting.GetSettingsLocaleID(), "Realistic Path Finding" },

            // ----- Tabs / Sections -----
            { m_Setting.GetOptionTabLocaleID(Setting.TransitSection),     "Transit" },
            { m_Setting.GetOptionTabLocaleID(Setting.PedestriansSection), "Pedestrians" },

            // ----- Groups (shown inside each section) -----
            // NOTE: your constant is 'WaitingTine' (typo); label shows fine as "Waiting time".
            { m_Setting.GetOptionGroupLocaleID(Setting.WaitingTimeGroup), "Waiting time" },
            { m_Setting.GetOptionGroupLocaleID(Setting.ModeWeightGroup),  "Transport Type Weights" },
            { m_Setting.GetOptionGroupLocaleID(Setting.PedestrianGroup),  "Walking Speeds" },

            // New section (tab)
            { m_Setting.GetOptionTabLocaleID(Setting.VehicleSection), "Vehicles" },

            // New groups inside the Vehicles section
            { m_Setting.GetOptionGroupLocaleID(Setting.TurnPenaltyGroup), "Turn penalties" },
            { m_Setting.GetOptionGroupLocaleID(Setting.RoadBiasGroup),    "Road hierarchy bias" },

            // Group (under Pedestrians)
            { m_Setting.GetOptionGroupLocaleID(Setting.LongDistanceGroup), "Long-distance walking" },

            // ============================
            // Transit → Waiting time group
            // ============================
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.transfer_penalty)),
              "Transfer penalty" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.transfer_penalty)),
              "Multiplier applied to the waiting time at each transfer (e.g., 1.5 = +50% perceived wait on transfers)." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.scheduled_wt_factor)),
              "Scheduled-mode wait factor" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.scheduled_wt_factor)),
              "Multiplier for scheduled modes (rail/ship/air). Example: 0.5 halves perceived waiting for those modes." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.crowdness_factor)),
                    "Crowding factor (max extra wait)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.crowdness_factor)),
                    "At high crowding (half of vehicle capacity waiting), increase perceived wait by up to this fraction (0.0–0.5). Example: 0.15 = +15% at/above capacity; 0 disables crowding." },

            // ============================
            // Transit → Mode weights group
            // ============================
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.bus_mode_weight)),
              "Bus in-vehicle time Weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.bus_mode_weight)),
              "Multiplier applied to bus in-vehicle time (1.0 = no change)." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.tram_mode_weight)),
              "Tram in-vehicle time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.tram_mode_weight)),
              "Multiplier applied to tram in-vehicle time." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.subway_mode_weight)),
              "Subway in-vehicle time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.subway_mode_weight)),
              "Multiplier applied to subway/metro in-vehicle time." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.train_mode_weight)),
              "Train in-vehicle time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.train_mode_weight)),
              "Multiplier applied to regional/commuter rail in-vehicle time." },

            // ============================
            // Pedestrians → Speeds group
            // ============================
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.average_walk_speed_child)),
              "Child average walk speed (mph)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.average_walk_speed_child)),
              "Average walking speed assumed for children. Units: miles per hour." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.average_walk_speed_teen)),
              "Teen average walk speed (mph)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.average_walk_speed_teen)),
              "Average walking speed assumed for teens. Units: miles per hour." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.average_walk_speed_adult)),
              "Adult average walk speed (mph)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.average_walk_speed_adult)),
              "Average walking speed assumed for adults. Units: miles per hour." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.average_walk_speed_elderly)),
              "Elderly average walk speed (mph)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.average_walk_speed_elderly)),
              "Average walking speed assumed for elderly citizens. Units: miles per hour." },

            // Turn penalties
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.min_turn_agle_deg)), "Minimum turn angle (deg)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.min_turn_agle_deg)), "Below this angle there is effectively no turn penalty. Acts as the start of the ramp." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.max_turn_agle_deg)), "Maximum turn angle (deg)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.max_turn_agle_deg)), "At/above this angle the full base turn penalty is applied. Angles between min/max are interpolated." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.base_turn_penalty)), "Base turn penalty (seconds)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.base_turn_penalty)), "Maximum seconds added for a sharp turn (reached at or above the max angle, zero at/below the min angle)." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.uturn_threshold_deg)), "U-turn threshold (deg)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.uturn_threshold_deg)), "If the computed turn angle is at/above this value, the U-turn extra penalty is added." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.uturn_sec_penalty)), "U-turn extra penalty (seconds)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.uturn_sec_penalty)), "Additional seconds applied when the turn angle exceeds the U-turn threshold." },

            // Road hierarchy bias
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.collector_bias)), "Collector bias (density add)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.collector_bias)), "Extra density applied to collector/arterial-like roads. Higher values make them less attractive vs. highways." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.local_bias)), "Local road bias (density add)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.local_bias)), "Extra density applied to local/residential roads to discourage cut-through routing." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.alleyway_bias)), "Alleyway/very local bias (density add)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.alleyway_bias)), "Largest density add for very slow/very local streets and alleys to strongly prefer higher-class roads." },

            // Comfort distance
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.walk_long_comfort_m)), "Comfort distance (m)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.walk_long_comfort_m)),
              "No penalty up to this origin→destination straight-line distance (meters). The multiplier starts decreasing beyond this." },

            // Ramp length
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.walk_long_ramp_m)), "Ramp length (m)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.walk_long_ramp_m)),
              "Over the next meters after the comfort distance, the multiplier ramps down to the minimum. Full effect at comfort + ramp." },

            // Minimum multiplier
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.walk_long_min_mult)), "Minimum speed multiplier" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.walk_long_min_mult)),
              "Floor for perceived walking speed on long trips. Example: 0.6 = treat long ODs as if walking 40% slower." },
        };

            return dict;
        }

        public void Unload() { }
    }

}
