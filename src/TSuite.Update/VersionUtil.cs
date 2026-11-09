namespace TSuite.Update;

/// <summary>Basit SemVer karsilastirma: "v1.2.3", "1.2.3-beta.1" (on-surum, kararli surumden kucuktur).</summary>
public static class VersionUtil
{
    public static int Compare(string a, string b)
    {
        var (na, pa) = Parse(a); var (nb, pb) = Parse(b);
        for (int i = 0; i < 4; i++)
        {
            int c = na[i].CompareTo(nb[i]);
            if (c != 0) return c;
        }
        if (pa.Length == 0 && pb.Length == 0) return 0;
        if (pa.Length == 0) return 1;
        if (pb.Length == 0) return -1;
        var xa = pa.Split('.'); var xb = pb.Split('.');
        for (int i = 0; i < Math.Max(xa.Length, xb.Length); i++)
        {
            if (i >= xa.Length) return -1;
            if (i >= xb.Length) return 1;
            bool ia = long.TryParse(xa[i], out var la), ib = long.TryParse(xb[i], out var lb);
            int c = ia && ib ? la.CompareTo(lb) : ia ? -1 : ib ? 1 : string.CompareOrdinal(xa[i], xb[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool IsNewer(string current, string candidate) => Compare(candidate, current) > 0;

    static (long[] nums, string pre) Parse(string v)
    {
        v = v.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        int plus = v.IndexOf('+'); if (plus >= 0) v = v[..plus];
        string pre = "";
        int dash = v.IndexOf('-'); if (dash >= 0) { pre = v[(dash + 1)..]; v = v[..dash]; }
        var nums = new long[4];
        var parts = v.Split('.');
        for (int i = 0; i < 4 && i < parts.Length; i++)
            nums[i] = long.TryParse(parts[i], out var n) ? n : 0;
        return (nums, pre);
    }
}
