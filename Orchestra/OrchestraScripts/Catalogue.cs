using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OrchestraMod
{
    /// <summary>One instrument block, as the converter needs to know it.</summary>
    public class Family
    {
        /// <summary>What the block is called: "Piano", "Drums".</summary>
        public string Name;

        /// <summary>Its &lt;ID&gt; in its own XML, which is what a save writes as
        /// `localId` and what the loader resolves the block by.</summary>
        public int LocalId;

        /// <summary>The entries in its Type menu, in order.</summary>
        public readonly List<string> Types = new List<string>();

        /// <summary>Which of them a newly placed block starts on, and which a song
        /// gets when it asks for the block without naming an instrument. From the
        /// module's `default` attribute; 0 when it names none.</summary>
        public int DefaultType;

        /// <summary>The id this Besiege gave the block, which is what a
        /// `BlockInfo` and a save's `id` attribute hold. Nought until
        /// <see cref="Catalogue.Resolve"/> has been able to work it out.</summary>
        public int BlockType;
    }

    /// <summary>
    /// The instrument blocks this mod ships, read at runtime out of the same XML
    /// files Besiege reads.
    ///
    /// The alternative -- asking the game -- does not work: a block prefab's
    /// module is filled in by `ModBlockBehaviourHandler.Awake`, and a prefab is an
    /// inactive object whose Awake has never run, so the Types of a block nobody
    /// has placed cannot be read from it. The XML is right there in the mod's own
    /// folder, `ModIO` will hand it over, and it is the same file the game parsed.
    ///
    /// Parsed with a regular expression rather than an XML reader because
    /// `System.Xml` is blacklisted for mods. These are our own files, of a shape
    /// this repository's build checks hold to.
    /// </summary>
    public static class Catalogue
    {
        private static List<Family> families;

        /// <summary>The `&lt;ID&gt;` of every block this mod ships, the loader
        /// included: what a registered prefab's name ends in, and so what says a
        /// prefab is ours whether or not a song can be written for it.</summary>
        private static readonly List<int> blockIds = new List<int>();

        private static string modId = "";
        private static string modVersion = "0.1.0";
        private static string modName = "Orchestra";
        private static string modAuthor = "";

        /// <summary>The mod's own GUID, as a machine's `requiredMods` names it.
        /// Empty if the manifest could not be read.</summary>
        public static string ModId
        {
            get { Load(); return modId; }
        }

        /// <summary>The mod's own name, as its manifest gives it.</summary>
        public static string ModName
        {
            get { Load(); return modName; }
        }

        /// <summary>What a machine's requiredMods entry says: id, version, name.</summary>
        public static string RequiredMods
        {
            get
            {
                Load();
                return modId.Length == 0
                    ? null : modId + "~L~" + modVersion + "~" + modName;
            }
        }

        public static List<Family> Families
        {
            get { Load(); return families == null ? new List<Family>() : families; }
        }

        /// <summary>A block by name, matched however it was typed, or null.</summary>
        public static Family Find(string name)
        {
            Load();
            if (families == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            string wanted = name.Trim().ToLower();
            for (int i = 0; i < families.Count; i++)
            {
                if (families[i].Name.ToLower() == wanted)
                {
                    return families[i];
                }
            }
            return null;
        }

        /// <summary>
        /// The index of a named instrument within a block, matched loosely -- "Kick"
        /// finds "Kick drum" -- or 0, which every block has.
        /// </summary>
        public static int TypeIndex(Family family, string wanted)
        {
            if (family == null)
            {
                return 0;
            }
            if (string.IsNullOrEmpty(wanted))
            {
                // Nobody named one, so the block's own default answers -- the same
                // instrument a block placed by hand starts on.
                return family.DefaultType;
            }
            string want = wanted.Trim().ToLower();
            for (int i = 0; i < family.Types.Count; i++)
            {
                if (family.Types[i].ToLower() == want)
                {
                    return i;
                }
            }
            for (int i = 0; i < family.Types.Count; i++)
            {
                if (family.Types[i].ToLower().Contains(want))
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>
        /// Works out what id this Besiege gave each instrument block.
        ///
        /// Cheap enough to call again: it walks the prefab table and writes what it
        /// finds. <see cref="Prefabs"/> does call it again, because the table is not
        /// filled at the moment the mod is loaded.
        ///
        /// A family whose prefab is not found keeps id 0, and the converter refuses
        /// to write a machine with it: a wrong id is a machine full of somebody
        /// else's blocks, which is worse than no machine at all.
        /// </summary>
        public static void Resolve()
        {
            Load();
            if (families == null)
            {
                // The mod's own files could not be read yet -- `ModIO` says nothing
                // until the mod is loaded enough to be identified. Load has said so;
                // the next call will try again.
                return;
            }
            Dictionary<string, int> ours = Ours();
            string said = "";
            int missing = 0;
            for (int i = 0; i < families.Count; i++)
            {
                Family family = families[i];
                int type;
                family.BlockType =
                    ours.TryGetValue(family.Name.ToLower(), out type) ? type : 0;
                if (family.BlockType == 0)
                {
                    missing++;
                }
                said += (said.Length > 0 ? ", " : "") + family.Name + " "
                     + (family.BlockType == 0 ? "?" : family.BlockType.ToString());
            }
            settled = missing == 0 && families.Count > 0
                   && mine >= blockIds.Count && blockIds.Count > 0;

            // Only when the answer changes: this runs once a second until it comes
            // out right, and a line a second is not a log.
            if (said != lastSaid && families.Count > 0)
            {
                lastSaid = said;
                Log.Info("instrument block ids: " + said + " (" + mine
                         + " of this mod's " + blockIds.Count + " prefabs found)");
                if (missing > 0)
                {
                    Log.Warn(missing + " instrument block(s) have no prefab in this "
                             + "game yet, so no song can be written for them.");
                }
            }
        }

        /// <summary>Every block is accounted for: nothing is left to look up.</summary>
        public static bool Settled { get { return settled; } }

        private static bool settled;
        private static string lastSaid;

        /// <summary>How many of this mod's prefabs the last look found, the loader
        /// block included -- which is more than the families, and is what says the
        /// whole mod has been registered.</summary>
        private static int mine;

        /// <summary>
        /// This mod's own block prefabs, as name -> block id.
        ///
        /// **A registered prefab is named `&lt;mod guid&gt;-&lt;local id&gt;`.**
        /// `BlockPrefabCreator.CreatePrefab` names the prefab object that, and
        /// `BlockLoader.RegisterPrefab` then calls `BlockPrefab.SetNameFromGameObject`,
        /// which copies it over the name the block XML gave. So by the time a prefab
        /// is in `PrefabMaster.BlockPrefabs` its `name` is not "Bass" at all -- it is
        /// this mod's GUID and the block's own `&lt;ID&gt;`, which names our blocks
        /// exactly and cannot be confused with another mod's.
        ///
        /// Three earlier answers were wrong, and each shipped:
        ///
        /// * **`BlockPrefab.locID`** is -1 on every modded block; its constructor
        ///   sets it and nothing writes it. Arithmetic on it landed on the Sound
        ///   Blocks mod's ids.
        /// * **The module behaviour is not on the prefab.**
        ///   `ModBlockBehaviourHandler.Awake` adds it to the block *instance*, so
        ///   looking for an `InstrumentBehaviour` on a prefab finds nothing, ever.
        /// * **`BlockPrefab.name` is not the block's `&lt;Name&gt;`** once it is
        ///   registered, as above. Matching on it found nothing, which is what left
        ///   every family without an id and the loader refusing to convert.
        ///
        /// The name check is kept as a second route, for a Besiege that has not
        /// renamed the prefab: there `nameKeywords` carries the mod's author, which
        /// `BlockPrefabCreator.SetupBehaviour` appends to the block's own keywords.
        /// </summary>
        private static Dictionary<string, int> Ours()
        {
            Dictionary<string, int> found = new Dictionary<string, int>();
            List<string> sameName = new List<string>();
            int ours = 0;
            try
            {
                foreach (KeyValuePair<int, BlockPrefab> pair in PrefabMaster.BlockPrefabs)
                {
                    BlockPrefab prefab = pair.Value;
                    if (prefab == null)
                    {
                        continue;
                    }
                    int local = LocalIdOf(prefab.name);
                    if (local >= 0)
                    {
                        ours++;
                        Family family = ByLocalId(local);
                        if (family != null)
                        {
                            found[family.Name.ToLower()] = (int)prefab.Type;
                        }
                        continue;
                    }
                    if (string.IsNullOrEmpty(prefab.name) || Find(prefab.name) == null)
                    {
                        continue;           // not a name we are after either
                    }
                    if (ByThisAuthor(prefab))
                    {
                        ours++;
                        found[prefab.name.ToLower()] = (int)prefab.Type;
                    }
                    else if (!sameName.Contains(prefab.name.ToLower()))
                    {
                        // Another mod's block of the same name. Remembered so the
                        // log can say why one of ours looks missing rather than
                        // leaving somebody to guess.
                        sameName.Add(prefab.name.ToLower());
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not read the block prefabs: " + e.Message);
            }
            mine = ours;
            if (found.Count == 0 && sameName.Count > 0)
            {
                // The names are all there but nothing matched the author either:
                // take them at their word, and say that is what is happening.
                Log.Warn("no block prefab carries this mod's guid or author, so the "
                         + "instrument blocks are being matched by name alone.");
                return ByNameAlone();
            }
            if (sameName.Count > 0)
            {
                Log.Info("another mod has blocks called: "
                         + string.Join(", ", sameName.ToArray()));
            }
            return found;
        }

        /// <summary>The `&lt;ID&gt;` in a registered prefab's name, or -1 if the
        /// name is not this mod's.</summary>
        private static int LocalIdOf(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || modId.Length == 0)
            {
                return -1;
            }
            // The GUID has hyphens of its own, so the last one is the separator:
            // what follows it is a number and nothing else.
            int cut = prefabName.LastIndexOf('-');
            if (cut <= 0 || cut + 1 >= prefabName.Length)
            {
                return -1;
            }
            if (string.Compare(prefabName.Substring(0, cut), modId, true) != 0)
            {
                return -1;
            }
            int local;
            return int.TryParse(prefabName.Substring(cut + 1), out local) ? local : -1;
        }

        /// <summary>The instrument block with that `&lt;ID&gt;`, or null -- which is
        /// the answer for the loader block itself, it having no instruments.</summary>
        private static Family ByLocalId(int local)
        {
            for (int i = 0; i < families.Count; i++)
            {
                if (families[i].LocalId == local)
                {
                    return families[i];
                }
            }
            return null;
        }

        /// <summary>The last resort: every prefab whose name is one of ours, taken
        /// at its word. A name two mods share is left out rather than guessed at.</summary>
        private static Dictionary<string, int> ByNameAlone()
        {
            Dictionary<string, int> found = new Dictionary<string, int>();
            List<string> twice = new List<string>();
            foreach (KeyValuePair<int, BlockPrefab> pair in PrefabMaster.BlockPrefabs)
            {
                BlockPrefab prefab = pair.Value;
                if (prefab == null || string.IsNullOrEmpty(prefab.name)
                    || Find(prefab.name) == null)
                {
                    continue;
                }
                string name = prefab.name.ToLower();
                if (found.ContainsKey(name))
                {
                    twice.Add(name);
                }
                found[name] = (int)prefab.Type;
            }
            for (int i = 0; i < twice.Count; i++)
            {
                Log.Warn("two blocks are called " + twice[i]
                         + "; refusing to guess which is this mod's.");
                found.Remove(twice[i]);
            }
            return found;
        }

        /// <summary>
        /// Whether a prefab came from this mod: `BlockPrefabCreator` appends the
        /// owning mod's author to every block's search keywords, and on a prefab
        /// that has not been renamed it is the only thing that says where the block
        /// is from.
        /// </summary>
        private static bool ByThisAuthor(BlockPrefab prefab)
        {
            if (prefab.nameKeywords == null || modAuthor.Length == 0)
            {
                // With no author in the manifest there is nothing to match on, and
                // the name has to stand on its own.
                return true;
            }
            for (int i = 0; i < prefab.nameKeywords.Length; i++)
            {
                if (prefab.nameKeywords[i] != null
                    && prefab.nameKeywords[i].ToLower() == modAuthor.ToLower())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Puts a catalogue in place instead of reading one.
        ///
        /// For `tools/tests/SongCheck.cs`, which builds a machine out of a
        /// made-up score and checks it without starting the game -- where `ModIO`
        /// refuses to say anything, there being no mod calling it. Nothing in the
        /// game calls this.
        /// </summary>
        public static void Seed(List<Family> known, string id, string version, string name)
        {
            families = known;
            blockIds.Clear();
            for (int i = 0; i < known.Count; i++)
            {
                blockIds.Add(known[i].LocalId);
            }
            modId = id;
            modVersion = version;
            modName = name;
        }

        // ---- reading the mod's own files -------------------------------------

        /// <summary>
        /// Reads the manifest and every block XML it names.
        ///
        /// **The block files are named by the manifest, not found by listing the
        /// folder.** `ModIO.GetFiles("")` looks like the way to list a mod's own
        /// directory and is not: `ModPaths.GetFilePath` combines the argument with
        /// the mod folder, and a result that does not end in a separator is treated
        /// as a *file*, so the folder listed is the mod folder's **parent** -- which
        /// then fails that method's own "path is not in mod directory" check and
        /// throws. The whole catalogue came back empty from that, which reads in
        /// game as "the instrument blocks could not be read". `Mod.xml` lists every
        /// block anyway, and it is the same list the game itself loads from.
        /// </summary>
        private static void Load()
        {
            if (families != null)
            {
                return;
            }

            string manifest = Files.ModText("Mod.xml");
            if (manifest == null)
            {
                // Deliberately not remembered as an answer: `ModIO` refuses to say
                // anything until the mod is loaded enough to be identified by its
                // calling assembly, and a failure cached here would outlive that.
                Log.Warn("could not read Mod.xml out of the mod's own folder, so "
                         + "there is no list of instrument blocks yet.");
                return;
            }

            List<Family> found = new List<Family>();
            modId = Text(manifest, "ID", modId);
            modVersion = Text(manifest, "Version", modVersion);
            modName = Text(manifest, "Name", modName);
            // Not decoration: the author is the one thing about this mod that
            // reaches a *prefab*, which is what makes its blocks findable. See
            // Ours().
            modAuthor = Text(manifest, "Author", modAuthor);

            int named = 0;
            int read = 0;
            string missed = "";
            foreach (Match entry in Regex.Matches(manifest, "<Block\\s+path=\"([^\"]+)\""))
            {
                named++;
                // The manifest is written with Windows separators, as Besiege's own
                // files are; ModIO wants either and gets on better with one.
                string file = entry.Groups[1].Value.Replace('\\', '/');
                string xml = Files.ModText(file);
                if (xml == null || xml.IndexOf("<Block>") < 0)
                {
                    missed += (missed.Length > 0 ? ", " : "") + file;
                    continue;
                }
                read++;

                Family family = new Family();
                family.Name = Text(xml, "Name", "");
                int local;
                if (family.Name.Length == 0
                    || !int.TryParse(Text(xml, "ID", ""), out local))
                {
                    missed += (missed.Length > 0 ? ", " : "") + file + " (no name or id)";
                    continue;
                }
                family.LocalId = local;
                if (!blockIds.Contains(local))
                {
                    blockIds.Add(local);
                }

                foreach (Match type in Regex.Matches(xml, "<Type\\s[^>]*name=\"([^\"]*)\""))
                {
                    family.Types.Add(type.Groups[1].Value);
                }
                family.DefaultType = Default(xml, family.Types);
                // A block with no instruments in it is not one a song can go to --
                // which is how the loader block itself is kept out of the list.
                if (family.Types.Count > 0)
                {
                    found.Add(family);
                }
            }
            found.Sort(ByName2);
            families = found;

            // Said once, and said in full: when this comes back empty the loader
            // block can do nothing at all, and the difference between "the manifest
            // named nothing", "the files would not open" and "they held no types"
            // is the difference between three quite different faults.
            Log.Info("catalogue: " + named + " block(s) named in Mod.xml, " + read
                     + " read, " + families.Count + " with instruments in them ["
                     + Named() + "]"
                     + (missed.Length > 0 ? "; could not read: " + missed : ""));
            if (families.Count == 0)
            {
                Log.Warn("no instrument blocks could be read out of the mod folder; "
                         + "the MIDI loader has nothing to write a song for.");
            }
        }

        /// <summary>
        /// Which instrument a block starts on, from the `default` attribute on its
        /// module element.
        ///
        /// Anchored on `&lt;OrchestraMod` rather than looking for any `default=`:
        /// an Extra carries one of its own, and matching that would set the block to
        /// whatever a slider happened to start at.
        /// </summary>
        private static int Default(string xml, List<string> types)
        {
            Match m = Regex.Match(xml, "<OrchestraMod[^>]*\\sdefault=\"([^\"]*)\"");
            if (!m.Success)
            {
                return 0;
            }
            string wanted = m.Groups[1].Value.Trim();
            for (int i = 0; i < types.Count; i++)
            {
                if (string.Compare(types[i], wanted, true) == 0)
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>The blocks and how many instruments each holds, for the log.</summary>
        private static string Named()
        {
            string said = "";
            for (int i = 0; i < families.Count; i++)
            {
                said += (said.Length > 0 ? ", " : "") + families[i].Name + " x"
                     + families[i].Types.Count.ToString();
            }
            return said;
        }

        private static int ByName2(Family a, Family b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        /// <summary>The text of the first &lt;tag&gt; in a document.</summary>
        private static string Text(string xml, string tag, string fallback)
        {
            Match m = Regex.Match(xml, "<" + tag + ">([^<]*)</" + tag + ">");
            return m.Success ? m.Groups[1].Value.Trim() : fallback;
        }
    }
}
