using System.Text;

namespace Intersect;


public static partial class SharedConstants
{

    public static readonly string VersionName = "Beta Eridani (201802251101-05)";

    // Reject peers that cannot synchronize the monster ownership packets, type and move content.
    public static readonly byte[] VersionData = Encoding.UTF8.GetBytes(VersionName + "|monster-ownership-v1|monster-types-v1|monster-moves-v1");

}
