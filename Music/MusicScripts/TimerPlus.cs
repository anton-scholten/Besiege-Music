using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MusicMod
{
    /// <summary>
    /// The Node Editor mod's Timer Plus block, where it is installed.
    ///
    /// A converted song is one timer per note, and that is most of what a song
    /// costs: 700 notes is 700 timer blocks. Timer Plus keeps its timers as *rows
    /// in a table* rather than as blocks -- up to 1024 of them, each with the same
    /// wait, duration and emulate a stock timer has -- so the same song is one
    /// block instead of seven hundred.
    ///
    /// So: if the player has Node Editor, the loader writes Timer Plus; if not, it
    /// writes Besiege's own timers exactly as before. Nothing else about the
    /// machine changes -- the instrument blocks, the variables they listen to and
    /// the key that starts the song are the same either way.
    ///
    /// This class is what the converter needs of another mod's block, which is not
    /// what that block needs of itself: whether it is there, what id to write, and
    /// the text its table is saved as. The same shape <see cref="Braids"/> had
    /// while the synth was somebody else's block.
    /// </summary>
    public static class TimerPlus
    {
        /// <summary>Node Editor's `&lt;ID&gt;`, from its Mod.xml.</summary>
        public const string ModGuid = "5e4cf293-3cb4-494d-8009-fce53ade8e4a";

        /// <summary>What a machine naming it in `requiredMods` calls it.</summary>
        public const string ModName = "Node Editor";

        /// <summary>The `&lt;ID&gt;` of that mod's TimerPlus.xml.</summary>
        public const int LocalId = 1;

        /// <summary>
        /// Rows one block will hold, from `TimerPlusBehaviour.MaxRows`. A song with
        /// more notes than this gets another block, and `Table.Load` stops reading
        /// at this many -- so writing past it would drop notes silently.
        /// </summary>
        public const int MaxRows = 1024;

        // Timer Plus's own mapper keys, from TimerPlusBehaviour.SafeAwake. The
        // per-row emulate keys are not among them: they are built from the table at
        // OnSimulateStart and never saved.
        private const string Activate = "bmt-Activate";
        private const string Automatic = "bmt-AutomaticKey";
        private const string Timers = "bmt-TimersKey";

        /// <summary>What `Table.Save` writes at the head of the table, and what
        /// `Table.Load` refuses to read a table without.</summary>
        private const string Header = "timers 1";

        private static bool asked;
        private static bool there;
        private static int blockType;

        /// <summary>
        /// Whether the block can be written: the mod is loaded *and* its block has
        /// a prefab, which is what an id to write comes from.
        ///
        /// Asked once. `Modding.Mods.IsModLoaded` is the public answer to "has the
        /// player got that mod" -- `InternalModding` is blacklisted, so it is the
        /// only one -- and the prefab table is where an installed mod's block id
        /// comes from. Both, because a mod that is loaded but whose block failed to
        /// register would otherwise be written into a machine as block 0.
        /// </summary>
        public static bool Available
        {
            get
            {
                Look();
                return there && blockType > 0;
            }
        }

        /// <summary>The block id to write. Zero until <see cref="Available"/> has
        /// said yes.</summary>
        public static int BlockType
        {
            get
            {
                Look();
                return blockType;
            }
        }

        /// <summary>
        /// What a machine holding one of these puts in `requiredMods`, or null.
        ///
        /// The version is the installed mod's own rather than one written here:
        /// `ModList.Compare` matches the entries by guid and then compares the
        /// version strings, so a number guessed at is a machine that warns about a
        /// mismatch the player does not have.
        /// </summary>
        public static string RequiredMods
        {
            get
            {
                if (!Available)
                {
                    return null;
                }
                string version = "";
                try
                {
                    Version got = Modding.Mods.GetVersion(new Guid(ModGuid));
                    if (got != null)
                    {
                        version = got.ToString();
                    }
                }
                catch (Exception)
                {
                    // Written without one rather than not written: the guid is what
                    // the warning is matched on.
                }
                return ModGuid + "~L~" + version + "~" + ModName;
            }
        }

        private static void Look()
        {
            if (asked)
            {
                return;
            }
            asked = true;
            try
            {
                there = Modding.Mods.IsModLoaded(new Guid(ModGuid));
            }
            catch (Exception e)
            {
                Log.Warn("could not ask whether " + ModName + " is installed: "
                         + e.Message);
                there = false;
            }
            if (!there)
            {
                return;
            }
            blockType = Registered();
            if (blockType <= 0)
            {
                Log.Warn(ModName + " is installed but its Timer Plus block has no "
                         + "prefab, so songs are written with Besiege's own timers.");
                return;
            }
            Log.Info(ModName + " is installed, so a song's timers go in Timer Plus "
                     + "tables (block " + blockType + ").");
        }

        /// <summary>
        /// The block id behind that mod's block, from the prefab table.
        ///
        /// **A registered prefab is named `&lt;mod guid&gt;-&lt;local id&gt;`** --
        /// `BlockPrefabCreator.CreatePrefab` names it that and
        /// `BlockPrefab.SetNameFromGameObject` copies it over the name the block XML
        /// gave. It is how <see cref="Catalogue"/> finds this mod's own blocks, and
        /// it names another mod's exactly as well.
        /// </summary>
        private static int Registered()
        {
            string wanted = ModGuid + "-" + LocalId;
            try
            {
                foreach (KeyValuePair<int, BlockPrefab> pair in PrefabMaster.BlockPrefabs)
                {
                    BlockPrefab prefab = pair.Value;
                    if (prefab != null && prefab.name == wanted)
                    {
                        return (int)prefab.Type;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not read the block prefabs: " + e.Message);
            }
            return 0;
        }

        /// <summary>One row of the table: when it fires, how long it holds, and the
        /// variable it presses.</summary>
        public class RowData
        {
            public float Wait;
            public float Duration;
            public string Variable;
        }

        /// <summary>
        /// The block's settings, as `TimerPlusBehaviour` declares them.
        ///
        /// The master key is the whole song's: every row waits its own time from
        /// the moment it is pressed, exactly as every stock timer waits its own
        /// time from the moment its own key is. With nothing bound, `AutomaticKey`
        /// starts the rows with the simulation instead -- the same two cases the
        /// stock timers have, decided once per block rather than once per note.
        /// </summary>
        public static void Fill(XDataHolder data, List<RowData> rows,
                                string startKey, string startVariable)
        {
            if (!string.IsNullOrEmpty(startVariable))
            {
                // A keycode goes in the array as well and is never answered to:
                // `Machine.InitSimBlock` files a key once per keycode it holds, so
                // a key with none is registered under no name and hears nothing.
                data.Write(new XStringArray(Activate, new string[]
                {
                    string.IsNullOrEmpty(startKey) ? "B" : startKey,
                    "Message=" + startVariable, "Use=True"
                }));
            }
            else if (string.IsNullOrEmpty(startKey))
            {
                data.Write(new XBoolean(Automatic, true));
            }
            else
            {
                data.Write(new XStringArray(Activate, new string[] { startKey }));
            }
            data.Write(new XString(Timers, Save(rows)));
        }

        /// <summary>
        /// The table as `Table.Save` writes it, which is what `Table.Load` reads:
        /// a header line, then one line per row --
        /// `&lt;wait&gt; &lt;duration&gt; &lt;hold&gt;&lt;stop&gt;&lt;loop&gt; v &lt;variable&gt;`.
        ///
        /// None of the three toggles is wanted: a note is pressed once, for as long
        /// as it lasts. The numbers are round-tripping ("R") and invariant, as that
        /// writer's are -- a comma for a decimal point is a table that loads as
        /// nonsense in one locale and fine in another.
        /// </summary>
        private static string Save(List<RowData> rows)
        {
            StringBuilder text = new StringBuilder(Header).Append('\n');
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                RowData row = rows[i];
                text.Append(row.Wait.ToString("R", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(row.Duration.ToString("R", CultureInfo.InvariantCulture))
                    .Append(" --- v ")
                    .Append(row.Variable == null ? ""
                        : row.Variable.Replace('\n', ' ').Replace('\r', ' '))
                    .Append('\n');
            }
            return text.ToString();
        }
    }
}
