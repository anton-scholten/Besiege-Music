// Builds a machine out of a made-up score, with the mod's own converter, and
// checks what came out -- without starting Besiege.
//
// This is the in-game half of what `tools/make-song.py --self-test` does for the
// command-line tool. The two write the same machine and are easy to let drift
// apart, so both are checked: this one reads the score, lays out the blocks and
// writes the .bsg, then reads that back and holds it to the things a machine has
// to get right or play nothing --
//
//   * a timer per note, waiting the right number of seconds;
//   * an instrument block per distinct voice, each on its own variable;
//   * every variable key carrying a keycode, without which
//     `Machine.InitSimBlock` never registers it and the machine is silent;
//   * every block flat on the ground and standing up;
//   * repeated notes separated, rather than swallowed by a counted emulator.
//
// It runs against the assembly that was just built, on Besiege's own Mono, and
// needs no game running: the converter touches no Unity object, which is the
// other thing this proves.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using OrchestraMod;

class SongCheck
{
    static int bad;

    public static int Main(string[] args)
    {
        Catalogue.Seed(Blocks(), "aca735ea-a614-4aef-9676-67ec1edd5059", "0.1.0",
                       "Orchestra");

        if (args.Length > 0)
        {
            // Given a real file, say what the converter makes of it, so it can be
            // held against what `tools/make-song.py` says about the same score.
            return Report(args[0], args.Length > 1 ? args[1] : "Piano");
        }

        byte[] file = Score();
        Midi midi = new Midi(file);
        List<MidiNote> notes = midi.Notes(0f);
        Is(notes.Count == 10, "10 notes read", notes.Count + " notes read");
        Near(notes[1].Start, 0.5f, "the second note starts half a second in");
        Near(notes[0].Length, 0.5f, "a note lasts half a second");

        SongOptions options = new SongOptions();
        options.Instrument = "Piano";
        SongPlan plan = Song.Plan(notes, options);

        // Eight distinct pitches in the scale; middle C is struck three times and
        // is one block. Nothing is dropped: the repeats start far enough apart.
        Is(plan.Voices == 8, "8 instrument blocks", plan.Voices + " instrument blocks");
        Is(plan.Timers == 10, "10 timers", plan.Timers + " timers");
        Is(plan.Crowded == 0, "no note crowded out",
           plan.Crowded + " note(s) crowded out");
        Is(plan.Blocks.Count == 18, "18 blocks laid out",
           plan.Blocks.Count + " blocks laid out");

        string text = Bsg.Write(plan, "Self test");
        XmlDocument doc = new XmlDocument();
        try
        {
            doc.LoadXml(text);
        }
        catch (Exception e)
        {
            Fail("the machine is not XML: " + e.Message);
            return Done();
        }

        XmlNodeList blocks = doc.SelectNodes("/Machine/Blocks/Block");
        Is(blocks.Count == 19, "19 blocks written, with the starting block",
           blocks.Count + " blocks written");

        int timers = 0;
        int modded = 0;
        List<double> waits = new List<double>();
        List<string> variables = new List<string>();

        foreach (XmlElement block in blocks)
        {
            if (block.GetAttribute("modId").Length > 0)
            {
                modded++;
                Is(block.GetAttribute("fallback") == "35",
                   "a modded block falls back to a ballast",
                   "a modded block falls back to '"
                   + block.GetAttribute("fallback") + "'");
            }
            if (block.GetAttribute("id") == Song.TimerBlock.ToString())
            {
                timers++;
                XmlNode wait = block.SelectSingleNode("Data/Single[@key='bmt-wait']");
                if (wait != null)
                {
                    waits.Add(double.Parse(wait.InnerText,
                        System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            // Flat, and standing up -- the starting block aside, which is the
            // machine's root rather than one of the instruments.
            XmlElement at = block.SelectSingleNode("Transform/Position") as XmlElement;
            Is(at.GetAttribute("y") == "0", "every block is at one height",
               "a block is at y=" + at.GetAttribute("y"));
            XmlElement turn = block.SelectSingleNode("Transform/Rotation") as XmlElement;
            string x = turn.GetAttribute("x");
            Is(x == "0" || x.StartsWith("-0.7071"), "every block faces up",
               "a block is turned x=" + x);
        }

        Is(timers == 10, "10 timer blocks in the file", timers + " timer blocks");
        Is(modded == 8, "8 modded blocks in the file", modded + " modded blocks");

        waits.Sort();
        Near((float)(waits[1] - waits[0]), 0.5f, "timers half a second apart");

        // Every key driven by a variable has to keep a keycode: without one,
        // `Machine.InitSimBlock` never registers it and nothing is ever heard.
        foreach (XmlElement keyed in doc.SelectNodes("//StringArray"))
        {
            XmlNodeList entries = keyed.SelectNodes("String");
            if (entries.Count == 0)
            {
                continue;                   // requiredMods, one inline value
            }
            string first = entries[0].InnerText;
            Is(!first.StartsWith("Message=") && !first.StartsWith("Use="),
               "a variable key keeps its keycode",
               "a variable key with no keycode: " + first);
            foreach (XmlElement entry in entries)
            {
                if (entry.InnerText.StartsWith("Message=")
                    && !variables.Contains(entry.InnerText))
                {
                    variables.Add(entry.InnerText);
                }
            }
        }
        Is(variables.Count == 8, "8 variables, one per instrument block",
           variables.Count + " variables");

        // And the same score with a key: the timers wait for it instead of
        // starting themselves.
        options.StartKey = "Space";
        SongPlan keyed2 = Song.Plan(new Midi(file).Notes(0f), options);
        string withKey = Bsg.Write(keyed2, "Self test");
        XmlDocument second = new XmlDocument();
        second.LoadXml(withKey);
        Is(second.SelectNodes("//StringArray[@key='bmt-activate']").Count == 10,
           "10 timers wait for the key",
           second.SelectNodes("//StringArray[@key='bmt-activate']").Count
           + " timers wait for the key");
        Is(second.SelectNodes("//Boolean[@key='bmt-automatic']").Count == 0,
           "no timer starts itself as well",
           "a keyed timer is automatic too");

        return Done();
    }

    /// <summary>What one real file comes to, in the same words the Python tool
    /// prints. Not part of the build's check: a way to compare the two.</summary>
    static int Report(string path, string instrument)
    {
        SongOptions options = new SongOptions();
        options.Instrument = instrument;
        SongPlan plan = Song.Convert(File.ReadAllBytes(path), options);
        Console.WriteLine(Path.GetFileName(path) + ": " + plan.Notes + " notes, "
                          + plan.Voices + " instrument block(s), "
                          + (plan.Voices + plan.Timers + 1) + " blocks, "
                          + plan.Seconds.ToString("0.0") + " seconds");
        if (plan.Crowded > 0)
        {
            Console.WriteLine("  " + plan.Crowded + " note(s) fell inside another "
                              + "note on the same block, and went");
        }
        if (plan.Dropped > 0)
        {
            Console.WriteLine("  " + plan.Dropped + " note(s) past the limit were dropped");
        }
        return 0;
    }

    /// <summary>The blocks the converter would have read out of the mod folder.</summary>
    static List<Family> Blocks()
    {
        List<Family> all = new List<Family>();
        all.Add(Made("Piano", 1, 1005, new string[]
            { "Grand piano", "Upright piano", "Electric piano", "Honky-tonk" }));
        all.Add(Made("Drums", 8, 1012, new string[]
            { "Kick", "Snare", "Tom" }));
        all.Add(Made("Cymbals", 9, 1013, new string[]
            { "Hi-hat", "Crash", "Ride" }));
        return all;
    }

    static Family Made(string name, int local, int type, string[] types)
    {
        Family family = new Family();
        family.Name = name;
        family.LocalId = local;
        family.BlockType = type;
        for (int i = 0; i < types.Length; i++)
        {
            family.Types.Add(types[i]);
        }
        return family;
    }

    /// <summary>
    /// A scale, then middle C twice over with the second starting while the first
    /// is still down -- the case a counted emulator swallows if the notes are not
    /// separated.
    /// </summary>
    static byte[] Score()
    {
        List<byte> events = new List<byte>();
        events.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 });
        int[] scale = { 60, 62, 64, 65, 67, 69, 71, 72 };
        for (int i = 0; i < scale.Length; i++)
        {
            VarLen(events, 0);
            events.AddRange(new byte[] { 0x90, (byte)scale[i], 100 });
            VarLen(events, 480);
            events.AddRange(new byte[] { 0x80, (byte)scale[i], 0 });
        }
        VarLen(events, 0);
        events.AddRange(new byte[] { 0x90, 60, 100 });
        VarLen(events, 240);
        events.AddRange(new byte[] { 0x90, 60, 100 });
        VarLen(events, 240);
        events.AddRange(new byte[] { 0x80, 60, 0 });
        VarLen(events, 0);
        events.AddRange(new byte[] { 0x80, 60, 0 });
        events.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });

        List<byte> file = new List<byte>();
        file.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
        file.AddRange(Big(6));
        file.AddRange(new byte[] { 0, 0, 0, 1, 1, 224 });    // format 0, 1 track, 480 ppq
        file.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
        file.AddRange(Big(events.Count));
        file.AddRange(events);
        return file.ToArray();
    }

    static byte[] Big(int value)
    {
        return new byte[] { (byte)(value >> 24), (byte)(value >> 16),
                            (byte)(value >> 8), (byte)value };
    }

    static void VarLen(List<byte> into, int value)
    {
        List<byte> out_ = new List<byte>();
        out_.Add((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            out_.Insert(0, (byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        into.AddRange(out_);
    }

    // ---- saying so ---------------------------------------------------------

    static void Is(bool ok, string good, string wrong)
    {
        if (!ok)
        {
            Fail(wrong);
        }
    }

    static void Near(float got, float want, string what)
    {
        if (Math.Abs(got - want) > 0.001f)
        {
            Fail(what + ": " + got.ToString("0.000"));
        }
    }

    static void Fail(string what)
    {
        Console.Error.WriteLine("  " + what);
        bad++;
    }

    static int Done()
    {
        if (bad > 0)
        {
            Console.Error.WriteLine("Song check: " + bad + " problem(s).");
            return 1;
        }
        Console.WriteLine("Song check: a scale becomes 8 instrument blocks and "
                          + "10 timers, and the machine reads back.");
        return 0;
    }
}
