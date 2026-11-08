using System.Text;

namespace HybridVR.Shared
{
    public static class LiveSettingKey
    {
        // Used only for explicitly host-specific options, never to merge settings
        // that merely have similar labels but different units or semantics.
        public static string Slug(string value)
        {
            var result = new StringBuilder();
            foreach (char raw in value)
            {
                char c = char.ToLowerInvariant(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) result.Append(c);
                else if (result.Length > 0 && result[result.Length - 1] != '-') result.Append('-');
            }
            return result.ToString().TrimEnd('-');
        }
    }
}
