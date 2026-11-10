using System.Text;

namespace Intersect;


public static partial class SharedConstants
{

    public static readonly string VersionName = "Beta Eridani (201802251101-05)";

    // Presentation adds ordered events and artwork; reject older peers that cannot preserve this content.
    public static readonly byte[] VersionData = Encoding.UTF8.GetBytes(VersionName + "|monster-ownership-v1|monster-types-v1|monster-moves-v1|monster-natures-v1|monster-battles-v2");

}
