using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Modding.Modules;
// Safe to import here: this file has no UnityEngine.Vector3 to clash with the
// one Modding.Serialization declares.
using Modding.Serialization;

namespace MusicMod
{
    /// <summary>
    /// One instrument block, as declared in its block XML. Everything that differs
    /// between a piano and a cymbal lives here rather than in code, so adding an
    /// instrument is XML and samples.
    ///
    /// System.Xml is blacklisted as *code*, but the loader's scanner never reads
    /// custom attributes, so [Xml*] markers are the supported way to name what a
    /// module deserialises.
    ///
    /// Every [XmlAttribute] below is REQUIRED in the XML unless it also carries
    /// [DefaultValue]: Serialization.Validate builds its list of members to check
    /// as `members.Where(m =&gt; !m.IsDefined(typeof(DefaultValueAttribute)))`, and
    /// reports any of them the element did not supply as
    /// "... must have &lt;name&gt; attribute!" -- after which the whole block XML is
    /// dropped and the block never reaches the toolbar. Besiege's own modules mark
    /// their optional attributes the same way; ShootingModule has eight.
    ///
    /// So the rule here is: a field that declares a default in C# carries the
    /// matching [DefaultValue] and is optional; a field without one is required.
    /// tools/tests/XmlCheck.cs enforces that against the block XMLs at build time.
    /// </summary>
    [XmlRoot("MusicMod")]
    public class MusicModule : BlockModule
    {
        /// <summary>
        /// What the family is called, for the panel's title: "Piano", "Brass".
        ///
        /// Declared here rather than read from the game. `BlockPrefabInfo.Name` is
        /// the only name the modding API offers for a block and it is the owning
        /// mod's id -- a title bar reading ACA735EA-A614-... is what asking for it
        /// gets you. The block's own &lt;Name&gt; element is right there in the same
        /// file, and `XmlCheck` holds this to it, so the two cannot drift.
        /// </summary>
        [XmlAttribute("block")]
        [DefaultValue("")]
        public string Family = "";

        /// <summary>The entries in the block's Type menu, in order.</summary>
        /// <summary>
        /// Which of the Types below a newly placed block is set to, by name.
        ///
        /// A name rather than a number, and an attribute rather than the order of
        /// the list: the menu is saved as an *index*, so putting a different type
        /// first would quietly change what every machine already built plays. This
        /// moves the default without moving the list.
        ///
        /// Empty means the first one, which is what every block but the piano says.
        /// </summary>
        [XmlAttribute("default")]
        [DefaultValue("")]
        public string DefaultType = "";

        [XmlArray("Types")]
        [XmlArrayItem("Type")]
        [RequireToValidate]
        public InstrumentType[] Types;

        /// <summary>Controls this instrument adds beyond the common ones.</summary>
        [XmlArray("Extras")]
        [XmlArrayItem("Extra")]
        [RequireToValidate]
        [CanBeEmpty]
        public ExtraControl[] Extras;
    }

    /// <summary>
    /// One entry in the Type menu: which engine renders it, and how.
    ///
    /// `engine` picks the voice: "sampler" plays the clips named by `samples`,
    /// "modal" rings a bank of partials, "drum" is a pitched body plus noise.
    /// The tuning fields mean slightly different things per engine, which is
    /// documented on each voice.
    /// </summary>
    [Serializable]
    public class InstrumentType : Element
    {
        [XmlAttribute("name")]
        public string Name;

        [XmlAttribute("engine")]
        [DefaultValue("modal")]
        public string Engine = "modal";

        /// <summary>
        /// Space-separated resource names, one per sampled note, each named
        /// `&lt;stem&gt;_&lt;midiNote&gt;` so the key map can read its own pitch.
        /// </summary>
        [XmlAttribute("samples")]
        [DefaultValue("")]
        public string Samples = "";

        /// <summary>
        /// Loop points, one `start-end` per entry in `samples` and in the same
        /// order, or `-` where a sample does not loop. In output samples, which is
        /// what the extractor writes -- the font's own indices are at its rate,
        /// not ours.
        /// </summary>
        [XmlAttribute("loops")]
        [DefaultValue("")]
        public string Loops = "";

        /// <summary>Seconds to fall to silence. Modal and drum voices.</summary>
        [XmlAttribute("decay")]
        [DefaultValue(2f)]
        public float Decay = 2f;

        /// <summary>How much brighter the partials start than they end.</summary>
        [XmlAttribute("brightness")]
        [DefaultValue(0.5f)]
        public float Brightness = 0.5f;

        /// <summary>Partial spacing. 1 is harmonic; above it, increasingly metallic.</summary>
        [XmlAttribute("inharmonicity")]
        [DefaultValue(1f)]
        public float Inharmonicity = 1f;

        /// <summary>Noise against tone, 0 to 1. A crash is near 1, a kick near 0.</summary>
        [XmlAttribute("noise")]
        [DefaultValue(0.5f)]
        public float Noise = 0.5f;

