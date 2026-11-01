using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinPreviewPlus
{
    /// <summary>Compact action bar similar to Edge PDF preview chrome.</summary>
    internal sealed class PreviewToolbar : Panel
    {
        private readonly FlowLayoutPanel _left;
        private readonly Label _meta;

        public PreviewToolbar()
        {
            Height = 34;
            Dock = DockStyle.Top;
            BackColor = PreviewUi.HeaderBg;
            Padding = new Padding(8, 4, 8, 4);
            PreviewUi.EnableDoubleBuffer(this);

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = PreviewUi.Border
            };

            _meta = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 120,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted,
                Padding = new Padding(8, 0, 0, 0)
            };

            _left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                BackColor = PreviewUi.HeaderBg,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            PreviewUi.EnableDoubleBuffer(_left);

            Controls.Add(_left);
            Controls.Add(_meta);
            Controls.Add(bottom);
        }

        public void SetMeta(string text)
        {
            _meta.Text = text ?? "";
        }

        public ToolButton AddButton(string text, string tip, EventHandler onClick)
        {
            var btn = new ToolButton(text, tip);
            btn.Click += onClick;
            _left.Controls.Add(btn);
            return btn;
        }

        public void AddSeparator()
        {
            _left.Controls.Add(new Panel
            {
                Width = 1,
                Height = 18,
                Margin = new Padding(6, 4, 6, 0),
                BackColor = PreviewUi.Border
            });
        }

        public void AddControl(Control c)
        {
            _left.Controls.Add(c);
        }

        internal sealed class ToolButton : Button
        {
            public ToolButton(string text, string tip)
            {
                Text = text;
                AutoSize = true;
                Height = 24;
                MinimumSize = new Size(28, 24);
                FlatStyle = FlatStyle.Flat;
                Font = PreviewUi.FontUi;
                ForeColor = PreviewUi.TextPrimary;
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                Margin = new Padding(0, 0, 4, 0);
                Padding = new Padding(8, 1, 8, 1);
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 233, 239);
                FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 219, 228);
                if (!string.IsNullOrEmpty(tip))
                    new ToolTip().SetToolTip(this, tip);
            }

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
                ForeColor = enabled ? PreviewUi.TextPrimary : PreviewUi.TextMuted;
            }
        }
    }
}
