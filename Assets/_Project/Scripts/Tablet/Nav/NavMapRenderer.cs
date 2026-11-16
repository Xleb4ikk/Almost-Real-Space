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
        /// While the player drags/zooms the map, the globe is rasterised at a coarse
        /// block size (a full-quality ray-sphere pass is a 100 ms main-thread hit).
        /// The station turns this off after the gesture and asks for one clean frame.
        /// </summary>
        public bool Interactive { get; set; }

        private readonly PixelCanvas canvas;
        private readonly Texture2D texture;
        private readonly List<PickEntry> picks = new List<PickEntry>();

        // Rendered planet disk cache: the per-pixel sphere raster is the single most
        // expensive part of the map. Reuse it until the view (or the planet's visible
        // orientation / light) actually changes; only the ship, orbits and HUD are
        // redrawn on the regular map tick.
        private Color32[] diskCache;
        private int diskCacheX;
        private int diskCacheY;
        private int diskCacheWidth;
        private int diskCacheHeight;
        private int diskCacheStep = -1;
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
        private static readonly Color32 ShipColor = TabletTheme.Bright;
        private static readonly Color32 ForecastColor = TabletTheme.Warn;
        private static readonly Color32 ManeuverForecastColor = TabletTheme.Bright;
        private static readonly Color32 SurfaceForecastColor = TabletTheme.Main;
        private static readonly Color32 SurfaceHistoryColor = TabletTheme.Dim;

        private struct PickEntry
        {
            public float X;
            public float Y;
            public float Radius;
            public string Text;
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

        /// <summary>Nearest labelled object to a texture pixel, as rich text for the UI tooltip.</summary>
        public bool TryPick(Vector2 texturePixel, out string text)
        {
            text = null;
            float best = 14f * 14f;
            for (int i = 0; i < picks.Count; i++)
            {
                PickEntry pick = picks[i];
                float dx = pick.X - texturePixel.x;
                float dy = pick.Y - texturePixel.y;
                float d = (dx * dx) + (dy * dy);
                if (d <= best)
                {
                    best = d;
                    text = pick.Text;
                }
            }

            return text != null;
        }

        // ─── Map geometry ─────────────────────────────────────────────────────

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
                    DrawPolyline(line.Points, Project, rect, TabletTheme.Main, false, 2);
                }
                else if (line.Kind == NavLineKind.BodyForecast)
                {
                    DrawPolyline(line.Points, Project, rect, ForecastColor, true, 2);
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
                    DrawPolyline(line.Points, Project, rect,
                        surface ? SurfaceHistoryColor : TabletTheme.Main, false, surface ? 1 : 2);
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
                        DrawPolyline(line.Points, Project, rect, SurfaceForecastColor, true, 1);
                    }
                    else
                    {
                        DrawPolyline(line.Points, Project, rect, ForecastColor, false, 2);
                    }
                }
                else if (line.Kind == NavLineKind.ManeuverForecast)
                {
                    DrawPolyline(line.Points, Project, rect, ManeuverForecastColor, true, 2);
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
                        DrawPolylineNear(line.Points, Project, rect,
                            surface ? SurfaceHistoryColor : TabletTheme.Main, false, surface ? 1 : 2,
                            occluderCenter, occluderRadius, screenRight, screenUp, camDir);
                    }
                    else if (line.Kind == NavLineKind.ShipForecast)
                    {
                        if (model.HasSurfaceForecast)
                        {
                            DrawPolylineNear(line.Points, Project, rect, SurfaceForecastColor, true, 1,
                                occluderCenter, occluderRadius, screenRight, screenUp, camDir);
                        }
                        else
                        {
                            DrawPolylineNear(line.Points, Project, rect, ForecastColor, false, 2,
                                occluderCenter, occluderRadius, screenRight, screenUp, camDir);
                        }
                    }
                    else if (line.Kind == NavLineKind.ManeuverForecast)
                    {
                        DrawPolylineNear(line.Points, Project, rect, ManeuverForecastColor, true, 2,
                            occluderCenter, occluderRadius, screenRight, screenUp, camDir);
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
            // orbit changes, not merely because time passed.
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

                DrawMarker(p.x, p.y, "ΔV" + burn.Index.ToString(), ForecastColor, rect);
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
                float labelY = bodyRadius > 40f ? p.y - bodyRadius - 12f : p.y - 3f;
                DrawLabel(p.x + (bodyRadius > 40f ? 0f : bodyRadius + 5f), labelY, name, rect, color);
                picks.Add(new PickEntry
                {
                    X = p.x,
                    Y = p.y,
                    Radius = Mathf.Max(5f, Mathf.Min(bodyRadius, 20f)),
                    Text = name + "\nR " + TabletFormat.Dist(body.RadiusMeters),
                });
            }

            // Apsides of the current osculating orbit.
            if (model.HasApsides)
            {
                if (!IsMarkerOccluded(model, occluderIndex, model.PeriapsisPosition, screenRight, screenUp, camDir))
                {
                    Vector2 pe = Project(model.PeriapsisPosition);
                    if (IsFinite(pe) && Inside(rect, pe, 12f))
                    {
                        DrawMarker(pe.x, pe.y, "П", TabletTheme.Main, rect);
                    }
                }

                if (model.HasApoapsis
                    && !IsMarkerOccluded(model, occluderIndex, model.ApoapsisPosition, screenRight, screenUp, camDir))
                {
                    Vector2 ap = Project(model.ApoapsisPosition);
                    if (IsFinite(ap) && Inside(rect, ap, 12f))
                    {
                        DrawMarker(ap.x, ap.y, "А", TabletTheme.Dim, rect);
                    }
                }
            }

            // The ship itself: a diamond with a short velocity pointer.
            if (model.HasShip
                && !IsMarkerOccluded(model, occluderIndex, model.FrameRelativeShipPosition, screenRight, screenUp, camDir))
            {
                Vector2 ship = Project(model.FrameRelativeShipPosition);
                if (IsFinite(ship) && Inside(rect, ship, 12f))
                {
                    DrawShip(ship.x, ship.y, model.FrameRelativeShipVelocity, screenRight, screenUp);
                    DrawLabel(ship.x + 9f, ship.y - 4f, "АППАРАТ", rect, TabletTheme.Bright);
                    double speed = model.FrameRelativeShipVelocity.Magnitude;
                    picks.Add(new PickEntry
                    {
                        X = ship.x,
                        Y = ship.y,
                        Radius = 12f,
                        Text = "АППАРАТ\nV " + TabletFormat.Speed(speed) + " М/С",
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
            DrawSurfaceShip(centerX, centerY, model);
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

        private void DrawSurfaceShip(float centerX, float centerY, NavMapModel model)
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

            picks.Add(new PickEntry
            {
                X = x,
                Y = y,
                Radius = 12f,
                Text = "АППАРАТ\nH " + TabletFormat.Dist(model.ShipHeightAboveTerrain)
                    + "\nV " + TabletFormat.Speed(speed) + " М/С",
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

        /// <summary>
        /// Draws only the part of a polyline that lies on the near side of the
        /// occluding globe and inside its projected disk. Used after the globe to
        /// put the near half of a trail on top without showing its far half through
        /// the planet.
        /// </summary>
        private void DrawPolylineNear(List<Vector3d> points, Func<Vector3d, Vector2> project, RectInt rect,
            Color32 color, bool dashed, int width, Vector3d occluderCenter, double occluderRadius,
            Vector3d screenRight, Vector3d screenUp, Vector3d camDir)
        {
            if (points == null || points.Count < 2 || !(occluderRadius > 0d))
            {
                return;
            }

            double radiusSquared = occluderRadius * occluderRadius * 1.02d;
            const int subdivisions = 4;
            float dashPhase = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3d a = points[i - 1];
                Vector3d b = points[i];
                Vector2 previous = project(a);
                for (int s = 1; s <= subdivisions; s++)
                {
                    double t = (double)s / subdivisions;
                    Vector3d point = a + ((b - a) * t);
                    Vector3d q = point - occluderCenter;
                    double dx = Vector3d.Dot(q, screenRight);
                    double dy = Vector3d.Dot(q, screenUp);
                    bool insideDisk = (dx * dx) + (dy * dy) <= radiusSquared;
                    bool nearSide = Vector3d.Dot(q, camDir) > 0d;
                    Vector2 current = project(point);
                    if (insideDisk && nearSide)
                    {
                        DrawClippedLine(previous, current, rect, color, dashed, width, ref dashPhase);
                    }

                    previous = current;
                }
            }
        }

        private void DrawPolyline(List<Vector3d> points, Func<Vector3d, Vector2> project, RectInt rect,
            Color32 color, bool dashed, int width)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            float dashPhase = 0f;
            Vector2 previous = project(points[0]);
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 current = project(points[i]);
                if (!IsFinite(previous) || !IsFinite(current))
                {
                    previous = current;
                    continue;
                }

                DrawClippedLine(previous, current, rect, color, dashed, width, ref dashPhase);
                previous = current;
            }
        }

        private static bool IsFinite(Vector2 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y);
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

        private void DrawMarker(float x, float y, string label, Color32 color, RectInt rect)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            canvas.VLine(xi, yi - 4, 9, color);
            canvas.HLine(xi - 4, yi, 9, color);
            if (Inside(rect, new Vector2(x, y), 10f))
            {
                canvas.Text(xi + 6, yi - 3, label, color);
            }
        }

        private void DrawShip(float x, float y, Vector3d velocity, Vector3d screenRight, Vector3d screenUp)
        {
            int xi = Mathf.RoundToInt(x);
            int yi = Mathf.RoundToInt(y);
            canvas.Line(xi, yi - 6, xi + 5, yi, ShipColor);
            canvas.Line(xi + 5, yi, xi, yi + 6, ShipColor);
            canvas.Line(xi, yi + 6, xi - 5, yi, ShipColor);
            canvas.Line(xi - 5, yi, xi, yi - 6, ShipColor);
            canvas.Px(xi, yi, ShipColor);

            if (velocity.SqrMagnitude < 1e-6d)
            {
                return;
            }

            Vector3d direction = velocity.Normalized;
            double vx = Vector3d.Dot(direction, screenRight);
            double vy = Vector3d.Dot(direction, screenUp);
            float length = 16f;
            float ex = x + (float)(vx * length);
            float ey = y - (float)(vy * length);
            canvas.Line(xi, yi, Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), TabletTheme.Main);
            canvas.Px(Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), ShipColor);
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

            NavBodyTexture texture = GetBodyTexture(planet);
            if (texture == null)
            {
                DrawDisc(center.x, center.y, radius, edge);
                return;
            }

            QuaternionD orientation = planet.GetVisualOrientation(model.TimeSeconds);
            Vector3d sun = model.SunDirection;

            // Two block sizes: a coarse interactive pass while the finger is down,
            // then one clean frame when the gesture ends. The clean pass is full
            // screen resolution (step 1) and is cached, so it is paid once per view
            // change — not on every map tick.
            int step = Interactive ? 4 : 1;
            if (!TryBlitPlanetDisk(planet, texture, center, radius, right, screenUp, viewDir,
                    sun, orientation, rect, step))
            {
                RasterisePlanetDisk(planet, texture, center, radius, right, screenUp, viewDir,
                    sun, orientation, rect, step);
            }

            // A thin atmosphere/limb so the body reads as a planet, not a flat disc.
            // When the globe is larger than the map, the limb is off-screen.
            if (radius < Mathf.Max(rect.width, rect.height) * 0.9f)
            {
                DrawCircle(center.x, center.y, radius + 1.5f, new Color32(70, 150, 190, 255));
                if (radius > 8f)
                {
                    DrawCircle(center.x, center.y, radius + 3f, new Color32(26, 64, 92, 255));
                }
            }
        }

        /// <summary>
        /// Ray-sphere raster of the planet. The disk is stored so panning the map,
        /// moving the ship marker and ticking the map do not repeat this work.
        /// </summary>
        private void RasterisePlanetDisk(OrbitingBody planet, NavBodyTexture texture, Vector2 center,
            float radius, Vector3d right, Vector3d screenUp, Vector3d viewDir, Vector3d sun,
            QuaternionD orientation, RectInt rect, int step)
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

            QuaternionD inverse = orientation.Conjugated;
            int canvasWidth = canvas.Width;
            int canvasHeight = canvas.Height;

            Action<int> rasterRow = y =>
            {
                int cellHeight = Mathf.Min(step, y1 - y + 1);
                float ny = (center.y - y) * invRadius;
                int rowTop = (canvasHeight - 1 - y) * canvasWidth;
                for (int x = x0; x <= x1; x += step)
                {
                    float nx = (x - center.x) * invRadius;
                    float rr = (nx * nx) + (ny * ny);
                    if (rr > 1f)
                    {
                        continue;
                    }

                    float nz = Mathf.Sqrt(1f - rr);
                    Vector3d worldNormal = (right * nx) + (screenUp * ny) + (viewDir * nz);
                    Vector3d bodyNormal = inverse.Rotate(worldNormal);

                    double lat = Math.Asin(Math.Max(-1d, Math.Min(1d, bodyNormal.Z)));
                    double lon = Math.Atan2(bodyNormal.Y, bodyNormal.X);

                    Color32 c;
                    if (step <= 1)
                    {
                        // Full-resolution clean pass: bilinear keeps the high-res
                        // map smooth instead of aliasing into speckle. The pass is
                        // cached, so the extra taps are paid once per view change.
                        double u = (((lon + Math.PI) / (Math.PI * 2d)) * texWidth) - 0.5d;
                        double v = (((lat + (Math.PI * 0.5d)) / Math.PI) * texHeight) - 0.5d;
                        c = SampleBilinear(pixels, texWidth, texHeight, u, v);
                    }
                    else
                    {
                        int tx = (int)(((lon + Math.PI) / (Math.PI * 2d)) * texWidth);
                        int ty = (int)(((lat + (Math.PI * 0.5d)) / Math.PI) * texHeight);
                        if (tx < 0) { tx = 0; } else if (tx >= texWidth) { tx = texWidth - 1; }
                        if (ty < 0) { ty = 0; } else if (ty >= texHeight) { ty = texHeight - 1; }
                        c = pixels[(ty * texWidth) + tx];
                    }

                    double light = Vector3d.Dot(worldNormal, sun);
                    double facing = Math.Max(0d, Vector3d.Dot(worldNormal, viewDir));
                    float shade = 0.28f + (0.50f * (float)Math.Max(0d, light)) + (0.22f * (float)facing);
                    shade = Mathf.Clamp(shade, 0.20f, 1f);

                    var shaded = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * shade), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * shade), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * shade), 0, 255),
                        255);

                    int cellWidth = Mathf.Min(step, x1 - x + 1);
                    for (int yy = 0; yy < cellHeight; yy++)
                    {
                        int row = rowTop - (yy * canvasWidth);
                        for (int xx = 0; xx < cellWidth; xx++)
                        {
                            canvas.Pixels[row + x + xx] = shaded;
                        }
                    }
                }
            };

            if (step <= 1)
            {
                // The full-res clean pass is the one expensive frame; split its
                // rows across workers so it costs a few milliseconds, not ~17.
                // Rows write disjoint canvas pixels, so no locking is needed.
                int rows = ((y1 - y0) / step) + 1;
                var options = new System.Threading.Tasks.ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1)),
                };
                System.Threading.Tasks.Parallel.For(0, rows, options, i => rasterRow(y0 + (i * step)));
            }
            else
            {
                for (int y = y0; y <= y1; y += step)
                {
                    rasterRow(y);
                }
            }

            StorePlanetDisk(planet, texture, center, radius, right, screenUp, viewDir, sun,
                orientation, rect, step, x0, y0, x1, y1);
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
                255);
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
            QuaternionD orientation, RectInt rect, int step)
        {
            if (!diskCacheValid || diskCache == null)
            {
                return false;
            }

            if (!ReferenceEquals(diskCacheBody, planet) || !ReferenceEquals(diskCacheTexture, texture)
                || diskCacheStep != step
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
            QuaternionD orientation, RectInt rect, int step, int x0, int y0, int x1, int y1)
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
            diskCacheStep = step;
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

        private void DrawLabel(float x, float y, string text, RectInt rect, Color32 color)
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

            canvas.Fill(xi - 2, yi - 1, width + 4, 9, TabletTheme.Bg);
            canvas.Text(xi, yi, text, color);
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
            int legendY = y - 33 - (model.PlannedWindowSeconds > 0d ? 13 : 0);
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