        /// <summary>
        /// FM: the modulator's frequency as a multiple of the carrier's. A whole
        /// number is a harmonic tone, anything else a bell.
        /// </summary>
        [XmlAttribute("ratio")]
        [DefaultValue(1f)]
        public float Ratio = 1f;

        /// <summary>FM: how far the modulator bends the carrier's phase, in
        /// radians, at the start of the note. `brightness` is the fraction of it
        /// left once the note has settled.</summary>
        [XmlAttribute("index")]
        [DefaultValue(2f)]
        public float Index = 2f;

        /// <summary>
        /// FM: what this type is turned down by, so that seven timbres of wildly
        /// different spectral density come out at one loudness. Measured rather
        /// than guessed -- see the table in Synth.xml -- because an operator pair
        /// at index 5 spreads its energy over a dozen partials and one at index 1
        /// puts it all in the fundamental.
        /// </summary>
        [XmlAttribute("level")]
        [DefaultValue(1f)]
        public float Level = 1f;

        /// <summary>FM: how much of the modulator's own output goes back into it,
        /// 0 to 1. A little takes a sine towards a saw.</summary>
        [XmlAttribute("feedback")]
        [DefaultValue(0f)]
        public float Feedback = 0f;

        /// <summary>Envelope, in seconds, for the sampler.</summary>
        [XmlAttribute("attack")]
        [DefaultValue(0.002f)]
        public float Attack = 0.002f;

        [XmlAttribute("release")]
        [DefaultValue(0.25f)]
        public float Release = 0.25f;

        /// <summary>
        /// Letting the key go damps the note, over <see cref="Release"/>. True of a
        /// piano, whose dampers fall back on the strings, and of anything bowed or
        /// blown, which stops when the player does. False of a guitar: taking your
        /// hand off a plucked string does not stop it.
        /// </summary>
        [XmlAttribute("damped")]
        [DefaultValue(false)]
        public bool Damped = false;

        /// <summary>
        /// The loop is a sustain: the note goes on for as long as the key is down,
        /// rather than fading through the loop over <see cref="Decay"/>.
        ///
        /// This is the difference between a bow and a hammer. Both kinds of sample
        /// carry loop points -- most of the font's are only a few milliseconds long
        /// -- and what separates them is whether the instrument puts energy in
        /// continuously. A violin does; a piano does not, and neither does a guitar,
        /// so their loops are what the note rings on with while it dies.
        /// </summary>
        [XmlAttribute("holds")]
        [DefaultValue(false)]
        public bool Holds = false;

        // ---- set by the block's controls, not by the XML --------------------
        //
        // These are what the Extras resolve to. They are on the type because a
        // voice is handed one object and nothing else, and because what each
        // means is per-engine: Damping is a palm on a guitar string and a mute in
        // a trumpet's bell, and both come out as the same filter.

        /// <summary>Amplitude wobble, 0 to 1. A vibraphone's motor.</summary>
        [XmlIgnore]
        public float Tremolo = 0f;

        /// <summary>Pitch wobble, 0 to 1. Roughly +/-50 cents at full.</summary>
        [XmlIgnore]
        public float Vibrato = 0f;

        /// <summary>Low-pass, 0 to 1. Palm mute, brass mute.</summary>
        [XmlIgnore]
        public float Damping = 0f;

        /// <summary>Breath or string noise mixed in with the note, 0 to 1.</summary>
        [XmlIgnore]
        public float Edge = 0f;

        /// <summary>Comb depth, 0 to 1. Where a string is plucked.</summary>
        [XmlIgnore]
        public float Comb = 0f;

        /// <summary>Struck rather than bowed or blown: the loop stops being a
        /// sustain and becomes a short ring-out. Pizzicato, palm mute.</summary>
        [XmlIgnore]
        public bool Struck = false;
    }

    /// <summary>
    /// A control the block adds to its mapper. `kind` is "toggle" or "slider";
    /// the instrument reads it back by `key`.
    /// </summary>
    [Serializable]
    public class ExtraControl : Element
    {
        [XmlAttribute("kind")]
        [DefaultValue("slider")]
        public string Kind = "slider";

        [XmlAttribute("key")]
        public string Key;

        [XmlAttribute("name")]
        public string Name;

        [XmlAttribute("min")]
        [DefaultValue(0f)]
        public float Min = 0f;

        [XmlAttribute("max")]
        [DefaultValue(1f)]
        public float Max = 1f;

        [XmlAttribute("default")]
        [DefaultValue(0.5f)]
        public float Default = 0.5f;

        /// <summary>
        /// The top of the part worth dragging through, where that is less than
        /// <see cref="Max"/>. A time in seconds will take almost anything -- a
        /// release of half a minute is a long fade and nothing worse -- but a handle
        /// that had to cover all of it could not be set to the half-second anybody
        /// wants, so the panel's box takes the rest.
        ///
        /// Nought, the default, means the handle covers the whole range.
        /// </summary>
        [XmlAttribute("dragMax")]
        [DefaultValue(0f)]
        public float DragMax = 0f;
    }
}
