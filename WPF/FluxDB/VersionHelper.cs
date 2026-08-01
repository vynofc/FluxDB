using System;

namespace FluxDB
{
    public static class VersionHelper
    {
        public static string NormalizeVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            if (s.Contains("!")) s = s.Split('!')[0].Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            return s.Trim();
        }

        public static int CompareVersions(string v1, string v2)
        {
            var s1 = NormalizeVersion(v1);
            var s2 = NormalizeVersion(v2);

            if (s1 == s2) return 0;

            var p1 = s1.Split('-');
            var p2 = s2.Split('-');

            if (Version.TryParse(p1[0], out Version ver1) && Version.TryParse(p2[0], out Version ver2))
            {
                int cmp = ver1.CompareTo(ver2);
                if (cmp != 0) return cmp;
            }

            bool hasSuffix1 = p1.Length > 1;
            bool hasSuffix2 = p2.Length > 1;

            if (!hasSuffix1 && hasSuffix2) return 1;
            if (hasSuffix1 && !hasSuffix2) return -1;

            if (hasSuffix1 && hasSuffix2)
            {
                return string.Compare(p1[1], p2[1], StringComparison.OrdinalIgnoreCase);
            }

            return 0;
        }
    }
}