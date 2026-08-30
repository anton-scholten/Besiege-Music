using Modding;
using Modding.Modules;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// Entry point. One module drives every instrument block: what differs is
    /// declared in each block's XML.
    /// </summary>
    public class Mod : ModEntryPoint
    {
        public override void OnLoad()
        {
            CustomModules.AddBlockModule<OrchestraModule, InstrumentBehaviour>("OrchestraMod", false);
            CustomModules.AddBlockModule<LoaderModule, LoaderBehaviour>("LoaderMod", false);

            // One panel for every block, on its own object so it outlives the
            // scene changes a block does not. It watches the mapper and shows
            // itself when one of ours is opened; without UI Factory it quietly
            // never builds and the stock mapper is all there is.
            GameObject host = new GameObject("OrchestraPanel");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<OrchestraPanel>();

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
            GameObject loader = new GameObject("OrchestraLoaderPanel");
            Object.DontDestroyOnLoad(loader);
            loader.AddComponent<LoaderPanel>();
        }
    }
}
