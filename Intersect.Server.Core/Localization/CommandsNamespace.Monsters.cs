using Intersect.Localization;
using Newtonsoft.Json;

namespace Intersect.Server.Localization;

public static partial class Strings
{
    public sealed partial class CommandsNamespace
    {
        public readonly LocaleCommand HealMonster = new() { Name = "healmonster", Help = "Restore owned monster HP/PP or cure status (server console only)", Description = "healmonster <individual-monster-UUID> <hp|pp|status|all>" };
        public readonly LocaleCommand StartMonsterBattle = new()
        {
            Name = "startmonsterbattle", Help = "Start a private 1v1 practice monster battle (console only)",
            Description = "startmonsterbattle <character-name-or-UUID> <owned-monster-UUID> <opponent-species-UUID> <opponent-level>",
        };
        public readonly LocaleCommand CancelMonsterBattle = new()
        {
            Name = "cancelmonsterbattle", Help = "Cancel a practice battle (console only)",
            Description = "cancelmonsterbattle <battle-UUID>",
        };
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public readonly LocaleCommand InspectMonster = new()
        {
            Name = "inspectmonster", Help = "Read-only persistent monster inspection (server console only)",
            Description = "inspectmonster <individual-monster-UUID>. Displays nature, IVs, EVs and calculated stats without changing data.",
        };

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public readonly LocaleCommand GrantMonster = new()
        {
            Name = "grantmonster", Help = "Grants one persistent monster (server console only)",
            Description = "grantmonster <character-name-or-UUID> <species-UUID> <request-UUID>. Reuse the request UUID to retry safely.",
        };
    }
}
