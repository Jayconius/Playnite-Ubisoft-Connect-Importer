using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UbisoftConnectImporter
{
    [LoadPlugin]
    public sealed class UbisoftLibrary : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string AssetBase = "https://ubistatic3-a.akamaihd.net/orbit/uplay_launcher_3_0/assets/";

        public override Guid Id { get; } = Guid.Parse("4c506dc1-9c4e-4da4-a541-92fefb68577b");
        public override string Name { get { return "Ubisoft Connect (Cache Compatible)"; } }

        public UbisoftLibrary(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var catalog = ReadCatalog();
            var installed = ReadInstalledGames();
            var output = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            foreach (var product in catalog)
            {
                if (product.root == null || product.root.is_ulc || product.root.third_party_platform != null || product.root.start_game == null)
                    continue;
                var id = product.uplay_id.ToString();
                var name = Localized(product, product.root.name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                output[id] = MakeGame(id, name, product, false, null);
            }

            foreach (var item in installed)
            {
                var match = catalog.FirstOrDefault(p => p.install_id == item.Id || p.uplay_id == item.Id);
                var id = match == null ? item.Id.ToString() : match.uplay_id.ToString();
                var title = match == null ? null : Localized(match, match.root == null ? null : match.root.name);
                if (string.IsNullOrWhiteSpace(title)) title = CleanFolderName(Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                output[id] = MakeGame(id, title, match, true, item.Path);
            }

            return output.Values;
        }

        private static GameMetadata MakeGame(string id, string name, ProductInfo product, bool isInstalled, string installDir)
        {
            var game = new GameMetadata
            {
                GameId = id,
                Name = name,
                Source = new MetadataNameProperty("Ubisoft Connect"),
                IsInstalled = isInstalled,
                InstallDirectory = installDir,
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                GameActions = new List<GameAction>
                {
                    new GameAction { Name = "Play", Type = GameActionType.URL, Path = "uplay://launch/" + id, IsPlayAction = true }
                }
            };
            if (product != null && product.root != null)
            {
                game.Icon = Asset(product.root.icon_image);
                game.CoverImage = Asset(product.root.thumb_image);
                game.BackgroundImage = Asset(product.root.background_image);
            }
            return game;
        }

        private static MetadataFile Asset(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            Uri uri;
            return new MetadataFile(Uri.TryCreate(path, UriKind.Absolute, out uri) ? path : AssetBase + path);
        }

        private static string Localized(ProductInfo product, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string localized;
            if (product.localizations != null && product.localizations.@default != null && product.localizations.@default.TryGetValue(value, out localized))
                return localized;
            return value;
        }

        private static string CleanFolderName(string name)
        {
            return Regex.Replace((name ?? "Ubisoft game").Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim();
        }

        private static List<ProductInfo> ReadCatalog()
        {
            var products = new List<ProductInfo>();
            foreach (var path in CacheCandidates())
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (string.Equals(Path.GetExtension(path), ".vdat", StringComparison.OrdinalIgnoreCase))
                    {
                        bytes = VdatDecryptor.Decrypt(bytes);
                    }
                    foreach (var cached in ProtobufReader.ReadLegacyProducts(bytes))
                    {
                        var product = Serialization.FromYaml<ProductInfo>(cached.GameInfo);
                        if (product == null || product.root == null) continue;
                        product.uplay_id = cached.UplayId;
                        product.install_id = cached.InstallId;
                        products.Add(product);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Ubisoft cache file is not in the legacy readable format: " + path);
                }
            }
            return products.GroupBy(p => p.uplay_id).Select(g => g.First()).ToList();
        }

        private static IEnumerable<string> CacheCandidates()
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher", "cache");
            var roots = new[] { local, Path.Combine(FindLauncherPath(), "cache") }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                yield return Path.Combine(root, "configuration", "configurations");
                yield return Path.Combine(root, "configuration", "configurations.vdat");
            }
        }

        private static string FindLauncherPath()
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (key == null) continue;
                        foreach (var sub in key.GetSubKeyNames())
                        using (var app = key.OpenSubKey(sub))
                        {
                            if (app == null || !string.Equals(app.GetValue("DisplayName") as string, "Ubisoft Connect", StringComparison.OrdinalIgnoreCase)) continue;
                            var location = app.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(location)) return location;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private sealed class Installed
        {
            public uint Id;
            public string Path;
        }

        private static List<Installed> ReadInstalledGames()
        {
            var found = new List<Installed>();
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = root.OpenSubKey(@"SOFTWARE\ubisoft\Launcher\Installs"))
                    {
                        if (key == null) continue;
                        foreach (var sub in key.GetSubKeyNames())
                        {
                            uint id;
                            if (!uint.TryParse(sub, out id)) continue;
                            using (var game = key.OpenSubKey(sub))
                            {
                                var dir = game == null ? null : game.GetValue("InstallDir") as string;
                                if (string.IsNullOrWhiteSpace(dir)) continue;
                                dir = dir.Replace('/', Path.DirectorySeparatorChar);
                                if (Directory.Exists(dir)) found.Add(new Installed { Id = id, Path = dir });
                            }
                        }
                    }
                }
                catch (Exception ex) { Logger.Debug(ex, "Could not read Ubisoft installed-game registry entries."); }
            }
            return found.GroupBy(g => g.Id).Select(g => g.First()).ToList();
        }
    }
}
