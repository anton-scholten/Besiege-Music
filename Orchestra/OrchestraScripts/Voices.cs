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

        private readonly float[] phase = new float[Partials];
        private readonly float[] step = new float[Partials];
        private readonly float[] amp = new float[Partials];
        private readonly float[] damp = new float[Partials];

        private float noiseLevel;
        private float noiseDamp;
        private uint noiseState = 22222u;

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
                    step[i] = 0f;
                    continue;
                }
                step[i] = 2f * Mathf.PI * f / rate;
                phase[i] = 0f;

                // Higher partials start louder with brightness, and always die first.
                float tilt = Mathf.Pow(i + 1f, -1.2f + bright);
                amp[i] = velocity * tilt / Partials * 6f;
                float partialDecay = decay / (1f + i * bright * 0.8f);
                damp[i] = Mathf.Exp(-1f / (partialDecay * rate));
            }

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
            float peak = 0f;
            for (int n = 0; n < frames; n++)
            {
                float s = 0f;
                for (int i = 0; i < Partials; i++)
                {
                    if (amp[i] <= 0.000001f)
                    {
                        continue;
                    }
                    phase[i] += step[i];
                    if (phase[i] > 6.2831853f)
                    {
                        phase[i] -= 6.2831853f;
                    }
                    s += Mathf.Sin(phase[i]) * amp[i];
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
    /// A drum: a pitched body that falls as it decays, plus a noise burst for the
    /// skin and the stick. `pitchDrop` is what separates a kick, which sweeps a
    /// long way down, from a tom, which barely moves.
    /// </summary>
    public class DrumVoice : Voice
    {
        private float phase;
        private float freq;
        private float freqEnd;
        private float freqFall;
        private float bodyAmp;
        private float bodyDamp;
        private float noiseAmp;
        private float noiseDamp;
        private float lp;
        private uint noiseState = 987654321u;

        public DrumVoice(int sampleRate) : base(sampleRate) { }

        public override void Start(InstrumentType type, float note, float velocity, float[] extras)
        {
            float decay = Mathf.Max(0.02f, type.Decay);
            freq = Hz(note + Mathf.Max(0f, type.PitchDrop));
            freqEnd = Hz(note);
            // Reaches the settled pitch in about a tenth of the decay.
            freqFall = Mathf.Exp(-1f / (decay * 0.1f * rate));
            phase = 0f;

            float noise = Mathf.Clamp01(type.Noise);
            bodyAmp = velocity * (1f - noise * 0.6f);
            bodyDamp = Mathf.Exp(-1f / (decay * rate));
            noiseAmp = velocity * noise;
            noiseDamp = Mathf.Exp(-1f / (Mathf.Max(0.01f, decay * 0.35f) * rate));
            lp = 0f;

            Active = true;
            Held = true;
            Age = 0;
        }

        public override void Render(float[] buffer, int frames)
        {
            for (int n = 0; n < frames; n++)
            {
                freq = freqEnd + (freq - freqEnd) * freqFall;
                phase += 2f * Mathf.PI * freq / rate;
                if (phase > 6.2831853f)
                {
                    phase -= 6.2831853f;
                }
                float s = Mathf.Sin(phase) * bodyAmp;
                bodyAmp *= bodyDamp;

                noiseState ^= noiseState << 13;
                noiseState ^= noiseState >> 17;
                noiseState ^= noiseState << 5;
                float white = ((int)(noiseState & 0xffff) - 32768) / 32768f;
                // One pole of smoothing: raw white is too fizzy for a skin.
                lp += (white - lp) * 0.45f;
                s += lp * noiseAmp;
                noiseAmp *= noiseDamp;

                buffer[n] += s;
            }

            if (bodyAmp < 0.00002f && noiseAmp < 0.00002f)
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
        private float damping;
        private float lowpass;
        private float edge;
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

            damping = Mathf.Clamp01(type.Damping);
            lowpass = 0f;
            edge = Mathf.Clamp01(type.Edge);

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

                if (edge > 0.001f)
                {
                    noiseState ^= noiseState << 13;
                    noiseState ^= noiseState >> 17;
                    noiseState ^= noiseState << 5;
                    float white = ((int)(noiseState & 0xffff) - 32768) / 32768f;
                    s += white * edge * 0.12f;
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
}
