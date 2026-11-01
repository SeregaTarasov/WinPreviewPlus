using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinPreviewPlus
{
    /// <summary>Тонкий seek-бар вместо WinForms TrackBar.</summary>
    internal sealed class SeekBar : Control
    {
        private int _minimum;
        private int _maximum = 1000;
        private int _value;
        private bool _dragging;
        private float _trackThickness = 4f;
        private float _thumbRadius = 6f;

        public event EventHandler ValueChanged;
        public event EventHandler SeekCommitted;

        public SeekBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            Height = 24;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = false;
        }

        public float TrackThickness
        {
            get => _trackThickness;
            set { _trackThickness = Math.Max(2f, value); Invalidate(); }
        }

        public float ThumbRadius
        {
            get => _thumbRadius;
            set { _thumbRadius = Math.Max(4f, value); Invalidate(); }
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_value < _minimum) Value = _minimum;
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(_minimum + 1, value);
                if (_value > _maximum) Value = _maximum;
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Max(_minimum, Math.Min(_maximum, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsDragging => _dragging;

        private RectangleF TrackRect
        {
            get
            {
                float h = _trackThickness;
                float pad = Math.Max(8f, _thumbRadius + 2f);
                float y = (ClientSize.Height - h) / 2f;
                return new RectangleF(pad, y, Math.Max(1f, ClientSize.Width - pad * 2f), h);
            }
        }

        private float Ratio
        {
            get
            {
                int span = _maximum - _minimum;
                if (span <= 0) return 0;
                return (_value - _minimum) / (float)span;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = TrackRect;
            float radius = Math.Min(track.Height / 2f, 4f);
            using (var bg = new SolidBrush(PreviewUi.Border))
            using (var path = RoundRect(track, radius))
                g.FillPath(bg, path);

            float fillW = track.Width * Ratio;
            if (fillW > 0.5f)
            {
                var fill = new RectangleF(track.X, track.Y, fillW, track.Height);
                using (var brush = new SolidBrush(PreviewUi.Accent))
                using (var path = RoundRect(fill, radius))
                    g.FillPath(brush, path);
            }

            float cx = track.X + track.Width * Ratio;
            float cy = track.Y + track.Height / 2f;
            float r = _dragging ? _thumbRadius + 2f : _thumbRadius;
            using (var thumb = new SolidBrush(PreviewUi.Accent))
                g.FillEllipse(thumb, cx - r, cy - r, r * 2f, r * 2f);
            using (var rim = new Pen(Color.White, Math.Max(1.5f, _thumbRadius / 4f)))
                g.DrawEllipse(rim, cx - r, cy - r, r * 2f, r * 2f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            Capture = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            SetFromMouse(e.X);
            SeekCommitted?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        private void SetFromMouse(int x)
        {
            var track = TrackRect;
            float t = (x - track.X) / track.Width;
            if (float.IsNaN(t) || float.IsInfinity(t)) t = 0;
            t = Math.Max(0f, Math.Min(1f, t));
            Value = _minimum + (int)Math.Round(t * (_maximum - _minimum));
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            if (r.Width < d || r.Height < d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
