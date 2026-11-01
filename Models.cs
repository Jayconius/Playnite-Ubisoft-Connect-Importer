using System.Collections.Generic;

namespace UbisoftConnectImporter
{
    internal sealed class ProductInfo
    {
        public ProductRoot root { get; set; }
        public Localizations localizations { get; set; }
        public uint uplay_id { get; set; }
        public uint install_id { get; set; }
    }

    internal sealed class ProductRoot
    {
        public string name { get; set; }
        public string background_image { get; set; }
        public string thumb_image { get; set; }
        public string icon_image { get; set; }
        public string logo_image { get; set; }
        public bool is_ulc { get; set; }
        public List<AddonInfo> addons { get; set; }
        public ThirdParty third_party_platform { get; set; }
        public StartGame start_game { get; set; }
    }

    internal sealed class Localizations
    {
        public Dictionary<string, string> @default { get; set; }
    }

    internal sealed class AddonInfo { public uint id { get; set; } }
    internal sealed class ThirdParty { public string name { get; set; } }
    internal sealed class StartGame { public object online { get; set; } public object offline { get; set; } }

    internal sealed class CacheProduct
    {
        public uint UplayId { get; set; }
        public uint InstallId { get; set; }
        public string GameInfo { get; set; }
    }
}
