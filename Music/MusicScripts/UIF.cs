using System;
using UnityEngine;
using UnityEngine.UI;

namespace MusicMod
{
    /// <summary>
    /// Thin wrapper over UI Factory 3 (https://gitlab.com/dagriefaa/ui-factory-3),
    /// Workshop item 2913469777.
    ///
    /// UI Factory ships Besiege's real interface as Unity prefabs -- window frame,
    /// buttons, sliders, font, hover and press animations. Instantiating those is
    /// the only way for a mod panel to look like part of the game: Besiege's own
    /// panel materials are reachable only from inside a custom mapper selector, and
    /// `InternalModding` is blacklisted.
    ///
    /// UI Factory is a **soft** dependency here. The block works from its ordinary
    /// mapper without it; only the panel needs it. That is why every reference to
    /// `Besiege.UI` in this mod is in this one file: a type that cannot be resolved
    /// fails as the method mentioning it is compiled, so keeping the mentions in one
    /// class means one guarded call site can tell whether the panel can exist at all
    /// -- see <see cref="Available"/>.
    /// </summary>
    public static class UIF
    {
        /// <summary>The name UI Factory registers its own prefabs under.</summary>
        public const string Package = "UIFactory3";

        // Prefab names, as registered by UI Factory's Mod.OnAllResourcesLoaded. The
        // full list is in Besiege.UI.Mod.OnLoad; these are the ones drawn here.
        public const string WindowPrefab = "Window";
        public const string TextPrefab = "Text";
        public const string SliderPrefab = "Slider";


        /// <summary>A real toggle, rather than a button painted to look like one.</summary>
        public const string TogglePrefab = "Text Toggle";

        /// <summary>A button with a word on it, as Besiege's own dialogs use.</summary>
        public const string ButtonPrefab = "Text Button";


        /// <summary>The square picture button a title bar's corner is made of; the
        /// window's own close cross is one.</summary>
        public const string IconButtonPrefab = "Icon Button";

        /// <summary>Besiege's own text box. It carries the behaviour that stops the
        /// game's hotkeys firing at whatever is being typed into it.</summary>
        public const string InputPrefab = "Input Field";

        /// <summary>The lettering the panels are written in. White, not the grey
        /// this was: a caption beside a slider is as much the answer as the number
        /// at the end of it. Grey is left for <see cref="QuietInk"/>.</summary>
        public static readonly Color Ink = Color.white;

        /// <summary>For text that is not an answer: a box's ghost prompt, there to
        /// be typed over. Kept here rather than read from <c>Besiege.UI.Consts</c>,
        /// so a panel's colours are not another thing that has to resolve first.
        /// </summary>
        public static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);

        /// <summary>
        /// Besiege's red, which is what the game paints the option in force. Kept
        /// here rather than read from <c>Besiege.UI.Consts</c> so that the panel's
        /// colours are not another thing that has to resolve before it can draw.
        /// </summary>
        public static readonly Color Selected = new Color(0.92f, 0.13f, 0.29f, 1f);

        /// <summary>The cyan Besiege uses for a reset, and here for the trace.</summary>
        public static readonly Color Trace = new Color(0.012f, 1f, 0.847f, 1f);

        private static bool asked;
        private static bool available;

        /// <summary>
        /// Whether UI Factory is installed and has finished loading its bundle.
        ///
        /// This is the guarded call site the whole soft dependency rests on. Touching
        /// <c>Besiege.UI</c> at all is what fails when the mod is absent, and it
        /// fails as the calling method is compiled -- so the try has to be here, one
        /// frame away from any of the panel's own code, and the answer is remembered
        /// rather than asked for again every frame.
        /// </summary>
        public static bool Available
        {
            get
            {
                if (asked)
                {
                    return available;
                }
                try
                {
                    available = Besiege.UI.Make.Instance != null
                             && Modding.ModResource.AllResourcesLoaded;
                }
                catch (Exception)
                {
                    available = false;
                }
                // Only remembered once it is true: UI Factory loads its bundle a
                // moment after the mod does, and "not yet" is not "not installed".
                asked = available;
                return available;
            }
        }

        /// <summary>
        /// Besiege's own lettering, for the controls this mod builds itself rather
        /// than taking from a prefab. Null if UI Factory is not there, which every
        /// caller answers with Unity's built-in Arial.
        /// </summary>
        public static Font Font
        {
            get
            {
                try
                {
                    return Besiege.UI.Make.Font;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gives a label UI Factory's font if it has none.
        ///
        /// The Input Field prefab's own text and placeholder come out of the
        /// bundle without one, and a Text with no font draws nothing at all -- so
        /// the box reads as one that swallows typing rather than one that failed to
        /// paint. `Besiege.UI.Make.Font` is the font the rest of the game is
        /// written in.
        /// </summary>
        public static void EnsureFont(Text label)
        {
            if (label == null || label.font != null)
            {
                return;
            }
            try
            {
                if (Besiege.UI.Make.Font != null)
                {
                    label.font = Besiege.UI.Make.Font;
                }
            }
            catch (Exception)
            {
                // A UI Factory that cannot say leaves Unity's own default in place.
            }
        }

        /// <summary>
        /// Instantiates one of UI Factory's prefabs. Returns null (and says why)
        /// rather than throwing into the caller.
        /// </summary>
        public static GameObject Spawn(string prefab, Transform parent)
        {
            try
            {
                return Besiege.UI.Make.Prefab(Package, prefab, parent);
            }
            catch (Exception e)
            {
                Log.Warn("UI Factory could not supply the prefab '" + prefab + "': " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Takes the Translator off a label. UI Factory's text carries one, and it
        /// would put the prefab's own wording back at the next language change.
        /// </summary>
        public static void Untranslate(Text label)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                Besiege.UI.Behaviours.Translator translator =
                    label.GetComponent<Besiege.UI.Behaviours.Translator>();
                if (translator != null)
                {
                    UnityEngine.Object.Destroy(translator);
                }
            }
            catch (Exception)
            {
                // A UI Factory without that behaviour still gives usable text.
            }
        }

        /// <summary>
        /// Takes a control's hover swell away. A hovered button grows 15%, which is
        /// right for a button the size of a button and wrong for a full-width row --
        /// it carries the text sideways instead of lighting the row up. Switched off
        /// rather than turned down, the two scales being private to UI Factory: a
        /// disabled behaviour is never told the pointer arrived.
        /// </summary>
        public static void NoSwell(GameObject control)
        {
            if (control == null)
            {
                return;
            }
            try
            {
                Besiege.UI.Bridge.ScaleAnimation scale =
                    control.GetComponent<Besiege.UI.Bridge.ScaleAnimation>();
                if (scale != null)
                {
                    scale.enabled = false;
                }
            }
            catch (Exception)
            {
                // An older UI Factory without that behaviour simply never swelled.
            }
        }
    }
}
