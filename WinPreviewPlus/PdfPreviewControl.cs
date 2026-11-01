using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using PdfiumViewer;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    public class PdfPreviewControl : PreviewHandlerControl, IPreviewFindHost
    {
        // Лёгкая заливка поверх текста — высокий alpha перекрывает буквы
        private static readonly Color HighlightYellow = Color.FromArgb(72, 255, 235, 59);
        private static readonly Color HighlightCurrent = Color.FromArgb(96, 255, 193, 7);

        private const int BarH = 44;
        private const int Btn = 28;

        private readonly Panel _host;
        private Panel _chrome;
        private Panel _bar;
        private PreviewFindBar _findBar;
        private Label _meta;
        private Label _zoomLbl;
        private Button _btnPrev;
        private Button _btnNext;
        private Button _btnZoomOut;
        private Button _btnZoomIn;
        private Button _btnText;
        private PdfViewer _viewer;
        private RichTextBox _textBox;
        private PdfDocument _doc;
        private PreviewSource _source;
        private bool _textMode;

        private string _findQuery = "";
        private readonly List<int> _textMatchStarts = new List<int>();
        private readonly List<PdfHit> _pdfHits = new List<PdfHit>();
        private int _matchIndex = -1;
        private PreviewHotkeys _hotkeys;
        private Button _btnFind;
        private Timer _findDebounce;

        private sealed class PdfHit
        {
            public int Page;
            public PdfTextSpan Span;
            public List<RectangleF> Bounds;
        }

        /// <summary>
        /// Отступ в экранных px (симметрично). Expand в PDF-координатах после
        /// BoundsFromPdf давал сдвиг вверх/вправо относительно глифов.
        /// </summary>
        private sealed class ScreenPadMarker : IPdfMarker
        {
            private readonly int _page;
            private readonly RectangleF _pdfBounds;
            private readonly Color _color;
            private readonly int _padX;
            private readonly int _padY;

            public ScreenPadMarker(int page, RectangleF pdfBounds, Color color, int padX = 2, int padY = 1)
            {
                _page = page;
                _pdfBounds = pdfBounds;
                _color = color;
                _padX = padX;
                _padY = padY;
            }

            public int Page => _page;

            public void Draw(PdfRenderer renderer, Graphics graphics)
            {
                if (renderer == null || graphics == null) return;
                var r = renderer.BoundsFromPdf(new PdfRectangle(_page, _pdfBounds));
                if (r.Width <= 0 || r.Height <= 0) return;
                r.Inflate(_padX, _padY);
                using (var brush = new SolidBrush(_color))
                    graphics.FillRectangle(brush, r);
            }
        }

        public void ShowFind() => ShowFindBar(true);

        public void FindNext(bool forward)
        {
            if (_findBar == null || !_findBar.Visible || string.IsNullOrEmpty(_findQuery))
                ShowFindBar(true);
            else
                StepMatch(forward);
        }

        public PdfPreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _host = new Panel { Dock = DockStyle.Fill, BackColor = PreviewUi.Surface };
            PreviewUi.EnableDoubleBuffer(_host);
            Controls.Add(_host);

            _hotkeys = new PreviewHotkeys(
                this,
                onFind: () => ShowFindBar(true),
                onFindNext: forward =>
                {
                    if (string.IsNullOrEmpty(_findQuery))
                        ShowFindBar(true);
                    else
                        StepMatch(forward);
                },
                onEscape: () =>
                {
                    if (_findBar != null && _findBar.Visible)
                        ShowFindBar(false);
                });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                ShowFindBar(true);
                return true;
            }
            if (keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3))
            {
                if (string.IsNullOrEmpty(_findQuery))
                    ShowFindBar(true);
                else
                    StepMatch(forward: keyData == Keys.F3);
                return true;
            }
            if (keyData == Keys.Escape && _findBar != null && _findBar.Visible)
            {
                ShowFindBar(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyText();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void LoadPdf(PreviewSource source)
        {
            _source = source;
            _textMode = false;
            ClearFindState();
            DisposeViewer();
            _host.Controls.Clear();

            try
            {
                _doc = PdfDocument.Load(source.Path);
                _viewer = new PdfViewer
                {
                    Dock = DockStyle.Fill,
                    Document = _doc,
                    ShowToolbar = false,
                    ShowBookmarks = false,
                    ZoomMode = PdfViewerZoomMode.FitWidth,
                    BackColor = PreviewUi.Surface
                };
                _viewer.Renderer.MouseWheelMode = MouseWheelMode.PanAndZoom;
                _viewer.Renderer.ZoomChanged += (s, e) => RefreshBar();

                _textBox = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    DetectUrls = false,
                    HideSelection = false,
                    Font = new Font("Segoe UI", 10.5f),
                    BackColor = PreviewUi.Surface,
                    ForeColor = PreviewUi.TextPrimary,
                    ScrollBars = RichTextBoxScrollBars.Both,
                    WordWrap = true,
                    Visible = false
                };
                _textBox.KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.F)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        ShowFindBar(true);
                    }
                };

                var ctx = new ContextMenuStrip();
                ctx.Items.Add("Найти…", null, (s, e) => ShowFindBar(true));
                ctx.Items.Add(new ToolStripSeparator());
                ctx.Items.Add("Копировать", null, (s, e) => CopyText());
                ctx.Items.Add("Копировать страницу", null, (s, e) => CopyPageText());
                ctx.Items.Add("Копировать весь текст", null, (s, e) => CopyAllText());
                ctx.Items.Add(new ToolStripSeparator());
                ctx.Items.Add("Режим текста", null, (s, e) => SetTextMode(true));
                _viewer.ContextMenuStrip = ctx;
                _textBox.ContextMenuStrip = ctx;

                CreateChrome();
                // Dock: Fill → find(Top) → chrome(Top). chrome.BringToFront → toolbar сверху, find под ним.
                _host.Controls.Add(_viewer);
                _host.Controls.Add(_textBox);
                _host.Controls.Add(_findBar);
                _host.Controls.Add(_chrome);
                _chrome.BringToFront();

                RefreshBar();
                try { _viewer.Focus(); } catch { /* ignore */ }
                PreviewLog.Info("PDF preview OK: " + source.Path + " pages=" + _doc.PageCount);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть PDF: " + ex.Message);
                PreviewLog.Error("PDF preview failed: " + source.Path, ex);
            }
        }

        /// <summary>
        /// Toolbar и find — отдельные Dock.Top на _host (не вложенные). В prevhost вложенный
        /// absolute layout давал наезд «Найти» на toolbar.
        /// </summary>
        private void CreateChrome()
        {
            _chrome = new Panel
            {
                Dock = DockStyle.Top,
                Height = BarH,
                BackColor = PreviewUi.HeaderBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            PreviewUi.EnableDoubleBuffer(_chrome);

            CreateBar();
            CreateFindBar();
            _chrome.Controls.Add(_bar);
            _bar.Dock = DockStyle.Fill;
            _chrome.Resize += (s, e) => LayoutBar();
            LayoutBar();
        }

        private void CreateBar()
        {
            _bar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PreviewUi.HeaderBg,
                Margin = Padding.Empty
            };
            PreviewUi.EnableDoubleBuffer(_bar);
            var bottomLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = PreviewUi.Border
            };

            _btnPrev = MakeBtn("‹");
            _btnPrev.Click += (s, e) => GoPage(-1);
            _btnNext = MakeBtn("›");
            _btnNext.Click += (s, e) => GoPage(+1);
            _btnZoomOut = MakeBtn("−");
            _btnZoomOut.Click += (s, e) => { if (!_textMode) _viewer.Renderer.ZoomOut(); };
            _btnZoomIn = MakeBtn("+");
            _btnZoomIn.Click += (s, e) => { if (!_textMode) _viewer.Renderer.ZoomIn(); };
            _btnText = MakeBtn("Т");
            _btnText.Width = 32;
            _btnFind = PreviewFindBar.MakeToolbarButton();
            var tip = new ToolTip();
            tip.SetToolTip(_btnText, "Режим текста — выделение и копирование");
            tip.SetToolTip(_btnFind, "Найти (Ctrl+F)");
            tip.SetToolTip(_btnZoomOut, "Уменьшить (Ctrl+колёсико)");
            tip.SetToolTip(_btnZoomIn, "Увеличить (Ctrl+колёсико)");
            _btnText.Click += (s, e) => SetTextMode(!_textMode);
            _btnFind.Click += (s, e) => ShowFindBar(true);

            _meta = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontTitle,
                ForeColor = PreviewUi.TextPrimary
            };
            _zoomLbl = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted
            };

            _bar.Controls.Add(bottomLine);
            _bar.Controls.Add(_zoomLbl);
            _bar.Controls.Add(_meta);
            _bar.Controls.Add(_btnFind);
            _bar.Controls.Add(_btnText);
            _bar.Controls.Add(_btnZoomIn);
            _bar.Controls.Add(_btnZoomOut);
            _bar.Controls.Add(_btnNext);
            _bar.Controls.Add(_btnPrev);
            _bar.Resize += (s, e) => LayoutBar();
            LayoutBar();
        }

        private void CreateFindBar()
        {
            if (_findDebounce == null)
            {
                _findDebounce = new Timer { Interval = 180 };
                _findDebounce.Tick += (s, e) =>
                {
                    _findDebounce.Stop();
                    RunFind(forward: true, allowStep: false);
                };
            }

            _findBar = new PreviewFindBar();
            _findBar.BoxKeyDown += FindBoxOnKeyDown;
            _findBar.BoxTextChanged += (s, e) =>
            {
                _findDebounce.Stop();
                _findDebounce.Start();
            };
            _findBar.PrevClick += (s, e) => StepMatch(false);
            _findBar.NextClick += (s, e) => StepMatch(true);
            _findBar.CloseClick += (s, e) => ShowFindBar(false);
        }
        private void ShowFindBar(bool show)
        {
            if (_findBar == null) return;
            PreviewLog.Info("ShowFindBar " + show);
            if (show)
            {
                _findBar.ShowBar(true, _host);
                if (_chrome != null) _chrome.BringToFront();
                _findBar.BringToFront();
            }
            else
            {
                if (_findDebounce != null) _findDebounce.Stop();
                _findBar.ShowBar(false);
                ClearHighlights();
                ClearFindState();
            }
        }

        private void FindBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RunFind(forward: !e.Shift);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                ShowFindBar(false);
            }
        }

        private void RunFind(bool forward, bool allowStep = true)
        {
            if (_doc == null || _findBar?.Box == null) return;
            var q = (_findBar.Box.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q))
            {
                ClearHighlights();
                ClearFindState();
                if (_findBar.Meta != null) _findBar.Meta.Text = "";
                return;
            }

            // Enter / ‹ › — шаг; набор символов — всегда пересборка
            bool sameQuery = allowStep
                             && string.Equals(_findQuery, q, StringComparison.CurrentCultureIgnoreCase)
                             && ((_textMode ? _textMatchStarts.Count : _pdfHits.Count) > 0);
            if (sameQuery && _matchIndex >= 0)
            {
                StepMatch(forward);
                return;
            }

            _findQuery = q;
            _matchIndex = -1;

            if (_textMode)
                BuildTextMatches(q);
            else
                BuildPdfMatches(q);

            int total = _textMode ? _textMatchStarts.Count : _pdfHits.Count;
            if (total == 0)
            {
                if (_findBar.Meta != null) _findBar.Meta.Text = "нет совпадений";
                ClearHighlights();
                return;
            }

            _matchIndex = 0;
            ApplyCurrentMatch();
        }

        private void StepMatch(bool forward)
        {
            if (string.IsNullOrEmpty(_findQuery))
            {
                if (_findBar?.Box != null && !string.IsNullOrWhiteSpace(_findBar.Box.Text))
                    RunFind(forward, allowStep: false);
                return;
            }

            int total = _textMode ? _textMatchStarts.Count : _pdfHits.Count;
            if (total == 0)
            {
                RunFind(forward, allowStep: false);
                return;
            }

            if (forward)
                _matchIndex = (_matchIndex + 1) % total;
            else
                _matchIndex = (_matchIndex - 1 + total) % total;
            ApplyCurrentMatch();
        }

        private void BuildTextMatches(string query)
        {
            _textMatchStarts.Clear();
            if (_textBox == null) return;
            if (string.IsNullOrEmpty(_textBox.Text))
                _textBox.Text = BuildAllText();

            string text = _textBox.Text ?? "";
            int start = 0;
            while (start < text.Length)
            {
                // CurrentCultureIgnoreCase: Счет / счет / СЧЕТ
                int i = text.IndexOf(query, start, StringComparison.CurrentCultureIgnoreCase);
                if (i < 0) break;
                _textMatchStarts.Add(i);
                start = i + Math.Max(1, query.Length);
            }
            HighlightAllTextMatches();
        }

        private void BuildPdfMatches(string query)
        {
            _pdfHits.Clear();
            var seen = new HashSet<string>();
            try
            {
                // Pdfium matchCase=false часто НЕ складывает кириллицу (вывоз ≠ Вывоз).
                // 1) быстрый Search 2) добор через GetPdfText + CurrentCultureIgnoreCase
                AddSearchHits(query, matchCase: false, seen);
                var ci = CultureInfo.CurrentCulture;
                AddSearchHits(query.ToLower(ci), matchCase: true, seen);
                AddSearchHits(query.ToUpper(ci), matchCase: true, seen);
                // «Вывоз» из «вывоз»
                if (query.Length > 0)
                {
                    string titled = char.ToUpper(query[0], ci) +
                                    (query.Length > 1 ? query.Substring(1).ToLower(ci) : "");
                    if (!string.Equals(titled, query, StringComparison.Ordinal))
                        AddSearchHits(titled, matchCase: true, seen);
                }

                BuildPdfMatchesCultureIgnore(query, seen);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("PDF Search failed", ex);
            }
            PreviewLog.Info("PDF find '" + query + "' hits=" + _pdfHits.Count);
            PaintPdfMarkers();
        }

        private void AddSearchHits(string text, bool matchCase, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(text) || _doc == null || seen == null) return;
            PdfMatches found;
            try { found = _doc.Search(text, matchCase, wholeWord: false); }
            catch { return; }
            if (found?.Items == null) return;

            foreach (var m in found.Items)
            {
                string key = m.Page + ":" + m.TextSpan.Offset + ":" + m.TextSpan.Length;
                if (!seen.Add(key)) continue;
                _pdfHits.Add(new PdfHit
                {
                    Page = m.Page,
                    Span = m.TextSpan,
                    Bounds = null
                });
            }
        }

        /// <summary>Надёжный ignore-case для кириллицы: IndexOf по тексту страницы → точный Search.</summary>
        private void BuildPdfMatchesCultureIgnore(string query, HashSet<string> seen)
        {
            if (_doc == null || string.IsNullOrEmpty(query)) return;

            for (int p = 0; p < _doc.PageCount; p++)
            {
                string pageText;
                try { pageText = _doc.GetPdfText(p) ?? ""; }
                catch { continue; }
                if (pageText.Length == 0) continue;

                int start = 0;
                while (start < pageText.Length)
                {
                    int i = pageText.IndexOf(query, start, StringComparison.CurrentCultureIgnoreCase);
                    if (i < 0) break;
                    int len = query.Length;
                    if (i + len > pageText.Length) break;
                    string exact = pageText.Substring(i, len);
                    start = i + Math.Max(1, len);

                    // Точный Search даёт верный TextSpan для маркеров
                    try
                    {
                        var found = _doc.Search(exact, matchCase: true, wholeWord: false, p, p);
                        if (found?.Items != null && found.Items.Count > 0)
                        {
                            foreach (var m in found.Items)
                            {
                                string key = m.Page + ":" + m.TextSpan.Offset + ":" + m.TextSpan.Length;
                                if (!seen.Add(key)) continue;
                                _pdfHits.Add(new PdfHit
                                {
                                    Page = m.Page,
                                    Span = m.TextSpan,
                                    Bounds = null
                                });
                            }
                            continue;
                        }
                    }
                    catch { /* fall through */ }

                    string key2 = p + ":" + i + ":" + len;
                    if (!seen.Add(key2)) continue;
                    _pdfHits.Add(new PdfHit
                    {
                        Page = p,
                        Span = new PdfTextSpan(p, i, len),
                        Bounds = null
                    });
                }
            }
        }

        /// <summary>
        /// Сырые GetTextBounds без PDF-expand (он давал сдвиг). Запас — в ScreenPadMarker.
        /// Несколько кусков span склеиваем в один бокс.
        /// </summary>
        private List<RectangleF> ResolveBounds(PdfHit hit)
        {
            if (hit.Bounds != null) return hit.Bounds;
            var list = new List<RectangleF>();
            if (_doc == null || hit.Span.Length <= 0)
            {
                hit.Bounds = list;
                return list;
            }

            try
            {
                RectangleF? union = null;
                var whole = _doc.GetTextBounds(hit.Span);
                if (whole != null)
                {
                    foreach (var r in whole)
                    {
                        if (!r.IsValid || !IsUsablePdfBox(r.Bounds)) continue;
                        union = union.HasValue ? UnionPdfBox(union.Value, r.Bounds) : r.Bounds;
                    }
                }

                if (!union.HasValue && hit.Span.Length <= 64)
                {
                    for (int c = 0; c < hit.Span.Length; c++)
                    {
                        var parts = _doc.GetTextBounds(
                            new PdfTextSpan(hit.Span.Page, hit.Span.Offset + c, 1));
                        if (parts == null) continue;
                        foreach (var r in parts)
                        {
                            if (!r.IsValid || !IsUsablePdfBox(r.Bounds)) continue;
                            union = union.HasValue ? UnionPdfBox(union.Value, r.Bounds) : r.Bounds;
                        }
                    }
                }

                if (union.HasValue)
                    list.Add(union.Value);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("ResolveBounds", ex);
            }

            if (list.Count == 0)
                PreviewLog.Info("ResolveBounds empty page=" + hit.Page + " off=" + hit.Span.Offset +
                    " len=" + hit.Span.Length);

            hit.Bounds = list;
            return list;
        }

        private static bool IsUsablePdfBox(RectangleF b)
        {
            return Math.Abs(b.Width) > 0.3f && Math.Abs(b.Height) > 0.3f;
        }

        private static RectangleF UnionPdfBox(RectangleF a, RectangleF b)
        {
            PdfBoxEdges(a, out float al, out float ar, out float at, out float ab);
            PdfBoxEdges(b, out float bl, out float br, out float bt, out float bb);
            float left = Math.Min(al, bl);
            float right = Math.Max(ar, br);
            float top = Math.Max(at, bt);
            float bottom = Math.Min(ab, bb);
            return new RectangleF(left, top, right - left, bottom - top);
        }

        private static void PdfBoxEdges(RectangleF r, out float left, out float right, out float top, out float bottom)
        {
            left = Math.Min(r.X, r.X + r.Width);
            right = Math.Max(r.X, r.X + r.Width);
            float y1 = r.Y;
            float y2 = r.Y + r.Height;
            top = Math.Max(y1, y2);
            bottom = Math.Min(y1, y2);
        }

        private void HighlightAllTextMatches()
        {
            if (_textBox == null) return;
            int caret = _textBox.SelectionStart;
            _textBox.SelectAll();
            _textBox.SelectionBackColor = _textBox.BackColor;
            _textBox.DeselectAll();

            int len = _findQuery.Length;
            foreach (int start in _textMatchStarts)
            {
                _textBox.Select(start, len);
                _textBox.SelectionBackColor = HighlightYellow;
            }
            _textBox.Select(Math.Min(caret, _textBox.TextLength), 0);
            _textBox.SelectionBackColor = _textBox.BackColor;
        }

        private void PaintPdfMarkers()
        {
            if (_viewer == null || _doc == null) return;
            _viewer.Renderer.Markers.Clear();
            int painted = 0;
            for (int i = 0; i < _pdfHits.Count; i++)
            {
                var m = _pdfHits[i];
                var color = (i == _matchIndex) ? HighlightCurrent : HighlightYellow;
                try
                {
                    var boxes = ResolveBounds(m);
                    if (boxes == null || boxes.Count == 0) continue;
                    foreach (var b in boxes)
                    {
                        if (!IsUsablePdfBox(b)) continue;
                        _viewer.Renderer.Markers.Add(new ScreenPadMarker(m.Page, b, color));
                        painted++;
                    }
                }
                catch { /* skip bad span */ }
            }
            PreviewLog.Info("PDF markers painted=" + painted + " hits=" + _pdfHits.Count +
                " idx=" + _matchIndex);
            _viewer.Renderer.Invalidate();
        }

        private void ApplyCurrentMatch()
        {
            if (_textMode)
            {
                if (_matchIndex < 0 || _matchIndex >= _textMatchStarts.Count || _textBox == null)
                    return;
                HighlightAllTextMatches();
                int start = _textMatchStarts[_matchIndex];
                _textBox.Select(start, _findQuery.Length);
                _textBox.SelectionBackColor = HighlightCurrent;
                _textBox.ScrollToCaret();
                if (_findBar?.Meta != null)
                    _findBar.Meta.Text = (_matchIndex + 1) + " из " + _textMatchStarts.Count;
            }
            else
            {
                if (_matchIndex < 0 || _matchIndex >= _pdfHits.Count || _viewer == null)
                    return;
                var m = _pdfHits[_matchIndex];
                _viewer.Renderer.Page = m.Page;
                PaintPdfMarkers();
                try
                {
                    var boxes = ResolveBounds(m);
                    if (boxes != null && boxes.Count > 0)
                        _viewer.Renderer.ScrollIntoView(new PdfRectangle(m.Page, boxes[0]));
                }
                catch { /* ignore scroll */ }
                if (_findBar?.Meta != null)
                    _findBar.Meta.Text = (_matchIndex + 1) + " из " + _pdfHits.Count;
                RefreshBar();
            }
        }

        private void ClearHighlights()
        {
            if (_textBox != null && !string.IsNullOrEmpty(_textBox.Text))
            {
                int c = _textBox.SelectionStart;
                _textBox.SelectAll();
                _textBox.SelectionBackColor = _textBox.BackColor;
                _textBox.Select(Math.Min(c, _textBox.TextLength), 0);
            }
            if (_viewer != null)
            {
                _viewer.Renderer.Markers.Clear();
                _viewer.Renderer.Invalidate();
            }
        }

        private void ClearFindState()
        {
            _findQuery = "";
            _textMatchStarts.Clear();
            _pdfHits.Clear();
            _matchIndex = -1;
        }

        private static Button MakeBtn(string text)
        {
            var btn = new Button
            {
                Text = text,
                Width = Btn,
                Height = Btn,
                FlatStyle = FlatStyle.Flat,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            return btn;
        }

        private void LayoutBar()
        {
            if (_bar == null) return;
            int innerH = _bar.ClientSize.Height - 1;
            int y = Math.Max(0, (innerH - Btn) / 2);
            int x = 8;

            void Place(Control c, int w)
            {
                if (c == null) return;
                c.SetBounds(x, y, w, Btn);
                x += w + 4;
            }

            Place(_btnPrev, Btn);
            Place(_btnNext, Btn);
            x += 4;
            Place(_btnZoomOut, Btn);
            Place(_btnZoomIn, Btn);
            Place(_btnText, 32);
            Place(_btnFind, _btnFind != null ? _btnFind.Width : 56);
            x += 8;

            int zoomW = Math.Max(56,
                TextRenderer.MeasureText(_zoomLbl?.Text ?? "100%", PreviewUi.FontMuted).Width + 12);
            int zoomX = _bar.ClientSize.Width - zoomW - 10;
            _zoomLbl.SetBounds(zoomX, y, zoomW, Btn);
            _meta.SetBounds(x, y, Math.Max(48, zoomX - x - 8), Btn);
        }

        private void RefreshBar()
        {
            if (_doc == null) return;
            int page = 1;
            int total = Math.Max(1, _doc.PageCount);
            if (_viewer != null)
                page = Math.Max(0, _viewer.Renderer.Page) + 1;

            if (_meta != null)
                _meta.Text = _textMode ? ("Текст · " + total + " стр.") : (page + " из " + total);
            if (_zoomLbl != null)
            {
                if (_textMode)
                    _zoomLbl.Text = "выделение";
                else if (_viewer != null)
                    _zoomLbl.Text = (int)Math.Round(_viewer.Renderer.Zoom * 100) + "%";
            }
            if (_btnPrev != null) _btnPrev.Enabled = !_textMode && page > 1;
            if (_btnNext != null) _btnNext.Enabled = !_textMode && page < total;
            if (_btnZoomIn != null) _btnZoomIn.Enabled = !_textMode;
            if (_btnZoomOut != null) _btnZoomOut.Enabled = !_textMode;
            if (_btnText != null)
            {
                _btnText.BackColor = _textMode ? PreviewUi.TabActive : PreviewUi.TabIdle;
                _btnText.ForeColor = _textMode ? Color.White : PreviewUi.TextPrimary;
            }
            LayoutBar();
        }

        private void SetTextMode(bool on)
        {
            if (_doc == null || _textBox == null || _viewer == null) return;
            _textMode = on;
            if (on)
            {
                if (string.IsNullOrEmpty(_textBox.Text))
                    _textBox.Text = BuildAllText();
                _textBox.Visible = true;
                _viewer.Visible = false;
                _textBox.BringToFront();
                _textBox.Focus();
            }
            else
            {
                _textBox.Visible = false;
                _viewer.Visible = true;
                _viewer.BringToFront();
            }
            BringChromeFront();
            RefreshBar();

            if (!string.IsNullOrEmpty(_findQuery) && _findBar != null && _findBar.Visible)
                RunFind(true, allowStep: false);
        }

        private void BringChromeFront()
        {
            if (_chrome != null)
                _chrome.BringToFront();
            if (_findBar != null && _findBar.Visible)
                _findBar.BringToFront();
        }

        private string BuildAllText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _doc.PageCount; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("──────── страница " + (i + 1) + " ────────");
                    sb.AppendLine();
                }
                try { sb.AppendLine(_doc.GetPdfText(i) ?? ""); }
                catch { sb.AppendLine("(не удалось извлечь текст)"); }
            }
            return sb.ToString().TrimEnd();
        }

        private string CurrentPageText()
        {
            if (_doc == null || _viewer == null) return "";
            try { return _doc.GetPdfText(_viewer.Renderer.Page) ?? ""; }
            catch { return ""; }
        }

        private void CopyText()
        {
            try
            {
                if (_textMode && _textBox != null && _textBox.SelectionLength > 0)
                {
                    Clipboard.SetText(_textBox.SelectedText);
                    return;
                }
                CopyPageText();
            }
            catch (Exception ex) { PreviewLog.Error("CopyText", ex); }
        }

        private void CopyPageText()
        {
            try
            {
                var t = CurrentPageText();
                if (!string.IsNullOrEmpty(t))
                    Clipboard.SetText(t);
            }
            catch (Exception ex) { PreviewLog.Error("CopyPageText", ex); }
        }

        private void CopyAllText()
        {
            try
            {
                var t = BuildAllText();
                if (!string.IsNullOrEmpty(t))
                    Clipboard.SetText(t);
            }
            catch (Exception ex) { PreviewLog.Error("CopyAllText", ex); }
        }

        private void GoPage(int delta)
        {
            if (_viewer == null || _doc == null || _textMode) return;
            int next = Math.Max(0, Math.Min(_doc.PageCount - 1, _viewer.Renderer.Page + delta));
            _viewer.Renderer.Page = next;
            RefreshBar();
        }

        private void ShowError(string message)
        {
            _host.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = PreviewUi.Danger,
                Font = PreviewUi.FontUi,
                Text = message,
                Padding = new Padding(16)
            });
        }

        private void DisposeViewer()
        {
            if (_findDebounce != null)
            {
                _findDebounce.Stop();
                _findDebounce.Dispose();
                _findDebounce = null;
            }
            if (_viewer != null)
            {
                _host.Controls.Remove(_viewer);
                _viewer.Dispose();
                _viewer = null;
            }
            if (_textBox != null)
            {
                _host.Controls.Remove(_textBox);
                _textBox.Dispose();
                _textBox = null;
            }
            if (_findBar != null)
            {
                _host.Controls.Remove(_findBar);
                _findBar.Dispose();
                _findBar = null;
            }
            if (_chrome != null)
            {
                _host.Controls.Remove(_chrome);
                _chrome.Dispose();
                _chrome = null;
            }
            if (_doc != null)
            {
                _doc.Dispose();
                _doc = null;
            }
            _bar = null;
            _meta = null;
            _zoomLbl = null;
            _btnPrev = null;
            _btnNext = null;
            _btnZoomOut = null;
            _btnZoomIn = null;
            _btnText = null;
            _btnFind = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeViewer();
                _hotkeys?.Dispose();
                _hotkeys = null;
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }
    }
}
