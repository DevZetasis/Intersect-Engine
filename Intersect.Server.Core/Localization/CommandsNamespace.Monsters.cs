using Intersect.Localization;
using Newtonsoft.Json;

namespace Intersect.Server.Localization;

public static partial class Strings
{
    public sealed partial class CommandsNamespace
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public readonly LocaleCommand GrantMonster = new()
        {
            Name = "grantmonster", Help = "Grants one persistent monster (server console only)",
            Description = "grantmonster <character-name-or-UUID> <species-UUID> <request-UUID>. Reuse the request UUID to retry safely.",
        };
    }
}
