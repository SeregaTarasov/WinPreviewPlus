using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WinPreviewPlus
{
    /// <summary>Единый визуальный язык превью (Excel / архив / документы).</summary>
    internal static class PreviewUi
    {
        public static readonly Color Bg = Color.FromArgb(252, 252, 253);
        public static readonly Color Surface = Color.White;
        public static readonly Color HeaderBg = Color.FromArgb(245, 246, 248);
        public static readonly Color Border = Color.FromArgb(210, 214, 222);
        public static readonly Color TextPrimary = Color.FromArgb(28, 32, 40);
        public static readonly Color TextMuted = Color.FromArgb(110, 118, 132);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color RowAlt = Color.FromArgb(248, 249, 251);
        public static readonly Color RowSelect = Color.FromArgb(220, 235, 252);
        public static readonly Color Folder = Color.FromArgb(90, 100, 120);
        public static readonly Color Danger = Color.FromArgb(180, 40, 40);
        public static readonly Color TabIdle = Color.FromArgb(236, 238, 242);
        public static readonly Color TabActive = Color.FromArgb(0, 103, 192);

        public const int RowHeight = 44;
        public const int HeaderHeight = 44;
        public const int CellPadH = 8;
        public const int CellPadV = 6;

        public static Font FontUi => new Font("Segoe UI", 9f, FontStyle.Regular);
        public static Font FontTitle => new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        public static Font FontMuted => new Font("Segoe UI", 8.25f, FontStyle.Regular);
        public static Font FontMono => new Font("Consolas", 9f, FontStyle.Regular);

        public static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, c, new object[] { true });
            }
            catch { /* ignore */ }
        }

        public static Panel CreateHeader(out Label title, out Label subtitle)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = HeaderBg,
                Padding = new Padding(12, 8, 12, 8)
            };

            title = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 18,
                Font = FontTitle,
                ForeColor = TextPrimary,
                Text = "Загрузка…",
                AutoEllipsis = true
            };
            subtitle = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 16,
                Font = FontMuted,
                ForeColor = TextMuted,
                Text = "",
                AutoEllipsis = true
            };

            var divider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Border
            };

            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            header.Controls.Add(divider);
            return header;
        }

        /// <summary>Общий стиль таблиц: Excel-превью и список архива.</summary>
        public static void StyleGrid(DataGridView grid)
        {
            EnableDoubleBuffer(grid);
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Surface;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            // Полная сетка: горизонталь + вертикаль
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars = ScrollBars.Both;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = HeaderBg,
                ForeColor = TextMuted,
                Font = FontMuted,
                SelectionBackColor = HeaderBg,
                SelectionForeColor = TextMuted,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(CellPadH, CellPadV + 1, CellPadH, CellPadV + 1)
            };
            grid.ColumnHeadersHeight = HeaderHeight;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = FontUi,
                SelectionBackColor = RowSelect,
                SelectionForeColor = TextPrimary,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(CellPadH, CellPadV, CellPadH, CellPadV),
                WrapMode = DataGridViewTriState.False
            };
            // Alignment здесь НЕ задаём: у AlternatingRows приоритет выше Column.DefaultCellStyle,
            // и MiddleLeft через строку сбивал «Размер» (MiddleRight) в зигзаг.
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = RowAlt,
                ForeColor = TextPrimary,
                SelectionBackColor = RowSelect,
                SelectionForeColor = TextPrimary,
                Padding = new Padding(CellPadH, CellPadV, CellPadH, CellPadV)
            };
            grid.RowTemplate.Height = RowHeight;

            // Колоночное выравнивание поверх AlternatingRows (у того дефолт MiddleLeft всё равно наследуется)
            grid.CellFormatting -= PreserveColumnAlignment;
            grid.CellFormatting += PreserveColumnAlignment;
        }

        private static void PreserveColumnAlignment(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
            var colAlign = grid.Columns[e.ColumnIndex].DefaultCellStyle.Alignment;
            e.CellStyle.Alignment = colAlign;
        }

        public static DataGridViewTextBoxColumn MakeColumn(string name, string header, int width,
            DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleLeft)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                MinimumWidth = 36,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.True,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = align,
                    Padding = new Padding(CellPadH, CellPadV, CellPadH, CellPadV)
                }
            };
        }

        /// <summary>
        /// Ширина по содержимому (+pad); последнее поле растягивается до правого края панели.
        /// </summary>
        public static void FitColumnsContentThenFillLast(DataGridView grid, double padFactor = 1.12)
        {
            if (grid == null || grid.Columns.Count == 0) return;
            int avail = grid.DisplayRectangle.Width;
            if (avail < 40) return;

            var font = grid.DefaultCellStyle.Font ?? FontUi;
            var headerFont = grid.ColumnHeadersDefaultCellStyle.Font ?? FontMuted;
            int n = grid.Columns.Count;
            var preferred = new int[n];

            for (int c = 0; c < n; c++)
            {
                int w = TextRenderer.MeasureText(grid.Columns[c].HeaderText ?? "", headerFont).Width
                        + CellPadH * 2 + 12;
                int sample = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (sample++ > 200) break;
                    if (row.IsNewRow) continue;
                    var val = row.Cells[c].Value as string ?? row.Cells[c].Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(val)) continue;
                    string measure = val.Length > 64 ? val.Substring(0, 64) + "…" : val;
                    w = Math.Max(w, TextRenderer.MeasureText(measure, font).Width + CellPadH * 2 + 8);
                }
                preferred[c] = Math.Max(40, (int)Math.Ceiling(w * padFactor));
            }

            int last = n - 1;
            int others = 0;
            for (int c = 0; c < last; c++)
                others += preferred[c];

            // последнее поле всегда доходит до правой границы
            preferred[last] = Math.Max(preferred[last], avail - others);
            if (preferred[last] < 40) preferred[last] = 40;

            // если не влезает — чуть ужимаем не-последние пропорционально
            int sum = others + preferred[last];
            if (sum > avail && others > 0)
            {
                double scale = (double)(avail - preferred[last]) / others;
                if (scale < 1 && scale > 0.3)
                {
                    for (int c = 0; c < last; c++)
                        preferred[c] = Math.Max(36, (int)(preferred[c] * scale));
                    others = 0;
                    for (int c = 0; c < last; c++) others += preferred[c];
                    preferred[last] = Math.Max(40, avail - others);
                }
            }

            for (int c = 0; c < n; c++)
                grid.Columns[c].Width = preferred[c];
        }

        public static RichTextBox CreateDocumentBox()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 10.25f),
                Padding = new Padding(8),
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
        }

        public static Button CreateSheetTab(string text, bool active)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                Font = active ? FontTitle : FontMuted,
                ForeColor = active ? Color.White : TextPrimary,
                BackColor = active ? TabActive : TabIdle,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(10, 2, 10, 2)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = active
                ? Color.FromArgb(0, 90, 170)
                : Color.FromArgb(224, 227, 234);
            return btn;
        }
    }
}
