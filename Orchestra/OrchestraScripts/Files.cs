using System;
using System.Collections.Generic;

namespace OrchestraMod
{
    /// <summary>
    /// Everything this mod does with the disk.
    ///
    /// `System.IO.File` and `Directory` are blacklisted, so `Modding.ModIO` is the
    /// only way in. (Written out in full throughout: there is also a `ModIO`
    /// *namespace*, and `using Modding` makes the short name mean that one.)
    ///
    /// Three of its rules decide the shape of everything here, and all three were
    /// read out of `ModPaths.GetFilePath`'s IL after they had already cost
    /// something:
    ///
    /// 1. **A mod may only reach its own folders.** The method resolves what it is
    ///    given, then walks that path's directory upwards looking for the mod's
    ///    own, and throws `Path is not in mod directory!` if it never arrives. An
    ///    absolute path elsewhere -- a MIDI file the player picked, Besiege's
    ///    SavedMachines -- is refused, whatever `Path.Combine` did with it.
    /// 2. **A folder argument must end in a slash.** Without one the resolved path
    ///    is treated as a *file*, and the folder acted on is its parent. That is
    ///    why `GetFiles("")` does not list the mod's folder: it lists `Mods/`,
    ///    which then fails rule 1 and throws -- which is how the block catalogue
    ///    came back empty and the loader said the instrument blocks could not be
    ///    read.
    /// 3. It works out which mod is calling from the **calling assembly**, so this
    ///    has to live in an assembly the manifest lists.
    ///
    /// The relative form with `data: true` lands in
    /// `Besiege_Data/Mods/Data/&lt;mod&gt;_&lt;guid&gt;/`, which is where the Songs
    /// folder is and the only place a player can put a file this mod can open.
    /// </summary>
    public static class Files
    {
        /// <summary>Where a player drops MIDI files, under the mod's data folder.
        /// The default; <see cref="SongFolder"/> is what is actually used.</summary>
        public const string DefaultSongFolder = "Songs";

        /// <summary>Where the songs are kept, as a path under the mod's data
        /// folder. Editable in the panel, and remembered between sessions -- but
        /// always inside that folder, rule 1 above leaving nowhere else to look.</summary>
        private static string songFolder;

        /// <summary>Remembers that folder, so it need only be set once.</summary>
        private const string FolderFile = "songs-folder.txt";

        public static string SongFolder
        {
            get
            {
                if (songFolder == null)
                {
                    songFolder = Tidy(Remember(FolderFile, null));
                }
                return songFolder;
            }
        }

        /// <summary>
        /// Points the panel at another folder under the mod's data directory.
        ///
        /// Takes what the player is shown -- the whole path -- as well as a plain
        /// name, and refuses anything outside the data folder rather than letting
        /// `ModIO` throw at it later. Returns null if it took, or why it did not.
        /// </summary>
        public static string SetSongFolder(string wanted)
        {
            string root = DataPath();
            string trimmed = (wanted == null ? "" : wanted.Trim()).Replace('\\', '/');
            while (trimmed.EndsWith("/"))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }
            if (root.Length > 0 && trimmed.ToLower().StartsWith(root.ToLower()))
            {
                trimmed = trimmed.Substring(root.Length).TrimStart('/');
            }
            if (trimmed.Length == 0)
            {
                // An empty box is not an instruction. It means the player cleared
                // it and thought better of it, or something else cleared it for
                // them -- see the note on Typing.
                return "";
            }
            if (trimmed.StartsWith("/") || trimmed.IndexOf(':') >= 0
                || trimmed.StartsWith(".."))
            {
                return "Besiege only lets a mod read its own folders, so this has "
                     + "to be inside " + root;
            }
            if (!Plain(trimmed))
            {
                // Nothing but a folder name gets written to disk. A field can end
                // up holding anything -- one of these once collected a run of
                // fullwidth digits from somewhere and made a folder out of it every
                // time the panel opened -- and a name is cheap to check.
                return "a folder name here, made of letters, digits, spaces, "
                     + "- _ . and /";
            }
            if (trimmed == songFolder)
            {
                return null;                // nothing to write
            }
            songFolder = trimmed;
            Remember(FolderFile, songFolder);
            return null;
        }

