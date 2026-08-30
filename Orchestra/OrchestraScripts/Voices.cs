using System;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// One sounding note. Voices are pooled and never allocated while playing:
    /// everything here runs on the audio thread, where a collection is a click.
    ///
    /// Rendering *adds* into the buffer, so the allocator can simply mix.
    /// </summary>
    public abstract class Voice
    {
        public bool Active;
        public bool Held;
        public int Age;

        protected float rate;
        protected float level;

        protected Voice(int sampleRate)
        {
            rate = sampleRate;
        }

        public abstract void Start(InstrumentType type, float note, float velocity, float[] extras);
        public abstract void Render(float[] buffer, int frames);

        /// <summary>The key went up. Sustaining voices release; struck ones ignore it.</summary>
        public virtual void Release()
        {
            Held = false;
        }

        /// <summary>Hz for a MIDI note number, A440.</summary>
        protected static float Hz(float note)
        {
            return 440f * Mathf.Pow(2f, (note - 69f) / 12f);
        }
    }

    /// <summary>
    /// A bank of exponentially decaying sine partials, struck at once.
    ///
    /// This is how a cymbal, a bell or a vibraphone bar actually behaves: rigid
    /// metal has modes that are not harmonics, and the high ones die first, which
    /// is why a crash starts bright and ends as a hum. `inharmonicity` stretches
    /// the spacing -- 1 is a harmonic series, and by 2 the partials are dense and
    /// clangorous -- while `brightness` sets how much louder the top starts.
    ///
    /// Cheaper than a sample, and unlike one it answers to the block's controls:
    /// changing size or decay really changes the physics rather than picking a
    /// different recording.
    /// </summary>
    public class ModalVoice : Voice
    {
        private const int Partials = 24;

        // Each partial is a resonator rather than a call to Sin.
        //
        // `Mathf.Sin` once per partial per sample is twenty-four transcendentals a
        // sample, and eight voices of that measured at 44% of a core for one
        // block -- four blocks ringing at once was a core gone, which is what a
        // machine full of mallets and cymbals sounds like when the frame rate
        // drops. The coupled form ("magic circle") gives the same sine from two
        // multiplies and two adds, and cannot drift out: the pair (x, y) walks
        // round a circle, so its amplitude is bounded whatever the arithmetic
        // does, where the y[n] = k*y[n-1] - y[n-2] recurrence slowly grows.
        private readonly float[] sinPart = new float[Partials];
        private readonly float[] cosPart = new float[Partials];
        private readonly float[] turn = new float[Partials];
        private readonly float[] amp = new float[Partials];
        private readonly float[] damp = new float[Partials];

        private float noiseLevel;
        private float noiseDamp;
        private uint noiseState = 22222u;

        /// <summary>How many partials are still worth adding up. They are damped
        /// hardest at the top, so the bank shortens from there as a note dies --
        /// and a marimba is silent above its eighth partial almost at once. Cheaper
        /// than testing all twenty-four every sample and skipping the dead.</summary>
        private int live;

        // Vibraphone motor: rotating discs over the resonators, which is an
        // amplitude wobble rather than any change of pitch.
        private float tremoloDepth;
        private float tremoloPhase;
        private float tremoloStep;

        public ModalVoice(int sampleRate) : base(sampleRate) { }

        public override void Start(InstrumentType type, float note, float velocity, float[] extras)
        {
            float f0 = Hz(note);
            float decay = Mathf.Max(0.05f, type.Decay);
            float stretch = Mathf.Max(1f, type.Inharmonicity);
            float bright = Mathf.Clamp01(type.Brightness);

            for (int i = 0; i < Partials; i++)
            {
                // Stretched series: harmonic at 1, progressively metallic above it.
                float ratio = Mathf.Pow(i + 1f, stretch);
                float f = f0 * ratio;
                if (f > rate * 0.45f)
                {
                    amp[i] = 0f;
                    turn[i] = 0f;
                    continue;
                }
                // 2 sin(w/2) is the step round the circle for a partial at f.
                turn[i] = 2f * Mathf.Sin(Mathf.PI * f / rate);
                sinPart[i] = 0f;
                cosPart[i] = 1f;

                // Higher partials start louder with brightness, and always die first.
                float tilt = Mathf.Pow(i + 1f, -1.2f + bright);
                amp[i] = velocity * tilt / Partials * 6f;
                float partialDecay = decay / (1f + i * bright * 0.8f);
                damp[i] = Mathf.Exp(-1f / (partialDecay * rate));
            }

            live = Partials;
            noiseLevel = velocity * Mathf.Clamp01(type.Noise) * 0.5f;
            noiseDamp = Mathf.Exp(-1f / (Mathf.Max(0.01f, decay * 0.12f) * rate));

            tremoloDepth = Mathf.Clamp01(type.Tremolo);
            tremoloPhase = 0f;
            // Roughly 1 to 10 Hz, which is the range a real motor covers.
            tremoloStep = 2f * Mathf.PI * (1f + tremoloDepth * 9f) / rate;
            Active = true;
            Held = true;
            Age = 0;
        }

        public override void Render(float[] buffer, int frames)
        {
            // Once a buffer, not once a sample: the top of the bank is dead for
            // good once it is quiet, so the inner loop gets shorter as the note
            // dies rather than testing every partial forty-eight thousand times a
            // second to skip it.
            while (live > 0 && amp[live - 1] <= 0.000001f)
            {
                live--;
            }

            float peak = 0f;
            for (int n = 0; n < frames; n++)
            {
                float s = 0f;
                for (int i = 0; i < live; i++)
                {
                    if (amp[i] <= 0.000001f)
                    {
                        continue;
                    }
                    // One step round the circle: x leads, y is the sine.
                    cosPart[i] -= turn[i] * sinPart[i];
                    sinPart[i] += turn[i] * cosPart[i];
                    s += sinPart[i] * amp[i];
                    amp[i] *= damp[i];
                }

                if (noiseLevel > 0.000001f)
                {
                    // xorshift: cheap, and the audio thread cannot afford anything
                    // that might allocate or lock.
                    noiseState ^= noiseState << 13;
                    noiseState ^= noiseState >> 17;
                    noiseState ^= noiseState << 5;
                    s += ((int)(noiseState & 0xffff) - 32768) / 32768f * noiseLevel;
                    noiseLevel *= noiseDamp;
                }

                if (tremoloDepth > 0.001f)
                {
                    tremoloPhase += tremoloStep;
                    if (tremoloPhase > 6.2831853f)
                    {
                        tremoloPhase -= 6.2831853f;
                    }
                    s *= 1f - tremoloDepth * 0.5f * (1f - Mathf.Cos(tremoloPhase));
                }

                buffer[n] += s;
                float a = s < 0f ? -s : s;
                if (a > peak)
                {
                    peak = a;
                }
            }

            if (peak < 0.00002f)
            {
                Active = false;
            }
        }
    }

    /// <summary>
    /// Plays a recorded note, pitch-shifted to the note asked for.
    ///
    /// The clips arrive through ModResource -- the only route in, since System.IO
    /// is blacklisted -- and are read once into `float[]` with AudioClip.GetData.
    /// The key map picks the nearest recorded pitch so the shift stays small;
    /// stretching one sample across an instrument is what makes cheap samplers
    /// sound like chipmunks.
    ///
    /// Interpolation is cubic (Catmull-Rom): linear costs audible high end once a
    /// sample is shifted down.
    /// </summary>
    public class SamplerVoice : Voice
    {
        /// <summary>ln(1000): the exponent that takes a ring-out to -60 dB in the
        /// decay it is given, so `decay` reads as "seconds to silence".</summary>
        private const float ToSilence = 6.9077553f;

        private float[] data;
        private double position;
        private double increment;
        private double loopStart = -1.0;
        private double loopEnd = -1.0;

        // The ring-out: where the recording stops being played forwards, the window
        // it turns round in, and the fade that carries it away. See SampleBank.FindTail.
        private double stopAt;
        private double tailStart = -1.0;
        private double tailEnd = -1.0;
        private float tailGain = 1f;
        private float ring;
        private float ringDamp;
        private bool ringing;

        // Articulations. All of them are cheap per-sample work, which is what lets
        // eight voices run without a mixer thread.
        private float vibratoDepth;
        private float vibratoPhase;
        private float vibratoStep;

        // A vibraphone's motor: rotating discs over the resonator tubes, which is
        // an amplitude wobble and no change of pitch. The recording is of the motor
        // switched off -- a font samples one note, not one note per speed -- so the
        // block puts it back.
        private float tremoloDepth;
        private float tremoloPhase;
        private float tremoloStep;
        private float damping;
        private float lowpass;
        private float edge;

        /// <summary>How loud the recording is just now, and how fast that is allowed
        /// to fall -- a peak follower, so the noise <see cref="edge"/> adds is a
        /// proportion of the note rather than a fixed hiss over it.</summary>
        private float follow;
        private float followFall;

        /// <summary>The two poles that turn white noise into breath: what is left
        /// after the top is taken off, and the rumble taken off that.</summary>
        private float hiss;
        private float rumble;
        private uint noiseState = 1234567u;
        private float combDepth;
        private int combDelay;
        private readonly float[] comb = new float[1024];
        private int combWrite;
        private float attackPerSample;
        private float releasePerSample;
        private bool releasing;
        private bool damped;

        /// <summary>The loop is a sustain rather than something to fade through.</summary>
        private bool holds;
        private float velocity;

        public SamplerVoice(int sampleRate) : base(sampleRate) { }

        public bool Prepare(SampleBank bank, InstrumentType type, float note)
        {
            SampleBank.Entry entry = bank.Nearest(note);
            if (entry == null || entry.Data == null || entry.Data.Length < 4)
            {
                return false;
            }
            data = entry.Data;
            increment = Mathf.Pow(2f, (note - entry.Note) / 12f) * (entry.Rate / rate);
            loopStart = entry.LoopStart;
            loopEnd = entry.LoopEnd;
            tailStart = entry.TailStart;
            tailEnd = entry.TailEnd;
            tailGain = entry.TailGain;
            return true;
        }

        public override void Start(InstrumentType type, float note, float vel, float[] extras)
        {
            position = 0.0;
            velocity = vel;
            level = 0f;
            releasing = false;
            damped = type.Damped && !type.Struck;
            // Pizzicato and palm mute: the recording is of a bowed or open note, so
            // what was its sustain becomes a ring-out, and a short one -- the block
            // sets `decay` down to match. Nothing is dropped: a string plucked
            // rather than bowed is the same string.
            holds = type.Holds && !type.Struck;

            // Where the recording stops being played forwards: short of the fade the
            // extractor put on the end, if there is a ring-out to turn round in.
            ring = 1f;
            ringing = false;
            stopAt = tailEnd > tailStart ? tailEnd : data.Length - 3;
            // Decay is what the recording does not have: it is cut while the note is
            // still sounding, so this is how long the rest of that note takes to go.
            ringDamp = Mathf.Exp(-ToSilence / (Mathf.Max(0.05f, type.Decay) * rate));

            vibratoDepth = Mathf.Clamp01(type.Vibrato);
            vibratoPhase = 0f;
            // 4 to 7 Hz: slower reads as seasick, faster as a trill.
            vibratoStep = 2f * Mathf.PI * (4f + vibratoDepth * 3f) / rate;

            tremoloDepth = Mathf.Clamp01(type.Tremolo);
            tremoloPhase = 0f;
            // Roughly 1 to 10 Hz, as the modal engine's is: a real motor's range.
            tremoloStep = 2f * Mathf.PI * (1f + tremoloDepth * 9f) / rate;

            // Signed, not clamped to nothing: positive is a lowpass, negative the
            // tilt the other way. See the HARDNESS control, whose top half is the
            // second of those.
            damping = Mathf.Clamp(type.Damping, -1f, 1f);
            lowpass = 0f;
            edge = Mathf.Clamp01(type.Edge);
            follow = 0f;
            hiss = 0f;
            rumble = 0f;
            // About 80 ms to fall, which is slower than any note's waveform and
            // faster than any note's decay.
            followFall = 1f / Mathf.Max(1f, 0.08f * rate);

            combDepth = Mathf.Clamp01(type.Comb);
            // Shorter delay is nearer the bridge, which is thinner and more nasal.
            combDelay = 16 + (int)((1f - combDepth) * 300f);
            for (int i = 0; i < comb.Length; i++)
            {
                comb[i] = 0f;
            }
            combWrite = 0;
            attackPerSample = 1f / Mathf.Max(1f, type.Attack * rate);
            releasePerSample = 1f / Mathf.Max(1f, type.Release * rate);
            Active = true;
            Held = true;
            Age = 0;
        }

        public override void Release()
        {
            Held = false;
            if (damped)
            {
                releasing = true;
            }
        }

        public override void Render(float[] buffer, int frames)
        {
            int last = data.Length - 3;
            for (int n = 0; n < frames; n++)
            {
                // Sustaining instruments hold by looping the middle of the
                // recording, which is what the SoundFont's loop points mark: a
                // violin's bow stroke is a second of audio and a held note is that
                // second going round.
                if (loopEnd > loopStart && position >= loopEnd)
                {
                    position -= loopEnd - loopStart;
                    // A bow or a breath holds the note up round that loop. A hammer
                    // or a plectrum does not: the loop is what is left of the note,
                    // and it fades from here.
                    if (!holds)
                    {
                        ringing = true;
                    }
                }

                // A struck one has no such loop: it plays its recording and reaches
                // the end of it, which is where the cut was made rather than where
                // the note finished. From there the last few periods go round while
                // the ring fades, so a plucked string rings on instead of stopping.
                if (position >= stopAt)
                {
                    if (tailEnd <= tailStart)
                    {
                        Active = false;
                        return;
                    }
                    position -= tailEnd - tailStart;
                    ringing = true;
                    // The recording is quieter at the end of that window than at its
                    // start, so turning round would step the level back up. This is
                    // how much it fell, put back -- which also carries the note on
                    // decaying at the rate it was already decaying at.
                    ring *= tailGain;
                }
                if (ringing)
                {
                    ring *= ringDamp;
                    if (ring < 0.001f)
                    {
                        Active = false;
                        return;
                    }
                }

                int i = (int)position;
                if (i >= last || i < 0)
                {
                    Active = false;
                    return;
                }

                float t = (float)(position - i);
                // Catmull-Rom between p1 and p2, with p0 and p3 setting the slope.
                float p1 = data[i];
                float p2 = data[i + 1];
                float p3 = data[i + 2];
                float p0 = i > 0 ? data[i - 1] : p1;
                float s = p1 + 0.5f * t * (p2 - p0
                        + t * (2f * p0 - 5f * p1 + 4f * p2 - p3
                        + t * (3f * (p1 - p2) + p3 - p0)));

                if (releasing)
                {
                    level -= releasePerSample;
                    if (level <= 0f)
                    {
                        Active = false;
                        return;
                    }
                }
                else if (level < 1f)
                {
                    level += attackPerSample;
                    if (level > 1f)
                    {
                        level = 1f;
                    }
                }

                if (damping > 0.001f)
                {
                    // One pole. At full damping only the fundamental survives,
                    // which is what a palm on the strings or a mute in the bell
                    // actually leaves you.
                    float cutoff = 1f - damping * 0.93f;
                    lowpass += (s - lowpass) * cutoff;
                    s = lowpass;
                }
                else if (damping < -0.001f)
                {
                    // The other way: what is left after the same pole is the top of
                    // the note, so adding it back tilts the recording brighter. A
                    // harder beater cannot put partials into a recording that has
                    // none, but it can lean on the ones it has, and that is what a
                    // hard mallet against a soft one sounds like.
                    lowpass += (s - lowpass) * 0.15f;
                    s += (s - lowpass) * (-damping) * 2f;
                }

                if (edge > 0.001f)
                {
                    noiseState ^= noiseState << 13;
                    noiseState ^= noiseState >> 17;
                    noiseState ^= noiseState << 5;
                    float white = ((int)(noiseState & 0xffff) - 32768) / 32768f;

                    // Breath through a flute is air moving, not static: a band of
                    // noise rather than the whole spectrum. One pole takes the fizz
                    // off the top, a second takes the rumble off the bottom by being
                    // subtracted, and what is left is the part that sounds like a
                    // player. Twice the level, because a band is quieter than the
                    // whole of what it was cut from.
                    hiss += (white - hiss) * 0.35f;
                    rumble += (hiss - rumble) * 0.02f;
                    float air = (hiss - rumble) * 2f;

                    // Noise rides the note rather than sitting under it, at a level
                    // proportional to what the recording is doing just now.
                    //
                    // Flat, it was the same hiss whatever the note was worth -- and
                    // these recordings are loudest at the strike and much quieter in
                    // the window they ring out through. Measured on the basses, the
                    // noise Slap added was 3% of the attack and then 8 to 24% of the
                    // loop, which is a hiss that arrives as the note dies. In
                    // proportion it stays at the attack's own 3 to 5% throughout,
                    // which is a bright strike rather than a noisy tail.
                    //
                    // The follower is quick to rise, so an attack is not softened,
                    // and slow to fall, so it does not flutter with the waveform.
                    float amp = s < 0f ? -s : s;
                    if (amp > follow)
                    {
                        follow = amp;
                    }
                    else
                    {
                        follow += (amp - follow) * followFall;
                    }
                    // 1.1, so a recording at full scale gets what it always got.
                    float ride = follow * 1.1f;
                    if (ride > 1f)
                    {
                        ride = 1f;
                    }
                    s += air * edge * 0.12f * ride;
                }

                if (combDepth > 0.001f)
                {
                    // A plucked string has a notch where it was struck; a comb is
                    // the cheap way to put one in a recording that has none.
                    int read = combWrite - combDelay;
                    if (read < 0)
                    {
                        read += comb.Length;
                    }
                    float delayed = comb[read];
                    comb[combWrite] = s;
                    combWrite = (combWrite + 1) % comb.Length;
                    s -= delayed * combDepth * 0.6f;
                }

                if (tremoloDepth > 0.001f)
                {
                    tremoloPhase += tremoloStep;
                    if (tremoloPhase > 6.2831853f)
                    {
                        tremoloPhase -= 6.2831853f;
                    }
                    s *= 1f - tremoloDepth * 0.5f * (1f - Mathf.Cos(tremoloPhase));
                }

                buffer[n] += s * level * velocity * ring;

                if (vibratoDepth > 0.001f)
                {
                    vibratoPhase += vibratoStep;
                    if (vibratoPhase > 6.2831853f)
                    {
                        vibratoPhase -= 6.2831853f;
                    }
                    // +/-50 cents at full depth, which is a player's vibrato
                    // rather than a siren.
                    position += increment * (1.0 + Mathf.Sin(vibratoPhase) * vibratoDepth * 0.03f);
                }
                else
                {
                    position += increment;
                }
            }
        }
    }

    /// <summary>
    /// Two-operator FM: one sine bending the phase of another.
    ///
    /// The one classic synthesis method a recording cannot stand in for, because
    /// what it does is move the timbre continuously while the note sounds. A
    /// sampler can play a bell; it cannot play a bell whose strike fades into a
    /// hum over two seconds unless somebody recorded exactly that bell.
    ///
    /// `ratio` is the modulator's frequency as a multiple of the carrier's, and it
    /// is what the sound *is*: a whole number gives a harmonic tone (1 for a
    /// hollow lead, 2 for a reedier one, 14 for the bright tine of an electric
    /// piano), and a number that is not whole gives an inharmonic one -- 3.5 is the
    /// bell everybody knows. `index` is how hard it bends, in radians, and where a
    /// synthesiser earns its keep is that the index falls across the note: bright
    /// at the strike and plain afterwards, which is one envelope doing what a whole
    /// wavetable would otherwise have to.
    ///
    /// Sine comes from a table rather than `Mathf.Sin`. Phase modulation needs
    /// sin(phase + bend), which the modal engine's trick cannot give -- that walks
    /// a circle and never names a phase -- and a transcendental per operator per
    /// sample is what made the modal engine cost 44% of a core. A 4096-entry table
    /// with linear interpolation is inaudible against it and about twenty times
    /// cheaper.
    /// </summary>
    public class FmVoice : Voice
    {
        private const int TableBits = 12;
        private const int TableSize = 1 << TableBits;

        /// <summary>One period of sine, shared by every voice in the game. Built
        /// once, read-only afterwards, so the audio thread may have it.</summary>
        private static readonly float[] Sine = BuildSine();

        private static float[] BuildSine()
        {
            float[] table = new float[TableSize + 1];
            for (int i = 0; i <= TableSize; i++)
            {
                table[i] = (float)Math.Sin(2.0 * Math.PI * i / TableSize);
            }
            return table;
        }

        // Phase as 32-bit fixed point: it wraps by itself, which is the whole
        // reason for counting in integers here.
        private uint carrierPhase;
        private uint modulatorPhase;
        private uint carrierStep;
        private uint modulatorStep;

        private float index;            // radians of bend, now
        private float indexEnd;         // where it settles
        private float indexFall;        // per sample, towards it

        private float feedback;
        private float lastModulator;

        private float trim;             // the type's own level, so all seven match
        private float amplitude;        // the note's own decay, for struck types
        private float amplitudeFall;
        private float envelope;         // the key's attack and release
        private float attackPerSample;
        private float releasePerSample;
        private bool holds;
        private bool releasing;
        private float velocity;

        public FmVoice(int sampleRate) : base(sampleRate) { }

        private static float Look(uint phase, float bend)
        {
            // The bend is in radians; a whole turn is TableSize entries.
            int at = (int)((phase >> (32 - TableBits))
                           + (int)(bend * (TableSize / (2f * Mathf.PI))));
            float f = table_frac(phase);
            at &= TableSize - 1;
            return Sine[at] + (Sine[at + 1] - Sine[at]) * f;
        }

        private static float table_frac(uint phase)
        {
            return (phase >> (32 - TableBits - 12) & 0xfff) / 4096f;
        }

        public override void Start(InstrumentType type, float note, float vel, float[] extras)
        {
            float f0 = Hz(note);
            double turn = 4294967296.0 / rate;
            carrierStep = (uint)(f0 * turn);
            modulatorStep = (uint)(f0 * Mathf.Max(0.01f, type.Ratio) * turn);
            carrierPhase = 0;
            modulatorPhase = 0;
            lastModulator = 0f;
            feedback = Mathf.Clamp01(type.Feedback);

            // Harder keys are brighter, which on an FM operator is more bend and
            // not more level -- the one thing everybody knows about a DX7.
            index = Mathf.Max(0f, type.Index) * (0.4f + 0.6f * vel);
            // `brightness` is what is left of that when the note has settled.
            indexEnd = index * Mathf.Clamp01(type.Brightness);
            // Across a fifth of the decay, so the bright part is the attack.
            float fallOver = Mathf.Max(0.02f, type.Decay * 0.2f);
            indexFall = Mathf.Exp(-1f / (fallOver * rate));

            trim = Mathf.Max(0f, type.Level);
            holds = type.Holds;
            amplitude = 1f;
            amplitudeFall = holds ? 1f
                : Mathf.Exp(-ToSilence / (Mathf.Max(0.05f, type.Decay) * rate));
            envelope = 0f;
            attackPerSample = 1f / Mathf.Max(1f, type.Attack * rate);
            releasePerSample = 1f / Mathf.Max(1f, type.Release * rate);
            releasing = false;
            velocity = vel;
            Active = true;
            Held = true;
            Age = 0;
        }

        /// <summary>ln(1000), as the sampler's: `decay` reads as seconds to
        /// silence.</summary>
        private const float ToSilence = 6.9077553f;

        public override void Release()
        {
            Held = false;
            releasing = true;
        }

        public override void Render(float[] buffer, int frames)
        {
            for (int n = 0; n < frames; n++)
            {
                // The modulator, with its own output fed back: a little of that is
                // what takes a sine towards a saw, and too much is noise.
                float self = lastModulator * feedback * 3f;
                float m = Look(modulatorPhase, self);
                lastModulator = m;
                float s = Look(carrierPhase, m * index);

                carrierPhase += carrierStep;
                modulatorPhase += modulatorStep;

                index = indexEnd + (index - indexEnd) * indexFall;
                amplitude *= amplitudeFall;

                if (releasing)
                {
                    envelope -= releasePerSample;
                    if (envelope <= 0f)
                    {
                        Active = false;
                        return;
                    }
                }
                else if (envelope < 1f)
                {
                    envelope += attackPerSample;
                    if (envelope > 1f) { envelope = 1f; }
                }

                buffer[n] += s * envelope * amplitude * velocity * trim * 0.35f;
            }

            if (amplitude < 0.00002f)
            {
                Active = false;
            }
        }
    }
}
