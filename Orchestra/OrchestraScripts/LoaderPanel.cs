using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrchestraMod
{
    /// <summary>
    /// The MIDI loader block's menu: where the files are, which one, what it comes
    /// to in blocks, and the two things that can be done with it.
    ///
    /// Besiege's own mapper above keeps **the key and nothing else** -- every timer
    /// this block writes waits for that key, so binding it there binds the whole
    /// song, and rebinding is the mapper's own business. The settings themselves
    /// are all here, drawn as the instrument blocks' are: the game's own
    /// `&lt; choice &gt;` selectors for the block and the instrument within it, and
    /// slider rows with a box at the end for anything the handle will not reach.
    ///
    /// Top to bottom: the two selectors; volume, range, transpose and delay; the
    /// folder to put MIDI files in, as a box that can be typed into, with buttons
    /// to open it and to list it again; the files in that folder; the summary; and
    /// ADD / SAVE.
    ///
    /// UI Factory is a soft dependency for the rest of this mod and a hard one for
    /// this block: Besiege's mapper has no text box, no list and no button, so
    /// without it there is nowhere to choose a file at all. The block says so once
    /// rather than sitting there inert.
    /// </summary>
    public class LoaderPanel : DockedPanel
    {
        private const float ButtonHeight = RowHeight * 1.5f;


        /// <summary>Two lines of path at 13pt, which is what a real install's
        /// folder comes to across a mapper-width window.</summary>
        private const float PathHeight = 34f;

        /// <summary>How many lines the summary is given. Fixed, so the window is
        /// the same height whatever it has to say and does not jump about as a
        /// file is chosen.</summary>
        private const int SummaryLines = 4;


        /// <summary>
        /// The panel could not be built, so Besiege's own mapper is the only way to
        /// set this block and must keep its controls. Static because it is the
        /// block that acts on it, and there is one panel for all of them.
        /// </summary>
        public static bool Failed { get; private set; }

        private LoaderBehaviour block;
        private bool hooked;
        private bool built;
        private bool failed;

        private readonly List<Text> summary = new List<Text>();
        private Text status;
        private Color statusInk = UIF.QuietInk;

        /// <summary>The folder the files are listed from -- its name, which is the
        /// part worth typing -- and the whole path underneath, which is the part
        /// worth reading.</summary>
        private InputField folderBox;
        private Text folderPath;

        /// <summary>What is in the Songs folder, by name, as of the last look.</summary>
        private readonly List<string> files = new List<string>();

        /// <summary>The list of them. The same control as the two selectors, minus
        /// its arrows: a folder of thirty files is thirty presses of an arrow, and
        /// the whole list is one click.</summary>
        private Chooser fileList;

        /// <summary>The two selectors: which block a song is written for, and which
        /// of that block's instruments. The second is refilled whenever the first
        /// moves. `shownFamily` and the rest are what the panel last saw them at,
        /// these controls raising no event of their own.</summary>
        private Chooser familyOption;
        private Chooser typeOption;
        private int shownFamily = -1;
        private int shownType = -1;
        private int shownFile = -1;

        /// <summary>Every setting that changes what the machine comes out as, as
        /// one string: when it differs from what the summary was drawn for, the
        /// file is read again. Cheaper than working out which control moved, and it
        /// catches Besiege's own mapper moving one.</summary>
        private string shownFor;

        // ---- lifetime --------------------------------------------------------

        private void Start()
        {
            try
            {
                BlockMapper.onMapperOpen += OnMapperOpen;
                BlockMapper.onMapperClose += OnMapperClose;
                hooked = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not watch the block mapper, so no loader panel: "
                         + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (!hooked)
            {
                return;
            }
            try
            {
                BlockMapper.onMapperOpen -= OnMapperOpen;
                BlockMapper.onMapperClose -= OnMapperClose;
            }
            catch (Exception)
            {
                // Nothing useful to do while the game is being torn down.
            }
            hooked = false;
        }

        private void OnMapperOpen()
        {
            LoaderBehaviour opened = null;
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper != null && mapper.Block != null)
                {
                    opened = mapper.Block.GetComponent<LoaderBehaviour>();
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not tell which block the mapper opened on: " + e.Message);
            }

            if (opened == null)
            {
                Hide();
                return;
            }
            Show(opened);
        }

        private void OnMapperClose()
        {
            Hide();
        }

        private void Show(LoaderBehaviour on)
        {
            block = on;
            if (!UIF.Available)
            {
                Complain();
                return;
            }

            // Both before building, not after: the rows are laid out to a width
            // and to a file count, and a window built to the wrong one would be
            // seen at it for a frame.
            Look();
            Rect frame;
            if (MapperFrame(out frame) && Widen(frame))
            {
                built = false;
            }
            if (!Build())
            {
                return;
            }
            window.SetActive(true);
            if (block.Plan == null && !string.IsNullOrEmpty(block.Path))
            {
                // A block that was saved with a file chosen shows what that file
                // comes to, rather than an empty summary nobody asked to refresh.
                block.Analyse();
            }
            Read();
            Canvas.ForceUpdateCanvases();
            Dock();
        }

        private void Hide()
        {
            block = null;
            if (window != null)
            {
                window.SetActive(false);
            }
        }

        /// <summary>Said once, when the one block that needs UI Factory is opened
        /// without it.</summary>
        private static bool complained;

        private static void Complain()
        {
            if (complained)
            {
                return;
            }
            complained = true;
            Log.Warn("the MIDI loader block needs UI Factory 3 (Workshop item "
                     + "2913469777) for its file list, its settings and its "
                     + "buttons; without it, tools/make-song.py does the same job "
                     + "outside the game.");
        }

        // ---- building --------------------------------------------------------

        private bool Build()
        {
            if (failed || !UIF.Available)
            {
                return false;
            }
            if (built)
            {
                return true;
            }
            try
            {
                Teardown();
                OpenWindow("Orchestra loader panel");

                // One order, no conditional rows: the window is the same height
                // whatever the folder holds and whatever the file turned out to be,
                // so it neither jumps as a file is chosen nor has to be rebuilt
                // when the list changes.
                float y = Margin;
                y = BuildSelectors(y);
                y = BuildSliders(y);
                y = BuildFolder(y);
                y = BuildList(y);
                y = BuildSummary(y);
                y = BuildButtons(y);
                CloseWindow(y);
                built = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not build the loader panel, so the stock mapper "
                         + "stands: " + e.Message);
                failed = true;
                Failed = true;
                Teardown();
            }
            return built;
        }

        private void Teardown()
        {
            summary.Clear();
            fileList = null;
            familyOption = null;
            typeOption = null;
            shownFamily = -1;
            shownType = -1;
            folderBox = null;
            folderPath = null;
            status = null;
            DestroyWindow();
            built = false;
        }

        /// <summary>
        /// The folder to put MIDI files in.
        ///
        /// The box holds its *name*, not the whole path: the name is the part that
        /// can be changed -- it is always a folder inside the mod's own data
        /// directory, Besiege refusing a mod anything outside that -- and the path
        /// is written out underneath where it can be read. Beside it, the two
        /// things worth doing with a folder: open it, and list it again.
        ///
        /// A box rather than the button it was: a button carries UI Factory's hover
        /// swell, and a whole path grown by an eighth spills out of its own row.
        ///
        /// There is no file-dialog button. `SFB.StandaloneFileBrowser` can show the
        /// whole disk and `ModIO` can open none of it but the mod's own folders, so
        /// a dialog could only ever hand back something unreadable.
        /// </summary>
        private float BuildFolder(float y)
        {
            Label("FOLDER", Margin, y, LabelWidth, RowHeight, 13,
                  TextAnchor.MiddleLeft, UIF.QuietInk);

            float side = RowHeight;
            float x = Margin + LabelWidth;
            // Two buttons and their gaps, the margin, and a gap of its own -- the
            // box stopped under the first button before that last one was counted.
            float w = width - Margin * 2f - LabelWidth - (side + RowGap) * 2f - RowGap;
            folderBox = Typing.On(Box(x, y, w, TextAnchor.MiddleLeft, 60,
                                      Files.DefaultSongFolder));
            if (folderBox != null)
            {
                folderBox.onEndEdit.AddListener(
                    new UnityEngine.Events.UnityAction<string>(Refolder));
            }

            float at = width - Margin - side * 2f - RowGap;
            Icon(IconArt.Folder(), "Open folder", at, y, side,
                 new UnityEngine.Events.UnityAction(OpenFolder));
            Icon(IconArt.Reload(), "Reload", at + side + RowGap, y, side,
                 new UnityEngine.Events.UnityAction(Refresh));

            // Where that folder actually is, said in full and not typed into: the
            // box holds the name because that is the part anybody would change,
            // and a whole path in a box that narrow is unreadable and unclickable.
            //
            // Across the whole window and wrapped onto a second line: a path is
            // longer than any row here, and cutting it off hides the half that
            // says which install it is.
            y += RowHeight + 2f;
            folderPath = Label("", Margin, y, width - Margin * 2f, PathHeight, 13,
                               TextAnchor.UpperLeft, UIF.QuietInk);
            if (folderPath != null)
            {
                folderPath.horizontalOverflow = HorizontalWrapMode.Wrap;
                folderPath.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return y + PathHeight + RowGap;
        }

        /// <summary>
        /// What is in that folder, in the same control as the selectors above but
        /// with no arrows: clicking it opens the whole list.
        ///
        /// A `Chooser` rather than a uGUI `Dropdown` for the reason the drop-down
        /// was abandoned everywhere else in this mod's family -- a Dropdown parents
        /// its open list to itself, so in a window built on a scroll view the list
        /// is clipped to the window. The Chooser hangs its list off the canvas.
        /// </summary>
        private float BuildList(float y)
        {
            Label("FILE", Margin, y, LabelWidth, RowHeight, 13,
                  TextAnchor.MiddleLeft, UIF.QuietInk);
            fileList = Chooser.Make(host, transform, Margin + LabelWidth, y,
                                    width - Margin * 2f - LabelWidth, RowHeight,
                                    Listed(), Chosen(), false);
            return y + RowHeight + RowGap * 2f;
        }

        /// <summary>The files, with a line in front of them for "none of these":
        /// the control always shows one of its entries, and until a file has been
        /// picked none of them is the answer.</summary>
        private List<string> Listed()
        {
            List<string> shown = new List<string>();
            shown.Add(files.Count == 0
                ? "(no .mid files in that folder)" : "(choose a file)");
            shown.AddRange(files);
            return shown;
        }

        /// <summary>Which line of that list the block's own file is.</summary>
        private int Chosen()
        {
            string chosen = block == null ? "" : Files.Leaf(block.Path);
            int at = files.IndexOf(chosen);
            return at < 0 ? 0 : at + 1;
        }

        /// <summary>
        /// The block a song is written for, and which of its instruments -- the
        /// game's own `&lt; choice &gt;` selectors, the same control the instrument
        /// panel puts its own instrument in, so the two menus match.
        /// </summary>
        private float BuildSelectors(float y)
        {
            familyOption = AddSelector("INSTRUMENT", y, block.Families,
                                       block.Instruments == null
                                           ? 0 : block.Instruments.Value);
            y += RowHeight + RowGap;
            block.RetypeMenu();
            typeOption = AddSelector("TYPE", y, block.Types == null
                                         ? new List<string>() : block.Types.Items,
                                     block.Types == null ? 0 : block.Types.Value);
            return y + RowHeight + RowGap * 2f;
        }

        /// <summary>
        /// The block's own settings, drawn as the instrument blocks' are: a caption,
        /// a slider, and a box at the end holding the number, which can be typed
        /// into for anything the handle will not reach.
        /// </summary>
        private float BuildSliders(float y)
        {
            if (block == null)
            {
                return y;
            }
            y = AddSlider(y, "VOLUME", block.Volume, false);
            y = AddSlider(y, "RANGE", block.Range, false);
            y = AddSlider(y, "TRANSPOSE", block.Transpose, false);
            y = AddSlider(y, "DELAY", block.Delay, false);
            return y + RowGap;
        }

        /// <summary>One of UI Factory's square picture buttons, with a drawn
        /// glyph on it.</summary>
        private void Icon(Sprite glyph, string name, float x, float y, float side,
                          UnityEngine.Events.UnityAction done)
        {
            GameObject button = UIF.Spawn(UIF.IconButtonPrefab, host);
            if (button == null)
            {
                return;
            }
            button.name = name;
            Place(button, x, y, side, side);
            Transform icon = button.transform.FindChild("Icon");
            Image face = icon == null
                ? button.GetComponentInChildren<Image>(true) : icon.GetComponent<Image>();
            if (face != null)
            {
                face.sprite = glyph;
                face.color = UIF.QuietInk;
                // The drawing is inset within its own square, so the picture is the
                // right size while the whole button stays the thing that is clicked.
                face.preserveAspect = false;
            }
            Button click = button.GetComponent<Button>();
            if (click != null)
            {
                click.onClick.AddListener(done);
            }
        }

        private float BuildSummary(float y)
        {
            for (int i = 0; i < SummaryLines; i++)
            {
                Text line = Label("", Margin, y, width - Margin * 2f, 18f, 12,
                                  TextAnchor.MiddleLeft, UIF.QuietInk);
                summary.Add(line);
                y += 18f;
            }
            y += RowGap;
            status = Label("", Margin, y, width - Margin * 2f, 20f, 12,
                           TextAnchor.MiddleLeft, UIF.QuietInk);
            return y + 20f + RowGap * 2f;
        }

        /// <summary>The two things that can be done with a converted song, side by
        /// side: into the machine being built, or out to a machine of its own.</summary>
        private float BuildButtons(float y)
        {
            float cell = (width - Margin * 2f - RowGap) / 2f;
            Press("ADD TO MACHINE", Margin, y, cell, ButtonHeight,
                  new UnityEngine.Events.UnityAction(Add));
            Press("SAVE AS MACHINE", Margin + cell + RowGap, y, cell, ButtonHeight,
                  new UnityEngine.Events.UnityAction(Save));
            return y + ButtonHeight + RowGap * 2f;
        }

        /// <summary>
        /// A button with a word on it. UI Factory's own swell is left alone here --
        /// a button that grows 15% under the pointer is what the rest of Besiege's
        /// interface does, and it is how a row says it can be clicked. (The
        /// instrument panel takes it off its full-width *toggles*, where growing
        /// the row carries the lettering out of the window.)
        /// </summary>
        private GameObject Press(string caption, float x, float y, float w, float h,
                                 UnityEngine.Events.UnityAction done)
        {
            GameObject go = UIF.Spawn(UIF.ButtonPrefab, host);
            if (go == null)
            {
                return null;
            }
            Place(go, x, y, w, h);
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                UIF.Untranslate(label);
                label.text = caption;
                label.fontSize = 13;
                label.resizeTextForBestFit = false;
                UIF.EnsureFont(label);
            }
            Button click = go.GetComponent<Button>();
            if (click == null)
            {
                click = go.GetComponentInChildren<Button>(true);
            }
            if (click != null)
            {
                click.onClick.AddListener(done);
            }
            return go;
        }

        /// <summary>One of Besiege's own text boxes, which is also what keeps the
        /// game's hotkeys from firing at whatever is being typed into it.</summary>
        private InputField Box(float x, float y, float w, TextAnchor align, int limit,
                               string ghostText)
        {
            GameObject go = UIF.Spawn(UIF.InputPrefab, host);
            if (go == null)
            {
                return null;
            }
            Place(go, x, y, w, RowHeight);
            InputField field = go.GetComponent<InputField>();
            if (field == null)
            {
                field = go.GetComponentInChildren<InputField>(true);
            }
            if (field == null)
            {
                return null;
            }
            Style(field.textComponent, align, Color.white);
            Text ghost = field.placeholder as Text;
            if (ghost != null)
            {
                UIF.Untranslate(ghost);
                Style(ghost, align, UIF.QuietInk);
                ghost.text = ghostText;
            }
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = limit;
            return field;
        }

        // ---- what it says ----------------------------------------------------

        /// <summary>Puts the block's state into the controls.</summary>
        private void Read()
        {
            if (!built || block == null)
            {
                return;
            }
            filling = true;
            if (folderBox != null && !folderBox.isFocused)
            {
                folderBox.text = Files.SongFolder;
            }
            if (folderPath != null)
            {
                string folder = Files.SongsPath();
                folderPath.text = folder.Length == 0
                    ? "(inside the mod's data folder)" : folder;
            }
            ShowFiles();
            ShowChoices();
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.Control != null && row.Bound != null)
                {
                    float low, high;
                    Span(row.Bound, out low, out high);
                    row.Control.minValue = low;
                    row.Control.maxValue = high;
                    row.Control.value = row.Bound.Value;
                }
                Write(row);
            }
            Describe();
            filling = false;
        }

        /// <summary>
        /// The summary: how long the song is, how many notes survived, and what it
        /// will cost in blocks. Written from the plan rather than from the file, so
        /// the numbers are the ones the machine will actually have.
        /// </summary>
        private void Describe()
        {
            for (int i = 0; i < summary.Count; i++)
            {
                if (summary[i] != null)
                {
                    summary[i].text = "";
                }
            }
            if (block == null)
            {
                return;
            }
            if (block.Plan == null)
            {
                Line(0, string.IsNullOrEmpty(block.Path)
                    ? "No file chosen yet."
                    : "Nothing read from that file yet.");
                Say(block.Trouble, block.Trouble == null ? UIF.QuietInk : Bad);
                return;
            }

            SongPlan plan = block.Plan;
            Line(0, "Length  " + Clock(plan.Seconds)
                  + "     Notes  " + plan.Notes.ToString());
            // The count ADD puts in the machine. Saving writes one more, the
            // starting block every machine of its own has to have.
            Line(1, "Blocks  " + (plan.Voices + plan.Timers).ToString()
                  + "  =  " + plan.Voices.ToString() + " instrument"
                  + (plan.Voices == 1 ? "" : "s")
                  + " + " + plan.Timers.ToString() + " timer"
                  + (plan.Timers == 1 ? "" : "s")
                  + ", +1 starting block when saved");
            Line(2, "Parts   " + Joined(plan.Parts));

            string lost = "";
            if (plan.Crowded > 0)
            {
                lost = plan.Crowded.ToString() + " note"
                     + (plan.Crowded == 1 ? "" : "s")
                     + " fell inside another on the same block";
            }
            if (plan.Dropped > 0)
            {
                lost += (lost.Length > 0 ? ", and " : "")
                     + plan.Dropped.ToString() + " past the note limit went";
            }
            // The key is the one setting still in Besiege's own mapper, so the
            // panel says what it is set to rather than leaving the player to look.
            string starts = block.OnSimulationStart
                ? "Starts with the simulation -- no key is bound."
                : "Starts " + block.Delay.Value.ToString("0.0") + " s after "
                  + block.KeyName() + " is pressed.";
            Line(3, lost.Length > 0 ? lost : starts);
            shownFor = Fingerprint();
            Say(block.Trouble, block.Trouble == null ? UIF.QuietInk : Bad);
        }

        private static readonly Color Bad = new Color(0.93f, 0.45f, 0.35f, 1f);
        private static readonly Color Good = new Color(0.55f, 0.85f, 0.55f, 1f);

        private void Line(int index, string text)
        {
            if (index < summary.Count && summary[index] != null)
            {
                summary[index].text = text;
            }
        }

        private void Say(string text, Color ink)
        {
            statusInk = ink;
            if (status != null)
            {
                status.text = text == null ? "" : text;
                status.color = ink;
            }
        }

        /// <summary>Seconds as minutes and seconds, which is how long a song is.</summary>
        private static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (whole / 60).ToString() + ":" + (whole % 60).ToString("00");
        }

        /// <summary>The parts, as one line that fits: the rest is a count.</summary>
        private static string Joined(List<string> parts)
        {
            if (parts.Count == 0)
            {
                return "none";
            }
            string line = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                if (line.Length + parts[i].Length > 46)
                {
                    return line + " (+" + (parts.Count - i).ToString() + " more)";
                }
                line += ", " + parts[i];
            }
            return line;
        }

        /// <summary>Reads the folder again, without touching the window.</summary>
        private void Look()
        {
            files.Clear();
            files.AddRange(Files.Songs());
        }

        /// <summary>Puts the folder's files in the dropdown, and shows whichever
        /// of them is chosen.</summary>
        private void ShowFiles()
        {
            if (fileList == null)
            {
                return;
            }
            fileList.Set(Listed(), Chosen());
            shownFile = fileList.Index;
        }

        /// <summary>
        /// Puts the two selectors on what the block is set to, and the type
        /// selector on the blocks the *first* one names -- which is the whole of
        /// why the second is refilled here rather than built once.
        /// </summary>
        private void ShowChoices()
        {
            if (block == null)
            {
                return;
            }
            shownFamily = block.Instruments == null ? 0 : block.Instruments.Value;
            if (familyOption != null)
            {
                familyOption.Set(block.Families, shownFamily);
            }

            block.RetypeMenu();
            shownType = block.Types == null ? 0 : block.Types.Value;
            if (typeOption != null)
            {
                typeOption.Set(block.Types == null
                    ? new List<string>() : block.Types.Items, shownType);
            }
        }

        /// <summary>
        /// Another folder was typed. Only somewhere inside the mod's own data
        /// directory will do -- that is the whole of what a mod may read -- so the
        /// box says why rather than letting `ModIO` throw at the next file.
        /// </summary>
        private void Refolder(string text)
        {
            if (filling)
            {
                return;
            }
            string refused = Files.SetSongFolder(text);
            if (refused == null)
            {
                Say("Listing " + Files.SongsPath(), UIF.QuietInk);
            }
            else if (refused.Length > 0)
            {
                Say(refused, Bad);
            }
            // Whatever was typed, the box goes back to showing the folder that is
            // actually being listed -- so a name that was refused, or a box that
            // was left empty, does not sit there looking like the setting.
            Look();
            Read();
        }

        // ---- input -----------------------------------------------------------

        /// <summary>Opens that folder in the desktop's own file manager, which is
        /// where a MIDI file has to be put for the list below to have anything in
        /// it -- and, Besiege letting a mod read nowhere else, the only place a
        /// file can be put at all.</summary>
        private void OpenFolder()
        {
            Files.ShowSongFolder();
            Say("Put .mid files in that folder, then press the reload arrow.",
                UIF.QuietInk);
        }

        /// <summary>
        /// The reload arrow: looks at the folder again. A file dropped in while
        /// Besiege is running is otherwise invisible until the game is restarted,
        /// which is what this is for.
        /// </summary>
        private void Refresh()
        {
            Look();
            Read();
            Say(files.Count == 0
                ? "Nothing in that folder yet."
                : files.Count.ToString() + " file"
                  + (files.Count == 1 ? "" : "s") + " in that folder.", UIF.QuietInk);
        }

        /// <summary>
        /// A file was chosen from the list.
        ///
        /// **Its name is kept, not its path.** `ModIO` is the only file API a mod
        /// has, and while it will take an absolute path, the form it is meant for
        /// -- and the form that cannot be wrong about where the mod's data folder
        /// is -- is a path relative to that folder. So the block remembers
        /// "waltz.mid", <see cref="Files.Read"/> looks for it in the Songs folder
        /// first, and only a path typed by hand is ever resolved absolutely.
        /// </summary>
        private void Chose(int which)
        {
            // Line 0 is "(choose a file)", so the files start at one.
            which -= 1;
            if (block == null || filling || which < 0 || which >= files.Count)
            {
                return;
            }
            block.Path = files[which];
            block.Analyse();
            Describe();
        }

        private void Add()
        {
            if (block == null)
            {
                return;
            }
            int placed = block.AddToMachine();
            Describe();
            if (placed < 0)
            {
                Say(block.Trouble == null ? "nothing to add" : block.Trouble, Bad);
                return;
            }
            Say(placed.ToString() + " blocks added, and selected -- drag them where "
                + "you want them.", Good);
        }

        /// <summary>
        /// Saving a song as a machine of its own, through Besiege's own save
        /// screen.
        ///
        /// A mod cannot write the file itself. `ModIO` refuses any path outside the
        /// mod's own folders -- `ModPaths.GetFilePath` walks the target's directory
        /// upwards looking for the mod's and throws "Path is not in mod directory!"
        /// if it never arrives -- and `XmlSaver.Save`, the game's own writer, is one
        /// of the four methods the loader forbids by name, with every caller of it
        /// private. So the blocks go into the machine first, selected, and Besiege's
        /// own machine save screen is opened over the top: its SELECTION ONLY button
        /// saves exactly what was just added, it asks about a name already taken,
        /// and it renders the thumbnail, none of which this could do.
        ///
        /// Where that screen cannot be found, the `.bsg` is written into the mod's
        /// own data folder instead and the panel says where -- a real machine file,
        /// one copy away from the machine list.
        /// </summary>
        private void Save()
        {
            if (block == null)
            {
                return;
            }
            int placed = block.AddToMachine();
            if (placed < 0)
            {
                Describe();
                Say(block.Trouble == null ? "nothing to save" : block.Trouble, Bad);
                return;
            }

            FileBrowserView screen = SaveScreen();
            if (screen != null)
            {
                Say(placed.ToString() + " blocks added and selected. Use SELECTION "
                    + "ONLY to save just them.", Good);
                Describe();
                // This closes the block mapper, and the panel with it.
                screen.Open(FileBrowserType.LocalMachines, true, true);
                return;
            }

            string written = block.SaveAs(block.Plan == null ? "Song" : block.Plan.Name);
            Describe();
            if (written == null)
            {
                Say(block.Trouble == null ? "could not save that" : block.Trouble, Bad);
                return;
            }
            Say("Besiege's save screen is not up; written to " + written
                + " -- copy it into SavedMachines.", Good);
        }

        /// <summary>
        /// Besiege's file browser, which is inactive while it is closed -- so it
        /// has to be looked for among *all* loaded objects rather than with
        /// `FindObjectOfType`, which only sees active ones.
        /// </summary>
        private static FileBrowserView SaveScreen()
        {
            try
            {
                FileBrowserView[] all =
                    Resources.FindObjectsOfTypeAll<FileBrowserView>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.scene.IsValid())
                    {
                        // A prefab is in no scene; the one in the level is.
                        return all[i];
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not find Besiege's save screen: " + e.Message);
            }
            return null;
        }

        // ---- keeping up ------------------------------------------------------

        private void LateUpdate()
        {
            if (block != null && built)
            {
                Dock();
            }
        }

        /// <summary>
        /// Notices the player working either selector.
        ///
        /// Polled rather than subscribed: `onValueChanged` is UI Factory's own event
        /// type, and reading two integers while the panel is open is cheaper than
        /// binding to a signature that could change beneath the mod. Moving the
        /// first selector refills the second, which is the whole reason the type
        /// list is not built once.
        /// </summary>
        private void Update()
        {
            if (block != null && built)
            {
                WatchSelectors();
                // Not while the mouse is down: a slider drag would re-read the
                // whole score every frame, and a long one takes long enough to be
                // felt. The drag settles into one read when the button comes up.
                if (block.Plan != null && !Input.GetMouseButton(0)
                    && Fingerprint() != shownFor)
                {
                    Reread();
                }
            }
            SettleDrags();
        }

        private void WatchSelectors()
        {
            if (block.Instruments == null || block.Types == null)
            {
                return;
            }
            // The file list is polled the same way, its control reporting no
            // event of its own.
            if (fileList != null && fileList.Index != shownFile)
            {
                shownFile = fileList.Index;
                Chose(shownFile);
            }
            int family = familyOption == null ? -1 : familyOption.Index;
            if (family >= 0 && family != shownFamily && family < block.Families.Count)
            {
                shownFamily = family;
                block.Instruments.Value = family;
                Queue(block.Instruments);
                // The instruments under it are different ones, so the second
                // selector is refilled and its choice reset to the first of them.
                block.RetypeMenu();
                block.Types.Value = 0;
                Queue(block.Types);
                shownType = 0;
                if (typeOption != null)
                {
                    typeOption.Set(block.Types.Items, 0);
                }
                Reread();
                return;
            }

            int type = typeOption == null ? -1 : typeOption.Index;
            if (type >= 0 && type != shownType && block.Types != null
                && type < block.Types.Items.Count)
            {
                shownType = type;
                block.Types.Value = type;
                Queue(block.Types);
                Reread();
            }
        }

        /// <summary>Any setting decides what the machine comes out as, so changing
        /// one changes the summary: the file is read again.</summary>
        private void Reread()
        {
            if (block == null || string.IsNullOrEmpty(block.Path))
            {
                return;
            }
            block.Analyse();
            Describe();
        }

        private string Fingerprint()
        {
            if (block == null)
            {
                return "";
            }
            return block.Path + "|" + block.Instrument + "|"
                 + block.Volume.Value.ToString("0.###") + "|"
                 + block.Range.Value.ToString("0.###") + "|"
                 + block.Transpose.Value.ToString("0.###") + "|"
                 + block.Delay.Value.ToString("0.###") + "|"
                 + (block.KeyName() == null ? "-" : block.KeyName());
        }

        /// <summary>
        /// The rows are laid out to the mapper's width, which changes with the
        /// screen: rebuilt, and refilled from the block, in the same frame.
        /// </summary>
        protected override bool Rebuild()
        {
            string said = status == null ? null : status.text;
            Color ink = statusInk;
            built = false;
            if (!Build())
            {
                return false;
            }
            Read();
            Say(said, ink);
            return true;
        }
    }
}
