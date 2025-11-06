using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.Tools;
using Game.UI;
using Game.UI.Widgets;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection.Emit;
using Unity.Entities;

namespace RealisticPathFinding
{
    [FileLocation("ModsSettings\\" + nameof(RealisticPathFinding) + "\\" + nameof(RealisticPathFinding))]
    [SettingsUIGroupOrder(CarModeWeightGroup, TurnPenaltyGroup, RoadBiasGroup, CongestionGroup, WaitingTimeGroup, ModeWeightGroup, BusLaneGroup, TaxiGroup, PedestrianGroup, LongDistanceGroup, PedestrianCrossingGroup, OtherGroup)]
    [SettingsUIShowGroupName(CarModeWeightGroup, TurnPenaltyGroup, RoadBiasGroup, CongestionGroup, WaitingTimeGroup, ModeWeightGroup, BusLaneGroup, PedestrianGroup, LongDistanceGroup, PedestrianCrossingGroup)]
    public class Setting : ModSetting
    {
        public const string PedestriansSection = "Pedestrians";
        public const string TransitSection = "Transit";
        public const string TaxiSection = "Taxi";
        public const string VehicleSection = "Vehicles";
        public const string WaitingTimeGroup = "WaitingTine";
        public const string ModeWeightGroup = "ModeWeight";
        public const string BusLaneGroup = "BusLaneGroup";
        public const string PedestrianGroup = "Pedestrians";
        public const string TaxiGroup = "Taxi";
        public const string LongDistanceGroup = "LongDistanceGroup";
        public const string PedestrianCrossingGroup = "PedestrianCrossingGroup";
        public const string TurnPenaltyGroup = "TurnPenalty";
        public const string RoadBiasGroup = "RoadBias";
        public const string CongestionGroup = "Congestion";
        public const string CarModeWeightGroup = "CarModeWeightGroup";
        public const string OtherSection = "Other";
        public const string OtherGroup = "OtherGroup";

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
            car_mode_weight = 0.90f;
            base_turn_penalty = 2f;
            min_turn_agle_deg = 45f;
            max_turn_agle_deg = 100f;
            uturn_threshold_deg = 150f;
            collector_bias = 0.05f;
            local_bias = 0.1f;
            alleyway_bias = 0.15f;
            uturn_sec_penalty = 4f;
            transfer_penalty = 1.5f;
            feeder_trunk_transfer_penalty = 1.2f;
            waiting_time_factor = 1.0f;
            scheduled_wt_factor = 0.5f;
            bus_mode_weight = 1.00f;
            tram_mode_weight = 0.95f;
            subway_mode_weight = 0.9f;
            train_mode_weight = 0.9f;
            average_walk_speed_child = 2.8f;
            average_walk_speed_teen = 3.3f;
            average_walk_speed_adult = 3.1f;
            average_walk_speed_elderly = 2.6f;
            crowdness_factor = 0.3f;
            crowdness_stop_threashold = 0.65f;
            walk_long_comfort_m = 600f;
            walk_long_ramp_m = 700f;
            walk_long_min_mult = 0.3f;
            ped_walk_time_factor = 5.0f;
            cong_min_sample_sec = 0.2f;
            cong_alpha = 0.2f;
            cong_min_push_sec = 0.5f;
            cong_max_ratio = 3f;
            cong_max_density = 0.5f;
            cong_min_ff_mps = 1f;
            disable_ped_cost = false;
            taxi_passengers_waiting_threashold = 7f;
            taxi_fare_increase = 0.3f;
            ped_crosswalk_factor = 0.7f;
            ped_unsafe_crosswalk_factor = 1f;
            ferry_mode_weight = 1f;
            nonbus_buslane_penalty_sec = 30f;
        }

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CarModeWeightGroup)]
        public float car_mode_weight { get; set; }


        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float min_turn_agle_deg { get; set; }

        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float max_turn_agle_deg { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float base_turn_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 180, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, TurnPenaltyGroup)]
        public float uturn_threshold_deg { get; set; }

        [SettingsUISlider(min = 0, max = 500, step = 1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
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

        [SettingsUISlider(min = 0, max = 1, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float waiting_time_factor { get; set; } 

        [SettingsUISlider(min = 0, max = 3, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float transfer_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 3, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float feeder_trunk_transfer_penalty { get; set; }

        [SettingsUISlider(min = 0, max = 1, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float scheduled_wt_factor { get; set; }

        [SettingsUISlider(min = 0, max = 2f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float crowdness_factor { get; set; }

        [SettingsUISlider(min = 0, max = 1f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, WaitingTimeGroup)]
        public float crowdness_stop_threashold { get; set; }

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

        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TransitSection, ModeWeightGroup)]
        public float ferry_mode_weight { get; set; }

        [SettingsUISlider(min = 0f, max = 300f, step = 1f)]
        [SettingsUISection(TransitSection, BusLaneGroup)]
        public float nonbus_buslane_penalty_sec { get; set; }

        [SettingsUISlider(min = 0, max = 20, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(TaxiSection, TaxiGroup)]
        public float taxi_passengers_waiting_threashold { get; set; }

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(TaxiSection, TaxiGroup)]
        public float taxi_fare_increase { get; set; }

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

        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        public bool disable_ped_cost { get; set; }

        [SettingsUISlider(min = 1f, max = 50f, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, PedestrianGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(disable_ped_cost))]
        public float ped_walk_time_factor { get; set; }

        [SettingsUISlider(min = 500f, max = 1000f, step = 50f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_comfort_m { get; set; }

        [SettingsUISlider(min = 500f, max = 1000f, step = 50f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_ramp_m { get; set; }

        [SettingsUISlider(min = 0, max = 1f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(PedestriansSection, LongDistanceGroup)]
        public float walk_long_min_mult { get; set; }

        [SettingsUISlider(min = 0.1f, max = 5f, step = 0.05f)]
        [SettingsUISection(PedestriansSection, PedestrianCrossingGroup)]
        public float ped_crosswalk_factor { get; set; }

        [SettingsUISlider(min = 0.1f, max = 5f, step = 0.05f)]
        [SettingsUISection(PedestriansSection, PedestrianCrossingGroup)]
        public float ped_unsafe_crosswalk_factor { get; set; }


        [SettingsUISlider(min = 0.05f, max = 1f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_alpha { get; set; }

        [SettingsUISlider(min = 0f, max = 2f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_min_push_sec { get; set; }

        [SettingsUISlider(min = 1f, max = 5f, step = 0.25f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_max_ratio { get; set; }

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_max_density { get; set; }

        [SettingsUISlider(min = 0.5f, max = 5f, step = 0.1f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_min_ff_mps { get; set; }

        [SettingsUISlider(min = 0f, max = 2f, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(VehicleSection, CongestionGroup)]
        public float cong_min_sample_sec { get; set; }


        [SettingsUIButton]
        [SettingsUISection(OtherSection, OtherGroup)]
        public bool Button
        {
            set
            {
                SetDefaults();
            }

        }


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
            { m_Setting.GetOptionTabLocaleID(Setting.TaxiSection), "Taxi" },
            { m_Setting.GetOptionTabLocaleID(Setting.OtherSection), "Other" },

            // ----- Groups (shown inside each section) -----
            // NOTE: your constant is 'WaitingTine' (typo); label shows fine as "Waiting time".
            { m_Setting.GetOptionGroupLocaleID(Setting.WaitingTimeGroup), "Waiting time" },
            { m_Setting.GetOptionGroupLocaleID(Setting.ModeWeightGroup),  "Transport Type Weights" },
            { m_Setting.GetOptionGroupLocaleID(Setting.CarModeWeightGroup),  "Vehicle Weight" },
            { m_Setting.GetOptionGroupLocaleID(Setting.PedestrianGroup),  "Walking Speeds" },
            { m_Setting.GetOptionGroupLocaleID(Setting.TaxiGroup),  "Taxi" },

            // New section (tab)
            { m_Setting.GetOptionTabLocaleID(Setting.VehicleSection), "Vehicles" },

            // New groups inside the Vehicles section
            { m_Setting.GetOptionGroupLocaleID(Setting.TurnPenaltyGroup), "Turn penalties" },
            { m_Setting.GetOptionGroupLocaleID(Setting.RoadBiasGroup),    "Road hierarchy bias" },
            { m_Setting.GetOptionGroupLocaleID(Setting.BusLaneGroup),    "Bus Lanes" },

            // Group (under Pedestrians)
            { m_Setting.GetOptionGroupLocaleID(Setting.LongDistanceGroup), "Long-distance walking" },
            { m_Setting.GetOptionGroupLocaleID(Setting.PedestrianCrossingGroup), "Crossings" },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.car_mode_weight)), "Car perceived-time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.car_mode_weight)), "Multiplier on in-vehicle car time used for route choice. 1.0 = neutral; less than 1.0 makes driving feel faster, more than 1.0 makes it feel slower." },


            // ============================
            // Transit → Waiting time group
            // ============================
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.waiting_time_factor)),
              "Waiting time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.waiting_time_factor)),
              "Multiplier applied to the waiting time at the initial transit stop." },
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
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.crowdness_stop_threashold)),
                      "Crowding threshold (load ratio)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.crowdness_stop_threashold)),
                     "Applies crowding delay when the number of people at a stop exceeds this fraction of vehicle capacity (0–1). Example: 0.5 means crowding begins when the stop is half a vehicle’s capacity." },
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.nonbus_buslane_penalty_sec)), "Non-bus on bus-only lane penalty (s)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.nonbus_buslane_penalty_sec)),  "Extra behavior time per bus-only segment for non-bus vehicles. 0 = off." },

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

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ferry_mode_weight)),
              "Ferry in-vehicle time weight" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.ferry_mode_weight)),
              "Multiplier applied to Ferry/metro in-vehicle time." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.feeder_trunk_transfer_penalty)), "Feeder→Trunk transfer penalty" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.feeder_trunk_transfer_penalty)), "Multiplier applied to the wait time when transferring from a feeder mode (bus/tram) to a trunk mode (metro/train/ship/plane). Use 1.0 for no extra penalty; values < 1.0 reduce hassle, > 1.0 increase it." },

            // ============================
            // Taxi → Taxi options group
            // ============================
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.taxi_passengers_waiting_threashold)),
              "Waiting passengers threshold" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.taxi_passengers_waiting_threashold)),
              "If more than this many passengers are queued at a stand, the taxi crowding rule activates for that stand." },
            
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.taxi_fare_increase)),
              "Crowding fare increase" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.taxi_fare_increase)),
              "Extra fraction added to the starting fee when the stand is crowded. Example: 0.20 = +20% starting fee when the threshold is exceeded." },

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

            // ----- Options: Crosswalk factor -----
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ped_crosswalk_factor)),
              "Crosswalk cost factor" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.ped_crosswalk_factor)),
              "Multiplier applied to pedestrian crosswalk pathfind cost. 1.0 = no change." },
            
            // ----- Options: Unsafe crosswalk factor -----
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ped_unsafe_crosswalk_factor)),
              "Unsafe crosswalk cost factor" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.ped_unsafe_crosswalk_factor)),
              "Multiplier applied to pedestrian unsafe crosswalk cost. 1.0 = no change." },

            // Minimum multiplier
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.walk_long_min_mult)), "Minimum speed multiplier" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.walk_long_min_mult)),
              "Floor for perceived walking speed on long trips. Example: 0.6 = treat long ODs as if walking 40% slower." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ped_walk_time_factor)),
            "Walking cost multiplier" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.ped_walk_time_factor)),
            "Multiplies the pedestrian walking cost. 1.0 = no change; higher values make walking less attractive overall." },
            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.disable_ped_cost)), "Disable Walking cost multiplier" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.disable_ped_cost)), $"Disable Walking cost multiplier. This will make this mod more compatible with other mods that affect pedestrian Path Finding" },

            // Group under Vehicles
            { m_Setting.GetOptionGroupLocaleID(Setting.CongestionGroup), "Congestion feedback" },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_alpha)), "Smoothing (alpha)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_alpha)),
              "Exponential smoothing for live travel times. Higher = reacts faster; lower = steadier." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_min_push_sec)), "Update threshold (s)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_min_push_sec)),
              "Only update routing costs if the smoothed travel time changes by at least this many seconds." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_max_ratio)), "Max slowdown (× freeflow)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_max_ratio)),
              "Cap for the measured/freeflow time ratio (e.g., 3.0 = at most three times slower than freeflow)." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_max_density)), "Max density add" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_max_density)),
              "Upper limit on the extra density applied to congested lanes (0–1). Larger values slow cars more." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_min_ff_mps)), "Min freeflow speed (m/s)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_min_ff_mps)),
              "Floor used when estimating baseline freeflow from lane length and speed limit; avoids extreme ratios." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.cong_min_sample_sec)), "Min sample duration (s)" },
            { m_Setting.GetOptionDescLocaleID(nameof(Setting.cong_min_sample_sec)),
              "Ignore very short lane traversals under this duration when building the live average." },

            { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Button)), "Reset Settings" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Button)), $"Reset settings to default values" },
        };

            return dict;
        }

        public void Unload() { }
    }

}