        /// <summary>Whether a name is one this mod is prepared to make a folder
        /// of: plain ASCII, and nothing that means something to a path.</summary>
        private static bool Plain(string name)
        {
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9')
                       || c == ' ' || c == '-' || c == '_' || c == '.' || c == '/';
                if (!ok)
                {
                    return false;
                }
            }
            return name.Length > 0 && name.Length < 100;
        }

        private static string Tidy(string folder)
        {
            if (folder == null || folder.Trim().Length == 0)
            {
                return DefaultSongFolder;
            }
            return folder.Trim();
        }

        /// <summary>Remembers the last file converted, so the box is not empty
        /// every time the block is opened.</summary>
        private const string LastFile = "last-midi.txt";

        // ---- reading ---------------------------------------------------------

        /// <summary>
        /// A file's bytes. An absolute path is read where it is; a bare name is
        /// looked for in the Songs folder, so somebody who dropped a file in there
        /// can type "waltz.mid" rather than the whole of where it lives.
        /// </summary>
        public static byte[] Read(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("no file chosen");
            }

            // Always by name, inside the songs folder. A mod may not read anywhere
            // else at all -- rule 1 above -- so a path from somewhere else is not a
            // file this can open, however real it is.
            string inFolder = SongFolder + "/" + Leaf(path);
            if (Ask(inFolder, true))
            {
                byte[] bytes = Modding.ModIO.ReadAllBytes(inFolder, true);
                Log.Info("read " + inFolder + " (" + bytes.Length + " bytes)");
                return bytes;
            }
            Log.Warn("no file at " + inFolder + " under the mod's data folder"
                     + " (asked for '" + path + "')");
            if (path != Leaf(path) && Outside(path))
            {
                throw new Exception("Besiege only lets a mod read files in its own "
                                    + "folders. Copy it into " + SongsPath()
                                    + " and press the reload arrow.");
            }
            throw new Exception("there is no " + Leaf(path) + " in " + SongsPath());
        }

        /// <summary>Whether a file is there, without minding how the question was
        /// refused: `ModIO` throws for a mod it cannot place, and an unreadable
        /// path is a missing file as far as the panel is concerned.</summary>
        private static bool Ask(string path, bool data)
        {
            try
            {
                return Modding.ModIO.ExistsFile(path, data);
            }
            catch (Exception e)
            {
                // Worth saying: the common cause is the path being outside the
                // mod's own folders, which is a refusal rather than a miss, and
                // the two look identical from here.
                Log.Warn("could not ask about " + path + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Text out of the mod's own folder -- its manifest, or a block's
        /// XML. Null if it is not there, which is not worth an exception: the
        /// caller has a sensible answer for a catalogue it could not read.</summary>
        public static string ModText(string relative)
        {
            try
            {
                return Modding.ModIO.ExistsFile(relative, false)
                    ? Modding.ModIO.ReadAllText(relative, false) : null;
            }
            catch (Exception e)
            {
                Log.Warn("could not read " + relative + " out of the mod folder: "
                         + e.Message);
                return null;
            }
        }

        // ---- the songs folder ------------------------------------------------

        /// <summary>
        /// Where the Songs folder is, written out in full so it can be shown to
        /// somebody who has to put a file in it.
        ///
        /// `ModIO` will not say: everything it takes and returns is relative, and
        /// the class that knows -- `InternalModding.Misc.ModPaths` -- is in a
        /// blacklisted namespace. So the same path is built from the same pieces
        /// it uses: `ModManager.DefaultModPath` is `StaticSettings.DataPath +
        /// "/Mods/"`, the data directory is `Data/` inside that, and a mod's own
        /// folder there is its manifest name without spaces, an underscore, and
        /// its GUID.
        /// </summary>
        public static string SongsPath()
        {
            string root = DataPath();
            return root.Length == 0 ? "" : root + "/" + SongFolder;
        }

        /// <summary>The mod's own data folder, written out in full.</summary>
        public static string DataPath()
        {
            string name = Catalogue.ModName.Replace(" ", "");
            string id = Catalogue.ModId;
            if (name.Length == 0 || id.Length == 0)
            {
                return "";                  // the manifest could not be read
            }
            return StaticSettings.DataPath + "/Mods/Data/" + name + "_" + id;
        }

        /// <summary>Every MIDI file in the mod's Songs folder, which is made if it
        /// is not there, by name alone.</summary>
        public static List<string> Songs()
        {
            List<string> found = new List<string>();
            string folder = SongFolder + "/";        // a folder, not a file: rule 2
            try
            {
                if (!Modding.ModIO.ExistsDirectory(folder, true))
                {
                    // Only the default is ever made. A folder the player named and
                    // that is not there is a mistake to be shown, not a folder to
                    // create -- creating them is how a bad name became six empty
                    // directories in somebody's mod data.
                    if (SongFolder != DefaultSongFolder)
                    {
                        Log.Warn("there is no folder " + SongsPath());
                        return found;
                    }
                    Modding.ModIO.CreateDirectory(folder, true);
                }
                string[] all = Modding.ModIO.GetFiles(folder, true);
                for (int i = 0; i < all.Length; i++)
                {
                    string lower = all[i].ToLower();
                    if (lower.EndsWith(".mid") || lower.EndsWith(".midi"))
                    {
                        found.Add(Leaf(all[i]));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not list " + SongsPath() + ": " + e.Message);
            }
            found.Sort();
            Log.Info("songs: " + found.Count + " file(s) in " + SongsPath());
            return found;
        }

        /// <summary>The last part of a path, extension and all.</summary>
        public static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            int cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        /// <summary>Opens the Songs folder in the desktop's own file manager, so a
        /// player can put something in it. Best effort: `ModIO` starts the path as
        /// a process, which not every desktop answers.</summary>
        public static void ShowSongFolder()
        {
            try
            {
                string folder = SongFolder + "/";
                if (!Modding.ModIO.ExistsDirectory(folder, true)
                    && SongFolder == DefaultSongFolder)
                {
                    Modding.ModIO.CreateDirectory(folder, true);
                }
                Modding.ModIO.OpenFolderInFileBrowser(folder, true);
            }
            catch (Exception e)
            {
                Log.Warn("could not open the Songs folder: " + e.Message);
            }
        }

        // ---- what was chosen last --------------------------------------------

        /// <summary>Whether a path names something the mod is not allowed to
        /// open.</summary>
        public static bool Outside(string path)
        {
            string root = DataPath();
            if (string.IsNullOrEmpty(path) || root.Length == 0)
            {
                return false;
            }
            string tidy = path.Replace('\\', '/');
            return tidy.StartsWith("/") && !tidy.ToLower().StartsWith(root.ToLower());
        }

        /// <summary>Reads a remembered value, or writes one when `value` is not
        /// null. One method, because they are one file.</summary>
        private static string Remember(string file, string value)
        {
            try
            {
                if (value != null)
                {
                    Modding.ModIO.WriteAllText(file, value, true);
                    return value;
                }
                return Modding.ModIO.ExistsFile(file, true)
                    ? Modding.ModIO.ReadAllText(file, true).Trim() : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static string Remembered()
        {
            try
            {
                return Modding.ModIO.ExistsFile(LastFile, true)
                    ? Modding.ModIO.ReadAllText(LastFile, true).Trim() : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static void Remember(string path)
        {
            try
            {
                Modding.ModIO.WriteAllText(LastFile, path == null ? "" : path, true);
            }
            catch (Exception)
            {
                // A forgotten path is not worth telling anybody about.
            }
        }

        // ---- saved machines --------------------------------------------------

        /// <summary>Where a machine written by this mod goes, under the mod's own
        /// data folder.</summary>
        public const string MachineFolder = "Machines";

        /// <summary>
        /// Writes a machine into the mod's own data folder, and returns where it
        /// went.
        ///
        /// **Not into Besiege's SavedMachines**, which a mod may not write to: rule
        /// 1 at the top refuses any path outside the mod's folders, and
        /// `XmlSaver.Save` -- the game's own writer -- is one of the four methods
        /// the loader forbids outright. This is the fallback for when Besiege's own
        /// save screen cannot be opened; the file is a real `.bsg` and copying it
        /// into SavedMachines is all it needs.
        /// </summary>
        public static string SaveMachine(string name, string text)
        {
            string clean = StaticSettings.SanatizeFileName(name == null ? "" : name.Trim());
            if (clean.Length == 0)
            {
                clean = "Song";
            }
            string folder = MachineFolder + "/";
            if (!Modding.ModIO.ExistsDirectory(folder, true))
            {
                Modding.ModIO.CreateDirectory(folder, true);
            }
            Modding.ModIO.WriteAllText(folder + clean + ".bsg", text, true);
            return DataPath() + "/" + MachineFolder + "/" + clean + ".bsg";
        }

        /// <summary>The last part of a path, whichever slash it was written with.</summary>
        public static string NameOf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            int cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string leaf = cut < 0 ? path : path.Substring(cut + 1);
            int dot = leaf.LastIndexOf('.');
            return dot <= 0 ? leaf : leaf.Substring(0, dot);
        }
    }
}
