using UnityEngine;
using UnityEngine.UIElements;

namespace Galilego.Tablet
{
    /// <summary>
    /// Rate-slider control: rounded track, fill from the centre to the thumb, centre tick, round glowing thumb.
    /// All behaviour is in <see cref="RateSlider"/>; this element only feeds it pointer positions and draws it.
    /// The owner calls <see cref="Model"/>.Tick(dt) every frame and <see cref="Sync"/> to repaint.
    /// </summary>
    [UxmlElement]
    public partial class RateSliderElement : VisualElement
    {
        private static readonly Color TrackColor = new Color(0.30f, 0.88f, 0.49f, 0.12f);
        private static readonly Color FillColor = new Color(0.30f, 0.88f, 0.49f, 0.85f);
        private static readonly Color TickColor = new Color(0.30f, 0.88f, 0.49f, 0.55f);
        private static readonly Color ThumbColor = new Color(0.30f, 0.88f, 0.49f, 1f);
        private static readonly Color ThumbActive = new Color(0.89f, 1f, 0.94f, 1f);

        /// <summary>Logic object. Replace it to bind the element to an existing slider (e.g. NavState.ForecastSlider).</summary>
        public RateSlider Model { get; set; } = new RateSlider();

        private float lastDeviation = float.NaN;
        private bool lastDragging;

        public RateSliderElement()
        {
            AddToClassList("rs");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        /// <summary>Repaints only when the thumb moved or the drag state changed.</summary>
        public void Sync()
        {
            if (Mathf.Approximately(lastDeviation, Model.Deviation) && lastDragging == Model.Dragging) { return; }
            lastDeviation = Model.Deviation;
            lastDragging = Model.Dragging;
            MarkDirtyRepaint();
        }

        private float Pos01(Vector2 local)
        {
            float w = resolvedStyle.width;
            return w > 1f ? Mathf.Clamp01(local.x / w) : 0.5f;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) { return; }
            this.CapturePointer(e.pointerId);
            Model.Press(Pos01(e.localPosition));
            Sync();
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) { return; }
            Model.Drag(Pos01(e.localPosition));
            Sync();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) { this.ReleasePointer(e.pointerId); }
            Model.Release();
            Sync();
        }

        private void OnCaptureOut(PointerCaptureOutEvent e)
        {
            Model.Release();
            Sync();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width;
            float h = contentRect.height;
            if (w < 10f || h < 4f) { return; }

            Painter2D p = ctx.painter2D;
            float cy = h * 0.5f;
            float r = Mathf.Min(h * 0.43f, 7f);
            float x0 = r + 1f;
            float x1 = w - r - 1f;
            float xm = (x0 + x1) * 0.5f;
            float tx = Mathf.Lerp(x0, x1, Model.ThumbPos01);

            p.lineCap = LineCap.Round;

            p.lineWidth = 4f;
            p.strokeColor = TrackColor;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, cy));
            p.LineTo(new Vector2(x1, cy));
            p.Stroke();

            if (Mathf.Abs(tx - xm) > 0.5f)
            {
                p.lineWidth = 2f;
                p.strokeColor = FillColor;
                p.BeginPath();
                p.MoveTo(new Vector2(xm, cy));
                p.LineTo(new Vector2(tx, cy));
                p.Stroke();
            }

            p.lineCap = LineCap.Butt;
            p.lineWidth = 1f;
            p.strokeColor = TickColor;
            p.BeginPath();
            p.MoveTo(new Vector2(xm, cy - r * 0.8f));
            p.LineTo(new Vector2(xm, cy + r * 0.8f));
            p.Stroke();

            // glow: a few translucent discs, then the thumb
            for (int i = 3; i >= 1; i--)
            {
                p.fillColor = new Color(0.30f, 0.88f, 0.49f, 0.07f);
                p.BeginPath();
                p.Arc(new Vector2(tx, cy), r + i * 1.6f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }

            p.fillColor = Model.Dragging ? ThumbActive : ThumbColor;
            p.BeginPath();
            p.Arc(new Vector2(tx, cy), r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
