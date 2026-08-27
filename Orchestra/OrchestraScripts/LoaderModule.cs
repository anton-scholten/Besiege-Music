using System.ComponentModel;
using System.Xml.Serialization;
using Modding.Modules;

namespace OrchestraMod
{
    /// <summary>
    /// The MIDI loader block, as declared in Loader.xml.
    ///
    /// It has almost nothing in it: what the block does is decided by its own
    /// controls and by the file it is given, not by the XML. The two attributes
    /// are the block's name -- which the panel puts in its own captions, and which
    /// the build's XML check holds to the block's &lt;Name&gt; -- and which
    /// instrument a score's pitched parts go to before anybody changes it.
    ///
    /// The [Xml*] and [DefaultValue] rules are the ones
    /// <see cref="OrchestraModule"/> explains at length: an attribute without a
    /// [DefaultValue] is required, and a block XML missing one is dropped whole.
    /// </summary>
    [XmlRoot("LoaderMod")]
    public class LoaderModule : BlockModule
    {
        [XmlAttribute("block")]
        [DefaultValue("")]
        public string Family = "";

        /// <summary>The block a score's pitched parts go to, as the loader's own
        /// menu is set when it is first placed.</summary>
        [XmlAttribute("instrument")]
        [DefaultValue("Piano")]
        public string Instrument = "Piano";
    }
}
