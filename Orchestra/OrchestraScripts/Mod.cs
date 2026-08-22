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

            // One panel for every block, on its own object so it outlives the
            // scene changes a block does not. It watches the mapper and shows
            // itself when one of ours is opened; without UI Factory it quietly
            // never builds and the stock mapper is all there is.
            GameObject host = new GameObject("OrchestraPanel");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<OrchestraPanel>();
        }
    }
}
