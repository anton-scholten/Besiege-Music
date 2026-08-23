using System;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The one thing the mod remembers between sessions: where the panel was left.
    ///
    /// Kept where Besiege's modding API says to keep it.
    /// <c>Modding.Configuration.GetData()</c> hands back an <c>XDataHolder</c>
    /// belonging to this mod alone; the loader reads it in with the mod and writes
    /// it out on a clean quit, so the round trip is the game's to manage. It lands
    /// in <c>Besiege_Data/Mods/Config/Orchestra_&lt;id&gt;.xml</c>. (PlayerPrefs would
    /// work too, but it is Unity's store, not Besiege's, and uninstalling the mod
    /// would leave its settings in the game's own options file.)
    ///
    /// Two Singles rather than a Vector2, because that is the type every block
    /// setting in every .bsg uses and the holder is sure to have it.
    /// </summary>
    public static class Prefs
    {
        // Written down rather than derived from anything: these are on disk, so
        // they are not free to change.
        private const string CornerXKey = "panel-x";
        private const string CornerYKey = "panel-y";

        /// <summary>
        /// This mod's configuration, or null if the API refuses -- which it does by
        /// throwing. A mod that cannot store a preference should still run.
        /// </summary>
        private static XDataHolder Data()
        {
            try
            {
                return Modding.Configuration.GetData();
            }
            catch (Exception e)
            {
                Log.Warn("no configuration to keep the panel's position in: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Where the panel's top-left corner was left, in canvas units from the
        /// middle of the screen. False if it has never been put anywhere, in which
        /// case the first window keeps whatever place it is built in.
        /// </summary>
        public static bool Corner(out Vector2 at)
        {
            at = Vector2.zero;
            XDataHolder data = Data();
            if (data == null || !data.HasKey(CornerXKey) || !data.HasKey(CornerYKey))
            {
                return false;
            }
            try
            {
                at = new Vector2(data.ReadFloat(CornerXKey), data.ReadFloat(CornerYKey));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Stores the corner and writes it out. Called when the panel closes rather
        /// than while it is dragged, which is why the disk write can go with it.
        /// </summary>
        public static void SetCorner(Vector2 at)
        {
            XDataHolder data = Data();
            if (data == null)
            {
                return;
            }
            try
            {
                data.Write(CornerXKey, at.x);
                data.Write(CornerYKey, at.y);
                Modding.Configuration.Save();
            }
            catch (Exception)
            {
                // A position that cannot be stored is not worth a log line every
                // time the mapper is closed.
            }
        }
    }
}
