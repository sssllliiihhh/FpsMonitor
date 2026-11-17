using System.Diagnostics;
using System.IO;

namespace FpsMonitor.Services;

public sealed record GameProcessInfo(int Pid, string DisplayName, string ExeName, string ExePath)
{
    public string Label =>
        string.Equals(DisplayName, Path.GetFileNameWithoutExtension(ExeName), StringComparison.OrdinalIgnoreCase)
            ? $"{ExeName}  (PID {Pid})"
            : $"{DisplayName} ({ExeName})  [PID {Pid}]";
}

public static class GameProcessService
{
    private static readonly Dictionary<string, string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2.exe"] = "Counter-Strike 2",
        ["csgo.exe"] = "Counter-Strike",
        ["valorant-win64-shipping.exe"] = "VALORANT",
        ["r5apex.exe"] = "Apex Legends",
        ["fortniteclient-win64-shipping.exe"] = "Fortnite",
        ["gta5.exe"] = "GTA V",
        ["gta5_enhanced.exe"] = "GTA V Enhanced",
        ["rdr2.exe"] = "Red Dead Redemption 2",
        ["eldenring.exe"] = "Elden Ring",
        ["dota2.exe"] = "Dota 2",
        ["overwatch.exe"] = "Overwatch 2",
        ["minecraft.exe"] = "Minecraft",
        ["javaw.exe"] = "Minecraft / Java Game",
        ["tarkov.exe"] = "Escape from Tarkov",
        ["rustclient.exe"] = "Rust",
        ["pubg.exe"] = "PUBG",
        ["helldivers2.exe"] = "Helldivers 2",
        ["cyberpunk2077.exe"] = "Cyberpunk 2077",
        ["witcher3.exe"] = "The Witcher 3",
        ["rocketleague.exe"] = "Rocket League",
        ["rainbowsix.exe"] = "Rainbow Six Siege",
        ["rainbowsix_vulkan.exe"] = "Rainbow Six Siege Vulkan",
        ["forza_horizon5.exe"] = "Forza Horizon 5",
        ["fh5.exe"] = "Forza Horizon 5",
        ["acsh.exe"] = "Assetto Corsa"
    };

    private static readonly string[] GamePathMarkers =
    [
        @"\steamapps\common\",
        @"\epic games\",
        @"\riot games\",
        @"\ubisoft game launcher\games\",
        @"\ea games\",
        @"\battle.net\",
        @"\xboxgames\"
    ];

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe",
        "riotclientservices.exe", "riotclientux.exe", "eadesktop.exe",
        "battle.net.exe", "explorer.exe", "taskmgr.exe", "discord.exe",
        "chrome.exe", "msedge.exe", "firefox.exe", "svchost.exe",
        "conhost.exe", "dllhost.exe", "fpsmonitor.exe"
    };

    public static List<GameProcessInfo> GetProcesses(bool includeAllProcesses = false)
    {
        var results = new List<GameProcessInfo>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var exePath = proc.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathRooted(exePath))
                    continue;

                var exeName = Path.GetFileName(exePath);
                if (Excluded.Contains(exeName))
                    continue;

                string? displayName = null;
                if (KnownGames.TryGetValue(exeName, out var known))
                {
                    displayName = known;
                }
                else if (GamePathMarkers.Any(marker => exePath.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    displayName = Path.GetFileNameWithoutExtension(exeName);
                }
                else if (includeAllProcesses)
                {
                    displayName = string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                        ? Path.GetFileNameWithoutExtension(exeName)
                        : proc.MainWindowTitle;
                }
                else
                {
                    continue;
                }

                results.Add(new GameProcessInfo(proc.Id, displayName, exeName, exePath));
            }
            catch
            {
                // Нет доступа к системному процессу или процесс уже завершился.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return results
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
