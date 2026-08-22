using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The recorded notes for one instrument type, read out of Besiege's resources
    /// once and kept as plain float arrays.
    ///
    /// Reading them out matters: the audio thread cannot call into Unity, so
    /// AudioClip.GetData has to happen on the game thread, at load, and the voices
    /// then index a managed array. It is also the only way in at all -- System.IO
    /// is blacklisted, so ModResource is the sole route from disk to memory.
    ///
    /// A sample's own pitch comes from its name: `piano_grand_60` is middle C.
    /// Keeping it in the name means the key map needs no second data file, and a
    /// sample can be dropped in without editing code.
    /// </summary>
    public class SampleBank
    {
        public class Entry
        {
            public float[] Data;
            public int Note;
            public int Rate;

            /// <summary>Loop bounds in samples, or -1 for a sample that does not loop.</summary>
            public int LoopStart = -1;
            public int LoopEnd = -1;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public int Count { get { return entries.Count; } }

        /// <summary>
        /// Loads every clip named in a type's `samples` attribute. Missing clips
        /// are logged and skipped rather than thrown: one absent sample should
        /// cost that instrument its top octave, not the whole mod.
        /// </summary>
        public static SampleBank Load(string names, string loops)
        {
            SampleBank bank = new SampleBank();
            if (string.IsNullOrEmpty(names))
            {
                return bank;
            }

            string[] parts = names.Split(' ');
            string[] loopParts = string.IsNullOrEmpty(loops)
                ? new string[0] : loops.Split(' ');
            int loopIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                loopIndex++;

                int note = NoteFromName(name);
                if (note < 0)
                {
                    ModConsole.Log("Orchestra: sample '" + name + "' has no note suffix; skipped.");
                    continue;
                }

                AudioClip clip = null;
                try
                {
                    clip = ModResource.GetAudioClip(name);
                }
                catch (Exception)
                {
                    clip = null;
                }
                if (clip == null)
                {
                    ModConsole.Log("Orchestra: sample '" + name + "' did not load; skipped.");
                    continue;
                }

                float[] data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                if (clip.channels > 1)
                {
                    data = ToMono(data, clip.channels);
                }

                Entry entry = new Entry();
                entry.Data = data;
                entry.Note = note;
                entry.Rate = clip.frequency;
                ReadLoop(entry, loopParts, loopIndex, data.Length);
                bank.entries.Add(entry);
            }
            return bank;
        }

        /// <summary>
        /// Parses one `start-end` pair, positionally. A malformed or out-of-range
        /// pair leaves the sample unlooped rather than throwing: a bad number in a
        /// generated attribute should cost that note its sustain, nothing more.
        /// </summary>
        private static void ReadLoop(Entry entry, string[] loops, int index, int length)
        {
            if (index < 0 || index >= loops.Length)
            {
                return;
            }
            string spec = loops[index];
            int dash = spec.IndexOf('-');
            if (dash <= 0 || dash == spec.Length - 1)
            {
                return;
            }
            int start, end;
            if (!int.TryParse(spec.Substring(0, dash), out start) ||
                !int.TryParse(spec.Substring(dash + 1), out end))
            {
                return;
            }
            if (start < 0 || end <= start || end > length)
            {
                return;
            }
            entry.LoopStart = start;
            entry.LoopEnd = end;
        }

        /// <summary>The recorded note closest to the one asked for.</summary>
        public Entry Nearest(float note)
        {
            Entry best = null;
            float bestDistance = 1e9f;
            for (int i = 0; i < entries.Count; i++)
            {
                float d = entries[i].Note - note;
                if (d < 0f)
                {
                    d = -d;
                }
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = entries[i];
                }
            }
            return best;
        }

        /// <summary>Everything downstream is mono; the source spatialises it.</summary>
        private static float[] ToMono(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float[] mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += data[i * channels + c];
                }
                mono[i] = sum / channels;
            }
            return mono;
        }

        /// <summary>Trailing `_60` is MIDI note 60. Returns -1 when absent.</summary>
        private static int NoteFromName(string name)
        {
            int underscore = name.LastIndexOf('_');
            if (underscore < 0 || underscore == name.Length - 1)
            {
                return -1;
            }
            int note = 0;
            for (int i = underscore + 1; i < name.Length; i++)
            {
                char c = name[i];
                if (c < '0' || c > '9')
                {
                    return -1;
                }
                note = note * 10 + (c - '0');
            }
            return note > 127 ? -1 : note;
        }
    }
}
