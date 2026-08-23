using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The one picture this mod draws for itself: the speaker on the panel's LISTEN
    /// button.
    ///
    /// Generated rather than shipped as a PNG -- it cannot go missing from an
    /// install, it costs nothing at this size, and it sidesteps the case-sensitive
    /// resource loading that catches Workshop mods authored on Windows. UI Factory's
    /// sprite set is Besiege's own HUD sprites, and it cannot be listed, so asking it
    /// for a speaker would be a guess at a name.
    ///
    /// White on transparent, so whatever tint it is drawn with is what colours it.
    /// </summary>
    public static class IconArt
    {
        /// <summary>Coverage samples per pixel, per axis: the whole of the smoothing.</summary>
        private const int Samples = 4;

        /// <summary>
        /// How much of the square the drawing fills. The button's icon is stretched
        /// edge to edge -- which is what keeps the whole button clickable rather than
        /// only the part with a picture on it -- so the inset is drawn in rather than
        /// laid out.
        /// </summary>
        private const float Scale = 0.68f;

        // The glyph, in its own square, before that inset. A box against a cone, and
        // two arcs of sound leaving the mouth. Shift centres what those add up to:
        // the drawing is longer to the right than to the left of the cone.
        private const float Shift = 0.066f;
        private const float BoxLeft = 0.06f;
        private const float BoxRight = 0.24f;
        private const float BoxHalf = 0.11f;
        private const float ConeMouth = 0.46f;
        private const float MouthHalf = 0.30f;
        private const float WaveNear = 0.18f;
        private const float WaveFar = 0.31f;
        private const float WaveStroke = 0.075f;
        /// <summary>tan of the half-angle the arcs are drawn across, about 48 degrees.</summary>
        private const float WaveSpread = 1.11f;

        private static Sprite face;

        /// <summary>
        /// The speaker, made once and kept. Neither texture nor sprite is collected
        /// while the mod runs: nothing but this field refers to them.
        /// </summary>
        public static Sprite Speaker()
        {
            if (face == null)
            {
                Texture2D drawn = Render(64);
                drawn.hideFlags = HideFlags.HideAndDontSave;
                face = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                     new Vector2(0.5f, 0.5f));
                face.hideFlags = HideFlags.HideAndDontSave;
            }
            return face;
        }

        private static Texture2D Render(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float step = 1f / (size * Samples);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x * Samples + sx + 0.5f) * step;
                            float v = (y * Samples + sy + 0.5f) * step;
                            if (Lit(u, v))
                            {
                                hits++;
                            }
                        }
                    }
                    pixels[y * size + x] =
                        new Color32(255, 255, 255, (byte)(255 * hits / (Samples * Samples)));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>The glyph, inset into the square it is drawn in.</summary>
        private static bool Lit(float u, float v)
        {
            return Inside((u - 0.5f) / Scale + 0.5f, (v - 0.5f) / Scale + 0.5f);
        }

        private static bool Inside(float u, float v)
        {
            u -= Shift;
            float dy = v - 0.5f;
            float height = dy < 0f ? -dy : dy;

            if (u >= BoxLeft && u <= BoxRight)
            {
                return height <= BoxHalf;
            }
            if (u > BoxRight && u <= ConeMouth)
            {
                // The cone: the box's half-height at its back, the mouth's at its
                // front, straight between.
                float along = (u - BoxRight) / (ConeMouth - BoxRight);
                return height <= BoxHalf + (MouthHalf - BoxHalf) * along;
            }

            // The arcs, struck from the mouth and drawn only across the angle sound
            // would leave it at -- a full ring would be a target, not a speaker.
            float dx = u - ConeMouth;
            if (dx <= 0f || height > dx * WaveSpread)
            {
                return false;
            }
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float half = WaveStroke * 0.5f;
            return (radius >= WaveNear - half && radius <= WaveNear + half)
                || (radius >= WaveFar - half && radius <= WaveFar + half);
        }
    }
}
