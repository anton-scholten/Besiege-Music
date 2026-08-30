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
            return Report(args[0], args.Length > 1 ? args[1] : "Piano",
                          args.Length > 2 ? float.Parse(args[2]) : 0f);
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

        // A song that names a block but not an instrument gets that block's own
        // default, which is the instrument one placed by hand starts on -- not
        // whichever happens to be first in the list.
        Is(Catalogue.TypeIndex(Catalogue.Find("Piano"), "") == 1,
           "a song with no instrument named takes the block's default",
           "it took " + Catalogue.TypeIndex(Catalogue.Find("Piano"), ""));
        Is(Catalogue.TypeIndex(Catalogue.Find("Piano"), "Grand piano") == 0,
           "and naming one still picks that one",
           "naming Grand piano did not pick it");
        XmlNodeList typed = second.SelectNodes("//Integer[@key='bmt-TypeKey']");
        bool allDefault = typed.Count > 0;
        for (int i = 0; i < typed.Count; i++)
        {
            if (typed[i].InnerText.Trim() != "1") { allDefault = false; }
        }
        Is(allDefault, "every instrument block is written on that default",
           "the machine holds a type other than the default");

        // The variables are named after whatever the block's prefix says, and a
        // prefix that could not be a variable name falls back rather than writing a
        // machine whose timers and blocks disagree about what they are called.
        Is(Song.Named("") == SongOptions.DefaultPrefix,
           "an empty prefix falls back", "an empty prefix was kept");
        Is(Song.Named("a;b") == SongOptions.DefaultPrefix,
           "a prefix with a semicolon falls back -- MKey joins names with one",
           "a semicolon got through");
        Is(Song.Named("song2_") == "song2_", "a plain prefix is kept",
           "a plain prefix was refused");

        options.Prefix = "song2_";
        SongPlan renamed = Song.Plan(new Midi(file).Notes(0f), options);
        XmlDocument fourth = new XmlDocument();
        fourth.LoadXml(Bsg.Write(renamed, "Self test"));
        int ours = 0;
        foreach (XmlElement entry in fourth.SelectNodes("//StringArray/String"))
        {
            if (entry.InnerText.StartsWith("Message="))
            {
                ours++;
                Is(entry.InnerText.StartsWith("Message=song2_"),
                   "every variable is named after the prefix",
                   "a variable is called " + entry.InnerText);
            }
        }
        Is(ours == 18, "18 variable names written, one per block and per timer",
           ours + " variable names written");
        options.Prefix = SongOptions.DefaultPrefix;

        // Every General MIDI instrument points at a block this mod has, and the
        // ones a score actually uses point at the obvious one.
        Is(Gm.Count == 128, "128 General MIDI instruments",
           Gm.Count + " instruments in the table");
        Is(Gm.Instrument(40) == "Strings:Violin", "GM 40 is a violin",
           "GM 40 is " + Gm.Instrument(40));
        Is(Gm.Instrument(66) == "Woodwind:Sax", "GM 66 is a tenor sax",
           "GM 66 is " + Gm.Instrument(66));
        Is(Gm.Instrument(26) == "Guitar:Jazz", "GM 26 is a jazz guitar",
           "GM 26 is " + Gm.Instrument(26));
        Is(Gm.Instrument(-1) == Gm.Instrument(0),
           "a channel nobody set is the grand piano, as GM says",
           "an unset channel is " + Gm.Instrument(-1));
        // And every one of them names a block and an instrument this mod has.
        for (int program = 0; program < Gm.Count; program++)
        {
            string points = Gm.Instrument(program);
            int colon = points.IndexOf(':');
            Family family = colon < 0 ? null : Catalogue.Find(points.Substring(0, colon));
            Is(family != null, "GM " + program + " names a block this mod has",
               "GM " + program + " wants " + points + ", which is not a block here");
            if (family != null)
            {
                string type = points.Substring(colon + 1);
                Is(family.Types.Contains(type),
                   "GM " + program + " names an instrument that block has",
                   "GM " + program + " wants " + points + ", and " + family.Name
                   + " has no " + type);
            }
        }

        // What a newly placed loader block starts on, from Loader.xml. A name the
        // menu does not hold falls back to the *first* family -- which is Bass,
        // alphabetically -- so a typo here would be a silent change of instrument
        // rather than an error.
        string declared = Declared("Orchestra/Loader.xml", "instrument");
        bool known = declared == Gm.FromFile;
        for (int i = 0; i < Catalogue.Families.Count && !known; i++)
        {
            known = string.Compare(Catalogue.Families[i].Name, declared, true) == 0;
        }
        Is(known, "the loader's default instrument is one the menu holds",
           "Loader.xml says instrument=\"" + declared + "\", which is not a block "
           + "here nor \"" + Gm.FromFile + "\"");

        // And with a variable, which is what the loader block's own key comes to
        // when it is set to one: the timers listen to the name, and carry a keycode
        // they never answer to so that `Machine.InitSimBlock` registers them at all.
        options.StartVariable = "start-me";
        SongPlan varied = Song.Plan(new Midi(file).Notes(0f), options);
        XmlDocument third = new XmlDocument();
        third.LoadXml(Bsg.Write(varied, "Self test"));
        XmlNodeList started = third.SelectNodes("//StringArray[@key='bmt-activate']");
        Is(started.Count == 10, "10 timers wait for the variable",
           started.Count + " timers wait for the variable");
        string said = started.Count == 0 ? "" : started[0].InnerXml;
        Is(said.Contains("<String>Space</String>")
           && said.Contains("<String>Message=start-me</String>")
           && said.Contains("<String>Use=True</String>"),
           "a timer on a variable carries the name, the flag and a keycode",
           "a timer on a variable reads " + said);
        Is(third.SelectNodes("//Boolean[@key='bmt-automatic']").Count == 0,
           "no timer on a variable starts itself as well",
           "a timer on a variable is automatic too");

        return Done();
    }

    /// <summary>What one real file comes to, in the same words the Python tool
    /// prints. Not part of the build's check: a way to compare the two.</summary>
    static int Report(string path, string instrument, float bpm)
    {
        SongOptions options = new SongOptions();
        options.Instrument = instrument;
        // Nought follows the file's own tempo map, as the panel does until
        // somebody moves its TEMPO slider; anything else flattens the score to
        // that one speed, as the slider then does.
        options.Tempo = bpm;
        SongPlan plan = Song.Convert(File.ReadAllBytes(path), options);
        Console.WriteLine(Path.GetFileName(path) + ": " + plan.Notes + " notes, "
                          + plan.Voices + " instrument block(s), "
                          + (plan.Voices + plan.Timers + 1) + " blocks, "
                          + plan.Seconds.ToString("0.0") + " seconds, file says "
                          + plan.FileBpm.ToString("0.##") + " bpm"
                          + (bpm > 0f ? ", played at " + bpm.ToString("0.##") : ""));
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
        // All nine, as the block XMLs declare them -- not just the three this
        // test's own score touches. `Assign` falls back to the first family for a
        // block it cannot find, so a half-seeded catalogue quietly turns every
        // instrument into that one, and a check of "as the file says" against it
        // would have passed while proving nothing.
        all.Add(Made("Guitar", 2, 1006, new string[]
            { "Nylon", "Steel", "Jazz", "Clean", "Overdriven" }));
        all.Add(Made("Bass", 3, 1007, new string[]
            { "Acoustic", "Fingered", "Picked", "Fretless", "Synth" }));
        all.Add(Made("Strings", 4, 1008, new string[]
            { "Violin", "Viola", "Cello", "Double bass", "Ensemble" }));
        all.Add(Made("Brass", 5, 1009, new string[]
            { "Trumpet", "Trombone", "French horn", "Tuba", "Section" }));
        all.Add(Made("Woodwind", 6, 1010, new string[]
            { "Flute", "Clarinet", "Oboe", "Bassoon", "Sax" }));
        all.Add(Made("Mallets", 7, 1011, new string[]
            { "Glockenspiel", "Vibraphone", "Marimba", "Xylophone", "Tubular bells" }));

        Family piano = Made("Piano", 1, 1005, new string[]
            { "Grand piano", "Upright piano", "Electric piano", "Honky-tonk" });
        // As Piano.xml says: `default="Upright piano"`, which is the second of
        // them. The type is saved as an index, so the list stays in the order every
        // machine already built was written against.
        piano.DefaultType = 1;
        all.Add(piano);
        all.Add(Made("Drums", 8, 1012, new string[]
            { "Kick", "Snare", "Tom", "Rim", "Clap" }));
        all.Add(Made("Cymbals", 9, 1013, new string[]
            { "Crash", "Ride", "Hi-hat", "Splash", "Gong" }));
        return all;
    }

    /// <summary>One attribute of the module element in a block XML, read off the
    /// file rather than through ModIO -- which says nothing outside the game.</summary>
    static string Declared(string path, string attribute)
    {
        if (!File.Exists(path))
        {
            return "";
        }
        XmlDocument doc = new XmlDocument();
        doc.Load(path);
        XmlNode modules = doc.SelectSingleNode("/Block/Modules");
        if (modules == null)
        {
            return "";
        }
        foreach (XmlNode module in modules.ChildNodes)
        {
            XmlElement one = module as XmlElement;
            if (one != null && one.HasAttribute(attribute))
            {
                return one.GetAttribute(attribute);
            }
        }
        return "";
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
