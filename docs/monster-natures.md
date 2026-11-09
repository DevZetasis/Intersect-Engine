# Monster nature content and Milestone 5B integration

MonsterNatureDescriptor is editor-created game content keyed by a permanent UUID. No traditional nature list is seeded. Names and folder names are labels, not calculation keys. The game-data table holds Name, Id, TimeCreated, Description, Folder, SelectionWeight and Modifiers JSON. No player database or individual monster fields change in Milestone 5A.

Modifiers is a structured object with independent Increase and Decrease entries, each containing Stat and Percentage. Percentage is a nonnegative decimal magnitude in [0,50]; the entry name determines its direction. The editor supports two decimal places. Both magnitudes default to 10. None ignores its magnitude, so two None selections form a neutral nature while retaining the defaults. Selecting the same non-None stat in both directions is invalid, even when a magnitude is zero.

MonsterStat is separate from Intersect's RPG Stat enum. Persistent values: None=0, HP=1, Attack=2, Defense=3, SpecialAttack=4, SpecialDefense=5, Speed=6. HP exists as an explicit monster identifier but is forbidden by nature validation and omitted from both dropdowns. Preserve these values if additional monster stats are introduced later. The nested JSON can gain additional definition effects without flattening them into RPG stat arrays. No effects or stat calculations are applied now.

## Relative selection weights

SelectionWeight is an integer from 0 to 100000, default 100. Zero excludes a nature from future random assignment; it does not delete the definition or invalidate it for explicit content references. Weights are relative, not percentages or wild encounter rates. For weights 100,25,5, the future eligible total is 130 and relative chances are 100/130,25/130,5/130. No random selection algorithm is implemented here.

Milestone 5B must filter eligible definitions and sum only positive weights using checked arithmetic in a sufficiently wide integer accumulator. Handle an empty eligible set or total weight zero BEFORE any random draw or division. Recommended default: return an explicit neutral/unassigned result and a controlled diagnostic, never silently pick an excluded nature. If 5B requires every monster to have a nature UUID, validate a configured fallback before generation and fail that generation in a controlled way when no fallback exists; do not partially persist a monster or choose an arbitrary zero-weight nature. This policy must be finalized with the individual-monster schema and game rules.

## Future individual references and deletion

Future individual monsters must reference the permanent nature UUID, never a name, enum position, list index or folder. Add NatureId and its player-database migrations only in 5B, with an explicit policy for existing monsters. Include reference checks and indexes needed for efficient lookup.

The current editor permits deletion because no individual nature references exist yet. BEFORE enabling NatureId assignment, extend the deletion path and persistence validation to reject referenced natures unless a transactional replacement/migration is explicitly requested. Assignment and deletion must be coordinated, including across the separate game/player databases, so no monster receives a UUID that has just been deleted. Existing monsters must never be silently reassigned by deleting or renaming a nature. Weight zero alone must not block use by an already-referencing individual or remove its association.

## Future deterministic calculations and battles

Read the definition referenced by the individual's UUID; never branch on nature names. Keep HP unaffected. Define and test the Generation 4-inspired stat-calculation order and integer rounding stages before applying the configured nature percentage at the appropriate step. Convert decimal percentages to an exact fixed-point/rational representation or use deterministic decimal arithmetic with explicitly specified rounding; avoid incidental binary floating-point differences. This milestone supplies definitions only.

Editing a nature may affect every monster that references it when stats are recomputed. Milestone 5B should state when recalculation happens and how caches are invalidated. For future battles, snapshot the referenced nature UUID and effective modifier values, with a content revision/hash, when participants enter the battle. Use that immutable snapshot throughout the battle so editor changes cannot alter a turn or produce inconsistent calculations across participants. Later battles and out-of-battle recomputations may use the current saved definition according to the chosen rules.

There is no nature assignment, random selector, NatureId field, IV/EV implementation, final-stat calculator or battle system in Milestone 5A.
