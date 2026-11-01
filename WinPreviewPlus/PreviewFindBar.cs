using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinPreviewPlus
{
    /// <summary>
    /// Единый вид панели поиска: «Найти:» + поле + ‹ › × + счётчик.
    /// Без cue/placeholder в пустом поле.
    /// </summary>
    internal sealed class PreviewFindBar : Panel
    {
        public const int BarHeight = 44;
        public const int Pad = 6;
        public const int Btn = 28;

        public TextBox Box { get; private set; }
        public Label Meta { get; private set; }
        public Button BtnPrev { get; private set; }
        public Button BtnNext { get; private set; }
        public Button BtnClose { get; private set; }

        private Label _lbl;
        private Panel _bottomLine;

        public event KeyEventHandler BoxKeyDown;
        public event EventHandler BoxTextChanged;
        public event EventHandler PrevClick;
        public event EventHandler NextClick;
        public event EventHandler CloseClick;

        public PreviewFindBar()
        {
            Dock = DockStyle.Top;
            Height = BarHeight;
            MinimumSize = new Size(0, BarHeight);
            BackColor = Color.FromArgb(255, 252, 235);
            Visible = false;
            Margin = Padding.Empty;
            Padding = new Padding(Pad);
            PreviewUi.EnableDoubleBuffer(this);

            _bottomLine = new Panel
            {
                Height = 1,
                BackColor = PreviewUi.Border,
                Margin = Padding.Empty
            };

            _lbl = new Label
            {
                Text = "Найти:",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextMuted,
                AutoEllipsis = false
            };

            Box = new TextBox
            {
                Font = PreviewUi.FontUi,
                BorderStyle = BorderStyle.FixedSingle,
                Multiline = false,
                AutoSize = false
            };
            Box.KeyDown += (s, e) => BoxKeyDown?.Invoke(s, e);
            Box.TextChanged += (s, e) => BoxTextChanged?.Invoke(s, e);

            BtnPrev = MakeBtn("‹");
            BtnPrev.Click += (s, e) => PrevClick?.Invoke(s, e);
            BtnNext = MakeBtn("›");
            BtnNext.Click += (s, e) => NextClick?.Invoke(s, e);
            BtnClose = MakeBtn("×");
            BtnClose.Click += (s, e) => CloseClick?.Invoke(s, e);

            Meta = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted
            };

            Controls.Add(_bottomLine);
            Controls.Add(Meta);
            Controls.Add(BtnClose);
            Controls.Add(BtnNext);
            Controls.Add(BtnPrev);
            Controls.Add(Box);
            Controls.Add(_lbl);
            Resize += (s, e) => LayoutControls();
            LayoutControls();
        }

        public void ShowBar(bool show, Control host = null)
        {
            if (show)
            {
                Height = BarHeight;
                Visible = true;
                BringToFront();
                if (host != null)
                {
                    try { host.Controls.SetChildIndex(this, 0); } catch { }
                }
                LayoutControls();
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Box.Focus();
                            Box.SelectAll();
                        }
                        catch { }
                    }));
                }
                catch { }
            }
            else
            {
                Visible = false;
                Meta.Text = "";
            }
        }

        public void LayoutControls()
        {
            if (Box == null || _lbl == null) return;

            int need = Box.PreferredHeight + Pad * 2 + 2;
            if (need < BarHeight) need = BarHeight;
            if (Height < need)
            {
                Height = need;
                MinimumSize = new Size(0, need);
            }

            var area = DisplayRectangle;
            int boxH = Box.PreferredHeight;
            if (boxH < 18) boxH = 20;
            boxH = Math.Min(boxH, Math.Max(18, area.Height - 2));
            int y = area.Top + Math.Max(0, (area.Height - boxH) / 2);
            int btnY = area.Top + Math.Max(0, (area.Height - Btn) / 2);
            int x = area.Left;

            int lblW = TextRenderer.MeasureText("Найти:", PreviewUi.FontUi).Width + 6;
            _lbl.SetBounds(x, btnY, lblW, Btn);
            x += lblW + 6;

            int rightReserve = Btn * 3 + 16 + 90;
            int boxW = Math.Max(80, area.Width - (x - area.Left) - rightReserve);
            Box.SetBounds(x, y, boxW, boxH);
            x += boxW + 6;

            BtnPrev.SetBounds(x, btnY, Btn, Btn);
            x += Btn + 4;
            BtnNext.SetBounds(x, btnY, Btn, Btn);
            x += Btn + 8;
            Meta.SetBounds(x, btnY, Math.Max(40, area.Right - x - Btn - 8), Btn);
            BtnClose.SetBounds(area.Right - Btn, btnY, Btn, Btn);

            _bottomLine.SetBounds(0, ClientSize.Height - 1, ClientSize.Width, 1);
        }

        /// <summary>Кнопка тулбара «Найти» — один стиль для PDF / текст / Excel.</summary>
        public static Button MakeToolbarButton()
        {
            int w = TextRenderer.MeasureText("Найти", PreviewUi.FontUi).Width + 18;
            var b = new Button
            {
                Text = "Найти",
                FlatStyle = FlatStyle.Flat,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand,
                TabStop = false,
                AutoSize = false,
                Size = new Size(Math.Max(56, w), Btn),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            return b;
        }

        private static Button MakeBtn(string text)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand,
                TabStop = false,
                AutoSize = false,
                Size = new Size(Btn, Btn),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            return b;
        }
    }
}
