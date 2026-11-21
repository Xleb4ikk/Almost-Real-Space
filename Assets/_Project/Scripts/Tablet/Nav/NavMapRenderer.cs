using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>Numbers the monitor frame shows beside the map (not part of the map geometry).</summary>
    public sealed class NavMapInfo
    {
        public bool Connected;
        public double PlanDeltaVMs;
        public int ManeuverCount;
        public double MassKg;
        public double SpeedMs;
        public double AltitudeMeters;
        public double VerticalSpeedMps;
        public bool HasShip;
        public string Status = string.Empty;
    }

    /// <summary>
    /// Software renderer of the navigation map into a Texture2D. It draws exactly the
    /// geometry built by NavMapModel: body positions/velocities from the baked
    /// ephemerides, the ship's N-body forecast (coast plus planned impulses) and its
    /// actual history trail.
    ///
    /// The projection is orthographic with a user tilt, so an inclined orbit is an
    /// ellipse and an out-of-plane body really sits off the line. Pixels are drawn
    /// through the same PixelCanvas/PixelFont as the main tablet, so the monitor keeps
    /// the game's green phosphor look instead of being a separate 3D view.
    /// </summary>
    public sealed class NavMapRenderer
    {
        public readonly int Width;
        public readonly int Height;
        public readonly bool Preview;

        public float Zoom = 1f;

        /// <summary>
        /// Turntable camera angles. Azimuth is around the reference-plane normal,
        /// elevation is above that plane (0 = edge-on, +90° = north pole up).
        /// The visible angles ease toward <see cref="TargetAzimuthRad"/> /
        /// <see cref="TargetElevationRad"/> in <see cref="Tick"/>; input only moves
        /// the targets, so a drag never snaps the map.
        /// </summary>
        public float AzimuthRad = DefaultAzimuthRad;
        public float ElevationRad = DefaultElevationRad;
        public float TargetAzimuthRad = DefaultAzimuthRad;
        public float TargetElevationRad = DefaultElevationRad;

        private const float DefaultAzimuthRad = -Mathf.PI * 0.5f;
        private const float DefaultElevationRad = 1.19f;   // ~68° above the plane
        private const float MinElevationRad = -1.45f;      // keep the view off the pole
        private const float MaxElevationRad = 1.45f;
        private const float RotateSensitivity = 0.0055f;
        private const float RotateDamping = 13f;

        /// <summary>
        /// True while the player drags/zooms the map. The station uses it to keep
        /// redrawing during the gesture (and to render one final frame when it
        /// settles). It does not change the image: the globe is always rasterised
        /// at full resolution, so a drag never turns the planet blocky.
        /// </summary>
        public bool Interactive { get; set; }

        /// <summary>
        /// Duration of the last globe raster, in milliseconds. The station paces its
        /// gesture redraws with it: a slow machine gets fewer full-quality frames
        /// instead of a coarser image.
        /// </summary>
        public float LastRasterMs { get; private set; }

        /// <summary>
        /// Worker budget for the globe raster. One core stays free for the
        /// simulation, and the fan-out is capped: a 250k-pixel disk then lands in
        /// a few milliseconds instead of a full frame.
        /// </summary>
        private static readonly System.Threading.Tasks.ParallelOptions DiskRasterOptions =
            new System.Threading.Tasks.ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(16, Environment.ProcessorCount - 1)),
            };

        private readonly PixelCanvas canvas;
        private readonly Texture2D texture;
        private readonly List<PickEntry> picks = new List<PickEntry>();

        /// <summary>
        /// A fixed, very dim star field behind the map. Static on purpose: it is
        /// backdrop, not world geometry, so it must never read as a moving body.
        /// </summary>
        private readonly List<Star> stars = new List<Star>();

        private struct Star
        {
            public int X;
            public int Y;
            public byte Brightness;
            public bool Glint;
        }

        /// <summary>Pick currently under the cursor. Kept between draws so a tag can expand in place.</summary>
        private PickKind hoverKind = PickKind.None;
        private int hoverIndex = -1;

        // Rendered planet disk cache: the per-pixel sphere raster is the single most
        // expensive part of the map. Reuse it until the view (or the planet's visible
        // orientation / light) actually changes; only the ship, orbits and HUD are
        // redrawn on the regular map tick.
        private Color32[] diskCache;
        private int diskCacheX;
        private int diskCacheY;
        private int diskCacheWidth;
        private int diskCacheHeight;
        private int diskCacheMapX;
        private int diskCacheMapY;
        private int diskCacheMapWidth;
        private int diskCacheMapHeight;
        private bool diskCacheValid;
        private OrbitingBody diskCacheBody;
        private NavBodyTexture diskCacheTexture;
        private float diskCacheCenterX;
        private float diskCacheCenterY;
        private float diskCacheRadius;
        private QuaternionD diskCacheOrientation;
        private Vector3d diskCacheRight;
        private Vector3d diskCacheUp;
        private Vector3d diskCacheView;
        private Vector3d diskCacheSun;

        private double lastScale;
        private double lastSurfaceMetersPerPixel;
        private int lastFrameIndex = -1;
        private int lastCenterIndex = -1;
        private int lastBodyCount = -1;

        private static readonly Color32 OrbitColorA = TabletTheme.LineHi;
        private static readonly Color32 OrbitColorB = TabletTheme.Dim;
        private static readonly Color32 OrbitColorC = TabletTheme.Line;
        private static readonly Color32 ForecastColor = TabletTheme.Warn;
        private static readonly Color32 ManeuverForecastColor = TabletTheme.Bright;
        private static readonly Color32 SurfaceForecastColor = TabletTheme.Main;
        private static readonly Color32 SurfaceHistoryColor = TabletTheme.Dim;

        /// <summary>Colours of the ship / apsis markers. One set per kind of orbit.</summary>
        private struct MarkerPalette
        {
            public Color32 Main;
            public Color32 Light;
            public Color32 Dark;
        }

        /// <summary>Ballistic coast: cyan.</summary>
        private static readonly MarkerPalette BallisticMarkers = new MarkerPalette
        {
            Main = new Color32(70, 205, 255, 255),
            Light = new Color32(200, 244, 255, 255),
            Dark = new Color32(5, 40, 60, 255),
        };

        /// <summary>Maneuver (engine firing): orange.</summary>
        private static readonly MarkerPalette ManeuverMarkers = new MarkerPalette
        {
            Main = new Color32(255, 138, 56, 255),
            Light = new Color32(255, 226, 184, 255),
            Dark = new Color32(66, 28, 5, 255),
        };

        /// <summary>
        /// Apsides of the PLANNED orbit (after the last burn): violet. Deliberately
        /// outside the cyan ballistic / orange thrusting pair, so a glance separates
        /// "where I am going now" from "where the maneuver takes me".
        /// </summary>
        private static readonly MarkerPalette PlannedMarkers = new MarkerPalette
        {
            Main = new Color32(198, 132, 255, 255),
            Light = new Color32(236, 218, 255, 255),
            Dark = new Color32(40, 16, 62, 255),
        };

        /// <summary>
        /// Ground contact of a forecast leg: the HUD warning amber. Outside the cyan /
        /// orange / violet families on purpose — this is not where the craft is going,
        /// it is where the prediction stops.
        /// </summary>
        private static readonly MarkerPalette ImpactMarkers = new MarkerPalette
        {
            Main = TabletTheme.Warn,
            Light = new Color32(255, 248, 220, 255),
            Dark = new Color32(58, 40, 6, 255),
        };

        private static MarkerPalette MarkersFor(NavMapModel model)
        {
            return model.ShipThrusting ? ManeuverMarkers : BallisticMarkers;
        }

        /// <summary>What a pick entry belongs to. Apsis tags expand in place while hovered.</summary>
        private enum PickKind
        {
            None,
            Body,
            Burn,
            Apoapsis,
            Periapsis,
            Ship,

            /// <summary>Apoapsis of the planned (post-burn) orbit: the violet "АП·М" tag.</summary>
            PlannedApoapsis,

            /// <summary>Periapsis of the planned (post-burn) orbit: the violet "ПР·М" tag.</summary>
            PlannedPeriapsis,

            /// <summary>Ground contact of a forecast leg: the amber "УДАР" tag.</summary>
            Impact,
        }

        private struct PickEntry
        {
            public float X;
            public float Y;
            public float Radius;
            public string Text;
            public PickKind Kind;
            public int Index;

            /// <summary>Exact screen rectangle of the drawn tag; empty when the pick is only a point.</summary>
            public RectInt Box;
            public bool HasBox;
        }

        public NavMapRenderer(int width, int height, bool preview)
        {
            Width = width;
            Height = height;
            Preview = preview;
            canvas = new PixelCanvas(width, height);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = preview ? "NavMapPreview" : "NavMapOverlay",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            BuildStarfield(width, height);
        }

        public Texture2D Texture => texture;

        public void Dispose()
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>Redraw the full texture. Call at 1–4 Hz, not every frame.</summary>
        public void Draw(NavMapModel model, NavState state, NavMapInfo info)
        {
            canvas.Clear(TabletTheme.Bg);
            picks.Clear();

            if (model == null)
            {
                DrawDisconnected(info);
                Upload();
                return;
            }

            if (model.FrameIndex != lastFrameIndex)
            {
                // Keep the user's orientation across a focus change; ease back to
                // the default angles instead of snapping the map.
                Zoom = 1f;
                TargetAzimuthRad = DefaultAzimuthRad;
                TargetElevationRad = DefaultElevationRad;
                lastFrameIndex = model.FrameIndex;
            }

            if (model.CenterIndex != lastCenterIndex)
            {
                lastCenterIndex = model.CenterIndex;
            }

            if (model.Bodies.Count != lastBodyCount)
            {
                lastBodyCount = model.Bodies.Count;
            }

            RectInt mapRect = Preview
                ? new RectInt(18, 56, Mathf.Max(80, Width - 312), Height - 74)
                : new RectInt(8, 8, Width - 16, Height - 16);

            if (Preview)
            {
                DrawPreviewChrome(model, state, info, mapRect);
            }

            if (model.ViewMode == NavViewMode.Surface && model.DominantBody != null && model.DominantBody.Terrain != null)
            {
                DrawSurfaceMap(model, state, info, mapRect);
            }
            else
            {
                DrawMap(model, state, mapRect);
            }

            if (Preview)
            {
                DrawPreviewFooter(model, state, info, mapRect);
            }
            else if (model.ViewMode == NavViewMode.Surface && model.DominantBody != null && model.DominantBody.Terrain != null)
            {
                DrawSurfaceHud(model, rect: mapRect);
            }
            else
            {
                DrawHud(model, state, info, mapRect);
            }

            Upload();
        }

        public void RotateByPixels(Vector2 delta)
        {
            if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y))
            {
                return;
            }

            // Input moves the TARGET only. Tick() eases the visible angles toward it,
            // so a mouse event never rotates the map by a whole step.
            TargetAzimuthRad -= delta.x * RotateSensitivity;
            TargetElevationRad = Mathf.Clamp(
                TargetElevationRad + (delta.y * RotateSensitivity),
                MinElevationRad, MaxElevationRad);
        }

        /// <summary>
        /// Eases the visible camera toward the target angles. Returns true while the
        /// view is still moving, so the caller can redraw at a higher rate and then
        /// finish with one clean frame. Damping is frame-rate independent.
        /// </summary>
        public bool Tick(float deltaSeconds)
        {
            if (!(deltaSeconds > 0f) || !float.IsFinite(deltaSeconds))
            {
                deltaSeconds = 1f / 60f;
            }

            float blend = 1f - Mathf.Exp(-RotateDamping * deltaSeconds);
            AzimuthRad = Mathf.Lerp(AzimuthRad, TargetAzimuthRad, blend);
            ElevationRad = Mathf.Lerp(ElevationRad, TargetElevationRad, blend);

            bool moving = Mathf.Abs(AzimuthRad - TargetAzimuthRad) > 1e-4f
                || Mathf.Abs(ElevationRad - TargetElevationRad) > 1e-4f;
            if (!moving)
            {
                AzimuthRad = TargetAzimuthRad;
                ElevationRad = TargetElevationRad;
            }

            return moving;
        }

        /// <summary>
        /// Wheel zoom around the selected centre. The cursor is deliberately
        /// ignored: anchoring the world point under the mouse moved the chosen
        /// body off the screen centre, which made the map feel like it drifted.
        /// </summary>
        public void ZoomAt(Vector2 texturePixel, float wheelDelta)
        {
            if (!(Zoom > 0f) || !float.IsFinite(wheelDelta))
            {
                return;
            }

            float factor = Mathf.Pow(1.2f, -wheelDelta);
            // Deep zoom-out: from a planet-centred view the wheel must reach the
            // planet's own orbit around its star (≈1 AU), not stop at a local map.
            Zoom = Mathf.Clamp(Zoom * factor, 0.00005f, 80f);
        }

        public void ResetView()
        {
            Zoom = 1f;
            TargetAzimuthRad = DefaultAzimuthRad;
            TargetElevationRad = DefaultElevationRad;
        }

        /// <summary>
        /// Transfers the camera (zoom, turntable angles) from the other map
        /// renderer. The physical monitor and the full-screen overlay are separate
        /// renderers with different layouts; without this, opening or closing the
        /// tablet snapped the visible view back to that renderer's own last camera.
        /// </summary>
        public void CopyViewFrom(NavMapRenderer source)
        {
            if (source == null)
            {
                return;
            }

            Zoom = source.Zoom;
            TargetAzimuthRad = source.TargetAzimuthRad;
            TargetElevationRad = source.TargetElevationRad;
            AzimuthRad = source.AzimuthRad;
            ElevationRad = source.ElevationRad;
            lastFrameIndex = source.lastFrameIndex;
            lastCenterIndex = source.lastCenterIndex;

            // A changed camera must not reuse the previous globe raster.
            diskCacheValid = false;
        }

        /// <summary>
        /// Nearest labelled object to a texture pixel, as rich text for the UI tooltip.
        /// Tag rectangles are tested exactly first: pointing at the visible "АП"/"ПР"
        /// plate must work over the whole plate, not only near the orbit point.
        /// </summary>
        public bool TryPick(Vector2 texturePixel, out string text)
        {
            bool found = PickAt(texturePixel, out PickEntry pick);
            text = found ? pick.Text : null;
            return found;
        }

        /// <summary>
        /// Hover bookkeeping for the active map. Returns true when the hovered object
        /// changed (an in-place plate must be expanded/collapsed or redrawn); the
        /// tooltip text is still returned for callers that want it.
        /// </summary>
        public bool Hover(Vector2 texturePixel, out string text)
        {
            bool found = PickAt(texturePixel, out PickEntry pick);
            text = found ? pick.Text : null;
            PickKind kind = found ? pick.Kind : PickKind.None;
            int index = found ? pick.Index : -1;
            bool changed = kind != hoverKind || index != hoverIndex;
            hoverKind = kind;
            hoverIndex = index;
            return changed;
        }

        /// <summary>Clears the hovered pick; true when the map must be redrawn collapsed.</summary>
        public bool ClearHover()
        {
            bool hadHover = hoverKind != PickKind.None;
            hoverKind = PickKind.None;
            hoverIndex = -1;
            return hadHover;
        }

        private bool PickAt(Vector2 texturePixel, out PickEntry pick)
        {
            // Exact plate first, topmost (last drawn) wins. This is the apsis tag
            // itself, so the hover area and the drawn box always match.
            for (int i = picks.Count - 1; i >= 0; i--)
            {
                PickEntry entry = picks[i];
                if (entry.HasBox
                    && texturePixel.x >= entry.Box.x && texturePixel.x < entry.Box.x + entry.Box.width
                    && texturePixel.y >= entry.Box.y && texturePixel.y < entry.Box.y + entry.Box.height)
                {
                    pick = entry;
                    return true;
                }
            }

            pick = default;
            bool found = false;
            float best = 14f * 14f;
            for (int i = 0; i < picks.Count; i++)
            {
                PickEntry entry = picks[i];
                float dx = entry.X - texturePixel.x;
                float dy = entry.Y - texturePixel.y;
                float d = (dx * dx) + (dy * dy);
                if (d <= best)
                {
                    best = d;
                    pick = entry;
                    found = true;
                }
            }

            return found;
        }

        // ─── Map geometry ─────────────────────────────────────────────────────

        private void BuildStarfield(int width, int height)
        {
            const int cell = 22;
            for (int cy = 0; cy < height; cy += cell)
            {
                for (int cx = 0; cx < width; cx += cell)
                {
                    uint h = Hash(cx, cy);
                    if ((h & 7u) != 0u)
                    {
                        continue;
                    }

                    int x = cx + (int)((h >> 8) % cell);
                    int y = cy + (int)((h >> 16) % cell);
                    int brightness = 18 + (int)((h >> 24) % 46u);
                    if (((h >> 5) & 63u) == 0u)
                    {
                        brightness += 34;
                    }

                    stars.Add(new Star
                    {
                        X = x,
                        Y = y,
                        Brightness = (byte)Mathf.Clamp(brightness, 0, 120),
                        Glint = ((h >> 11) & 7u) == 0u,
                    });
                }
            }
        }

        private static uint Hash(int x, int y)
        {
            uint h = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ 0x9E3779B9u;
            h ^= h >> 13;
            h *= 0x85EBCA6Bu;
            h ^= h >> 16;
            return h;
        }

        /// <summary>Dim backdrop stars inside the map viewport only (not behind the HUD panels).</summary>
        private void DrawStarfield(RectInt rect)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                Star star = stars[i];
                if (star.X < rect.x || star.X >= rect.x + rect.width
                    || star.Y < rect.y || star.Y >= rect.y + rect.height)
                {
                    continue;
                }

                byte b = star.Brightness;
                var color = new Color32((byte)(b * 0.72f), b, (byte)(b * 0.92f), 255);
                canvas.PxBlend(star.X, star.Y, color, 0.85f);
                if (star.Glint)
                {
                    canvas.PxBlend(star.X - 1, star.Y, color, 0.22f);
                    canvas.PxBlend(star.X + 1, star.Y, color, 0.22f);
                    canvas.PxBlend(star.X, star.Y - 1, color, 0.22f);
                    canvas.PxBlend(star.X, star.Y + 1, color, 0.22f);
                }
            }
        }

        /// <summary>Edge falloff that sinks the map border into the bezel.</summary>
        private void DrawVignette(RectInt rect)
        {
            const int depth = 18;
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < depth; i++)
            {
                float t = 1f - (i / (float)depth);
                float alpha = 0.20f * t * t;
                canvas.FillBlend(rect.x, rect.y + i, rect.width, 1, black, alpha);
                canvas.FillBlend(rect.x, rect.y + rect.height - 1 - i, rect.width, 1, black, alpha);
                canvas.FillBlend(rect.x + i, rect.y, 1, rect.height, black, alpha);
                canvas.FillBlend(rect.x + rect.width - 1 - i, rect.y, 1, rect.height, black, alpha);
            }
        }

        private void DrawMap(NavMapModel model, NavState state, RectInt rect)
        {
            int centerIndex = state != null ? state.CenterIndex : model.CenterIndex;
            bool shipCenter = model.HasShip && centerIndex == model.ShipIndex;

            // The chosen centre is the camera pivot at every zoom level. The old
            // zoom-in slide toward the ship (and the cursor-anchored wheel) moved
            // the selected body off-centre, especially when zooming back out.
            Vector3d center = model.CenterPosition;

            double radius = AutoRadius(model, centerIndex, shipCenter);
            double scale = Math.Min(rect.width, rect.height) * 0.44d / radius * Zoom;
            lastScale = scale;

            Vector3d normal = model.OrbitNormal.Normalized;
            Vector3d right0 = model.OrbitRight.Normalized;
            right0 = (right0 - (normal * Vector3d.Dot(right0, normal))).Normalized;
            if (right0.SqrMagnitude < 0.5d)
            {
                right0 = Vector3d.Cross(normal, new Vector3d(0d, 1d, 0d)).Normalized;
                if (right0.SqrMagnitude < 0.5d)
                {
                    right0 = Vector3d.Cross(normal, new Vector3d(1d, 0d, 0d)).Normalized;
                }
            }

            Vector3d up0 = Vector3d.Cross(normal, right0).Normalized;

            // Turntable camera with a fixed north-up screen. Azimuth orbits around
            // the reference-plane normal, elevation raises the camera above the
            // plane. The screen up is the projected plane normal, so the map never
            // rolls and north stays at the top of the frame.
            float elevation = Mathf.Clamp(ElevationRad, MinElevationRad, MaxElevationRad);
            float yaw = AzimuthRad;
            float cosElevation = Mathf.Cos(elevation);
            float sinElevation = Mathf.Sin(elevation);
            Vector3d horizontal = (right0 * Mathf.Cos(yaw)) + (up0 * Mathf.Sin(yaw));
            Vector3d camDir = (horizontal * cosElevation) + (normal * sinElevation);
            Vector3d screenUp = normal - (camDir * Vector3d.Dot(normal, camDir));
            if (screenUp.SqrMagnitude < 1e-6d)
            {
                screenUp = up0;
            }

            screenUp = screenUp.Normalized;
            Vector3d screenRight = Vector3d.Cross(screenUp, camDir).Normalized;

            Vector2 Project(Vector3d point)
            {
                Vector3d d = point - center;
                double x = Vector3d.Dot(d, screenRight);
                double y = Vector3d.Dot(d, screenUp);
                float sx = (float)(rect.x + (rect.width * 0.5f) + (x * scale));
                float syPx = (float)(rect.y + (rect.height * 0.5f) - (y * scale));
                return new Vector2(sx, syPx);
            }

            DrawStarfield(rect);
            DrawVignette(rect);

            // A globe already gives scale and orientation; the old concentric rings
            // read as a fake orbit when the craft is on the ground. The only lines on
            // this map are real trajectories.
            // DrawGrid(rect, Project, radius, center, right, up);

            // 1. Reference frames are implied by the "ВЕРХ" cue and the turntable
            // camera; a full dashed ring around the planet reads as a fake orbit.

            // Full body orbit rails are intentionally not drawn: with the short
            // default history/forecast windows a complete ellipse reads as the
            // predicted path ("why is the whole orbit green?"). Only the
            // time-limited body/ship spans below are drawn.

            // 2b. The centred body's own history/forecast over the slider windows.
            // Same real ephemeris as its orbit rail, but the highlighted spans show
            // where the body was and where it will be; the rail itself stays faint.
            // History/forecast keep the ship-trail colours so the sliders visibly
            // control the drawn span.
            for (int i = 0; i < model.Lines.Count; i++)
            {
                NavLine line = model.Lines[i];
                if (line.Kind == NavLineKind.BodyHistory)
                {
                    DrawPolylineFade(line.Points, Project, rect,
                        PixelCanvas.Mix(TabletTheme.Main, TabletTheme.Bg, 0.55f), TabletTheme.Main,
                        false, 2.2f, 1.8f);
                }
                else if (line.Kind == NavLineKind.BodyForecast)
                {
                    DrawPolylineFade(line.Points, Project, rect, ForecastColor,
                        PixelCanvas.Mix(ForecastColor, TabletTheme.Bg, 0.55f), true, 2.2f, 1.8f);
                }
            }

            // 3. Ship trails: base pass behind the globe.
            for (int i = 0; i < model.Lines.Count; i++)
            {
                NavLine line = model.Lines[i];
                if (line.Kind == NavLineKind.ShipHistory)
                {
                    // A parked craft's "history" is the site path from the
                    // ephemerides, not a flown orbit: keep it dim and thin.
                    bool surface = model.HasSurfaceHistory;
                    Color32 trail = surface ? SurfaceHistoryColor : TabletTheme.Main;
                    DrawPolylineFade(line.Points, Project, rect,
                        PixelCanvas.Mix(trail, TabletTheme.Bg, 0.60f), trail, false,
                        surface ? 1.4f : 2.4f, surface ? 1f : 2.2f);
                }
            }

            for (int i = 0; i < model.Lines.Count; i++)
            {
                NavLine line = model.Lines[i];
                if (line.Kind == NavLineKind.ShipForecast)
                {
                    if (model.HasSurfaceForecast)
                    {
                        // Site track while standing: a dashed ground path, not a
                        // ballistic orbit. A solid amber circle at 4 m AGL reads as
                        // a very low orbit, which is exactly the wrong message.
                        DrawPolylineFade(line.Points, Project, rect, SurfaceForecastColor,
                            PixelCanvas.Mix(SurfaceForecastColor, TabletTheme.Bg, 0.55f), true, 1.4f, 1f);
                    }
                    else
                    {
                        DrawPolylineFade(line.Points, Project, rect, ForecastColor,
                            PixelCanvas.Mix(ForecastColor, TabletTheme.Bg, 0.50f), false, 2.4f, 2.6f);
                    }
                }
                else if (line.Kind == NavLineKind.ManeuverForecast)
                {
                    DrawPolylineFade(line.Points, Project, rect, ManeuverForecastColor,
                        PixelCanvas.Mix(ManeuverForecastColor, TabletTheme.Bg, 0.55f), true, 2.4f, 2.6f);
                }
            }

            int occluderIndex = centerIndex >= 0 && centerIndex < model.Bodies.Count
                ? centerIndex
                : model.DominantBodyIndex;

            // 4. Globes.
            for (int i = 0; i < model.Bodies.Count; i++)
            {
                NavBodyMarker body = model.Bodies[i];
                if (i != occluderIndex
                    && IsMarkerOccluded(model, occluderIndex, body.Position, screenRight, screenUp, camDir))
                {
                    continue;
                }

                Vector2 p = Project(body.Position);
                if (!IsFinite(p))
                {
                    continue;
                }

                double trueRadiusMeters = body.RadiusMeters * scale;
                if (trueRadiusMeters < 5d)
                {
                    continue;
                }

                float bodyRadius = Mathf.Max(2f, (float)trueRadiusMeters);
                bool centerBody = i == centerIndex;
                Color32 color = body.IsFrame || centerBody ? TabletTheme.Bright : BodyColor(i);
                DrawPlanetDisk(model, body, p, bodyRadius, screenRight, screenUp, camDir, rect, color);
            }

            // 5. Near halves of the ship trails over the globe. The far halves stay
            // under it, so a surface site path no longer shows through the planet.
            if (occluderIndex >= 0 && occluderIndex < model.Bodies.Count)
            {
                Vector3d occluderCenter = model.Bodies[occluderIndex].Position;
                double occluderRadius = model.Bodies[occluderIndex].RadiusMeters;
                for (int i = 0; i < model.Lines.Count; i++)
                {
                    NavLine line = model.Lines[i];
                    if (line.Kind == NavLineKind.ShipHistory)
                    {
                        bool surface = model.HasSurfaceHistory;
                        Color32 trail = surface ? SurfaceHistoryColor : TabletTheme.Main;
                        DrawPolylineNearFade(line.Points, Project, rect,
                            PixelCanvas.Mix(trail, TabletTheme.Bg, 0.60f), trail,
                            false, surface ? 1.4f : 2.4f,
                            occluderCenter, occluderRadius, screenRight, screenUp, camDir,
                            surface ? 1f : 2.2f);
                    }
                    else if (line.Kind == NavLineKind.ShipForecast)
                    {
                        if (model.HasSurfaceForecast)
                        {
                            DrawPolylineNearFade(line.Points, Project, rect, SurfaceForecastColor,
                                PixelCanvas.Mix(SurfaceForecastColor, TabletTheme.Bg, 0.55f), true, 1.4f,
                                occluderCenter, occluderRadius, screenRight, screenUp, camDir, 1f);
                        }
                        else
                        {
                            DrawPolylineNearFade(line.Points, Project, rect, ForecastColor,
                                PixelCanvas.Mix(ForecastColor, TabletTheme.Bg, 0.50f), false, 2.4f,
                                occluderCenter, occluderRadius, screenRight, screenUp, camDir, 2.6f);
                        }
                    }
                    else if (line.Kind == NavLineKind.ManeuverForecast)
                    {
                        DrawPolylineNearFade(line.Points, Project, rect, ManeuverForecastColor,
                            PixelCanvas.Mix(ManeuverForecastColor, TabletTheme.Bg, 0.55f), true, 2.4f,
                            occluderCenter, occluderRadius, screenRight, screenUp, camDir, 2.6f);
                    }
                }
            }

            // Forecast endpoint cross, on top of the globe when in front.
            for (int i = 0; i < model.Lines.Count; i++)
            {
                NavLine line = model.Lines[i];
                if ((line.Kind == NavLineKind.ShipForecast
                        || line.Kind == NavLineKind.ManeuverForecast
                        || line.Kind == NavLineKind.BodyForecast)
                    && line.Points.Count > 1)
                {
                    Vector3d endpoint = line.Points[line.Points.Count - 1];
                    if (IsMarkerOccluded(model, occluderIndex, endpoint, screenRight, screenUp, camDir))
                    {
                        continue;
                    }

                    Vector2 end = Project(endpoint);
                    Color32 endColor = line.Kind == NavLineKind.ShipForecast && model.HasSurfaceForecast
                        ? SurfaceForecastColor
                        : ForecastColor;
                    DrawCross(end.x, end.y, 4f, endColor);
                }
            }

            // History start marker: a trail has a beginning, an orbit does not.
            for (int i = 0; i < model.Lines.Count; i++)
            {
                NavLine line = model.Lines[i];
                if ((line.Kind == NavLineKind.ShipHistory || line.Kind == NavLineKind.BodyHistory)
                    && line.Points.Count > 1)
                {
                    if (IsMarkerOccluded(model, occluderIndex, line.Points[0], screenRight, screenUp, camDir))
                    {
                        continue;
                    }

                    Vector2 start = Project(line.Points[0]);
                    if (IsFinite(start) && Inside(rect, start, 4f))
                    {
                        int sx = Mathf.RoundToInt(start.x);
                        int sy = Mathf.RoundToInt(start.y);
                        canvas.Fill(sx - 1, sy - 1, 3, 3,
                            model.HasSurfaceHistory ? SurfaceHistoryColor : TabletTheme.Main);
                    }
                }
            }

            // Planned burn points: the forecast changes colour there because the
            // orbit changes, not merely because time passed. The marker exists as
            // soon as the maneuver has a time, so the player sees the ignition
            // point while still choosing it (ΔV may be 0).
            for (int i = 0; i < model.BurnMarkers.Count; i++)
            {
                NavBurnMarker burn = model.BurnMarkers[i];
                if (IsMarkerOccluded(model, occluderIndex, burn.Position, screenRight, screenUp, camDir))
                {
                    continue;
                }

                Vector2 p = Project(burn.Position);
                if (!IsFinite(p))
                {
                    continue;
                }

                // The label carries what the marker is and when it fires: the
                // overlay map has no tooltip, so the plate cannot be the only place
                // that shows the countdown.
                double toBurn = Math.Max(0d, burn.TimeSeconds - model.TimeSeconds);
                string label = "М" + burn.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " " + TabletFormat.Time(toBurn);
                DrawMarker(p.x, p.y, label, ManeuverForecastColor, rect);

                string detail = "T− " + TabletFormat.Time(toBurn)
                    + "\nΔV " + TabletFormat.Num(burn.DvMs, 0) + " М/С";
                picks.Add(new PickEntry
                {
                    X = p.x,
                    Y = p.y,
                    Radius = 10f,
                    Kind = PickKind.Burn,
                    Index = burn.Index,
                    Text = "МАНЁВР " + burn.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + detail,
                });
            }

            // 6. Body markers and labels, on top of every line.
            for (int i = 0; i < model.Bodies.Count; i++)
            {
                NavBodyMarker body = model.Bodies[i];
                if (i != occluderIndex
                    && IsMarkerOccluded(model, occluderIndex, body.Position, screenRight, screenUp, camDir))
                {
                    continue;
                }

                Vector2 p = Project(body.Position);
                if (!IsFinite(p))
                {
                    continue;
                }

                double trueRadiusMeters = body.RadiusMeters * scale;
                float bodyRadius = Mathf.Max(2f, (float)trueRadiusMeters);
                bool centerBody = i == centerIndex;
                Color32 color = body.IsFrame || centerBody ? TabletTheme.Bright : BodyColor(i);
                bool centerVisible = Inside(rect, p, 12f);
                bool isGlobe = trueRadiusMeters >= 5d;

                if (!isGlobe && centerVisible)
                {
                    DrawDisc(p.x, p.y, bodyRadius, color);
                }

                if (!centerVisible)
                {
                    continue;
                }

                if ((body.IsFrame || centerBody) && !isGlobe)
                {
                    DrawCircle(p.x, p.y, bodyRadius + 3f, TabletTheme.Main);
                }

                string name = body.IsFrame ? "[" + body.Name + "]" : body.Name;
                string radiusLine = "R " + TabletFormat.Dist(body.RadiusMeters);
                float labelY = bodyRadius > 40f ? p.y - bodyRadius - 12f : p.y - 3f;
                float labelX = p.x + (bodyRadius > 40f ? 0f : bodyRadius + 5f);
                RectInt labelBox;
                if (hoverKind == PickKind.Body && hoverIndex == i)
                {
                    // Same in-place plate as the apsis/ship markers: header plus the
                    // data row, no separate floating window.
                    var pal = new MarkerPalette { Main = color, Light = color, Dark = TabletTheme.Bg };
                    int tw = Mathf.Max(PixelCanvas.TextWidth(name), PixelCanvas.TextWidth(radiusLine)) + 14;
                    const int th = 12 + 3 + PlateRowHeight;
                    int tx = Mathf.RoundToInt(labelX) - 2;
                    int ty = Mathf.RoundToInt(labelY) - 1;
                    tx = Mathf.Clamp(tx, rect.x + 2, Mathf.Max(rect.x + 2, rect.x + rect.width - tw - 2));
                    ty = Mathf.Clamp(ty, rect.y + 2, Mathf.Max(rect.y + 2, rect.y + rect.height - th - 2));
                    DrawTagBox(tx, ty, tw, th, pal);
                    canvas.Text(tx + 7, ty + 2, name, pal.Main);
                    canvas.HLine(tx + 4, ty + 12, tw - 7, PixelCanvas.Mix(pal.Main, pal.Dark, 0.30f));
                    canvas.Text(tx + 7, ty + 15, radiusLine, pal.Light);
                    labelBox = new RectInt(tx, ty, tw, th);
                }
                else
                {
                    labelBox = DrawLabel(labelX, labelY, name, rect, color);
                }

                picks.Add(new PickEntry
                {
                    X = p.x,
                    Y = p.y,
                    Radius = Mathf.Max(5f, Mathf.Min(bodyRadius, 20f)),
                    Kind = PickKind.Body,
                    Index = i,
                    Box = labelBox,
                    HasBox = true,
                    Text = name + "\n" + radiusLine,
                });
            }

            // Ground contact of the forecast: the line cannot continue past it, so the
            // map names the point, the countdown and the impact speed instead of simply
            // stopping in the middle of a globe. Drawn before the apsis plates so their
            // placement code keeps clear of it.
            for (int i = 0; i < model.Impacts.Count; i++)
            {
                NavImpactMarker impact = model.Impacts[i];
                if (IsMarkerOccluded(model, occluderIndex, impact.Position, screenRight, screenUp, camDir))
                {
                    continue;
                }

                Vector2 p = Project(impact.Position);
                if (!IsFinite(p))
                {
                    continue;
                }

                Vector2 placed = ClampToMap(rect, p, ApsisEdgeMargin, out _);
                double toImpact = Math.Max(0d, impact.TimeSeconds - model.TimeSeconds);
                string title = "УДАР" + (impact.Planned ? "·М" : string.Empty);
                DrawImpactMarker(placed.x, placed.y, title, impact.BodyName, toImpact, impact.SpeedMps,
                    rect, hoverKind == PickKind.Impact && hoverIndex == i, out RectInt box);
                picks.Add(new PickEntry
                {
                    X = placed.x,
                    Y = placed.y,
                    Radius = 10f,
                    Kind = PickKind.Impact,
                    Index = i,
                    Box = box,
                    HasBox = true,
                    Text = "УДАР О " + impact.BodyName.ToUpperInvariant()
                        + "\n" + (impact.Planned ? "МАНЁВР" : "БАЛЛИСТИКА")
                        + "\nT+ " + TabletFormat.Time(toImpact)
                        + "\nV " + TabletFormat.Speed(impact.SpeedMps) + " М/С",
                });
            }

            // Apsides of the current osculating orbit. In a frame whose body is far
            // from the craft (SOL while in LEO) the real apsis point is off-map at
            // AU scale: clamp it to the map edge with an outward arrow, so Ap/Pe are
            // never silently missing when the reference frame changes.
            MarkerPalette markers = MarkersFor(model);
            string orbitKind = model.ShipThrusting ? "МАНЁВР" : "БАЛЛИСТИКА";
            if (model.HasApsides)
            {
                if (!IsMarkerOccluded(model, occluderIndex, model.PeriapsisPosition, screenRight, screenUp, camDir))
                {
                    Vector2 pe = Project(model.PeriapsisPosition);
                    if (IsFinite(pe))
                    {
                        Vector2 placed = ClampToMap(rect, pe, ApsisEdgeMargin, out bool offMap);
                        string detail = ApsisDetail(model.PeriapsisAltitudeMeters, model.PeriapsisSpeedMps);
                        DrawApsisMarker(placed.x, placed.y, false, markers, rect, detail,
                            hoverKind == PickKind.Periapsis, offMap, out RectInt box);
                        picks.Add(new PickEntry
                        {
                            X = placed.x,
                            Y = placed.y,
                            Radius = 10f,
                            Kind = PickKind.Periapsis,
                            Box = box,
                            HasBox = true,
                            Text = "ПЕРИЦЕНТР\n" + orbitKind + "\n" + detail,
                        });
                    }
                }

                if (model.HasApoapsis
                    && !IsMarkerOccluded(model, occluderIndex, model.ApoapsisPosition, screenRight, screenUp, camDir))
                {
                    Vector2 ap = Project(model.ApoapsisPosition);
                    if (IsFinite(ap))
                    {
                        Vector2 placed = ClampToMap(rect, ap, ApsisEdgeMargin, out bool offMap);
                        string detail = ApsisDetail(model.ApoapsisAltitudeMeters, model.ApoapsisSpeedMps);
                        DrawApsisMarker(placed.x, placed.y, true, markers, rect, detail,
                            hoverKind == PickKind.Apoapsis, offMap, out RectInt box);
                        picks.Add(new PickEntry
                        {
                            X = placed.x,
                            Y = placed.y,
                            Radius = 10f,
                            Kind = PickKind.Apoapsis,
                            Box = box,
                            HasBox = true,
                            Text = "АПОЦЕНТР\n" + orbitKind + "\n" + detail,
                        });
                    }
                }
            }

            // Apsides of the PLANNED orbit: the conic right after the last burn.
            // Same plates as the ballistic pair, but violet and marked "·М", with
            // the post-burn numbers. Drawn after them so the maneuver result is the
            // one that stays on top when the two orbits are close.
            if (model.HasPlannedApsides)
            {
                string plannedKind = string.IsNullOrEmpty(model.PlannedApsisBodyName)
                    ? "МАНЁВР"
                    : "МАНЁВР · " + model.PlannedApsisBodyName;
                if (!IsMarkerOccluded(model, occluderIndex, model.PlannedPeriapsisPosition,
                        screenRight, screenUp, camDir))
                {
                    Vector2 pe = Project(model.PlannedPeriapsisPosition);
                    if (IsFinite(pe))
                    {
                        Vector2 placed = ClampToMap(rect, pe, ApsisEdgeMargin, out bool offMap);
                        string detail = ApsisDetail(model.PlannedPeriapsisAltitudeMeters,
                            model.PlannedPeriapsisSpeedMps);
                        DrawApsisMarker(placed.x, placed.y, false, PlannedMarkers, rect, detail,
                            hoverKind == PickKind.PlannedPeriapsis, offMap, out RectInt box, "·М");
                        picks.Add(new PickEntry
                        {
                            X = placed.x,
                            Y = placed.y,
                            Radius = 10f,
                            Kind = PickKind.PlannedPeriapsis,
                            Box = box,
                            HasBox = true,
                            Text = "ПЕРИЦЕНТР\n" + plannedKind + "\n" + detail,
                        });
                    }
                }

                if (model.HasPlannedApoapsis
                    && !IsMarkerOccluded(model, occluderIndex, model.PlannedApoapsisPosition,
                        screenRight, screenUp, camDir))
                {
                    Vector2 ap = Project(model.PlannedApoapsisPosition);
                    if (IsFinite(ap))
                    {
                        Vector2 placed = ClampToMap(rect, ap, ApsisEdgeMargin, out bool offMap);
                        string detail = ApsisDetail(model.PlannedApoapsisAltitudeMeters,
                            model.PlannedApoapsisSpeedMps);
                        DrawApsisMarker(placed.x, placed.y, true, PlannedMarkers, rect, detail,
                            hoverKind == PickKind.PlannedApoapsis, offMap, out RectInt box, "·М");
                        picks.Add(new PickEntry
                        {
                            X = placed.x,
                            Y = placed.y,
                            Radius = 10f,
                            Kind = PickKind.PlannedApoapsis,
                            Box = box,
                            HasBox = true,
                            Text = "АПОЦЕНТР\n" + plannedKind + "\n" + detail,
                        });
                    }
                }
            }

            // The ship itself: a diamond with a velocity arrow.
            if (model.HasShip
                && !IsMarkerOccluded(model, occluderIndex, model.FrameRelativeShipPosition, screenRight, screenUp, camDir))
            {
                Vector2 ship = Project(model.FrameRelativeShipPosition);
                if (IsFinite(ship) && Inside(rect, ship, 12f))
                {
                    DrawShip(ship.x, ship.y, model.FrameRelativeShipVelocity, screenRight, screenUp, markers);
                    double speed = model.FrameRelativeShipVelocity.Magnitude;
                    string detail = "V " + TabletFormat.Speed(speed) + " М/С";
                    DrawShipTag(ship.x, ship.y, model.ShipThrusting ? "АППАРАТ · МАНЁВР" : "АППАРАТ", markers, rect,
                        model.FrameRelativeShipVelocity, screenRight, screenUp,
                        detail, hoverKind == PickKind.Ship, out RectInt tagBox);
                    picks.Add(new PickEntry
                    {
                        X = ship.x,
                        Y = ship.y,
                        Radius = 12f,
                        Kind = PickKind.Ship,
                        Box = tagBox,
                        HasBox = true,
                        Text = "АППАРАТ · " + orbitKind + "\n" + detail,
                    });
                }
            }
        }

        /// <summary>
        /// True when a marker is behind the globe (or inside it) and must not be
        /// drawn through the surface. Uses the same orthographic disk test as the
        /// near/far trail passes.
        /// </summary>
        private static bool IsMarkerOccluded(NavMapModel model, int occluderIndex, Vector3d point,
            Vector3d screenRight, Vector3d screenUp, Vector3d camDir)
        {
            if (occluderIndex < 0 || occluderIndex >= model.Bodies.Count)
            {
                return false;
            }

            NavBodyMarker body = model.Bodies[occluderIndex];
            double radius = body.RadiusMeters;
            if (!(radius > 0d))
            {
                return false;
            }

            Vector3d q = point - body.Position;
            if (q.SqrMagnitude < radius * radius)
            {
                return true; // physically inside the body
            }

            double dx = Vector3d.Dot(q, screenRight);
            double dy = Vector3d.Dot(q, screenUp);
            double disk = radius * radius * 1.02d;
            return ((dx * dx) + (dy * dy) <= disk) && Vector3d.Dot(q, camDir) < 0d;
        }

        private void DrawSurfaceMap(NavMapModel model, NavState state, NavMapInfo info, RectInt rect)
        {
            OrbitingBody body = model.DominantBody;
            ITerrainModel terrain = body != null ? body.Terrain : null;
            if (body == null || terrain == null)
            {
                DrawMap(model, state, rect);
                return;
            }

            // Real local map: a top-down view of the actual heightfield around the
            // ship's sub-point. The span widens with altitude so an ascent still
            // shows the regional relief, then the globe/orbit view takes over.
            double altitude = Math.Max(0d, model.ShipHeightAboveTerrain);
            double span = Math.Max(20_000d, Math.Min(6_000_000d, 20_000d + (altitude * 4d)));
            double mpp = span / Math.Max(1, Math.Min(rect.width, rect.height)) / Math.Max(0.05f, Zoom);
            lastSurfaceMetersPerPixel = mpp;
            lastScale = 1d / mpp;

            float centerX = rect.x + (rect.width * 0.5f);
            float centerY = rect.y + (rect.height * 0.5f);

            double lat0 = model.ShipLatitudeDegrees * (Math.PI / 180d);
            double lon0 = model.ShipLongitudeDegrees * (Math.PI / 180d);
            double radius = Math.Max(1d, body.Radius);
            double cosLat = Math.Max(1e-4d, Math.Cos(lat0));

            int sampleW = 64;
            int sampleH = Mathf.Clamp(Mathf.RoundToInt(sampleW * rect.height / (float)Math.Max(1, rect.width)), 36, 80);
            double[] heights = new double[sampleW * sampleH];
            double[] latitudes = new double[sampleW * sampleH];
            double[] longitudes = new double[sampleW * sampleH];

            for (int j = 0; j < sampleH; j++)
            {
                double py = rect.y + ((j + 0.5d) * rect.height / sampleH);
                double north = (centerY - py) * mpp;
                double lat = lat0 + (north / radius);
                for (int i = 0; i < sampleW; i++)
                {
                    double px = rect.x + ((i + 0.5d) * rect.width / sampleW);
                    double east = (px - centerX) * mpp;
                    double lon = NormalizeLongitude(lon0 + (east / (radius * cosLat)));
                    int k = (j * sampleW) + i;
                    latitudes[k] = lat;
                    longitudes[k] = lon;
                    try
                    {
                        heights[k] = terrain.GetRawHeightMeters(body, lat, lon);
                    }
                    catch (Exception)
                    {
                        heights[k] = 0d;
                    }
                }
            }

            TerrainPaletteData palette = (terrain as HeightfieldTerrain)?.Palette ?? DefaultPalette;
            double seaLevel = model.BodySeaLevelMeters;
            double amplitude = Math.Max(1d, model.BodyTerrainAmplitudeMeters);
            double eastStep = mpp * rect.width / sampleW;
            double northStep = mpp * rect.height / sampleH;
            float sunEast = (float)Vector3d.Dot(model.SunDirection, model.ShipEast);
            float sunNorth = (float)Vector3d.Dot(model.SunDirection, model.ShipNorth);
            float sunUp = (float)Vector3d.Dot(model.SunDirection, model.ShipUp);

            Color32[] colors = new Color32[sampleW * sampleH];
            for (int j = 0; j < sampleH; j++)
            {
                for (int i = 0; i < sampleW; i++)
                {
                    int k = (j * sampleW) + i;
                    int left = (j * sampleW) + Math.Max(0, i - 1);
                    int right = (j * sampleW) + Math.Min(sampleW - 1, i + 1);
                    int down = (Math.Min(sampleH - 1, j + 1) * sampleW) + i;
                    int up = (Math.Max(0, j - 1) * sampleW) + i;
                    double slopeEast = (heights[right] - heights[left]) / Math.Max(1d, eastStep * (right == left ? 1d : 2d));
                    double slopeNorth = (heights[down] - heights[up]) / Math.Max(1d, northStep * (down == up ? 1d : 2d));
                    float light = Hillshade(slopeEast, slopeNorth, sunEast, sunNorth, sunUp);
                    colors[k] = TerrainColor(palette, heights[k], seaLevel, amplitude, latitudes[k], slopeEast, slopeNorth, light);
                }
            }

            // Fill the map in blocks (the tablet is a pixel display; bilinear
            // interpolation over 350k pixels was the dominant cost of a rebuild).
            for (int j = 0; j < sampleH; j++)
            {
                int y0 = rect.y + ((j * rect.height) / sampleH);
                int y1 = rect.y + (((j + 1) * rect.height) / sampleH);
                int cellHeight = Math.Max(1, y1 - y0);
                for (int i = 0; i < sampleW; i++)
                {
                    int x0 = rect.x + ((i * rect.width) / sampleW);
                    int x1 = rect.x + (((i + 1) * rect.width) / sampleW);
                    int cellWidth = Math.Max(1, x1 - x0);
                    canvas.Fill(x0, y0, cellWidth, cellHeight, colors[(j * sampleW) + i]);
                }
            }

            // Real ground track and the ship at its true sub-point.
            DrawSurfaceHistory(model, body, centerX, centerY, mpp, radius, lat0, lon0, cosLat);
            DrawSurfaceShip(centerX, centerY, model, rect);
        }

        private void DrawSurfaceHistory(NavMapModel model, OrbitingBody body, float centerX, float centerY,
            double mpp, double radius, double lat0, double lon0, double cosLat)
        {
            if (model.HistorySeconds <= 0d || model.History.Count < 2 || mpp <= 0d)
            {
                return;
            }

            bool hasPrevious = false;
            Vector2 previous = default;
            for (int i = 0; i < model.History.Count; i++)
            {
                NavShipSample sample = model.History[i];
                try
                {
                    body.EvaluateWorldState(sample.TimeSeconds, out Vector3d bodyPosition, out _);
                    if ((sample.Position - bodyPosition).Magnitude > body.Radius * 4d)
                    {
                        hasPrevious = false;
                        continue;
                    }

                    body.SurfaceLatLonAt(sample.Position, sample.TimeSeconds, out double latDeg, out double lonDeg);
                    double lat = latDeg * (Math.PI / 180d);
                    double lon = lon0 + NormalizeLongitude((lonDeg * (Math.PI / 180d)) - lon0);
                    double east = NormalizeLongitude(lon - lon0) * radius * cosLat;
                    double north = (lat - lat0) * radius;
                    var screen = new Vector2(
                        centerX + (float)(east / mpp),
                        centerY - (float)(north / mpp));
                    if (!IsFinite(screen) || !Inside(new RectInt(0, 0, Width, Height), screen, 2f))
                    {
                        hasPrevious = false;
                        continue;
                    }

                    if (hasPrevious)
                    {
                        canvas.Line(
                            Mathf.RoundToInt(previous.x), Mathf.RoundToInt(previous.y),
                            Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y), TabletTheme.Main);
                    }

                    previous = screen;
                    hasPrevious = true;
                }
                catch (Exception)
                {
                    hasPrevious = false;
                }
            }
        }

        private void DrawSurfaceShip(float centerX, float centerY, NavMapModel model, RectInt rect)
        {
            int x = Mathf.RoundToInt(centerX);
            int y = Mathf.RoundToInt(centerY);
            Color32 color = TabletTheme.Bright;
            canvas.Line(x, y - 7, x + 6, y, color);
            canvas.Line(x + 6, y, x, y + 7, color);
            canvas.Line(x, y + 7, x - 6, y, color);
            canvas.Line(x - 6, y, x, y - 7, color);
            canvas.Px(x, y, color);

            double east = model.ShipGroundEastMps;
            double north = model.ShipGroundNorthMps;
            double speed = Math.Sqrt((east * east) + (north * north));
            if (speed > 0.2d)
            {
                float length = 18f;
                float ex = x + (float)(east / speed * length);
                float ey = y - (float)(north / speed * length);
                canvas.Line(x, y, Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), TabletTheme.Main);
                canvas.Px(Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), color);
            }

            string altitude = "H " + TabletFormat.Dist(model.ShipHeightAboveTerrain);
            string velocity = "V " + TabletFormat.Speed(speed) + " М/С";
            RectInt box = default;
            bool hasBox = false;
            if (hoverKind == PickKind.Ship)
            {
                // In-place readout beside the marker: no separate tooltip window.
                MarkerPalette pal = MarkersFor(model);
                int tw = Mathf.Max(PixelCanvas.TextWidth(altitude), PixelCanvas.TextWidth(velocity)) + 10;
                const int th = 4 + (2 * PlateRowHeight);
                int tx = Mathf.Clamp(x + 16, rect.x + 2, Mathf.Max(rect.x + 2, rect.x + rect.width - tw - 2));
                int ty = Mathf.Clamp(y - (th / 2), rect.y + 2, Mathf.Max(rect.y + 2, rect.y + rect.height - th - 2));
                DrawTagBox(tx, ty, tw, th, pal);
                canvas.Text(tx + 5, ty + 3, altitude, pal.Light);
                canvas.Text(tx + 5, ty + 3 + PlateRowHeight, velocity, pal.Light);
                box = new RectInt(tx, ty, tw, th);
                hasBox = true;
            }

            picks.Add(new PickEntry
            {
                X = x,
                Y = y,
                Radius = 12f,
                Kind = PickKind.Ship,
                Box = box,
                HasBox = hasBox,
                Text = "АППАРАТ\n" + altitude + "\n" + velocity,
            });
        }

        private static double NormalizeLongitude(double radians)
        {
            double twoPi = Math.PI * 2d;
            radians -= twoPi * Math.Floor((radians + Math.PI) / twoPi);
            return radians;
        }

        private static float Hillshade(double slopeEast, double slopeNorth,
            float sunEast, float sunNorth, float sunUp)
        {
            Vector3 normal = new Vector3((float)-slopeEast, (float)-slopeNorth, 1f).normalized;
            Vector3 light = new Vector3(sunEast, sunNorth, Mathf.Max(0.05f, sunUp)).normalized;
            return Mathf.Clamp01(Vector3.Dot(normal, light));
        }

        internal static readonly TerrainPaletteData DefaultPalette = new TerrainPaletteData();

        internal static Color32 TerrainColor(TerrainPaletteData palette, double height, double seaLevel,
            double amplitude, double latitude, double slopeEast, double slopeNorth, float light)
        {
            if (palette == null)
            {
                palette = DefaultPalette;
            }

            bool hasSea = seaLevel > -1e29d;
            double rel;
            Color color;

            if (hasSea && height < seaLevel)
            {
                double depth = Math.Min(1d, (seaLevel - height) / Math.Max(100d, amplitude * 0.5d));
                color = Color.Lerp(palette.Sea, new Color(0.01f, 0.04f, 0.11f, 1f), (float)(depth * 0.8d));
                float shade = 0.82f + (0.18f * light);
                return ToColor32(color, shade);
            }

            if (hasSea)
            {
                rel = Math.Max(0d, Math.Min(1d, (height - seaLevel) / amplitude));
            }
            else
            {
                rel = Math.Max(0d, Math.Min(1d, ((height + (amplitude * 0.5d)) / amplitude)));
            }

            if (rel < 0.02d)
            {
                color = palette.Sand;
            }
            else if (rel < 0.08d)
            {
                color = Color.Lerp(palette.Sand, palette.Grass, (float)(rel / 0.08d));
            }
            else if (rel < 0.25d)
            {
                color = Color.Lerp(palette.Grass, palette.Forest, (float)((rel - 0.08d) / 0.17d));
            }
            else if (rel < 0.55d)
            {
                color = Color.Lerp(palette.Forest, palette.Rock, (float)((rel - 0.25d) / 0.30d));
            }
            else
            {
                color = Color.Lerp(palette.Rock, palette.Snow, (float)Math.Min(1d, (rel - 0.55d) / 0.45d));
            }

            double absLat = Math.Abs(latitude);
            if (absLat > 1.0d)
            {
                color = Color.Lerp(color, palette.Tundra, (float)Math.Min(1d, (absLat - 1.0d) / 0.35d));
            }

            double slope = Math.Sqrt((slopeEast * slopeEast) + (slopeNorth * slopeNorth));
            if (slope > 0.35d)
            {
                color = Color.Lerp(color, palette.Rock, (float)Math.Min(0.85d, (slope - 0.35d) / 1.1d));
            }

            if (slope > 1.2d || rel > 0.9d)
            {
                color = Color.Lerp(color, palette.Snow, (float)Math.Min(0.8d, Math.Max((slope - 1.2d) / 1.4d, (rel - 0.9d) / 0.1d)));
            }

            float shading = 0.55f + (0.45f * light);
            return ToColor32(color, shading);
        }

        private static Color32 ToColor32(Color color, float shade)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * shade * 255f), 0, 255),
                255);
        }

        private void DrawSurfaceHud(NavMapModel model, RectInt rect)
        {
            string name = model.DominantBody != null ? (model.DominantBody.Name ?? "?").ToUpperInvariant() : "?";
            int x = rect.x + 10;
            int y = rect.y + rect.height - 92;
            canvas.Fill(x - 4, y - 4, 236, 50, TabletTheme.PanelBg);
            canvas.Frame(x - 4, y - 4, 236, 50, TabletTheme.Line);
            canvas.Text(x, y, "РЕЛЬЕФ · " + name, TabletTheme.Main);
            canvas.Text(x, y + 12,
                "H " + TabletFormat.Dist(model.ShipHeightAboveTerrain) + " AGL", TabletTheme.Bright);
            canvas.Text(x, y + 22,
                "Ш " + TabletFormat.Signed(model.ShipLatitudeDegrees, 2) + "\u00B0  Д "
                + TabletFormat.Num(model.ShipLongitudeDegrees, 2) + "\u00B0", TabletTheme.Dim);
            canvas.Text(x, y + 32,
                "ВЕРТ. " + TabletFormat.Signed(model.ShipVerticalSpeedMps, 1) + " М/С", TabletTheme.Dim);

            int nx = rect.x + rect.width - 26;
            int ny = rect.y + 22;
            canvas.VLine(nx, ny, 18, TabletTheme.Bright);
            canvas.Line(nx, ny, nx - 4, ny + 6, TabletTheme.Bright);
            canvas.Line(nx, ny, nx + 4, ny + 6, TabletTheme.Bright);
            canvas.Text(nx - 3, ny - 10, "С", TabletTheme.Bright);

            DrawScaleBar(rect, 1d / Math.Max(1e-9d, lastSurfaceMetersPerPixel));
        }

        private double AutoRadius(NavMapModel model, int centerIndex, bool shipCenter)
        {
            double radius = 0d;
            Vector3d center = model.CenterPosition;

            if (shipCenter)
            {
                for (int i = 0; i < model.Lines.Count; i++)
                {
                    NavLine line = model.Lines[i];
                    if (line.Kind == NavLineKind.BodyOrbit)
                    {
                        continue;
                    }

                    radius = Math.Max(radius, MaxDistance(line.Points, center));
                }

                double frameDistance = model.FrameRelativeShipPosition.Magnitude;
                if (frameDistance > 0d)
                {
                    // Show the WHOLE planet plus the craft: the body centre is offset
                    // from the ship by frameDistance, so the required radius is
                    // frameDistance + body radius, not just the ship's distance.
                    radius = Math.Max(radius, (frameDistance + model.BodyRadiusMeters) * 1.12d);
                }
            }
            else if (centerIndex >= 0 && centerIndex < model.Bodies.Count)
            {
                NavBodyMarker body = model.Bodies[centerIndex];
                // Planet view: fit the body and the craft, not the whole moon system.
                // Deep orbits stay reachable with the wheel zoom.
                if (model.HasShip && model.FrameRelativeShipPosition.Magnitude > 0d)
                {
                    double shipDistance = (model.FrameRelativeShipPosition - center).Magnitude;
                    if (shipDistance > 0d)
                    {
                        // Whole planet + the craft marker (at/above the surface).
                        radius = Math.Max(radius, shipDistance * 1.08d);
                    }
                }

                radius = Math.Max(radius, body.RadiusMeters * 1.08d);
            }

            if (!(radius > 1d) || !double.IsFinite(radius))
            {
                radius = Math.Max(1e5d, model.FrameRelativeShipPosition.Magnitude * 2d);
            }

            return radius;
        }

        private static double MaxDistance(List<Vector3d> points, Vector3d center)
        {
            double max = 0d;
            for (int i = 0; i < points.Count; i++)
            {
                double d = (points[i] - center).Magnitude;
                if (d > max)
                {
                    max = d;
                }
            }

            return max;
        }

        private void DrawGrid(RectInt rect, Func<Vector3d, Vector2> project, double radius,
            Vector3d center, Vector3d right, Vector3d up)
        {
            // Rings at fractions of the auto-fit radius. Drawn in the orbital plane:
            // the tilt turns them into the same ellipses as the orbit lines.
            for (int ring = 1; ring <= 4; ring++)
            {
                double r = radius * ring / 4d;
                Vector2 previous = default;
                bool hasPrevious = false;
                for (int i = 0; i <= 64; i++)
                {
                    double a = (Math.PI * 2d) * i / 64d;
                    Vector3d p = center + (right * (Math.Cos(a) * r)) + (up * (Math.Sin(a) * r));
                    Vector2 screen = project(p);
                    if (hasPrevious)
                    {
                        DrawClippedLine(previous, screen, rect, TabletTheme.Line, false);
                    }

                    previous = screen;
                    hasPrevious = true;
                }
            }
        }

        private void DrawPlaneRing(Func<Vector3d, Vector2> project, Vector3d right, Vector3d up,
            double ringRadius, RectInt rect)
        {
            if (!(ringRadius > 0d) || !double.IsFinite(ringRadius))
            {
                return;
            }

            const int segments = 96;
            Vector2 previous = default;
            bool hasPrevious = false;
            for (int i = 0; i <= segments; i++)
            {
                double angle = (Math.PI * 2d) * i / segments;
                Vector3d point = (right * (Math.Cos(angle) * ringRadius)) + (up * (Math.Sin(angle) * ringRadius));
                Vector2 screen = project(point);
                if (hasPrevious)
                {
                    DrawClippedLine(previous, screen, rect, TabletTheme.Line, true, 1);
                }

                previous = screen;
                hasPrevious = true;
            }
        }

        private static bool IsFinite(Vector2 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y);
        }

        /// <summary>
        /// Keeps a projected marker inside the map. When the real point is off-map
        /// (an AU-scale apsis in a star frame at planet zoom) the marker is placed
        /// on the edge and <paramref name="outside"/> tells the drawing code to add
        /// an outward arrow, so the apsis never silently disappears with the frame.
        /// </summary>
        private static Vector2 ClampToMap(RectInt rect, Vector2 point, float margin, out bool outside)
        {
            float xmin = rect.x + margin;
            float xmax = rect.x + rect.width - margin;
            float ymin = rect.y + margin;
            float ymax = rect.y + rect.height - margin;
            float x = Mathf.Clamp(point.x, xmin, xmax);
            float y = Mathf.Clamp(point.y, ymin, ymax);
            outside = x != point.x || y != point.y;
            return new Vector2(x, y);
        }

        // ─── Curves ───────────────────────────────────────────────────────────
        //
        // Every trajectory used to be a Bresenham chain between rounded integer
        // endpoints. Two things made that read as "broken and pixelated":
        //   * a 2 px staircase per segment, worst on the shallow chords of an orbit;
        //   * the source polyline is only ~160 samples long, so a zoomed view showed
        //     its corners as real kinks (a chord can be a hundred pixels long).
        // A line is now built in three steps:
        //   1. project the samples (and drop the pieces hidden by a globe);
        //   2. resample the projected polyline as a smooth Hermite curve with about
        //      one mini-segment per pixel, so the corners become arcs at ANY zoom;
        //   3. rasterise each mini-segment as an anti-aliased capsule
        //      (PixelCanvas.StrokeAA), which removes the staircase and keeps a thick
        //      stroke evenly bright (the pieces tessellate, they do not overlap).
        // The buffers are reused: a rebuild runs a few times per second and a camera
        // drag redraws at 30 fps, so a per-line allocation would churn the GC.

        private Vector2[] curvePoints = new Vector2[512];
        private Color32[] curveColors = new Color32[512];

        /// <summary>True when the segment from this index to the next one must not be drawn.</summary>
        private bool[] curveBreaks = new bool[512];

        /// <summary>Target length of one mini-segment, screen pixels.</summary>
        private const float CurveStepPixels = 1.25f;

        /// <summary>Dash pattern along the curve, screen pixels (5 on / 4 off, as the old lines).</summary>
        private const float DashOn = 5f;
        private const float DashPeriod = 9f;

        private void EnsureCurveCapacity(int count)
        {
            if (curvePoints.Length >= count)
            {
                return;
            }

            int size = Mathf.NextPowerOfTwo(count);
            Array.Resize(ref curvePoints, size);
            Array.Resize(ref curveColors, size);
            Array.Resize(ref curveBreaks, size);
        }

        /// <summary>
        /// Polyline whose colour runs from <paramref name="start"/> to
        /// <paramref name="end"/> along the samples: a trail fades into the past and
        /// a forecast fades into the future, so the direction of time is visible.
        /// <paramref name="glow"/> adds a wider, very dim halo pass first, so a bright
        /// trajectory reads as phosphor glow instead of a hard ridge.
        /// </summary>
        private void DrawPolylineFade(List<Vector3d> points, Func<Vector3d, Vector2> project, RectInt rect,
            Color32 start, Color32 end, bool dashed, float width, float glow)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            int count = ProjectCurve(points, project, start, end);
            DrawCurveBuffers(count, rect, dashed, width, glow);
        }

        /// <summary>
        /// Same curve, but only the pieces in front of the globe are kept. The
        /// occlusion test subdivides each source segment, so the visible edge follows
        /// the horizon instead of jumping a whole chord.
        /// </summary>
        private void DrawPolylineNearFade(List<Vector3d> points, Func<Vector3d, Vector2> project, RectInt rect,
            Color32 start, Color32 end, bool dashed, float width, Vector3d occluderCenter,
            double occluderRadius, Vector3d screenRight, Vector3d screenUp, Vector3d camDir, float glow)
        {
            if (points == null || points.Count < 2 || !(occluderRadius > 0d))
            {
                return;
            }

            int count = ProjectCurveNear(points, project, start, end, occluderCenter, occluderRadius,
                screenRight, screenUp, camDir);
            DrawCurveBuffers(count, rect, dashed, width, glow);
        }

        /// <summary>Projects a sample list into the curve buffers with a fade colour along it.</summary>
        private int ProjectCurve(List<Vector3d> points, Func<Vector3d, Vector2> project,
            Color32 start, Color32 end)
        {
            int count = points.Count;
            EnsureCurveCapacity(count);
            float last = Mathf.Max(1, count - 1);
            for (int i = 0; i < count; i++)
            {
                curvePoints[i] = project(points[i]);
                curveColors[i] = i == count - 1 ? end : PixelCanvas.Mix(start, end, i / last);
                curveBreaks[i] = false;
            }

            return count;
        }

        private int ProjectCurveNear(List<Vector3d> points, Func<Vector3d, Vector2> project,
            Color32 start, Color32 end, Vector3d occluderCenter, double occluderRadius,
            Vector3d screenRight, Vector3d screenUp, Vector3d camDir)
        {
            const int Subdivisions = 4;
            int last = points.Count - 1;
            int count = (last * Subdivisions) + 1;
            EnsureCurveCapacity(count);

            double radiusSquared = occluderRadius * occluderRadius * 1.02d;
            float fade = Mathf.Max(1, last);

            curvePoints[0] = project(points[0]);
            curveColors[0] = start;
            curveBreaks[0] = false;
            int n = 1;
            for (int i = 1; i <= last; i++)
            {
                Vector3d a = points[i - 1];
                Vector3d b = points[i];
                for (int s = 1; s <= Subdivisions; s++)
                {
                    float t = s / (float)Subdivisions;
                    Vector3d point = a + ((b - a) * t);
                    Vector3d q = point - occluderCenter;
                    double dx = Vector3d.Dot(q, screenRight);
                    double dy = Vector3d.Dot(q, screenUp);
                    bool insideDisk = (dx * dx) + (dy * dy) <= radiusSquared;
                    bool nearSide = Vector3d.Dot(q, camDir) > 0d;

                    curvePoints[n] = project(point);
                    curveColors[n] = PixelCanvas.Mix(start, end, ((i - 1) + t) / fade);
                    curveBreaks[n] = !(insideDisk && nearSide);
                    n++;
                }
            }

            return n;
        }

        private void DrawCurveBuffers(int count, RectInt rect, bool dashed, float width, float glow)
        {
            if (count < 2)
            {
                return;
            }

            if (glow > 0f)
            {
                // The halo runs on a coarser tessellation: it is soft, so a 3 px step
                // is invisible, and the wider capsule would otherwise triple its cost.
                RasterizeCurve(count, rect, dashed, width + (glow * 2f), 0.085f,
                    CurveStepPixels * 2.5f);
            }

            RasterizeCurve(count, rect, dashed, width, 1f, CurveStepPixels);
        }

        private void RasterizeCurve(int count, RectInt rect, bool dashed, float width, float alpha,
            float stepPixels)
        {
            float dashPhase = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 p1 = curvePoints[i];
                Vector2 p2 = curvePoints[i + 1];
                if (!IsFinite(p1) || !IsFinite(p2))
                {
                    continue;
                }

                bool drawn = !curveBreaks[i];
                Vector2 p0 = i > 0 && !curveBreaks[i - 1] ? curvePoints[i - 1] : p1;
                Vector2 p3 = i + 2 < count && !curveBreaks[i + 1] ? curvePoints[i + 2] : p2;

                float chord = Vector2.Distance(p1, p2);
                int steps = Mathf.Clamp(Mathf.CeilToInt(chord / stepPixels), 1, 128);
                Color32 colorA = curveColors[i];
                Color32 colorB = curveColors[i + 1];
                Vector2 previous = p1;
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 point = SmoothPoint(p0, p1, p2, p3, t);
                    if (!IsFinite(point))
                    {
                        previous = p2;
                        continue;
                    }

                    float length = Vector2.Distance(previous, point);
                    bool visible = drawn
                        && (!dashed || Mathf.Repeat(dashPhase + (length * 0.5f), DashPeriod) < DashOn);
                    if (visible && length > 1e-4f)
                    {
                        Vector2 a = previous;
                        Vector2 b = point;
                        if (Clip(ref a, ref b, rect, 4f))
                        {
                            canvas.StrokeAA(a, b, PixelCanvas.Mix(colorA, colorB, t), width, alpha);
                        }
                    }

                    previous = point;
                    dashPhase += length;
                }
            }
        }

        /// <summary>
        /// Cubic Hermite point through <paramref name="p1"/> and <paramref name="p2"/>.
        /// The tangent direction comes from the neighbours, but its magnitude is the
        /// local chord length: that is what turns a 160-sample ellipse into the arc it
        /// represents, while an unevenly sampled trail cannot bulge far past its real
        /// corners the way a uniform Catmull-Rom spline would.
        /// </summary>
        private static Vector2 SmoothPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            Vector2 chord = p2 - p1;
            float length = chord.magnitude;
            Vector2 first = p2 - p0;
            Vector2 second = p3 - p1;
            float firstLength = first.magnitude;
            float secondLength = second.magnitude;
            Vector2 tangent1 = firstLength > 1e-4f ? first * (length / firstLength) : chord;
            Vector2 tangent2 = secondLength > 1e-4f ? second * (length / secondLength) : chord;

            float t2 = t * t;
            float t3 = t2 * t;
            return (p1 * ((2f * t3) - (3f * t2) + 1f))
                + (tangent1 * (t3 - (2f * t2) + t))
                + (p2 * ((-2f * t3) + (3f * t2)))
                + (tangent2 * (t3 - t2));
        }

        private void DrawClippedLine(Vector2 a, Vector2 b, RectInt rect, Color32 color, bool dashed, int width = 1)
        {
            float dashPhase = 0f;
            DrawClippedLine(a, b, rect, color, dashed, width, ref dashPhase);
        }

        private void DrawClippedLine(Vector2 a, Vector2 b, RectInt rect, Color32 color, bool dashed,
            int width, ref float dashPhase)
        {
            if (!Clip(ref a, ref b, rect, 4f))
            {
                return;
            }

            int x0 = Mathf.RoundToInt(a.x);
            int y0 = Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x);
            int y1 = Mathf.RoundToInt(b.y);

            if (width <= 1)
            {
                if (dashed)
                {
                    DashedLine(x0, y0, x1, y1, color, ref dashPhase);
                }
                else
                {
                    canvas.Line(x0, y0, x1, y1, color);
                }

                return;
            }

            int half = width / 2;
            float startPhase = dashPhase;
            for (int oy = -half; oy <= half; oy++)
            {
                for (int ox = -half; ox <= half; ox++)
                {
                    if (dashed)
                    {
                        // Every thickness copy shows the same dash pattern; the
                        // phase advances once for the whole segment below.
                        float localPhase = startPhase;
                        DashedLine(x0 + ox, y0 + oy, x1 + ox, y1 + oy, color, ref localPhase);
                    }
                    else
                    {
                        canvas.Line(x0 + ox, y0 + oy, x1 + ox, y1 + oy, color);
                    }
                }
            }

            if (dashed)
            {
                dashPhase = startPhase + Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) + 1;
            }
        }

        /// <summary>
        /// Dashed Bresenham with a phase carried across the polyline's segments.
        /// Restarting the pattern at every vertex made the dash length depend on the
        /// segment length (a dense orbit trail turned into irregular blobs).
        /// </summary>
        private void DashedLine(int x0, int y0, int x1, int y1, Color32 c, ref float dashPhase)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int step = Mathf.FloorToInt(dashPhase);
            int start = step;
            while (true)
            {
                if (((step / 5) % 2) == 0)
                {
                    canvas.Px(x0, y0, c);
                }

                step++;
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            dashPhase = start + Mathf.Max(dx, -dy) + 1;
        }

        /// <summary>Liang–Barsky clip; a zero-length segment is clipped to the map rect.</summary>
        private static bool Clip(ref Vector2 a, ref Vector2 b, RectInt rect, float margin)
        {
            float xmin = rect.x + margin;
            float ymin = rect.y + margin;
            float xmax = rect.x + rect.width - margin;
            float ymax = rect.y + rect.height - margin;
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float t0 = 0f;
            float t1 = 1f;

            if (!ClipTest(-dx, a.x - xmin, ref t0, ref t1)
                || !ClipTest(dx, xmax - a.x, ref t0, ref t1)
                || !ClipTest(-dy, a.y - ymin, ref t0, ref t1)
                || !ClipTest(dy, ymax - a.y, ref t0, ref t1))
            {
                return false;
            }

            Vector2 original = a;
            a = new Vector2(original.x + (dx * t0), original.y + (dy * t0));
            b = new Vector2(original.x + (dx * t1), original.y + (dy * t1));
            return true;
        }

        private static bool ClipTest(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f)
            {
                return q >= 0f;
            }

            float r = q / p;
            if (p < 0f)
            {
                if (r > t1)
                {
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }

        private static bool Inside(RectInt rect, Vector2 p, float margin)
        {
            return p.x >= rect.x - margin && p.x <= rect.x + rect.width + margin
                && p.y >= rect.y - margin && p.y <= rect.y + rect.height + margin;
        }

        private static Vector3d RotateAround(Vector3d v, Vector3d axis, float radians)
        {
            if (Math.Abs(radians) < 1e-6f)
            {
                return v;
            }

            QuaternionD q = QuaternionD.FromAxisAngle(axis, radians);
            return q.Rotate(v);
        }

        private static Color32 BodyColor(int index)
        {
            switch (Mathf.Abs(index) % 3)
            {
                case 0: return OrbitColorA;
                case 1: return OrbitColorB;
                default: return OrbitColorC;
            }
        }

        // ─── Markers ──────────────────────────────────────────────────────────

        private void DrawDisc(float cx, float cy, float radius, Color32 color)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    if ((x * x) + (y * y) <= r * r)
                    {
                        canvas.Px(Mathf.RoundToInt(cx) + x, Mathf.RoundToInt(cy) + y, color);
                    }
                }
            }
        }

        private void DrawCircle(float cx, float cy, float radius, Color32 color)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));
            int cxI = Mathf.RoundToInt(cx);
            int cyI = Mathf.RoundToInt(cy);
            int x = r;
            int y = 0;
            int err = 1 - r;
            while (x >= y)
            {
                Plot8(cxI, cyI, x, y, color);
                Plot8(cxI, cyI, y, x, color);
                y++;
                if (err < 0)
                {
                    err += (2 * y) + 1;
                }
                else
                {
                    x--;
                    err += (2 * (y - x)) + 1;
                }
            }
        }

        private void Plot8(int cx, int cy, int x, int y, Color32 color)
        {
            canvas.Px(cx + x, cy + y, color);
            canvas.Px(cx + y, cy + x, color);
            canvas.Px(cx - y, cy + x, color);
            canvas.Px(cx - x, cy + y, color);
            canvas.Px(cx - x, cy - y, color);
            canvas.Px(cx - y, cy - x, color);
            canvas.Px(cx + y, cy - x, color);
            canvas.Px(cx + x, cy - y, color);
        }

        private void DrawCross(float x, float y, float size, Color32 color)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            int s = Mathf.RoundToInt(size);
            canvas.Line(xi - s, yi - s, xi + s, yi + s, color);
            canvas.Line(xi - s, yi + s, xi + s, yi - s, color);
        }

        private static readonly string[] ApoGlyph = { "..#..", ".###.", "#####" };
        private static readonly string[] PeriGlyph = { "#####", ".###.", "..#.." };

        /// <summary>Hazard reticle glyph for the ground-contact plate (an X in a box).</summary>
        private static readonly string[] ImpactGlyph = { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" };

        /// <summary>Chunky 2x tag plate: the old 1x labels were unreadable on a monitor.</summary>
        private const int ApsisTagScale = 2;
        private const int ApsisHeaderHeight = 20;
        private const int ApsisValueRowHeight = 18;

        /// <summary>Inset of an off-map apsis marker from the map edge (room for its arrow).</summary>
        private const int ApsisEdgeMargin = 26;

        /// <summary>Row step for the smaller 1x plates (ship, body, surface readout).</summary>
        private const int PlateRowHeight = 9;

        private static string ApsisDetail(double altitudeMeters, double speedMps)
        {
            return "H " + TabletFormat.Dist(altitudeMeters)
                + "\nV " + TabletFormat.Speed(speedMps) + " М/С";
        }

        /// <summary>Small cross with a letter: used for the planned burn points.</summary>
        private void DrawMarker(float x, float y, string label, Color32 color, RectInt rect)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            canvas.PxBlend(xi, yi, color, 0.20f);
            canvas.VLine(xi, yi - 4, 9, color);
            canvas.HLine(xi - 4, yi, 9, color);
            canvas.PxBlend(xi - 1, yi, TabletTheme.Bg, 1f);
            canvas.PxBlend(xi + 1, yi, TabletTheme.Bg, 1f);
            canvas.PxBlend(xi, yi - 1, TabletTheme.Bg, 1f);
            canvas.PxBlend(xi, yi + 1, TabletTheme.Bg, 1f);
            if (Inside(rect, new Vector2(x, y), 10f))
            {
                canvas.TextHalo(xi + 6, yi - 3, label, color, TabletTheme.Bg);
            }
        }

        /// <summary>
        /// Tag plate: soft shadow, dark two-tone fill, a 2 px colour "spine" on the
        /// left and a single-pixel frame. Still pixel art, but it reads as a HUD
        /// callout instead of a flat coloured box.
        /// </summary>
        private void DrawTagBox(int x, int y, int w, int h, MarkerPalette pal)
        {
            if (w < 3 || h < 3)
            {
                return;
            }

            var black = new Color32(0, 0, 0, 255);
            canvas.FillBlend(x + 2, y + 2, w, h, black, 0.34f);

            Color32 top = PixelCanvas.Mix(pal.Dark, pal.Main, 0.09f);
            Color32 bottom = PixelCanvas.Mix(pal.Dark, black, 0.30f);
            for (int i = 0; i < h; i++)
            {
                float t = h > 1 ? i / (float)(h - 1) : 0f;
                canvas.HLine(x, y + i, w, PixelCanvas.Mix(top, bottom, t));
            }

            canvas.HLine(x + 2, y + 1, Mathf.Max(1, w - 4), PixelCanvas.Mix(top, pal.Light, 0.10f));
            canvas.Frame(x, y, w, h, PixelCanvas.Mix(pal.Main, pal.Dark, 0.42f));

            // Colour spine on the left edge; the two tones keep it from reading
            // as a 2 px border.
            canvas.VLine(x, y + 1, h - 2, pal.Main);
            canvas.VLine(x + 1, y + 2, Mathf.Max(1, h - 4), PixelCanvas.Mix(pal.Main, pal.Dark, 0.55f));
        }

        /// <summary>Small callout notch that joins the plate to its leader line.</summary>
        private void DrawTagNotch(int x, int y, int w, int h, MarkerPalette pal, bool left, int midY)
        {
            int mid = Mathf.Clamp(midY, y + 3, y + h - 4);
            int edgeX = left ? x : x + w - 1;
            int dir = left ? -1 : 1;
            Color32 fill = PixelCanvas.Mix(pal.Dark, pal.Main, 0.08f);
            Color32 edge = PixelCanvas.Mix(pal.Main, pal.Dark, 0.30f);
            for (int i = -2; i <= 2; i++)
            {
                int length = 3 - Mathf.Abs(i);
                int start = edgeX + (dir * length);
                int count = length + 1;
                if (count <= 0)
                {
                    continue;
                }

                canvas.HLine(Mathf.Min(start, start + count - 1), mid + i, count, fill);
                canvas.PxBlend(start, mid + i, edge, 0.9f);
            }
        }

        private void FillDiamond(int cx, int cy, int r, Color32 color)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int half = r - Mathf.Abs(dy);
                canvas.HLine(cx - half, cy + dy, (half * 2) + 1, color);
            }
        }

        /// <summary>Line with a 1 px dark outline so it stays readable over orbits and the globe.</summary>
        private void DrawHaloLine(int x0, int y0, int x1, int y1, Color32 color, Color32 halo)
        {
            canvas.Line(x0 + 1, y0, x1 + 1, y1, halo);
            canvas.Line(x0 - 1, y0, x1 - 1, y1, halo);
            canvas.Line(x0, y0 + 1, x1, y1 + 1, halo);
            canvas.Line(x0, y0 - 1, x1, y1 - 1, halo);
            canvas.Line(x0, y0, x1, y1, color);
        }

        /// <summary>True when a plate would cover an already drawn tag or name plate.</summary>
        private bool PlateOverlaps(RectInt box)
        {
            for (int i = 0; i < picks.Count; i++)
            {
                PickEntry entry = picks[i];
                if (!entry.HasBox)
                {
                    continue;
                }

                RectInt other = entry.Box;
                if (box.x < other.x + other.width + 2
                    && box.x + box.width + 2 > other.x
                    && box.y < other.y + other.height + 2
                    && box.y + box.height + 2 > other.y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Places a callout plate beside its anchor, moving it to the next free side
        /// or row when it would cover another plate. Clamped to the map rect.
        /// </summary>
        private bool TryPlacePlate(RectInt bounds, int anchorX, int anchorY, int w, int h,
            bool preferRight, bool preferDown, int gapX, int gapY, out int x, out int y)
        {
            int side = preferRight ? 1 : -1;
            int vertical = preferDown ? 1 : -1;
            int baseX = anchorX + (side * gapX) - (side < 0 ? w : 0);
            int baseY = anchorY + (vertical * gapY) - (vertical < 0 ? h : 0);
            int oppositeX = anchorX - (side * gapX) - (side > 0 ? w : 0);

            x = baseX;
            y = baseY;
            for (int attempt = 0; attempt < 9; attempt++)
            {
                int cx = baseX;
                int cy = baseY;
                switch (attempt)
                {
                    case 1:
                        cy += vertical * (h + 6);
                        break;
                    case 2:
                        cy -= vertical * (h + 6);
                        break;
                    case 3:
                        cy += vertical * 2 * (h + 6);
                        break;
                    case 4:
                        cx = oppositeX;
                        break;
                    case 5:
                        cy -= vertical * 2 * (h + 6);
                        break;
                    case 6:
                        cx = oppositeX;
                        cy += vertical * (h + 6);
                        break;
                    case 7:
                        cx = oppositeX;
                        cy -= vertical * (h + 6);
                        break;
                    case 8:
                        cx = anchorX - (w / 2);
                        cy = bounds.y + 2;
                        break;
                }

                cx = Mathf.Clamp(cx, bounds.x + 2, Mathf.Max(bounds.x + 2, bounds.x + bounds.width - w - 2));
                cy = Mathf.Clamp(cy, bounds.y + 2, Mathf.Max(bounds.y + 2, bounds.y + bounds.height - h - 2));
                if (!PlateOverlaps(new RectInt(cx, cy, w, h)))
                {
                    x = cx;
                    y = cy;
                    return true;
                }
            }

            x = Mathf.Clamp(x, bounds.x + 2, Mathf.Max(bounds.x + 2, bounds.x + bounds.width - w - 2));
            y = Mathf.Clamp(y, bounds.y + 2, Mathf.Max(bounds.y + 2, bounds.y + bounds.height - h - 2));
            return false;
        }

        /// <summary>
        /// Apsis mark: a reticle on the exact orbit point, a leader and a large tag
        /// with a triangle (up = apoapsis, down = periapsis) and the letters.
        /// While hovered the same plate expands in place with the altitude and speed
        /// at that point; <paramref name="box"/> is the exact drawn rectangle and is
        /// reused as the pick area, so the hover zone always matches the plate.
        /// <paramref name="offMap"/> draws an outward chevron: the real point is
        /// outside the view and the marker is clamped to the map edge.
        /// <paramref name="suffix"/> is appended to the "АП"/"ПР" letters — the
        /// planned orbit's pair carries "·М" so it cannot be confused with the
        /// ballistic pair even on a colour-blind display.
        /// </summary>
        private void DrawApsisMarker(float x, float y, bool apoapsis, MarkerPalette pal, RectInt rect,
            string detail, bool expanded, bool offMap, out RectInt box, string suffix = "")
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            string letter = (apoapsis ? "АП" : "ПР") + suffix;

            string[] rows = expanded && !string.IsNullOrEmpty(detail)
                ? detail.Split('\n')
                : Array.Empty<string>();
            int glyphW = 5 * ApsisTagScale;
            int letterW = PixelCanvas.TextWidth(letter, ApsisTagScale);
            int innerW = glyphW + 5 + letterW;
            int tw = innerW + 13;
            int dataH = rows.Length > 0 ? 3 + (rows.Length * ApsisValueRowHeight) : 0;
            for (int i = 0; i < rows.Length; i++)
            {
                tw = Mathf.Max(tw, PixelCanvas.TextWidth(rows[i], ApsisTagScale) + 14);
            }

            int th = ApsisHeaderHeight + dataH;
            // Apoapsis grows upward, periapsis downward: the header stays put, the
            // values appear away from the orbit point and never cover the reticle.
            TryPlacePlate(rect, xi, yi, tw, th, true, !apoapsis, 15, 11, out int tx, out int ty);

            // Leader from the reticle to the notch on the plate's near edge.
            bool plateRight = tx > xi;
            int edgeX = plateRight ? tx - 4 : tx + tw + 3;
            int edgeY = Mathf.Clamp(yi, ty + 4, ty + th - 4);
            DrawHaloLine(xi + (plateRight ? 6 : -6), yi, edgeX, edgeY, pal.Main, pal.Dark);

            DrawTagBox(tx, ty, tw, th, pal);
            DrawTagNotch(tx, ty, tw, th, pal, plateRight, edgeY);

            int headerY = apoapsis ? ty + dataH : ty;
            // Anchored to the left edge so the header does not slide when the plate
            // widens for the value rows.
            int headerX = tx + 6;
            canvas.Bitmap(headerX, headerY + 7, apoapsis ? ApoGlyph : PeriGlyph, pal.Main, ApsisTagScale);
            canvas.Text(headerX + glyphW + 5, headerY + 3, letter, pal.Light, ApsisTagScale);

            if (rows.Length > 0)
            {
                canvas.HLine(tx + 4, apoapsis ? headerY - 1 : headerY + ApsisHeaderHeight, tw - 7,
                    PixelCanvas.Mix(pal.Main, pal.Dark, 0.30f));
                int rowY = apoapsis ? ty + 2 : headerY + ApsisHeaderHeight + 3;
                for (int i = 0; i < rows.Length; i++)
                {
                    canvas.Text(tx + 7, rowY, rows[i], pal.Light, ApsisTagScale);
                    rowY += ApsisValueRowHeight;
                }
            }

            // Reticle on top of everything else: a soft glow, a dark separation
            // ring, the main ring, four ticks and a bright centre.
            DrawCircleBlend(xi, yi, 8f, pal.Main, 0.10f);
            DrawCircle(xi, yi, 6, pal.Dark);
            DrawCircle(xi, yi, 5, pal.Main);
            canvas.HLine(xi - 10, yi, 4, pal.Main);
            canvas.HLine(xi + 7, yi, 4, pal.Main);
            canvas.VLine(xi, yi - 10, 4, pal.Main);
            canvas.VLine(xi, yi + 7, 4, pal.Main);
            canvas.Px(xi, yi, pal.Light);

            // Off-map: an outward chevron on the edge marker. The reticle is clamped,
            // but it must not read as the true apsis point inside the view.
            if (offMap)
            {
                Vector2 outward = new Vector2(
                    xi - (rect.x + (rect.width * 0.5f)),
                    yi - (rect.y + (rect.height * 0.5f)));
                if (outward.sqrMagnitude > 1e-6f)
                {
                    outward.Normalize();
                    int tipX = xi + Mathf.RoundToInt(outward.x * 14f);
                    int tipY = yi + Mathf.RoundToInt(outward.y * 14f);
                    int baseX = xi + Mathf.RoundToInt(outward.x * 8f);
                    int baseY = yi + Mathf.RoundToInt(outward.y * 8f);
                    int perpX = Mathf.RoundToInt(-outward.y * 4f);
                    int perpY = Mathf.RoundToInt(outward.x * 4f);
                    DrawHaloLine(tipX, tipY, baseX + perpX, baseY + perpY, pal.Main, pal.Dark);
                    DrawHaloLine(tipX, tipY, baseX - perpX, baseY - perpY, pal.Main, pal.Dark);
                }
            }

            box = new RectInt(tx, ty, tw, th);
        }

        /// <summary>
        /// Ground contact of a forecast leg: a hazard reticle on the surface point, a
        /// leader and a plate. The countdown is always shown (it is the reason the line
        /// stops there); hovering adds the body and the impact speed. Same plate
        /// machinery as the apsis tags, so the plates never overlap.
        /// </summary>
        private void DrawImpactMarker(float x, float y, string title, string bodyName,
            double toImpact, double speedMps, RectInt rect, bool expanded, out RectInt box)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);

            string[] rows = expanded
                ? new[]
                {
                    bodyName.ToUpperInvariant(),
                    "T+ " + TabletFormat.Time(toImpact),
                    "V " + TabletFormat.Speed(speedMps) + " М/С",
                }
                : new[] { "T+ " + TabletFormat.Time(toImpact) };

            int glyphW = 5 * ApsisTagScale;
            int tw = glyphW + 5 + PixelCanvas.TextWidth(title, ApsisTagScale) + 13;
            for (int i = 0; i < rows.Length; i++)
            {
                tw = Mathf.Max(tw, PixelCanvas.TextWidth(rows[i], ApsisTagScale) + 14);
            }

            int th = ApsisHeaderHeight + 3 + (rows.Length * ApsisValueRowHeight);
            TryPlacePlate(rect, xi, yi, tw, th, true, false, 15, 11, out int tx, out int ty);

            bool plateRight = tx > xi;
            int edgeX = plateRight ? tx - 4 : tx + tw + 3;
            int edgeY = Mathf.Clamp(yi, ty + 4, ty + th - 4);
            DrawHaloLine(xi + (plateRight ? 6 : -6), yi, edgeX, edgeY, ImpactMarkers.Main, ImpactMarkers.Dark);

            DrawTagBox(tx, ty, tw, th, ImpactMarkers);
            DrawTagNotch(tx, ty, tw, th, ImpactMarkers, plateRight, edgeY);

            canvas.Bitmap(tx + 6, ty + 5, ImpactGlyph, ImpactMarkers.Main, ApsisTagScale);
            canvas.Text(tx + 6 + glyphW + 5, ty + 3, title, ImpactMarkers.Light, ApsisTagScale);
            canvas.HLine(tx + 4, ty + ApsisHeaderHeight - 1, tw - 7,
                PixelCanvas.Mix(ImpactMarkers.Main, ImpactMarkers.Dark, 0.30f));
            int rowY = ty + ApsisHeaderHeight + 3;
            for (int i = 0; i < rows.Length; i++)
            {
                canvas.Text(tx + 7, rowY, rows[i], ImpactMarkers.Light, ApsisTagScale);
                rowY += ApsisValueRowHeight;
            }

            // Reticle: a ring with an X inside, so it reads as a contact point and not
            // as a moving object. Drawn last so it stays on top of its own plate leader.
            DrawCircleBlend(xi, yi, 9f, ImpactMarkers.Main, 0.12f);
            DrawCircle(xi, yi, 6, ImpactMarkers.Dark);
            DrawCircle(xi, yi, 5, ImpactMarkers.Main);
            canvas.Line(xi - 3, yi - 3, xi + 3, yi + 3, ImpactMarkers.Light);
            canvas.Line(xi - 3, yi + 3, xi + 3, yi - 3, ImpactMarkers.Light);

            box = new RectInt(tx, ty, tw, th);
        }

        private void DrawCircleBlend(float cx, float cy, float radius, Color32 color, float alpha)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));
            int cxI = Mathf.RoundToInt(cx);
            int cyI = Mathf.RoundToInt(cy);
            int x = r;
            int y = 0;
            int err = 1 - r;
            while (x >= y)
            {
                Plot8Blend(cxI, cyI, x, y, color, alpha);
                Plot8Blend(cxI, cyI, y, x, color, alpha);
                y++;
                if (err < 0)
                {
                    err += (2 * y) + 1;
                }
                else
                {
                    x--;
                    err += (2 * (y - x)) + 1;
                }
            }
        }

        /// <summary>Soft circular shading: a marker halo that never shows a rectangular edge.</summary>
        private void DrawGlowDisc(float cx, float cy, float radius, Color32 color, float alpha)
        {
            int r = Mathf.Max(1, Mathf.CeilToInt(radius));
            int cxI = Mathf.RoundToInt(cx);
            int cyI = Mathf.RoundToInt(cy);
            float inv = 1f / r;
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    float d = Mathf.Sqrt((x * x) + (y * y));
                    if (d > r)
                    {
                        continue;
                    }

                    float t = 1f - (d * inv);
                    canvas.PxBlend(cxI + x, cyI + y, color, alpha * t * t);
                }
            }
        }

        private void Plot8Blend(int cx, int cy, int x, int y, Color32 color, float alpha)
        {
            canvas.PxBlend(cx + x, cy + y, color, alpha);
            canvas.PxBlend(cx + y, cy + x, color, alpha);
            canvas.PxBlend(cx - y, cy + x, color, alpha);
            canvas.PxBlend(cx - x, cy + y, color, alpha);
            canvas.PxBlend(cx - x, cy - y, color, alpha);
            canvas.PxBlend(cx - y, cy - x, color, alpha);
            canvas.PxBlend(cx + y, cy - x, color, alpha);
            canvas.PxBlend(cx + x, cy - y, color, alpha);
        }

        /// <summary>Screen-space direction of a world velocity (x right, y down). False when it has none.</summary>
        private static bool TryProjectDirection(Vector3d velocity, Vector3d screenRight, Vector3d screenUp,
            out float px, out float py)
        {
            px = 0f;
            py = 0f;
            if (velocity.SqrMagnitude < 1e-6d)
            {
                return false;
            }

            Vector3d direction = velocity.Normalized;
            px = (float)Vector3d.Dot(direction, screenRight);
            py = -(float)Vector3d.Dot(direction, screenUp);
            return true;
        }

        /// <summary>
        /// Name plate next to the ship, tinted by the kind of orbit. It sits on the
        /// side the velocity arrow does not point at, so the arrow is never hidden.
        /// Hovering expands the same plate with the flight data instead of opening
        /// a separate floating window.
        /// </summary>
        private void DrawShipTag(float x, float y, string text, MarkerPalette pal, RectInt rect,
            Vector3d velocity, Vector3d screenRight, Vector3d screenUp,
            string detail, bool expanded, out RectInt box)
        {
            string[] rows = expanded && !string.IsNullOrEmpty(detail)
                ? detail.Split('\n')
                : Array.Empty<string>();
            int tw = PixelCanvas.TextWidth(text) + 12;
            const int headerH = 12;
            int dataH = rows.Length > 0 ? 3 + (rows.Length * PlateRowHeight) : 0;
            for (int i = 0; i < rows.Length; i++)
            {
                tw = Mathf.Max(tw, PixelCanvas.TextWidth(rows[i]) + 14);
            }

            int th = headerH + dataH;
            float px, py;
            TryProjectDirection(velocity, screenRight, screenUp, out px, out py);

            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);

            // The plate sits on the side the velocity arrow does not point at, so
            // the arrow is never hidden. Collision avoidance may move it further.
            bool preferRight = px <= 0f;
            bool preferDown = py < 0f;
            TryPlacePlate(rect, xi, yi, tw, th, preferRight, preferDown,
                14, preferDown ? 4 : 16, out int tx, out int ty);
            // Keep the header on the side nearest the ship, even after a collision
            // shift moved the plate to the other side.
            bool above = (ty + (th / 2)) < yi;
            int headerOffset = above ? dataH : 0;
            int headerY = ty + headerOffset;

            // Leader from the marker to the plate, so the tag is unambiguously
            // attached to this craft even when several markers sit close together.
            bool plateRight = (tx + (tw / 2)) > xi;
            int edgeX = plateRight ? tx - 4 : tx + tw + 3;
            int edgeY = headerY + (headerH / 2);
            float dx = edgeX - xi;
            float dy = edgeY - yi;
            float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
            if (distance > 12f)
            {
                int sx = xi + Mathf.RoundToInt(dx / distance * 8f);
                int sy = yi + Mathf.RoundToInt(dy / distance * 8f);
                DrawHaloLine(sx, sy, edgeX, edgeY, pal.Main, pal.Dark);
            }

            DrawTagBox(tx, ty, tw, th, pal);
            DrawTagNotch(tx, ty, tw, th, pal, plateRight, edgeY);
            canvas.Text(tx + 7, headerY + 2, text, pal.Light);

            if (rows.Length > 0)
            {
                canvas.HLine(tx + 4, above ? headerY - 1 : headerY + headerH, tw - 7,
                    PixelCanvas.Mix(pal.Main, pal.Dark, 0.30f));
                int rowY = above ? ty + 3 : headerY + headerH + 3;
                for (int i = 0; i < rows.Length; i++)
                {
                    canvas.Text(tx + 7, rowY, rows[i], pal.Light);
                    rowY += PlateRowHeight;
                }
            }

            box = new RectInt(tx, ty, tw, th);
        }

        /// <summary>Ship: glowing ringed diamond with a core and a velocity arrow, tinted by the kind of orbit.</summary>
        private void DrawShip(float x, float y, Vector3d velocity, Vector3d screenRight, Vector3d screenUp, MarkerPalette pal)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);

            // Soft circular halo so the marker separates from bright terrain
            // without darkening a visible square around it.
            DrawGlowDisc(xi, yi, 12f, TabletTheme.Bg, 0.55f);
            DrawCircleBlend(xi, yi, 10f, pal.Main, 0.20f);

            // Velocity arrow first, so the body covers its root. The projected length
            // foreshortens when the craft flies along the view axis.
            if (TryProjectDirection(velocity, screenRight, screenUp, out float px, out float py))
            {
                float mag = Mathf.Sqrt((px * px) + (py * py));
                if (mag > 0.12f)
                {
                    float dx = px / mag;
                    float dy = py / mag;
                    float tip = 9f + (15f * mag);
                    int sx = xi + Mathf.RoundToInt(dx * 8f);
                    int sy = yi + Mathf.RoundToInt(dy * 8f);
                    int ex = xi + Mathf.RoundToInt(dx * tip);
                    int ey = yi + Mathf.RoundToInt(dy * tip);
                    DrawHaloLine(sx, sy, ex, ey, pal.Main, pal.Dark);

                    float angle = Mathf.Atan2(dy, dx);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float a = angle + (side * 2.5f);
                        int hx = ex + Mathf.RoundToInt(Mathf.Cos(a) * 5f);
                        int hy = ey + Mathf.RoundToInt(Mathf.Sin(a) * 5f);
                        DrawHaloLine(ex, ey, hx, hy, pal.Main, pal.Dark);
                    }

                    canvas.Px(ex, ey, pal.Light);
                }
            }

            FillDiamond(xi, yi, 7, pal.Dark);
            canvas.Line(xi, yi - 6, xi + 6, yi, pal.Main);
            canvas.Line(xi + 6, yi, xi, yi + 6, pal.Main);
            canvas.Line(xi, yi + 6, xi - 6, yi, pal.Main);
            canvas.Line(xi - 6, yi, xi, yi - 6, pal.Main);
            FillDiamond(xi, yi, 3, pal.Main);
            FillDiamond(xi, yi, 1, pal.Light);
        }

        private void DrawPlanetDisk(NavMapModel model, NavBodyMarker body, Vector2 center, float radius,
            Vector3d right, Vector3d screenUp, Vector3d viewDir, RectInt rect, Color32 edge)
        {
            OrbitingBody planet = body.Body;
            if (planet == null || radius < 1f)
            {
                DrawDisc(center.x, center.y, Mathf.Max(2f, radius), edge);
                return;
            }

            if (planet.Parent == null)
            {
                DrawStarDisk(center, radius);
                return;
            }

            Vector3d sun = model.SunDirection;

            // A soft atmosphere ring first: the globe then sits inside it. Drawing
            // the halo as two hard circles (the old look) read as a sticker outline.
            if (radius < Mathf.Max(rect.width, rect.height) * 0.9f)
            {
                DrawAtmosphere(center, radius, right, screenUp, sun, rect);
            }

            NavBodyTexture texture = GetBodyTexture(planet);
            if (texture == null)
            {
                if (planet.Terrain != null)
                {
                    // The first terrain map is built on a worker. A shaded sphere
                    // reads as "planet, still resolving"; a flat disc reads as a bug.
                    DrawLoadingGlobe(center, radius, right, screenUp, viewDir, sun, rect);
                }
                else
                {
                    DrawDisc(center.x, center.y, radius, edge);
                }

                return;
            }

            QuaternionD orientation = planet.GetVisualOrientation(model.TimeSeconds);

            // The globe is always rasterised at full screen resolution. The old
            // interactive pass drew 4×4 blocks while the finger was down, which read
            // as a pixelated planet in the middle of every drag. A frame that does
            // not move the camera reuses the cached disk, so the clean image is paid
            // once per view change, not per map tick.
            if (!TryBlitPlanetDisk(planet, texture, center, radius, right, screenUp, viewDir,
                    sun, orientation, rect))
            {
                RasterisePlanetDisk(planet, texture, center, radius, right, screenUp, viewDir,
                    sun, orientation, rect);
            }
        }

        /// <summary>Soft, sun-weighted airglow just outside the limb.</summary>
        private void DrawAtmosphere(Vector2 center, float radius, Vector3d right, Vector3d screenUp,
            Vector3d sun, RectInt rect)
        {
            float inner = radius - 1.5f;
            float outer = radius + Mathf.Clamp(radius * 0.05f, 3f, 12f);
            if (outer <= inner)
            {
                return;
            }

            Vector3d sunN = sun.Normalized;
            float sunRight = (float)Vector3d.Dot(sunN, right);
            float sunUp = (float)Vector3d.Dot(sunN, screenUp);
            var glow = new Color32(74, 166, 214, 255);
            var limb = new Color32(146, 218, 246, 255);

            int x0 = Mathf.Max(rect.x, Mathf.FloorToInt(center.x - outer));
            int x1 = Mathf.Min(rect.x + rect.width - 1, Mathf.CeilToInt(center.x + outer));
            int y0 = Mathf.Max(rect.y, Mathf.FloorToInt(center.y - outer));
            int y1 = Mathf.Min(rect.y + rect.height - 1, Mathf.CeilToInt(center.y + outer));
            float span = outer - inner;

            for (int y = y0; y <= y1; y++)
            {
                float dy = center.y - y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - center.x;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (d < inner || d > outer)
                    {
                        continue;
                    }

                    float nx = d > 0.001f ? dx / d : 0f;
                    float ny = d > 0.001f ? dy / d : 0f;
                    float lit = 0.28f + (0.72f * Mathf.Clamp01((nx * sunRight) + (ny * sunUp)));
                    float t = Mathf.Clamp01((d - inner) / span);
                    float falloff = (1f - t) * (1f - t);

                    // A bright thin limb plus a wide, faint glow.
                    canvas.PxBlend(x, y, glow, 0.58f * falloff * lit);
                    if (t < 0.28f)
                    {
                        canvas.PxBlend(x, y, limb, 0.40f * (1f - (t / 0.28f)) * lit);
                    }
                }
            }
        }

        /// <summary>Placeholder sphere shown for the few frames before the first map lands.</summary>
        private void DrawLoadingGlobe(Vector2 center, float radius, Vector3d right, Vector3d screenUp,
            Vector3d viewDir, Vector3d sun, RectInt rect)
        {
            Vector3d sunN = sun.Normalized;
            float invRadius = 1f / Mathf.Max(1f, radius);
            int x0 = Mathf.Max(rect.x, Mathf.CeilToInt(center.x - radius));
            int x1 = Mathf.Min(rect.x + rect.width - 1, Mathf.FloorToInt(center.x + radius));
            int y0 = Mathf.Max(rect.y, Mathf.CeilToInt(center.y - radius));
            int y1 = Mathf.Min(rect.y + rect.height - 1, Mathf.FloorToInt(center.y + radius));
            var deep = new Color32(18, 48, 68, 255);

            for (int y = y0; y <= y1; y++)
            {
                float ny = (center.y - y) * invRadius;
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x - center.x) * invRadius;
                    float rr = (nx * nx) + (ny * ny);
                    if (rr > 1f)
                    {
                        continue;
                    }

                    float nz = Mathf.Sqrt(1f - rr);
                    Vector3d normal = (right * nx) + (screenUp * ny) + (viewDir * nz);
                    float light = (float)Vector3d.Dot(normal, sunN);
                    float wrap = SmoothStep(-0.14f, 0.30f, light);
                    float facing = Mathf.Max(0f, (float)Vector3d.Dot(normal, viewDir));
                    float shade = 0.20f + (0.11f * facing) + (0.72f * wrap);
                    shade *= 0.94f + (0.06f * facing);
                    shade = Mathf.Clamp(shade, 0.20f, 1f);
                    var color = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(deep.r * shade), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(deep.g * shade), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(deep.b * shade), 0, 255),
                        255);

                    float edge = Mathf.Clamp01((1f - Mathf.Sqrt(rr)) * radius * 1.4f);
                    canvas.Px(x, y, edge < 1f ? PixelCanvas.Mix(canvas.Get(x, y), color, edge) : color);
                }
            }
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
            return t * t * (3f - (2f * t));
        }

        /// <summary>
        /// Ray-sphere raster of the planet at full screen resolution. The disk is
        /// stored so panning the map, moving the ship marker and ticking the map do
        /// not repeat this work.
        ///
        /// The inner loop is the hottest code in the tablet, so everything that does
        /// not depend on the pixel is hoisted: the world→body rotation is one 3×3
        /// float matrix instead of a quaternion multiply chain per pixel, the sun and
        /// glint half-vector are reduced to screen-space components, and the facing
        /// term is just nz because viewDir is the third basis vector.
        /// </summary>
        private void RasterisePlanetDisk(OrbitingBody planet, NavBodyTexture texture, Vector2 center,
            float radius, Vector3d right, Vector3d screenUp, Vector3d viewDir, Vector3d sun,
            QuaternionD orientation, RectInt rect)
        {
            int texWidth = texture.Width;
            int texHeight = texture.Height;
            Color32[] pixels = texture.Pixels;
            float invRadius = 1f / Mathf.Max(1f, radius);

            int x0 = Mathf.Max(rect.x, Mathf.CeilToInt(center.x - radius));
            int x1 = Mathf.Min(rect.x + rect.width - 1, Mathf.FloorToInt(center.x + radius));
            int y0 = Mathf.Max(rect.y, Mathf.CeilToInt(center.y - radius));
            int y1 = Mathf.Min(rect.y + rect.height - 1, Mathf.FloorToInt(center.y + radius));
            if (x1 < x0 || y1 < y0)
            {
                return;
            }

            // worldNormal = right·nx + screenUp·ny + viewDir·nz, so the body-fixed
            // direction is a single matrix multiply by the inverse orientation applied
            // to those three basis vectors.
            QuaternionD inverse = orientation.Conjugated;
            Vector3d bodyRight = inverse.Rotate(right);
            Vector3d bodyUp = inverse.Rotate(screenUp);
            Vector3d bodyView = inverse.Rotate(viewDir);
            float m00 = (float)bodyRight.X, m01 = (float)bodyUp.X, m02 = (float)bodyView.X;
            float m10 = (float)bodyRight.Y, m11 = (float)bodyUp.Y, m12 = (float)bodyView.Y;
            float m20 = (float)bodyRight.Z, m21 = (float)bodyUp.Z, m22 = (float)bodyView.Z;

            int canvasWidth = canvas.Width;
            int canvasHeight = canvas.Height;

            Vector3d sunN = sun.Normalized;
            float sunRight = (float)Vector3d.Dot(right, sunN);
            float sunUp = (float)Vector3d.Dot(screenUp, sunN);
            float sunView = (float)Vector3d.Dot(viewDir, sunN);

            // The glint half-vector is constant for the whole frame; the old loop
            // normalised it (with a square root) on every pixel.
            Vector3d half = sunN + viewDir;
            double halfMagnitude = half.Magnitude;
            bool glintVisible = halfMagnitude > 1e-6d;
            float halfRight = 0f;
            float halfUp = 0f;
            float halfView = 0f;
            if (glintVisible)
            {
                half = half * (1d / halfMagnitude);
                halfRight = (float)Vector3d.Dot(right, half);
                halfUp = (float)Vector3d.Dot(screenUp, half);
                halfView = (float)Vector3d.Dot(viewDir, half);
            }

            float texelsPerRadianU = texWidth / (float)(Math.PI * 2d);
            float texelsPerRadianV = texHeight / (float)Math.PI;
            float texelOriginU = (texWidth * 0.5f) - 0.5f;
            float texelOriginV = (texHeight * 0.5f) - 0.5f;

            // The limb blend is a no-op outside the last ~1/1.5r of the radius: skip
            // the read-modify-write for the whole interior of the disk.
            float limbSq = 1f - (1f / (1.5f * Mathf.Max(1f, radius)));
            limbSq *= limbSq;
            float limbScale = radius * 1.5f;

            Action<int> rasterRow = y =>
            {
                float ny = (center.y - y) * invRadius;
                float rrY = ny * ny;
                if (rrY >= 1f)
                {
                    return;
                }

                float sunRow = ny * sunUp;
                float halfRow = ny * halfUp;
                int rowTop = (canvasHeight - 1 - y) * canvasWidth;
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x - center.x) * invRadius;
                    float rr = (nx * nx) + rrY;
                    if (rr > 1f)
                    {
                        continue;
                    }

                    float nz = Mathf.Sqrt(1f - rr);

                    float bx = (m00 * nx) + (m01 * ny) + (m02 * nz);
                    float by = (m10 * nx) + (m11 * ny) + (m12 * nz);
                    float bz = (m20 * nx) + (m21 * ny) + (m22 * nz);

                    float lat = Mathf.Asin(Mathf.Clamp(bz, -1f, 1f));
                    float u = (Mathf.Atan2(by, bx) * texelsPerRadianU) + texelOriginU;
                    float v = (lat * texelsPerRadianV) + texelOriginV;

                    // Bilinear keeps the high-res map smooth instead of aliasing
                    // into speckle; the raster is cached, so the taps are paid once
                    // per view change, not once per frame.
                    Color32 c = SampleBilinear(pixels, texWidth, texHeight, u, v);

                    // Alpha is the water mask, so the mask comes free with the colour.
                    float sea = c.a * (1f / 255f);

                    // Wrapped terminator: the night side stays readable but clearly
                    // unlit, and the day side gets a warmer, fuller light. facing is
                    // nz: viewDir is the third basis vector of the normal.
                    float light = (nx * sunRight) + sunRow + (nz * sunView);
                    float wrap = SmoothStep(-0.14f, 0.30f, light);
                    float shade = 0.20f + (0.11f * nz) + (0.72f * wrap);
                    shade *= 0.94f + (0.06f * nz);
                    shade = Mathf.Clamp(shade, 0.15f, 1.02f);

                    // Sun glint on water: a soft highlight makes the globe read as
                    // a lit sphere instead of a flat tinted map.
                    float glint = 0f;
                    if (glintVisible && sea > 0.01f)
                    {
                        float d = (nx * halfRight) + halfRow + (nz * halfView);
                        if (d > 0f)
                        {
                            // d^48 without Math.Pow: three squarings and products.
                            float d2 = d * d;
                            float d4 = d2 * d2;
                            float d8 = d4 * d4;
                            float d16 = d8 * d8;
                            float d32 = d16 * d16;
                            glint = d32 * d16 * sea * (0.10f + (0.70f * wrap));
                        }
                    }

                    int index = rowTop + x;
                    Color32 shaded = new Color32(
                        ShadeByte((c.r * shade) + (glint * 60f)),
                        ShadeByte((c.g * shade) + (glint * 82f)),
                        ShadeByte((c.b * shade) + (glint * 98f)),
                        255);

                    // Anti-aliased limb: blend the last pixel ring with what is
                    // behind the globe instead of leaving a hard stair edge. Only
                    // the ring is touched; inside it the blend is a no-op.
                    if (rr > limbSq)
                    {
                        float edge = (1f - Mathf.Sqrt(rr)) * limbScale;
                        if (edge < 1f)
                        {
                            shaded = PixelCanvas.Mix(canvas.Pixels[index], shaded, edge);
                        }
                    }

                    canvas.Pixels[index] = shaded;
                }
            };

            // Rows write disjoint canvas pixels, so no locking is needed. A small
            // disk (the preview monitor) stays serial: scheduling would cost more
            // than the work.
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int rows = y1 - y0 + 1;
            if (rows >= 24 && x1 - x0 >= 24)
            {
                System.Threading.Tasks.Parallel.For(0, rows, DiskRasterOptions, i => rasterRow(y0 + i));
            }
            else
            {
                for (int y = y0; y <= y1; y++)
                {
                    rasterRow(y);
                }
            }

            StorePlanetDisk(planet, texture, center, radius, right, screenUp, viewDir, sun,
                orientation, rect, x0, y0, x1, y1);
            LastRasterMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started)
                * 1000d / System.Diagnostics.Stopwatch.Frequency);
        }

        /// <summary>Bilinear texel fetch with longitude wrap and latitude clamp.</summary>
        private static Color32 SampleBilinear(Color32[] pixels, int width, int height, double u, double v)
        {
            double wrapped = u - (Math.Floor(u / width) * width);
            int x0 = (int)Math.Floor(wrapped);
            float fx = (float)(wrapped - x0);
            if (x0 < 0) { x0 = 0; fx = 0f; }
            if (x0 >= width) { x0 = width - 1; fx = 0f; }
            int x1 = x0 + 1;
            if (x1 >= width) { x1 = 0; }

            int y0 = (int)Math.Floor(v);
            float fy = (float)(v - y0);
            if (y0 < 0) { y0 = 0; fy = 0f; }
            if (y0 >= height) { y0 = height - 1; fy = 0f; }
            int y1 = y0 + 1;
            if (y1 >= height) { y1 = height - 1; }

            Color32 c00 = pixels[(y0 * width) + x0];
            Color32 c10 = pixels[(y0 * width) + x1];
            Color32 c01 = pixels[(y1 * width) + x0];
            Color32 c11 = pixels[(y1 * width) + x1];
            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx * fy;
            return new Color32(
                BlendChannel(c00.r, c10.r, c01.r, c11.r, w00, w10, w01, w11),
                BlendChannel(c00.g, c10.g, c01.g, c11.g, w00, w10, w01, w11),
                BlendChannel(c00.b, c10.b, c01.b, c11.b, w00, w10, w01, w11),
                BlendChannel(c00.a, c10.a, c01.a, c11.a, w00, w10, w01, w11));
        }

        /// <summary>Colour channel to a byte, rounding and clamping in one step.</summary>
        private static byte ShadeByte(float value)
        {
            if (!(value > 0f))
            {
                return 0; // also catches NaN
            }

            return value >= 255f ? (byte)255 : (byte)(value + 0.5f);
        }

        private static byte BlendChannel(byte c00, byte c10, byte c01, byte c11,
            float w00, float w10, float w01, float w11)
        {
            int value = (int)((c00 * w00) + (c10 * w10) + (c01 * w01) + (c11 * w11) + 0.5f);
            if (value < 0) { return 0; }
            return value > 255 ? (byte)255 : (byte)value;
        }

        private bool TryBlitPlanetDisk(OrbitingBody planet, NavBodyTexture texture, Vector2 center,
            float radius, Vector3d right, Vector3d up, Vector3d view, Vector3d sun,
            QuaternionD orientation, RectInt rect)
        {
            if (!diskCacheValid || diskCache == null)
            {
                return false;
            }

            if (!ReferenceEquals(diskCacheBody, planet) || !ReferenceEquals(diskCacheTexture, texture)
                || diskCacheMapX != rect.x || diskCacheMapY != rect.y
                || diskCacheMapWidth != rect.width || diskCacheMapHeight != rect.height)
            {
                return false;
            }

            if (Mathf.Abs(diskCacheCenterX - center.x) > 0.5f
                || Mathf.Abs(diskCacheCenterY - center.y) > 0.5f
                || Mathf.Abs(diskCacheRadius - radius) > 0.5f)
            {
                return false;
            }

            if (Vector3d.Dot(diskCacheRight, right) < 0.999995d
                || Vector3d.Dot(diskCacheUp, up) < 0.999995d
                || Vector3d.Dot(diskCacheView, view) < 0.999995d)
            {
                return false;
            }

            // The sun direction is not normalised: compare with the product of
            // the magnitudes, otherwise a valid cache misses on every draw.
            double sunDot = Vector3d.Dot(diskCacheSun, sun);
            double sunNorm = diskCacheSun.Magnitude * sun.Magnitude;
            if (!(sunNorm > 0d) || sunDot < 0.99999d * sunNorm)
            {
                return false;
            }

            double quaternionDot = Math.Abs(
                (diskCacheOrientation.X * orientation.X)
                + (diskCacheOrientation.Y * orientation.Y)
                + (diskCacheOrientation.Z * orientation.Z)
                + (diskCacheOrientation.W * orientation.W));
            if (quaternionDot < 0.99999d)
            {
                return false;
            }

            int x0 = diskCacheX;
            int y0 = diskCacheY;
            int width = diskCacheWidth;
            int height = diskCacheHeight;
            float invRadius = 1f / Mathf.Max(1f, diskCacheRadius);
            int centerX = Mathf.RoundToInt(diskCacheCenterX);
            int centerY = Mathf.RoundToInt(diskCacheCenterY);

            // Copy only the pixels inside the disk: the cached rect's corners hold
            // whatever was on the map before (orbit lines, background).
            for (int yy = 0; yy < height; yy++)
            {
                int y = y0 + yy;
                float ny = (diskCacheCenterY - y) * invRadius;
                float inside = 1f - (ny * ny);
                if (inside <= 0f)
                {
                    continue;
                }

                int half = Mathf.FloorToInt(diskCacheRadius * Mathf.Sqrt(inside));
                int left = Mathf.Max(x0, centerX - half);
                int rightEdge = Mathf.Min(x0 + width - 1, centerX + half);
                int count = rightEdge - left + 1;
                if (count <= 0)
                {
                    continue;
                }

                Array.Copy(
                    diskCache, (yy * width) + (left - x0),
                    canvas.Pixels, ((canvas.Height - 1 - y) * canvas.Width) + left,
                    count);
            }

            return true;
        }

        private void StorePlanetDisk(OrbitingBody planet, NavBodyTexture texture, Vector2 center,
            float radius, Vector3d right, Vector3d up, Vector3d view, Vector3d sun,
            QuaternionD orientation, RectInt rect, int x0, int y0, int x1, int y1)
        {
            int width = x1 - x0 + 1;
            int height = y1 - y0 + 1;
            if (diskCache == null || diskCacheWidth != width || diskCacheHeight != height
                || diskCache.Length != width * height)
            {
                diskCache = new Color32[width * height];
            }

            for (int y = y0; y <= y1; y++)
            {
                Array.Copy(
                    canvas.Pixels, ((canvas.Height - 1 - y) * canvas.Width) + x0,
                    diskCache, (y - y0) * width,
                    width);
            }

            diskCacheBody = planet;
            diskCacheTexture = texture;
            diskCacheCenterX = center.x;
            diskCacheCenterY = center.y;
            diskCacheRadius = radius;
            diskCacheRight = right;
            diskCacheUp = up;
            diskCacheView = view;
            diskCacheSun = sun;
            diskCacheOrientation = orientation;
            diskCacheX = x0;
            diskCacheY = y0;
            diskCacheWidth = width;
            diskCacheHeight = height;
            diskCacheMapX = rect.x;
            diskCacheMapY = rect.y;
            diskCacheMapWidth = rect.width;
            diskCacheMapHeight = rect.height;
            diskCacheValid = true;
        }

        private static QuaternionD RotationFromTo(Vector3d from, Vector3d to)
        {
            from = from.Normalized;
            to = to.Normalized;
            double dot = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(from, to)));
            if (dot > 0.999999d)
            {
                return QuaternionD.Identity;
            }

            if (dot < -0.999999d)
            {
                Vector3d axis = Vector3d.Cross(from, new Vector3d(0d, 0d, 1d));
                if (axis.SqrMagnitude < 1e-12d)
                {
                    axis = Vector3d.Cross(from, new Vector3d(1d, 0d, 0d));
                }

                return QuaternionD.FromAxisAngle(axis.Normalized, Math.PI);
            }

            Vector3d rotationAxis = Vector3d.Cross(from, to).Normalized;
            return QuaternionD.FromAxisAngle(rotationAxis, Math.Acos(dot));
        }

        private void DrawStarDisk(Vector2 center, float radius)
        {
            int cx = Mathf.RoundToInt(center.x);
            int cy = Mathf.RoundToInt(center.y);
            int r = Mathf.Min(300, Mathf.CeilToInt(radius));
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    float rr = (x * x) + (y * y);
                    if (rr > r * r)
                    {
                        continue;
                    }

                    float f = 1f - (rr / Mathf.Max(1f, r * r));
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(180f + (75f * f)), 0, 255);
                    canvas.Px(cx + x, cy + y, new Color32(v, (byte)Mathf.Clamp(v - 20, 0, 255), (byte)Mathf.Clamp(v - 70, 0, 255), 255));
                }
            }

            DrawCircle(center.x, center.y, radius + 2f, TabletTheme.Warn);
        }

        private NavBodyTexture GetBodyTexture(OrbitingBody planet)
        {
            return NavBodyTextureCache.Get(planet);
        }

        private RectInt DrawLabel(float x, float y, string text, RectInt rect, Color32 color)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            int width = PixelCanvas.TextWidth(text);
            if (xi + width > rect.x + rect.width - 4)
            {
                xi = rect.x + rect.width - width - 4;
            }

            if (xi < rect.x + 4)
            {
                xi = rect.x + 4;
            }

            if (yi < rect.y + 2)
            {
                yi = rect.y + 2;
            }

            if (yi > rect.y + rect.height - 9)
            {
                yi = rect.y + rect.height - 9;
            }

            // A dark 1 px outline instead of a filled box: the name sits on the
            // globe like a map callout, not like a label sticker.
            canvas.TextHalo(xi, yi, text, color, TabletTheme.Bg);
            return new RectInt(xi - 3, yi - 2, width + 6, 11);
        }

        // ─── HUD / preview ────────────────────────────────────────────────────

        private void DrawHud(NavMapModel model, NavState state, NavMapInfo info, RectInt rect)
        {
            int x = rect.x + 10;
            int y = rect.y + rect.height - 32;

            string frame = model.FrameName;
            canvas.Text(x, y, "СК: " + frame, TabletTheme.Main);
            canvas.Text(x, y + 10, TabletFormat.Clip(model.Note, rect.width - 20), NoteColor(model.Note));
            if (model.HasSurfaceFrame)
            {
                canvas.Text(x, y - 10,
                    "H " + TabletFormat.Dist(model.ShipHeightAboveTerrain) + " AGL   V "
                    + TabletFormat.Signed(model.ShipVerticalSpeedMps, 1) + " М/С", TabletTheme.Dim);
            }

            int legendX = x;
            // No full-orbit row: rails are not drawn. The plan row needs one more
            // line; lift the legend while it is visible so it never collides with
            // the status text at y.
            int legendY = y - 33 - (model.PlannedWindowSeconds > 0d || model.BurnMarkers.Count > 0 ? 13 : 0);
            canvas.Line(legendX, legendY + 3, legendX + 16, legendY + 3,
                model.HasSurfaceHistory ? SurfaceHistoryColor : TabletTheme.Main);
            string history;
            if (model.HistorySeconds <= 0d)
            {
                history = "ИСТОРИЯ ВЫКЛ";
            }
            else
            {
                NavLine historyLine = null;
                for (int i = 0; i < model.Lines.Count; i++)
                {
                    if (model.Lines[i].Kind == NavLineKind.ShipHistory)
                    {
                        historyLine = model.Lines[i];
                        break;
                    }
                }

                if (historyLine == null)
                {
                    for (int i = 0; i < model.Lines.Count; i++)
                    {
                        if (model.Lines[i].Kind == NavLineKind.BodyHistory)
                        {
                            historyLine = model.Lines[i];
                            break;
                        }
                    }
                }

                if (historyLine == null || historyLine.Points.Count < 2
                    || !double.IsFinite(historyLine.StartTimeS))
                {
                    history = "ИСТОРИЯ НЕТ";
                }
                else
                {
                    Vector3d origin = historyLine.Points[0];
                    double extent = 0d;
                    for (int i = 1; i < historyLine.Points.Count; i++)
                    {
                        double distance = (historyLine.Points[i] - origin).Magnitude;
                        if (distance > extent)
                        {
                            extent = distance;
                        }
                    }

                    history = extent > 1000d
                        ? "ИСТОРИЯ " + TabletFormat.Time(historyLine.EndTimeS - historyLine.StartTimeS)
                        : "ИСТОРИЯ БЕЗ ДВИЖЕНИЯ";
                }
            }

            if (model.HasSurfaceHistory && history.StartsWith("ИСТОРИЯ", StringComparison.Ordinal))
            {
                history = "ИСТОРИЯ СТОЯНКИ" + history.Substring("ИСТОРИЯ".Length);
            }

            canvas.Text(legendX + 20, legendY, history, TabletTheme.Dim);

            // The craft's own path is solid only while it coasts. A standing craft
            // has a site track (planet rotation), which must not read as an orbit:
            // same dashed sample and label as the standing note.
            string ballistic;
            if (model.HasSurfaceForecast)
            {
                DashedHudLine(legendX, legendY + 15, 16, SurfaceForecastColor);
                ballistic = "ПРОГНОЗ СТОЯНКИ "
                    + TabletFormat.Time(model.ForecastWindowSeconds);
            }
            else
            {
                canvas.Line(legendX, legendY + 15, legendX + 16, legendY + 15, ForecastColor);
                if (model.ForecastValid)
                {
                    double span = model.BallisticWindowSeconds > 0d
                        ? model.BallisticWindowSeconds
                        : model.ForecastWindowSeconds;
                    ballistic = "БАЛЛИСТИКА " + TabletFormat.Time(span);
                }
                else
                {
                    NavLine bodyForecast = null;
                    for (int i = 0; i < model.Lines.Count; i++)
                    {
                        if (model.Lines[i].Kind == NavLineKind.BodyForecast)
                        {
                            bodyForecast = model.Lines[i];
                            break;
                        }
                    }

                    ballistic = bodyForecast != null
                        ? "ПРОГНОЗ " + TabletFormat.Time(bodyForecast.EndTimeS - bodyForecast.StartTimeS)
                        : "ПРОГНОЗ ВЫКЛ";
                }
            }

            canvas.Text(legendX + 20, legendY + 12, ballistic,
                model.ForecastValid ? TabletTheme.Dim : ForecastColor);

            if (model.PlannedWindowSeconds > 0d)
            {
                DashedHudLine(legendX, legendY + 28, 16, ManeuverForecastColor);
                string plan = "МАНЁВР " + Math.Max(1, model.AppliedManeuverCount).ToString();
                canvas.Text(legendX + 20, legendY + 25, plan, TabletTheme.Dim);
            }
            else if (model.BurnMarkers.Count > 0)
            {
                // Time chosen, ΔV not yet: the ignition point is on the coast line,
                // there is deliberately no second orbit.
                DashedHudLine(legendX, legendY + 28, 16, ManeuverForecastColor);
                string plan = "МАНЁВР " + model.BurnMarkers.Count.ToString() + " · ΔV 0";
                canvas.Text(legendX + 20, legendY + 25, plan, TabletTheme.Dim);
            }

            // Orientation and controls. The screen is a turntable: north is always
            // up, so the corner cue stays in place while the globe turns under it.
            int hintX = rect.x + 10;
            int hintY = rect.y + 8;
            canvas.Text(hintX, hintY, "ЛКМ/ПКМ — ВРАЩ", TabletTheme.Dim);
            canvas.Text(hintX, hintY + 10, "КОЛЕСО — МАСШТАБ", TabletTheme.Dim);
            canvas.Text(hintX, hintY + 20, "2 КЛИКА — СБРОС", TabletTheme.Dim);

            int northX = rect.x + rect.width - 16;
            int northCenter = northX - 18;
            canvas.TextRight(northX, hintY, "ВЕРХ", TabletTheme.Main);
            canvas.VLine(northCenter, hintY + 10, 5, TabletTheme.Main);
            canvas.Line(northCenter, hintY + 10, northCenter - 3, hintY + 14, TabletTheme.Main);
            canvas.Line(northCenter, hintY + 10, northCenter + 3, hintY + 14, TabletTheme.Main);
            int tilt = Mathf.RoundToInt(ElevationRad * Mathf.Rad2Deg);
            canvas.TextRight(northX, hintY + 20, "УГОЛ " + tilt + "°", TabletTheme.Dim);

            DrawScaleBar(rect, lastScale);
        }

        private void DrawScaleBar(RectInt rect, double scale)
        {
            if (!(scale > 0d) || !double.IsFinite(scale))
            {
                return;
            }

            double target = rect.width * 0.18d / scale;
            double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(Math.Max(1d, target))));
            double distance = magnitude;
            double[] steps = { 1d, 2d, 5d };
            for (int i = 0; i < steps.Length; i++)
            {
                double candidate = steps[i] * magnitude;
                if (candidate <= target)
                {
                    distance = candidate;
                }
            }

            int length = Mathf.Max(8, Mathf.RoundToInt((float)(distance * scale)));
            int x1 = rect.x + rect.width - 12;
            int x0 = x1 - length;
            int y = rect.y + rect.height - 18;
            canvas.HLine(x0, y, length + 1, TabletTheme.Dim);
            canvas.VLine(x0, y - 3, 7, TabletTheme.Dim);
            canvas.VLine(x1, y - 3, 7, TabletTheme.Dim);
            canvas.TextCenter((x0 + x1) / 2, y - 12, TabletFormat.Dist(distance), TabletTheme.Dim);
        }

        private void DashedHudLine(int x, int y, int length, Color32 color)
        {
            for (int i = 0; i < length; i++)
            {
                if ((i / 3) % 2 == 0)
                {
                    canvas.Px(x + i, y, color);
                }
            }
        }

        private static Color32 NoteColor(string note)
        {
            if (string.IsNullOrEmpty(note))
            {
                return TabletTheme.Dim;
            }

            if (note.IndexOf("УДАР", StringComparison.OrdinalIgnoreCase) >= 0
                || note.IndexOf("ОСТАНОВ", StringComparison.OrdinalIgnoreCase) >= 0
                || note.IndexOf("ВЫКЛ", StringComparison.OrdinalIgnoreCase) >= 0
                || note.IndexOf("КОНФЛИКТ", StringComparison.OrdinalIgnoreCase) >= 0
                || note.IndexOf("ЗА ОКНОМ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TabletTheme.Warn;
            }

            return TabletTheme.Dim;
        }

        private void DrawPreviewChrome(NavMapModel model, NavState state, NavMapInfo info, RectInt mapRect)
        {
            canvas.Text(22, 14, "НАВИГАЦИЯ", TabletTheme.Main, 2);
            string subtitle = model != null && model.HasSurfaceFrame && model.DominantBody != null
                ? "ПЛАНЕТА · " + (model.DominantBody.Name ?? "?").ToUpperInvariant()
                : "КАРТА ОРБИТ · N-BODY";
            canvas.Text(24, 36, TabletFormat.Clip(subtitle, 300), TabletTheme.Dim);
            canvas.Frame(mapRect.x - 4, mapRect.y - 4, mapRect.width + 8, mapRect.height + 8, TabletTheme.Line);

            int panelX = Width - 286;
            int panelY = 18;
            int panelW = 268;
            int panelH = Height - 36;
            canvas.Fill(panelX, panelY, panelW, panelH, TabletTheme.PanelBg);
            canvas.Frame(panelX, panelY, panelW, panelH, TabletTheme.Line);

            canvas.Text(panelX + 12, panelY + 12, "ΔV ПЛАНА", TabletTheme.Dim);
            canvas.Text(panelX + 12, panelY + 26, TabletFormat.Num(info != null ? info.PlanDeltaVMs : 0d, 0),
                TabletTheme.Bright, 2);
            canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth("М/С", 1), panelY + 34, "М/С", TabletTheme.Dim);

            string maneuvers = info != null && info.ManeuverCount > 0
                ? "МАНЁВРОВ: " + info.ManeuverCount
                : "МАНЁВРОВ НЕТ";
            canvas.Text(panelX + 12, panelY + 56, maneuvers, TabletTheme.Main);

            if (info != null)
            {
                canvas.Text(panelX + 12, panelY + 82, "МАССА", TabletTheme.Dim);
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(TabletFormat.Mass(info.MassKg)),
                    panelY + 82, TabletFormat.Mass(info.MassKg), TabletTheme.Main);
                canvas.Text(panelX + 12, panelY + 94, "СКОРОСТЬ", TabletTheme.Dim);
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(TabletFormat.Speed(info.SpeedMs) + " М/С"),
                    panelY + 94, TabletFormat.Speed(info.SpeedMs) + " М/С", TabletTheme.Main);
                if (info.HasShip)
                {
                    canvas.Text(panelX + 12, panelY + 106, "ВЫСОТА AGL", TabletTheme.Dim);
                    canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(TabletFormat.Dist(info.AltitudeMeters)),
                        panelY + 106, TabletFormat.Dist(info.AltitudeMeters), TabletTheme.Bright);
                    canvas.Text(panelX + 12, panelY + 118, "ВЕРТ.", TabletTheme.Dim);
                    canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(TabletFormat.Signed(info.VerticalSpeedMps, 1) + " М/С"),
                        panelY + 118, TabletFormat.Signed(info.VerticalSpeedMps, 1) + " М/С", TabletTheme.Main);
                }
            }

            if (model != null)
            {
                canvas.Text(panelX + 12, panelY + 132, "СК", TabletTheme.Dim);
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(model.FrameName),
                    panelY + 132, model.FrameName, TabletTheme.Main);
                canvas.Text(panelX + 12, panelY + 144, "ТЕЛ", TabletTheme.Dim);
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(model.Bodies.Count.ToString()),
                    panelY + 144, model.Bodies.Count.ToString(), TabletTheme.Main);
                canvas.Text(panelX + 12, panelY + 156, "ПРОГНОЗ", TabletTheme.Dim);
                string forecastText = model.ForecastValid
                    ? TabletFormat.Time(model.ForecastWindowSeconds)
                    : "ВЫКЛ";
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(forecastText),
                    panelY + 156, forecastText, model.ForecastValid ? TabletTheme.Main : TabletTheme.Warn);
                canvas.Text(panelX + 12, panelY + 168, "ИСТОРИЯ", TabletTheme.Dim);
                string historyText = model.HistorySeconds > 0d
                    ? TabletFormat.Time(model.HistorySeconds)
                    : "ВЫКЛ";
                canvas.Text(panelX + panelW - 12 - PixelCanvas.TextWidth(historyText),
                    panelY + 168, historyText, model.HistorySeconds > 0d ? TabletTheme.Main : TabletTheme.Dim);
            }

            string status = info != null && !string.IsNullOrEmpty(info.Status) ? info.Status : (model != null ? model.Note : "НЕТ ДАННЫХ");
            int statusY = panelY + panelH - 64;
            canvas.HLine(panelX + 12, statusY - 10, panelW - 24, TabletTheme.Line);
            canvas.Text(panelX + 12, statusY, TabletFormat.Clip(status, panelW - 24), NoteColor(status));

            canvas.Text(panelX + 12, panelY + panelH - 22, "E — ОТКРЫТЬ", TabletTheme.Main);
        }

        private void DrawPreviewFooter(NavMapModel model, NavState state, NavMapInfo info, RectInt mapRect)
        {
            int x = mapRect.x + 4;
            int y = mapRect.y + mapRect.height + 6;
            canvas.Text(x, y, TabletTime.Mission(model != null ? model.TimeSeconds : 0d), TabletTheme.Dim);
            string frame = model != null ? model.FrameName : "?";
            canvas.TextRight(mapRect.x + mapRect.width - 4, y, "СК " + frame, TabletTheme.Dim);
        }

        private void DrawDisconnected(NavMapInfo info)
        {
            canvas.Fill(0, 0, Width, Height, TabletTheme.Bg);
            canvas.Text(24, 24, "НАВИГАЦИЯ", TabletTheme.Main, 2);
            canvas.Text(24, 50, "НЕТ СВЯЗИ С СИМУЛЯЦИЕЙ", TabletTheme.Warn);
            if (Preview)
            {
                canvas.Text(24, Height - 28, "E — ОТКРЫТЬ", TabletTheme.Line);
            }
        }

        private void Upload()
        {
            texture.SetPixels32(canvas.Pixels);
            texture.Apply(false);
        }
    }
}
