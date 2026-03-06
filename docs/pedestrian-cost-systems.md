# Pedestrian Cost Apply/Restore Lifecycle

This note documents the shared apply/restore model behind `PedestrianWalkCostFactorSystem`
and `PedestrianCrosswalkCostFactorSystem`. It is intentionally narrower than pedestrian
pathfinding as a whole and focuses on how these two systems apply settings, restore baseline
values, and avoid stacking their own changes.

## Purpose

Use this note when changing:

- how pedestrian cost factors are applied after a settings update
- how `disable_ped_cost` restores prior overrides
- when cached origin components are created or reused
- how the walking-cost live graph tracks previously applied lane factors

## Lifecycle

Both systems are event-driven setting appliers.

- `OnSettingsApplied` decides whether a new pass is needed.
- `Enabled` is only the scheduler flag for that next pass.
- `OnUpdate` re-reads the current setting values and computes the effective multiplier for this run.

That split matters because "disabled" is domain state, not scheduler state. A disabled pedestrian
setting still needs one run so the system can restore any previously applied override back to the
1x baseline.

## Baseline Restoration

Older behavior treated `disable_ped_cost` or an effective `1x` multiplier as an early return.

- Result: previously applied pedestrian overrides could remain in prefabs or the live graph.

Current behavior treats disabled or `1x` settings as a baseline restoration pass.

- Cached overrides are recomputed from the stored original values and restored to baseline.
- If nothing was ever cached, the pass exits quietly without taking ownership of untouched data.

## Invariants

- Prefab adjustments are always derived from cached original values (`PedWalkCostOrig` and `PedCrosswalkCostOrig`).
- The walking-cost system tracks the last RPF factor applied to each live pedestrian lane in `_prevFactorByLane`.
- Repeated applies must not stack. Prefabs recompute from cached originals, and live graph lanes apply only the delta ratio between the new factor and the previously applied factor.
- Baseline passes should avoid unnecessary writes. If a prefab or lane already matches the desired value, the system should leave it alone.

## Compatibility Limitation

These systems only track the baseline they captured and the live-graph deltas that RPF applied itself.

If another mod changes the same pedestrian prefab cost or live graph edge after that capture point,
a later RPF reapply can overwrite the other mod's change because RPF restores from its own cached
view of the world.

`disable_ped_cost` improves compatibility by letting users restore RPF's pedestrian changes, but it
does not make the shared data fully multi-writer safe.

## Contributor Notes

Keep the walking-cost and crosswalk-cost systems aligned when changing any of the following behaviors:

- event-driven settings application
- baseline restoration semantics
- when origin components are created
- write suppression and logging rules
- compatibility notes around other pedestrian-cost mods

If one system changes its restoration rules or cache ownership model, review the other system in
the same change so contributors do not need to reason about two different pedestrian-cost lifecycles.
