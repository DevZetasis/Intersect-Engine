# Monster move content and future effects

MonsterMoveDescriptor and the category/target enums are content definitions, independent of real-time spells and owned-monster state. Category values are Physical=0, Special=1, Status=2. Targets are OneOpponent=0, Self=1, OneAlly=2, AllOpponents=3, AllBattlefield=4. Preserve these numeric values when extending them.

Moves require an existing monster type. The editor creates the initial move using the first type ordered by name; the developer can change it. Physical/Special power is 1..Int32.MaxValue; Status power permits zero. Accuracy is an integer 1..100 even when AlwaysHits is selected. AlwaysHits is explicit metadata, not a sentinel accuracy. Priority accepts any signed Int32. AnimationId is an optional existing animation UUID; it does not invoke an animation or spell during this milestone.

Species learnsets contain move UUID/positive integer level pairs. Entries can share a level and a reusable move can appear in several species or at several levels. The editor displays entries by level, preserving insertion order for ties. Level 1 is template content for future starting moves. No move is taught to an owned monster automatically. Learnsets do not represent current learned moves or equipped move slots.

## Recommended future move-effect system

Add reusable, versioned effect definitions rather than branches for individual move names, species or elemental types. A future move can reference an ordered list of effect definitions plus parameters. Start with typed contracts for heal, stat-stage modification and applying/removing a configurable status; add chance, duration, target scope and timing (before/after hit, per target, end of turn) as explicit settings. Keep passive abilities as a distinct system that can call shared effect primitives later.

A server-authoritative battle service should resolve targets, roll accuracy, calculate category-based damage, and invoke effects through a deterministic battle execution context. That context should expose validated participants, battle state, seeded RNG, turn phase and emitted battle events. Effects must not directly mutate arbitrary player/NPC objects or send client packets. Validate parameter ranges and content references on save, and retain reference-safe deletion as new effect/status objects are added.

Physical damage will use Attack/Defense, Special damage SpecialAttack/SpecialDefense. Type effectiveness and configurable STAB should remain independent modifiers. Category alone should not hard-code healing, buffs or status behavior. Future owned-monster move inventories should persist separately and reference immutable move UUIDs; species learnset edits should not retroactively rewrite an individual's training without an explicit progression policy.

These are recommendations only. Milestone 4 implements no effect execution, targeting execution, damage, STAB, battle sessions, or individual learned-move inventory.
