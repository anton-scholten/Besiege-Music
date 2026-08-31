using Modding;
using Modding.Modules;
using UnityEngine;

namespace MusicMod
{
    /// <summary>
    /// Entry point. One module drives every instrument block: what differs is
    /// declared in each block's XML.
    /// </summary>
    public class Mod : ModEntryPoint
    {
        public override void OnLoad()
        {
            CustomModules.AddBlockModule<MusicModule, InstrumentBehaviour>("MusicMod", false);
            CustomModules.AddBlockModule<LoaderModule, LoaderBehaviour>("LoaderMod", false);
            // The Braids block, which used to be a mod of its own and is now one of
            // these. Its sources are under MusicScripts/Braids, unchanged apart
            // from this line -- what was its own `Mod.OnLoad` is these two.
            CustomModules.AddBlockModule<BraidsSynth.SynthModule,
                                         BraidsSynth.BraidsBehaviour>("BraidsSynth", false);

            // One panel for every block, on its own object so it outlives the
            // scene changes a block does not. It watches the mapper and shows
            // itself when one of ours is opened; without UI Factory it quietly
            // never builds and the stock mapper is all there is.
            GameObject host = new GameObject("MusicPanel");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<MusicPanel>();

            // Finds this mod's blocks in the prefab table as soon as they are in
            // it: their ids, which a song is written as, and their skins, which
            // Besiege would otherwise repaint the toolbar's models with.
            host.AddComponent<Prefabs>();

            // The last stop before the speakers. Master shares one gain between the
            // blocks from an estimate; this one reads the finished mix and is the
            // reason a chord on one instrument cannot clip however in phase it is.
            host.AddComponent<Ears>();

            // The loader block's panel is a second window of its own: it draws
            // something else, on its own canvas, for one block rather than nine.
            GameObject loader = new GameObject("MusicLoaderPanel");
            Object.DontDestroyOnLoad(loader);
            loader.AddComponent<LoaderPanel>();

            // And the Braids block's own panel, which is a third window again: the
            // model chooser, its scope, and the sliders that go with them. It
            // outlives scene loads for the same reason the others do -- the
            // mapper's callbacks are static delegates.
            GameObject braids = new GameObject("BraidsPanelHost");
            Object.DontDestroyOnLoad(braids);
            braids.AddComponent<BraidsSynth.BraidsPanel>();
        }
    }
}
