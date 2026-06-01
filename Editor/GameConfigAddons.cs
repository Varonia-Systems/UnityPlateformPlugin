using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Extensibility contract for the GameConfig editor window. An addon contributes
    /// its own themed section, reads existing values on load, declares which JSON keys
    /// it owns (so they are excluded from the generic "extra fields" card), and writes
    /// its values back into the JObject before the file is saved.
    /// </summary>
    public interface IGameConfigAddon
    {
        /// <summary>JSON keys this addon manages — hidden from the generic extra-fields list.</summary>
        IEnumerable<string> ClaimedKeys { get; }

        /// <summary>Called when the window refreshes, with the parsed config JSON (may be null).</summary>
        void OnLoad(JObject json);

        /// <summary>Draws the addon's section inside the window, using the shared theme.</summary>
        void OnDraw(GameConfigReflectionEditor.AddonContext ctx);

        /// <summary>Merges this addon's values into the JObject right before it's written to disk.</summary>
        void OnSave(JObject json);
    }

    /// <summary>
    /// Registry addons hook into (typically from an [InitializeOnLoad] static constructor).
    /// The list is static and is reset on every domain reload, so re-registration each
    /// reload is expected and does not produce duplicates.
    /// </summary>
    public static class GameConfigAddons
    {
        public static readonly List<IGameConfigAddon> Registered = new List<IGameConfigAddon>();

        public static void Register(IGameConfigAddon addon)
        {
            if (addon != null && !Registered.Contains(addon))
                Registered.Add(addon);
        }
    }
}
