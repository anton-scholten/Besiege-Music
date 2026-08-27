using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The pictures this mod draws for itself: the speaker on the instrument
    /// panel's LISTEN button, and the folder and reload arrow on the loader's.
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

        // The reload arrow: a ring with a bite out of it and a head on one end.
        // Drawn rather than asked for, as the speaker is, and for the same reason
        // -- UI Factory's sprite set is Besiege's own HUD and cannot be listed.
        private const float RingRadius = 0.30f;
        private const float RingStroke = 0.085f;
        /// <summary>Where the ring stops, in radians, and where it starts again:
        /// the gap the head sits in.</summary>
        private const float RingFrom = 0.75f;
        private const float RingTo = 6.0f;
        private const float HeadReach = 0.30f;
        private const float HeadHalf = 0.17f;

        // The folder: a body with a tab along the top of its left half, which is
        // the shape everything has drawn a folder as since 1984.
        private const float FolderLeft = 0.10f;
        private const float FolderRight = 0.90f;
        private const float FolderFoot = 0.22f;
        private const float FolderHead = 0.68f;
        private const float TabRight = 0.46f;
        private const float TabTop = 0.78f;

        private static Sprite face;
        private static Sprite arrow;
        private static Sprite folder;

        /// <summary>What a glyph looks like: true where the ink is.</summary>
        public delegate bool Shape(float u, float v);

        /// <summary>
        /// The speaker, made once and kept. Neither texture nor sprite is collected
        /// while the mod runs: nothing but this field refers to them.
        /// </summary>
        public static Sprite Speaker()
        {
            if (face == null)
            {
                Texture2D drawn = Render(64, new Shape(Lit));
                drawn.hideFlags = HideFlags.HideAndDontSave;
                face = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                     new Vector2(0.5f, 0.5f));
                face.hideFlags = HideFlags.HideAndDontSave;
            }
            return face;
        }

        /// <summary>The reload arrow, made once and kept, as <see cref="Speaker"/>.</summary>
        public static Sprite Reload()
        {
            if (arrow == null)
            {
                Texture2D drawn = Render(64, new Shape(Circling));
                drawn.hideFlags = HideFlags.HideAndDontSave;
                arrow = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                      new Vector2(0.5f, 0.5f));
                arrow.hideFlags = HideFlags.HideAndDontSave;
            }
            return arrow;
        }

        /// <summary>The folder, made once and kept, as <see cref="Speaker"/>.</summary>
        public static Sprite Folder()
        {
            if (folder == null)
            {
                Texture2D drawn = Render(64, new Shape(Foldered));
                drawn.hideFlags = HideFlags.HideAndDontSave;
                folder = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                       new Vector2(0.5f, 0.5f));
                folder.hideFlags = HideFlags.HideAndDontSave;
            }
            return folder;
        }

        private static Texture2D Render(int size, Shape shape)
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
                            if (shape(u, v))
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

        /// <summary>
        /// A ring with a bite taken out of it, and an arrowhead closing one end of
        /// the bite, so it reads as turning rather than as a letter O.
        /// </summary>
        private static bool Circling(float u, float v)
        {
            float x = (u - 0.5f) / Scale;
            float y = (v - 0.5f) / Scale;
            float radius = Mathf.Sqrt(x * x + y * y);

            if (Mathf.Abs(radius - RingRadius) <= RingStroke * 0.5f)
            {
                // atan2 comes back in (-pi, pi]; the ring is measured from 0 round
                // to 2pi so the gap can be one span rather than two.
                float angle = Mathf.Atan2(y, x);
                if (angle < 0f)
                {
                    angle += Mathf.PI * 2f;
                }
                if (angle >= RingFrom && angle <= RingTo)
                {
                    return true;
                }
            }

            // The head, on the end of the ring at RingFrom: a triangle across the
            // stroke, pointing the way the ring is going.
            float cos = Mathf.Cos(RingFrom);
            float sin = Mathf.Sin(RingFrom);
            float tipX = cos * RingRadius + -sin * -HeadReach;
            float tipY = sin * RingRadius + cos * -HeadReach;
            return InTriangle(x, y,
                              cos * (RingRadius + HeadHalf), sin * (RingRadius + HeadHalf),
                              cos * (RingRadius - HeadHalf), sin * (RingRadius - HeadHalf),
                              tipX, tipY);
        }

        private static bool InTriangle(float x, float y, float ax, float ay,
                                       float bx, float by, float cx, float cy)
        {
            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Mathf.Abs(area) < 1e-6f)
            {
                return false;
            }
            float w0 = ((bx - ax) * (y - ay) - (by - ay) * (x - ax)) / area;
            float w1 = ((x - ax) * (cy - ay) - (y - ay) * (cx - ax)) / area;
            return w0 >= 0f && w1 >= 0f && w0 + w1 <= 1f;
        }

        /// <summary>A folder: the body, and the tab standing on top of its left
        /// half.</summary>
        private static bool Foldered(float u, float v)
        {
            float x = (u - 0.5f) / Scale + 0.5f;
            float y = (v - 0.5f) / Scale + 0.5f;
            if (x < FolderLeft || x > FolderRight)
            {
                return false;
            }
            if (y >= FolderFoot && y <= FolderHead)
            {
                return true;
            }
            return x <= TabRight && y > FolderHead && y <= TabTop;
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
