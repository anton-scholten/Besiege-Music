using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Modding.Modules;
// Safe to import here: this file has no UnityEngine.Vector3 to clash with the
// one Modding.Serialization declares.
using Modding.Serialization;

namespace OrchestraMod
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
    [XmlRoot("OrchestraMod")]
    public class OrchestraModule : BlockModule
    {
        /// <summary>The entries in the block's Type menu, in order.</summary>
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

        /// <summary>Semitones the body falls over the note. Drum voice.</summary>
        [XmlAttribute("pitchDrop")]
        [DefaultValue(0f)]
        public float PitchDrop = 0f;

        /// <summary>Envelope, in seconds, for the sampler.</summary>
        [XmlAttribute("attack")]
        [DefaultValue(0.002f)]
        public float Attack = 0.002f;

        [XmlAttribute("release")]
        [DefaultValue(0.25f)]
        public float Release = 0.25f;

        /// <summary>Sustaining instruments hold while the key is down.</summary>
        [XmlAttribute("sustains")]
        [DefaultValue(false)]
        public bool Sustains = false;

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

        /// <summary>Struck rather than held: no loop, and a short tail.</summary>
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
    }
}
