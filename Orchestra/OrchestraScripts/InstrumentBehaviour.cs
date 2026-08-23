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

        /// <summary>Within this many metres the block is at full volume, as the
        /// source's own minDistance used to say.</summary>
        private const float NearDistance = 1f;

        /// <summary>How long the panel's LISTEN holds the note down. Long enough to
        /// hear what a bowed note settles into, short enough not to be waited out.</summary>
        private const float AuditionSeconds = 1.2f;

        /// <summary>Seconds a damped string rings on for: a hand on it is what took
        /// the rest of the note away.</summary>
        private const float MutedRing = 0.25f;

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

        /// <summary>
        /// What every instrument block's AudioSource plays: one sample of silence,
        /// looped. It is never heard -- <see cref="OnAudioFilterRead"/> writes over
        /// it -- but a source has to be playing something for there to be a filter
        /// chain to write into.
        /// </summary>
        private static AudioClip silence;

        /// <summary>
        /// The listener the block is heard from. Re-found rather than held, because
        /// Besiege swaps cameras between building and running and the listener goes
        /// with them.
        /// </summary>
        private AudioListener ear;

        private Voice[] modal;
        private Voice[] drums;
        private SamplerVoice[] samplers;
        private float[] mix;
        private float[] extraValues;

        // Game thread writes, audio thread reads. Primitives only.
        private volatile int wantType;
        private volatile float wantNote = 60f;
        private volatile float wantVolume = 0.7f;

        // Where the block stands, as a gain in each ear. Worked out on the game
        // thread by Place, because a transform may not be read from the audio one.
        private volatile float wantLeft = 1f;
        private volatile float wantRight = 1f;

        // Audio thread only: the gains the last buffer ended on, so the next one
        // slides onto the new pair rather than stepping onto it.
        private float heldLeft = 1f;
        private float heldRight = 1f;

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

        /// <summary>Unscaled time the panel's audition releases at, or 0 for none.</summary>
        private float auditionUntil;

        /// <summary>How much bigger the instrument gets at the top of a note.</summary>
        private const float SwellDepth = 0.12f;

        /// <summary>Seconds to grow, and seconds to settle back. Short and not
        /// equal: a struck thing moves at once and comes back at its leisure, and a
        /// swell slow enough to notice as an animation reads as lag.</summary>
        private const float SwellRise = 0.05f;
        private const float SwellFall = 0.22f;

        /// <summary>What the block shows, and the scale each part was built at.</summary>
        private Transform[] visuals;
        private Vector3[] visualScales;

        /// <summary>Seconds into the swell, or -1 when the block is sitting still.
        /// Nothing writes a scale while it is -1, so a block nobody is playing is a
        /// block this code does not touch.</summary>
        private float swellAt = -1f;

        /// <summary>The audio thread saying it has not reached silence yet, so the
        /// source is held up for a note's release rather than cut off at the gate.</summary>
        private volatile bool sounding;

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

        /// <summary>
        /// What the block is called -- "Piano", "Brass" -- for the panel's title,
        /// as its own XML declares. See OrchestraModule.Family for why it is not
        /// asked of the game.
        /// </summary>
        public string BlockName
        {
            get { return Module.Family == null ? "" : Module.Family; }
        }

        /// <summary>True while the panel's LISTEN is holding a note down.</summary>
        public bool IsAuditioning { get { return auditionUntil > 0f; } }

        /// <summary>
        /// The panel's LISTEN: plays the block's note where it stands, with the
        /// settings as they are, while the machine is being built.
        ///
        /// A run owns the block, so this does nothing during one -- the key is what
        /// plays it there. Pressed again while a note is sounding it retriggers,
        /// which is what a key does and what auditioning a setting wants.
        /// </summary>
        public void Audition()
        {
            if (source == null || StatMaster.levelSimulating)
            {
                return;
            }
            PushSettings();
            Place();
            if (!source.isPlaying)
            {
                heldLeft = wantLeft;
                heldRight = wantRight;
                source.Play();
            }
            Push(1);
            auditionUntil = Time.unscaledTime + AuditionSeconds;
        }

        /// <summary>Lets an audition go, leaving the note its release.</summary>
        public void StopAudition()
        {
            if (auditionUntil <= 0f)
            {
                return;
            }
            auditionUntil = 0f;
            Push(0);
        }

        /// <summary>
        /// The one rule outside a run: the source plays while the panel is
        /// auditioning or a voice is still sounding, and is stopped otherwise.
        ///
        /// Re-checked every frame rather than switched from the callbacks that change
        /// it, because a simulation runs on a *clone* of the machine: OnSimulateStart
        /// and OnSimulateStop land on that copy, never on the block the panel edits.
        /// The clone takes the early return here and keeps the source those two gave
        /// it.
        /// </summary>
        private void Update()
        {
            if (source == null)
            {
                return;
            }
            if (StatMaster.levelSimulating)
            {
                // A run owns the block: OnSimulateStart and OnSimulateStop hold the
                // source there, and nothing here may fight them. An audition caught
                // by the start of a run is let go rather than dropped, so its note
                // releases instead of hanging. The clone never had one to let go.
                StopAudition();
                return;
            }

            if (auditionUntil > 0f)
            {
                // So a slider moved while the note rings is heard as it moves.
                PushSettings();
                Place();
                if (Time.unscaledTime >= auditionUntil)
                {
                    StopAudition();
                }
            }

            // The source outlives the gate: releasing the note and stopping the
            // source in the same breath is what would cut the release off. The
            // sounding term only *keeps* a playing source up -- a stale one, left
            // true by the last buffer of a run that has since been stopped, must not
            // be able to start it again.
            bool wanted = auditionUntil > 0f || (sounding && source.isPlaying);
            if (wanted != source.isPlaying)
            {
                if (wanted) { source.Play(); } else { source.Stop(); }
            }
            if (!source.isPlaying)
            {
                // Nothing is driving the audio callback now, so it cannot clear this
                // itself.
                sounding = false;
            }
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
            // Sized for the largest buffer Unity asks a filter for, so the audio
            // thread never has to grow it under a running note.
            mix = new float[4096];
        }

        /// <summary>
        /// A 2D source playing silence, whose filter chain is where the notes are
        /// written -- see <see cref="OnAudioFilterRead"/>.
        ///
        /// The obvious alternative is a streaming clip with a PCM reader callback,
        /// which is fed *before* Unity's 3D stage and so gets distance, doppler and
        /// panning for free. It costs the stream's read-ahead: that callback runs
        /// well before the samples it fills are heard, so a note queued by a keypress
        /// is rendered into audio that does not reach the speakers for some way yet,
        /// and the block answers the key late. A filter runs in the mixer, on the
        /// buffer about to be played.
        ///
        /// So the source is 2D and the block places itself; see <see cref="Place"/>.
        /// </summary>
        private void BuildSource()
        {
            source = GetComponent<AudioSource>();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
            }
            source.clip = Silence(sampleRate);
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        private static AudioClip Silence(int rate)
        {
            if (silence == null)
            {
                silence = AudioClip.Create("OrchestraSilence", 1, 1, rate, false);
                // Out of the scene and out of UnloadUnusedAssets' reach: the only
                // thing referencing it is this static field.
                silence.hideFlags = HideFlags.HideAndDontSave;
            }
            return silence;
        }

        public override void OnSimulateStart()
        {
            emulatedPressPending = false;
            emulatedDown = false;
            gateOpen = false;
            auditionUntil = 0f;
            queueRead = queueWrite;
            for (int i = 0; i < VoiceCount; i++)
            {
                modal[i].Active = false;
                drums[i].Active = false;
                samplers[i].Active = false;
            }
            PushSettings();
            Place();
            Settle();
            heldLeft = wantLeft;
            heldRight = wantRight;
            source.Play();
        }

        public override void OnSimulateStop()
        {
            gateOpen = false;
            sounding = false;
            Settle();
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
            Place();

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

            Swell(Time.deltaTime);
        }

        private void PushSettings()
        {
            wantType = TypeMenu.Value;
            wantNote = NoteSlider.Value;
            wantVolume = VolumeSlider.Value;

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

        /// <summary>
        /// How loud the block is in each ear, from where it stands relative to the
        /// listener. Unity's job normally, but its 3D stage runs before the filter
        /// that makes the sound, so the block does it itself.
        ///
        /// The falloff is the one the source used to be given: full volume within a
        /// metre, silent at RANGE, straight line between. Panned by how far round
        /// the listener the block sits. Doppler is the one thing not reproduced --
        /// it was Unity's resampling, and there is nothing here to resample.
        ///
        /// Game thread only: a transform may not be touched from the audio thread.
        /// </summary>
        private void Place()
        {
            if (ear == null || !ear.isActiveAndEnabled)
            {
                // Besiege swaps cameras between building and running, and the
                // listener goes with them, so a held one goes stale rather than null.
                ear = (AudioListener)UnityEngine.Object.FindObjectOfType(typeof(AudioListener));
                if (ear == null)
                {
                    wantLeft = 1f;
                    wantRight = 1f;
                    return;
                }
            }

            Transform head = ear.transform;
            Vector3 delta = transform.position - head.position;
            float distance = delta.magnitude;

            float far = RangeSlider.Value;
            if (far < NearDistance + 0.01f)
            {
                far = NearDistance + 0.01f;
            }
            float gain;
            if (distance <= NearDistance) { gain = 1f; }
            else if (distance >= far) { gain = 0f; }
            else { gain = (far - distance) / (far - NearDistance); }

            // -1 hard left, +1 hard right. Each ear keeps full gain until the block
            // crosses to the other side, so a block straight ahead is as loud as it
            // was before any of this.
            float pan = 0f;
            if (distance > 0.001f)
            {
                pan = Vector3.Dot(head.right, delta / distance);
            }
            wantLeft = gain * Mathf.Min(1f, 1f - pan);
            wantRight = gain * Mathf.Min(1f, 1f + pan);
        }

        // ---- the block moving when it is played -------------------------------

        /// <summary>
        /// The block's visible parts, and the scale each was built at.
        ///
        /// They hang *off* the block rather than on it. The XML's &lt;Mesh&gt; carries
        /// its own position and scale, which have to live on something that is not
        /// the block's own transform -- that one is the physics body, and the
        /// colliders are placed against it. So a swell moves what is seen and
        /// nothing that is touched: a machine does not become springy because it is
        /// playing.
        ///
        /// The block keeps the list itself, in its visual controller, which is the
        /// one place that knows what a block looks like -- a search of the children
        /// is the fallback, for a block whose controller a run has taken away.
        ///
        /// Read on the first note rather than in SafeAwake: a simulation runs on a
        /// clone, and it is the clone's own parts that are on screen.
        /// </summary>
        private void FindVisuals()
        {
            List<Transform> parts = new List<Transform>();
            MeshRenderer[] found = null;
            BlockVisualController controller =
                BlockBehaviour == null ? null : BlockBehaviour.VisualController;
            if (controller != null)
            {
                found = controller.renderers;
            }
            if (found == null || found.Length == 0)
            {
                found = GetComponentsInChildren<MeshRenderer>(true);
            }
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].transform != transform)
                {
                    parts.Add(found[i].transform);
                }
            }
            if (parts.Count == 0)
            {
                // Every renderer this block has is on the block's own transform,
                // which is the physics body: growing that would grow the colliders
                // with it. The note still plays; it just does not show.
                Log.Warn("Orchestra: " + Module.Family + " has no visual of its own to swell.");
            }
            visuals = parts.ToArray();
            visualScales = new Vector3[visuals.Length];
            for (int i = 0; i < visuals.Length; i++)
            {
                visualScales[i] = visuals[i].localScale;
            }
        }

        /// <summary>
        /// Starts the swell over, so a block struck twice in quick succession moves
        /// twice rather than halfway. Only during a run: outside one the panel's
        /// LISTEN plays the same note through the same queue, and nothing is
        /// advancing the swell there -- a block left grown would stay grown.
        /// </summary>
        private void Strike()
        {
            if (!StatMaster.levelSimulating)
            {
                return;
            }
            swellAt = 0f;
        }

        /// <summary>
        /// Grows the instrument and lets it back down, once a frame while a note is
        /// being shown. On game time rather than unscaled, so a machine watched in
        /// slow motion moves in slow motion with the rest of it.
        /// </summary>
        private void Swell(float step)
        {
            if (swellAt < 0f)
            {
                return;
            }
            if (visuals == null)
            {
                FindVisuals();
            }
            swellAt += step;

            float shape;
            if (swellAt < SwellRise)
            {
                shape = swellAt / SwellRise;
            }
            else
            {
                float fallen = (swellAt - SwellRise) / SwellFall;
                shape = fallen >= 1f ? 0f : (1f - fallen) * (1f - fallen);
                if (shape <= 0f)
                {
                    // Back where it started. The scale is still written this once,
                    // so the block lands exactly on the size it was built at rather
                    // than a fraction above it.
                    swellAt = -1f;
                }
            }

            float size = 1f + SwellDepth * shape;
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] != null)
                {
                    visuals[i].localScale = visualScales[i] * size;
                }
            }
        }

        /// <summary>Puts every part back to the size it was built at.</summary>
        private void Settle()
        {
            swellAt = -1f;
            if (visuals == null)
            {
                return;
            }
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] != null)
                {
                    visuals[i].localScale = visualScales[i];
                }
            }
        }

        /// <summary>1 is a note on, 0 a note off. Dropped if the queue is full.</summary>
        private void Push(int message)
        {
            if (message == 1)
            {
                Strike();
            }
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
        /// Renders the block into the buffer the mixer is about to play. Runs on
        /// Unity's audio thread: no Unity calls, no allocation, no locks.
        ///
        /// The queue is drained at the head of the buffer, so a key pressed this
        /// frame is heard in the next block of samples rather than after whatever a
        /// streaming clip had already read ahead.
        ///
        /// The source plays silence and is 2D, so this writes over the buffer rather
        /// than adding to it, and applies the block's own placement while it does.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0)
            {
                return;
            }
            int frames = data.Length / channels;
            if (mix == null || mix.Length < frames)
            {
                // Sized for the largest buffer Unity asks for, in BuildVoices; this
                // is the standing order for an unusual one, and costs a collection
                // exactly once.
                mix = new float[frames];
            }
            for (int i = 0; i < frames; i++)
            {
                mix[i] = 0f;
            }

            if (Module.Types == null || Module.Types.Length == 0)
            {
                sounding = false;
                Silent(data, frames, channels);
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
            bool live = false;
            for (int v = 0; v < pool.Length; v++)
            {
                if (!pool[v].Active)
                {
                    continue;
                }
                pool[v].Age++;
                pool[v].Render(mix, frames);
                // Read after rendering: a voice that reached silence in this buffer
                // has just switched itself off, and the source can go with it.
                live = live || pool[v].Active;
            }
            sounding = live;

            // The placement is a frame old and moves in steps; slide onto it across
            // the buffer, or a turning camera is heard as a staircase.
            float leftTo = wantLeft;
            float rightTo = wantRight;
            float left = heldLeft;
            float right = heldRight;
            float leftStep = (leftTo - left) / frames;
            float rightStep = (rightTo - right) / frames;
            float volume = wantVolume;

            for (int i = 0; i < frames; i++)
            {
                float s = mix[i] * volume;
                // The tanh knee keeps eight voices at once from clipping hard.
                if (s > 0.7f || s < -0.7f)
                {
                    s = s > 0f ? 0.7f + 0.3f * (1f - 1f / (1f + (s - 0.7f) * 3f))
                               : -0.7f - 0.3f * (1f - 1f / (1f - (s + 0.7f) * 3f));
                }

                left += leftStep;
                right += rightStep;

                int at = i * channels;
                if (channels == 1)
                {
                    data[at] = s * (left + right) * 0.5f;
                    continue;
                }
                data[at] = s * left;
                data[at + 1] = s * right;
                for (int c = 2; c < channels; c++)
                {
                    // Anything past the front pair gets the mono sum, which is what
                    // a centre or a rear channel should hear from one small block.
                    data[at + c] = s * (left + right) * 0.5f;
                }
            }
            heldLeft = leftTo;
            heldRight = rightTo;
        }

        /// <summary>Leaves the mixer's buffer empty. The source's own clip is
        /// silence, so this is only about not passing anything else on.</summary>
        private static void Silent(float[] data, int frames, int channels)
        {
            int n = frames * channels;
            for (int i = 0; i < n; i++)
            {
                data[i] = 0f;
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
            scratch.Damped = type.Damped;
            scratch.Holds = type.Holds;
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
                    // Pedal down: the dampers stay off the strings, so the key no
                    // longer stops the note -- it rings on and dies by itself.
                    scratch.Damped = false;
                }

                // Pizzicato: the recording is bowed, so this drops the loop and
                // cuts the tail rather than pretending to be a different sample.
                if (iPizz >= 0 && Extra(iPizz, 0f) > 0.5f)
                {
                    scratch.Struck = true;
                    scratch.Release = 0.12f;
                    scratch.Attack = 0.001f;
                    scratch.Comb = 0.5f;
                    scratch.Decay = MutedRing;
                }

                // A palm on the strings and a mute in a bell are the same thing to
                // a filter: everything above the fundamental goes.
                if (iMute >= 0 && Extra(iMute, 0f) > 0.5f)
                {
                    scratch.Damping = 0.7f;
                    if (type.Holds)
                    {
                        scratch.Edge = 0.05f;   // a muted horn still buzzes
                    }
                    else
                    {
                        scratch.Struck = true;  // palm mute also kills the ring
                        scratch.Release = 0.15f;
                        scratch.Decay = MutedRing;
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
