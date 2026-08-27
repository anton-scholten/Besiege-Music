using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// Looks this mod's blocks up in Besiege's prefab table, once they are in it.
    ///
    /// A song is written as block ids, and the id a modded block gets is decided by
    /// the game at load, not by the XML. <see cref="Catalogue.Resolve"/> reads it
    /// back off the prefabs -- and cannot be left until a block is placed, because
    /// the loader has to know the ids of blocks nobody has placed.
    ///
    /// Asked for once a second rather than once at load because the mod is loaded
    /// before the prefabs are registered, and quietly: `Resolve` says something only
    /// when its answer changes.
    /// </summary>
    public class Prefabs : MonoBehaviour
    {
        private const float AskEvery = 1f;

        /// <summary>Two minutes of asking. A game that has not registered this
        /// mod's blocks by then is not going to, and something else is wrong --
        /// which `Resolve` has already said, in more detail than another hundred
        /// attempts would add.</summary>
        private const int Attempts = 120;

        private float askAt;
        private int tried;

        private void Update()
        {
            if (Catalogue.Settled || tried >= Attempts)
            {
                // Nothing left to look up. The component stays for the next scene
                // load rather than being destroyed, and costs a branch a frame.
                return;
            }
            if (Time.unscaledTime < askAt)
            {
                return;
            }
            askAt = Time.unscaledTime + AskEvery;
            tried++;
            Catalogue.Resolve();
            if (tried >= Attempts && !Catalogue.Settled)
            {
                Log.Warn("gave up looking for this mod's block prefabs; the MIDI "
                         + "loader will refuse to write a machine rather than write "
                         + "one full of the wrong blocks.");
            }
        }
    }
}
