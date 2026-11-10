using System.Text;

namespace Intersect;


public static partial class SharedConstants
{

    public static readonly string VersionName = "Beta Eridani (201802251101-05)";

    // Battles add packet types and configurable PP; reject older clients/editors that cannot preserve them.
    public static readonly byte[] VersionData = Encoding.UTF8.GetBytes(VersionName + "|monster-ownership-v1|monster-types-v1|monster-moves-v1|monster-natures-v1|monster-battles-v1");

}
