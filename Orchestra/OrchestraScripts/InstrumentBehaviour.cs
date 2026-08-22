using System;
using System.Collections.Generic;
using Modding;
using Modding.Modules;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// Every instrument block. What separates a piano from a cymbal is declared in
    /// the block XML and read through <see cref="OrchestraModule"/>; this drives
    /// all of them.
    ///
    /// The block plays one note, on one key, so a tune is a row of blocks each on
    /// its own variable. That is not a simplification: Besiege binds variables to
    /// `MKey` and to nothing else, so a note slider could never be automated.
    /// Polyphony is still worth having -- it is what stops a retrigger cutting the
    /// note already ringing.
    /// </summary>
    public class InstrumentBehaviour : BlockModuleBehaviour<OrchestraModule>
    {
        private const int VoiceCount = 8;
        private const int QueueSize = 32;

        private MKey PlayKey;
        private MMenu TypeMenu;
        private MSlider NoteSlider;
        private MSlider VolumeSlider;
        private MSlider RangeSlider;
        private MToggle PushToggle;

        private readonly List<MToggle> extraToggles = new List<MToggle>();
        private readonly List<MSlider> extraSliders = new List<MSlider>();
        private readonly List<string> extraKeys = new List<string>();

        // Indices into extraValues, resolved once. -1 where the block does not
        // declare that control, which is most of them for most blocks.
        private int iTune = -1, iDecay = -1, iDamp = -1, iSize = -1, iOpen = -1;
        private int iHard = -1, iMotor = -1, iSustain = -1, iRelease = -1;
        private int iVibrato = -1, iMute = -1, iPizz = -1, iBreath = -1;
        private int iSlap = -1, iPluck = -1, iAttack = -1;

        /// <summary>
        /// The type actually handed to a voice: the XML entry with this block's
        /// extras folded in. One instance, reused, because note-on happens on the
        /// audio thread where allocation is a click.
        /// </summary>
        private InstrumentType scratch = new InstrumentType();

        private AudioSource source;
        private SampleBank[] banks;
        private int sampleRate;

        private Voice[] modal;
        private Voice[] drums;
        private SamplerVoice[] samplers;
        private float[] mix;
        private float[] extraValues;

        // Game thread writes, audio thread reads. Primitives only.
        private volatile int wantType;
        private volatile float wantNote = 60f;
        private volatile float wantVolume = 0.7f;

        // Single producer, single consumer. The game thread pushes note-ons and
        // note-offs; the audio thread drains them at the head of each buffer, so a
        // press is heard within one buffer rather than on the next frame.
        private readonly int[] queue = new int[QueueSize];
        private volatile int queueWrite;
        private volatile int queueRead;

        // Latched by Besiege's emulation pass, consumed by the next update.
        private bool emulatedPressPending;
        private bool emulatedDown;
        private bool gateOpen;

        public override void SafeAwake()
        {
            sampleRate = AudioSettings.outputSampleRate;

            PlayKey = AddKey("Play", "Activate", KeyCode.N);

            List<string> names = new List<string>();
            if (Module.Types != null)
            {
                for (int i = 0; i < Module.Types.Length; i++)
                {
                    names.Add(Module.Types[i].Name);
                }
            }
            if (names.Count == 0)
            {
                names.Add("None");
            }
            TypeMenu = AddMenu("TypeKey", 0, names, false);

            NoteSlider = AddSlider("Note", "NoteKey", 60f, 21f, 108f);
            VolumeSlider = AddSlider("Volume", "VolumeKey", 0.7f, 0f, 1f);
            RangeSlider = AddSlider("Range", "RangeKey", 120f, 5f, 500f);
            PushToggle = AddToggle("Toggle", "ToggleKey", false);

            if (Module.Extras != null)
            {
                for (int i = 0; i < Module.Extras.Length; i++)
                {
                    ExtraControl e = Module.Extras[i];
                    if (e.Kind == "toggle")
                    {
                        extraToggles.Add(AddToggle(e.Name, e.Key, e.Default > 0.5f));
                    }
                    else
                    {
                        extraSliders.Add(AddSlider(e.Name, e.Key, e.Default, e.Min, e.Max));
                    }
                }

                // Sliders are stored first, then toggles, so the key list is built
                // in that same order rather than declaration order.
                for (int i = 0; i < Module.Extras.Length; i++)
                {
                    if (Module.Extras[i].Kind != "toggle") { extraKeys.Add(Module.Extras[i].Key); }
                }
                for (int i = 0; i < Module.Extras.Length; i++)
                {
                    if (Module.Extras[i].Kind == "toggle") { extraKeys.Add(Module.Extras[i].Key); }
                }
            }
            extraValues = new float[extraSliders.Count + extraToggles.Count];

            iTune = IndexOf("TuneKey");
            iDecay = IndexOf("DecayKey");
            iDamp = IndexOf("DampKey");
            iSize = IndexOf("SizeKey");
            iOpen = IndexOf("OpenKey");
            iHard = IndexOf("HardKey");
            iMotor = IndexOf("MotorKey");
            iSustain = IndexOf("SustainKey");
            iRelease = IndexOf("ReleaseKey");
            iVibrato = IndexOf("VibratoKey");
            iMute = IndexOf("MuteKey");
            iPizz = IndexOf("PizzKey");
            iBreath = IndexOf("BreathKey");
            iSlap = IndexOf("SlapKey");
            iPluck = IndexOf("PluckKey");
            iAttack = IndexOf("AttackKey");

            LoadBanks();
            BuildVoices();
            BuildSource();
        }

        private int IndexOf(string key)
        {
            for (int i = 0; i < extraKeys.Count; i++)
            {
                if (extraKeys[i] == key)
                {
                    return i;
                }
            }
            return -1;
        }

        private float Extra(int index, float fallback)
        {
            return index >= 0 && index < extraValues.Length ? extraValues[index] : fallback;
        }

        // ---- what the panel needs -------------------------------------------
        //
        // The panel builds itself from these rather than from a list of its own, so
        // declaring an Extra in a block's XML is enough to give it a row.

        public MMenu Types { get { return TypeMenu; } }
        public MSlider Note { get { return NoteSlider; } }
        public MSlider Volume { get { return VolumeSlider; } }
        public MSlider Range { get { return RangeSlider; } }
        public MToggle Latch { get { return PushToggle; } }
        public List<MSlider> ExtraSliders { get { return extraSliders; } }
        public List<MToggle> ExtraToggles { get { return extraToggles; } }

        public int TypeCount
        {
            get { return Module.Types != null ? Module.Types.Length : 0; }
        }

        public int SelectedType
        {
            get
            {
                int v = TypeMenu.Value;
                return v >= 0 && v < TypeCount ? v : 0;
            }
        }

        public string TypeName(int index)
        {
            return index >= 0 && index < TypeCount ? Module.Types[index].Name : "None";
        }

        public string SelectedTypeName
        {
            get { return TypeCount > 0 ? TypeName(SelectedType) : "Instrument"; }
        }

        /// <summary>The keys bound to Play, for the panel's read-only line.</summary>
        public string KeyDescription
        {
            get
            {
                if (PlayKey.KeysCount == 0)
                {
                    return "unbound";
                }
                string text = "";
                for (int i = 0; i < PlayKey.KeysCount; i++)
                {
                    if (i > 0)
                    {
                        text += " / ";
                    }
                    text += PlayKey.GetKey(i).ToString();
                }
                return text;
            }
        }

        /// <summary>
        /// Reads every sampled note into managed memory, once, on the game thread.
        /// AudioClip.GetData cannot be called from the audio callback, and
        /// ModResource is the only way in at all: System.IO is blacklisted.
        /// </summary>
        private void LoadBanks()
        {
            int count = Module.Types != null ? Module.Types.Length : 0;
            banks = new SampleBank[count];
            for (int i = 0; i < count; i++)
            {
                if (Module.Types[i].Engine == "sampler")
                {
                    banks[i] = SampleBank.Load(Module.Types[i].Samples, Module.Types[i].Loops);
                }
            }
        }

        private void BuildVoices()
        {
            modal = new Voice[VoiceCount];
            drums = new Voice[VoiceCount];
            samplers = new SamplerVoice[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                modal[i] = new ModalVoice(sampleRate);
                drums[i] = new DrumVoice(sampleRate);
                samplers[i] = new SamplerVoice(sampleRate);
            }
            mix = new float[2048];
        }

        /// <summary>
        /// A streaming mono clip fed by <see cref="ReadPcm"/>, on a fully 3D
        /// source.
        ///
        /// This is what makes the block a point source. The alternative,
        /// OnAudioFilterRead, sits in the source's filter chain and hands back a
        /// buffer that is already spatialised, so writing into it destroys the
        /// panning and the mod has to pan by hand. A PCM reader callback is pulled
        /// *before* spatialisation: Unity takes mono samples and then applies
        /// distance, doppler and stereo position itself.
        /// </summary>
        private void BuildSource()
        {
            source = GetComponent<AudioSource>();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
            }
            AudioClip clip = AudioClip.Create("OrchestraVoice", sampleRate, 1, sampleRate,
                true, new AudioClip.PCMReaderCallback(ReadPcm));
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.dopplerLevel = 0.5f;
        }

        public override void OnSimulateStart()
        {
            emulatedPressPending = false;
            emulatedDown = false;
            gateOpen = false;
            queueRead = queueWrite;
            for (int i = 0; i < VoiceCount; i++)
            {
                modal[i].Active = false;
                drums[i].Active = false;
                samplers[i].Active = false;
            }
            PushSettings();
            source.Play();
        }

        public override void OnSimulateStop()
        {
            gateOpen = false;
            source.Stop();
        }

        /// <summary>
        /// Besiege's own emulation pass, once per fixed step, after every emulator
        /// and variable has raised its count. The edges exist for exactly that one
        /// step, so reading them from Update -- which may run twice or not at all
        /// within it -- reports presses that never happened, or misses them.
        /// </summary>
        public override void KeyEmulationUpdate()
        {
            if (PlayKey.EmulationPressed())
            {
                emulatedPressPending = true;
            }
            emulatedDown = PlayKey.EmulationHeld(true);
        }

        public override void SimulateUpdateAlways()
        {
            PushSettings();

            bool pressed = PlayKey.IsPressed || emulatedPressPending;
            bool held = PlayKey.IsHeld || emulatedDown;
            emulatedPressPending = false;

            if (PushToggle.IsActive)
            {
                if (pressed)
                {
                    gateOpen = !gateOpen;
                    Push(gateOpen ? 1 : 0);
                }
            }
            else
            {
                if (pressed)
                {
                    Push(1);
                }
                if (gateOpen && !held)
                {
                    Push(0);
                }
                gateOpen = held;
            }
        }

        private void PushSettings()
        {
            wantType = TypeMenu.Value;
            wantNote = NoteSlider.Value;
            wantVolume = VolumeSlider.Value;
            source.maxDistance = RangeSlider.Value;

            int k = 0;
            for (int i = 0; i < extraSliders.Count; i++)
            {
                extraValues[k++] = extraSliders[i].Value;
            }
            for (int i = 0; i < extraToggles.Count; i++)
            {
                extraValues[k++] = extraToggles[i].IsActive ? 1f : 0f;
            }
        }

        /// <summary>1 is a note on, 0 a note off. Dropped if the queue is full.</summary>
        private void Push(int message)
        {
            int next = (queueWrite + 1) % QueueSize;
            if (next == queueRead)
            {
                return;
            }
            queue[queueWrite] = message;
            queueWrite = next;
        }

        // ---- audio thread ----------------------------------------------------

        /// <summary>
        /// Fills the streaming clip. Runs on Unity's audio thread: no Unity calls,
        /// no allocation, no locks.
        /// </summary>
        private void ReadPcm(float[] data)
        {
            int frames = data.Length;
            for (int i = 0; i < frames; i++)
            {
                data[i] = 0f;
            }
            if (mix == null || Module.Types == null || Module.Types.Length == 0)
            {
                return;
            }

            int typeIndex = wantType;
            if (typeIndex < 0 || typeIndex >= Module.Types.Length)
            {
                typeIndex = 0;
            }
            InstrumentType type = Module.Types[typeIndex];

            while (queueRead != queueWrite)
            {
                int message = queue[queueRead];
                queueRead = (queueRead + 1) % QueueSize;
                if (message == 1)
                {
                    NoteOn(type, typeIndex);
                }
                else
                {
                    NoteOff();
                }
            }

            Voice[] pool = PoolFor(type);
            float volume = wantVolume;
            for (int v = 0; v < pool.Length; v++)
            {
                if (!pool[v].Active)
                {
                    continue;
                }
                pool[v].Age++;
                pool[v].Render(data, frames);
            }

            for (int i = 0; i < frames; i++)
            {
                float s = data[i] * volume;
                // The tanh knee keeps eight voices at once from clipping hard.
                if (s > 0.7f || s < -0.7f)
                {
                    s = s > 0f ? 0.7f + 0.3f * (1f - 1f / (1f + (s - 0.7f) * 3f))
                               : -0.7f - 0.3f * (1f - 1f / (1f - (s + 0.7f) * 3f));
                }
                data[i] = s;
            }
        }

        private Voice[] PoolFor(InstrumentType type)
        {
            if (type.Engine == "drum")
            {
                return drums;
            }
            if (type.Engine == "sampler")
            {
                return samplers;
            }
            return modal;
        }

        private void NoteOn(InstrumentType type, int typeIndex)
        {
            Voice[] pool = PoolFor(type);
            Voice pick = null;
            int oldest = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].Active)
                {
                    pick = pool[i];
                    break;
                }
                if (pool[i].Age > oldest)
                {
                    oldest = pool[i].Age;
                    pick = pool[i];
                }
            }
            if (pick == null)
            {
                return;
            }

            InstrumentType resolved = Resolve(type);
            float note = wantNote + Extra(iTune, 0f);

            if (type.Engine == "sampler")
            {
                SamplerVoice sv = (SamplerVoice)pick;
                if (banks == null || typeIndex >= banks.Length || banks[typeIndex] == null
                    || !sv.Prepare(banks[typeIndex], resolved, note))
                {
                    return;
                }
            }
            pick.Start(resolved, note, 1f, extraValues);
        }

        /// <summary>
        /// The XML type with this block's own controls applied. Each extra means
        /// something different per engine, which is the point of them: Size on a
        /// cymbal is a physical dimension, so it lengthens the decay *and* spreads
        /// the partials, while Hardness on a mallet only changes how bright the
        /// strike is.
        /// </summary>
        private InstrumentType Resolve(InstrumentType type)
        {
            scratch.Name = type.Name;
            scratch.Engine = type.Engine;
            scratch.Samples = type.Samples;
            scratch.Decay = type.Decay;
            scratch.Brightness = type.Brightness;
            scratch.Inharmonicity = type.Inharmonicity;
            scratch.Noise = type.Noise;
            scratch.PitchDrop = type.PitchDrop;
            scratch.Attack = type.Attack;
            scratch.Release = type.Release;
            scratch.Sustains = type.Sustains;
            scratch.Tremolo = 0f;
            scratch.Vibrato = 0f;
            scratch.Damping = 0f;
            scratch.Edge = 0f;
            scratch.Comb = 0f;
            scratch.Struck = false;

            if (type.Engine == "drum")
            {
                scratch.Decay = Extra(iDecay, type.Decay);
                // Damping shortens the ring and mutes the skin together, the way a
                // hand on a head does.
                float damp = Extra(iDamp, 0f);
                scratch.Decay *= 1f - damp * 0.8f;
                scratch.Noise = type.Noise * (1f - damp * 0.5f);
            }
            else if (type.Engine == "modal")
            {
                float size = Extra(iSize, 0.5f);
                // A bigger plate rings longer and lower, and its modes crowd together.
                scratch.Decay = type.Decay * (0.4f + size * 1.6f);
                scratch.Inharmonicity = type.Inharmonicity * (1.1f - size * 0.2f);

                float hard = Extra(iHard, -1f);
                if (hard >= 0f)
                {
                    scratch.Brightness = hard;
                }
                if (iOpen >= 0 && Extra(iOpen, 1f) < 0.5f)
                {
                    // Closed: choked to a tick, which is the whole difference
                    // between a hi-hat's two sounds.
                    scratch.Decay *= 0.12f;
                }
                scratch.Tremolo = Extra(iMotor, 0f);
            }
            else if (type.Engine == "sampler")
            {
                scratch.Release = Extra(iRelease, type.Release);
                scratch.Attack = Extra(iAttack, type.Attack);
                scratch.Vibrato = Extra(iVibrato, 0f);

                if (iSustain >= 0 && Extra(iSustain, 0f) > 0.5f)
                {
                    // Pedal down: the note is not released with the key.
                    scratch.Sustains = false;
                }

                // Pizzicato: the recording is bowed, so this drops the loop and
                // cuts the tail rather than pretending to be a different sample.
                if (iPizz >= 0 && Extra(iPizz, 0f) > 0.5f)
                {
                    scratch.Struck = true;
                    scratch.Release = 0.12f;
                    scratch.Attack = 0.001f;
                    scratch.Comb = 0.5f;
                }

                // A palm on the strings and a mute in a bell are the same thing to
                // a filter: everything above the fundamental goes.
                if (iMute >= 0 && Extra(iMute, 0f) > 0.5f)
                {
                    scratch.Damping = 0.7f;
                    if (type.Sustains)
                    {
                        scratch.Edge = 0.05f;   // a muted horn still buzzes
                    }
                    else
                    {
                        scratch.Struck = true;  // palm mute also kills the ring
                        scratch.Release = 0.15f;
                    }
                }

                scratch.Edge += Extra(iBreath, 0f) * 0.8f;
                scratch.Comb += Extra(iPluck, 0f);

                // Slap: a hard attack and a bright edge, which is mostly what the
                // technique sounds like against a fingered note.
                if (iSlap >= 0 && Extra(iSlap, 0f) > 0.5f)
                {
                    scratch.Attack = 0.0005f;
                    scratch.Edge += 0.25f;
                    scratch.Comb += 0.35f;
                }
            }
            return scratch;
        }

        private void NoteOff()
        {
            for (int i = 0; i < VoiceCount; i++)
            {
                if (modal[i].Active && modal[i].Held) { modal[i].Release(); }
                if (drums[i].Active && drums[i].Held) { drums[i].Release(); }
                if (samplers[i].Active && samplers[i].Held) { samplers[i].Release(); }
            }
        }
    }
}
