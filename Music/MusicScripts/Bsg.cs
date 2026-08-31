using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MusicMod
{
    /// <summary>
    /// Writes a plan out as a Besiege machine file.
    ///
    /// Besiege's own saver is `XmlSaver.Save`, and that is one of the four methods
    /// the mod loader forbids outright -- along with `LevelXMLSaver.Create` and
    /// `AssetBundle.LoadFromFile`. Every entry point that reaches it
    /// (`MachineFileBrowserController.Save`, `SaveSelection`) is private, so a mod
    /// cannot get at the game's writer either directly or through the load screen.
    ///
    /// So the file is written here. The format is not guesswork: it is the same
    /// `.bsg` `tools/make-song.py` has been writing and this game has been loading,
    /// element for element. `System.Xml` is blacklisted as well, hence the string
    /// building -- there is nothing to escape here but a machine's name, which the
    /// player types.
    /// </summary>
    public static class Bsg
    {
        /// <summary>Where a machine of loose blocks is dropped in from. Besiege's
        /// own saves carry a spawn position and this is a sensible one: high enough
        /// that a field of blocks is not born inside the ground.</summary>
        private const float SpawnHeight = 5.05f;

        public static string Write(SongPlan plan, string name)
        {
            StringBuilder out_ = new StringBuilder();
            out_.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            out_.Append("<!--Besiege machine save file.-->\n");
            out_.Append("<Machine version=\"1\" bsgVersion=\"1.4\" name=\"")
                .Append(Escaped(name)).Append("\">\n");

            out_.Append("    <Global>\n");
            out_.Append("        <Position x=\"0\" y=\"")
                .Append(Number(SpawnHeight)).Append("\" z=\"0\" />\n");
            out_.Append("        <Rotation x=\"0\" y=\"0\" z=\"0\" w=\"1\" />\n");
            out_.Append("    </Global>\n");

            // What the game writes when it saves a machine with modded blocks in
            // it, so opening this one without Music warns rather than quietly
            // swapping every instrument for a ballast.
            List<string> mods = new List<string>();
            if (Catalogue.RequiredMods != null)
            {
                mods.Add(Catalogue.RequiredMods);
            }
            // The Braids block used to be another mod's, and a machine holding it
            // had to name that mod too or the game would swap it for the fallback
            // without saying so. It is one of these blocks now, so this mod's own
            // entry covers it and there is nothing else to name.
            if (mods.Count > 0)
            {
                out_.Append("    <Data>\n");
                out_.Append("        <StringArray key=\"requiredMods\">");
                if (mods.Count == 1)
                {
                    // One mod is written inline, which is what the game writes and
                    // what every machine this mod has produced so far holds.
                    out_.Append(Escaped(mods[0]));
                }
                else
                {
                    // More than one needs the array spelled out, an entry each.
                    out_.Append("\n");
                    for (int i = 0; i < mods.Count; i++)
                    {
                        out_.Append("            <String>").Append(Escaped(mods[i]))
                            .Append("</String>\n");
                    }
                    out_.Append("        ");
                }
                out_.Append("</StringArray>\n");
                out_.Append("    </Data>\n");
            }

            out_.Append("    <Blocks>\n");
            // Every machine has one of these and the game is happier when it is
            // first. Left in the orientation Besiege gives it: it is the machine's
            // root, not one of the instruments.
            Block(out_, Song.StartingBlock, 0, Vector3.zero, Quaternion.identity, null);
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                SongBlock block = plan.Blocks[i];
                Block(out_, block.Type, block.LocalId, block.Position, block.Rotation,
                      block.Data);
            }
            out_.Append("    </Blocks>\n");
            out_.Append("</Machine>\n");
            return out_.ToString();
        }

        private static void Block(StringBuilder out_, int type, int localId,
                                  Vector3 at, Quaternion facing, XDataHolder data)
        {
            out_.Append("        <Block id=\"").Append(type)
                .Append("\" guid=\"").Append(Guid.NewGuid().ToString()).Append("\"");
            if (localId > 0)
            {
                // A modded block is resolved by modId and localId --
                // `XmlLoader.HandleMod` recomputes the numeric id from those two --
                // and `fallback` is the vanilla block shown when the mod is absent.
                out_.Append(" modId=\"").Append(Catalogue.ModId)
                    .Append("\" localId=\"").Append(localId)
                    .Append("\" fallback=\"").Append(Song.Fallback).Append("\"");
            }
            out_.Append(">\n");

            out_.Append("            <Transform>\n");
            out_.Append("                <Position x=\"").Append(Number(at.x))
                .Append("\" y=\"").Append(Number(at.y))
                .Append("\" z=\"").Append(Number(at.z)).Append("\" />\n");
            out_.Append("                <Rotation x=\"").Append(Number(facing.x))
                .Append("\" y=\"").Append(Number(facing.y))
                .Append("\" z=\"").Append(Number(facing.z))
                .Append("\" w=\"").Append(Number(facing.w)).Append("\" />\n");
            out_.Append("                <Scale x=\"1\" y=\"1\" z=\"1\" />\n");
            out_.Append("            </Transform>\n");

            out_.Append("            <Data>\n");
            if (data != null)
            {
                foreach (XData value in data.ReadAll())
                {
                    Value(out_, value);
                }
            }
            out_.Append("            </Data>\n");
            out_.Append("        </Block>\n");
        }

        /// <summary>
        /// One setting, written as the element its own type names: an `XSingle`'s
        /// Type is the string "Single", which is exactly what the file calls it.
        /// So this needs no table of kinds and cannot fall behind one.
        /// </summary>
        private static void Value(StringBuilder out_, XData value)
        {
            string kind = value.Type;
            out_.Append("                <").Append(kind)
                .Append(" key=\"").Append(Escaped(value.Key)).Append("\">");

            string[] many = value.RawValue as string[];
            if (many != null)
            {
                out_.Append("\n");
                for (int i = 0; i < many.Length; i++)
                {
                    out_.Append("                    <String>")
                        .Append(Escaped(many[i])).Append("</String>\n");
                }
                out_.Append("                ");
            }
            else
            {
                out_.Append(Escaped(Plain(value.RawValue)));
            }
            out_.Append("</").Append(kind).Append(">\n");
        }

        /// <summary>A value as the file spells it: invariant, and floats without
        /// an exponent, which is what Besiege's own parser expects.</summary>
        private static string Plain(object value)
        {
            if (value is float)
            {
                return Number((float)value);
            }
            if (value is bool)
            {
                return ((bool)value) ? "True" : "False";
            }
            if (value is IFormattable)
            {
                return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            }
            return value == null ? "" : value.ToString();
        }

        private static string Number(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string Escaped(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("&", "&amp;").Replace("<", "&lt;")
                       .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
