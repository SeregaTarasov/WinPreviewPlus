using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using SharpShell.SharpPreviewHandler;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace WinPreviewPlus
{
    public class OfficePreviewControl : PreviewHandlerControl, IPreviewFindHost
    {
        private readonly Panel _host;
        private PreviewSource _source;

        private Panel _sheetBar;
        private Label _sheetName;
        private Label _sheetMeta;
        private Button _btnPrev;
        private Button _btnNext;
        private DataGridView _grid;
        private RichTextBox _docBox;
        private string _xlsxPath;
        private List<SheetInfo> _sheets;
        private int _activeSheet;
        private bool _fittingCols;
        /// <summary>colIndex → null/пусто = нет; иначе точное значение или "~текст" (содержит).</summary>
        private readonly Dictionary<int, string> _columnFilters = new Dictionary<int, string>();

        private PreviewFindBar _findBar;
        private Timer _findDebounce;
        private string _findQuery = "";
        private readonly List<int> _docMatchStarts = new List<int>();
        private readonly List<Point> _gridHits = new List<Point>(); // X=col, Y=row
        private int _matchIndex = -1;

        private sealed class SheetInfo
        {
            public string Name;
            public string RelId;
        }

        public OfficePreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _host = new Panel { Dock = DockStyle.Fill, BackColor = PreviewUi.Surface };
            PreviewUi.EnableDoubleBuffer(_host);
            Controls.Add(_host);
        }

        public void ShowFind()
        {
            EnsureFindBar();
            ShowFindBar(true);
        }

        public void FindNext(bool forward)
        {
            EnsureFindBar();
            if (_findBar == null || !_findBar.Visible || string.IsNullOrEmpty(_findQuery))
            {
                ShowFindBar(true);
                return;
            }
            StepMatch(forward);
        }

        public void LoadOffice(PreviewSource source)
        {
            DisposeFindUi();
            _source = source;
            _sheetBar = null;
            _sheetName = null;
            _sheetMeta = null;
            _btnPrev = null;
            _btnNext = null;
            _grid = null;
            _docBox = null;
            _host.Controls.Clear();
            _xlsxPath = null;
            _sheets = null;
            _activeSheet = 0;

            var ext = Path.GetExtension(source.Path).ToLowerInvariant();
            try
            {
                var kind = DetectOfficeKind(source.Path, ext);
                switch (kind)
                {
                    case "docx":
                        ShowDocx(source.Path);
                        break;
                    case "xlsx":
                        ShowXlsx(source.Path);
                        break;
                    case "pptx":
                        ShowPptx(source.Path);
                        break;
                    default:
                        ShowError("Неподдерживаемый тип: " + ext);
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть: " + ex.Message);
                PreviewLog.Error("Office preview failed: " + source.Path, ex);
            }
        }

        private void ShowError(string message)
        {
            var lbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = PreviewUi.Danger,
                Font = PreviewUi.FontUi,
                Text = message,
                Padding = new Padding(16)
            };
            _host.Controls.Add(lbl);
        }

        private static string DetectOfficeKind(string path, string ext)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read, true))
                {
                    if (zip.GetEntry("xl/workbook.xml") != null) return "xlsx";
                    if (zip.GetEntry("word/document.xml") != null) return "docx";
                    if (zip.GetEntry("ppt/presentation.xml") != null) return "pptx";
                }
            }
            catch { /* fall through */ }

            if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx")
                return ext.TrimStart('.');
            return null;
        }

        private void ShowDocx(string path)
        {
            var box = PreviewUi.CreateDocumentBox();
            var sb = new StringBuilder();
            int blocks = 0;
            using (var doc = WordprocessingDocument.Open(path, false))
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body != null)
                    AppendDocxBlocks(body, sb, ref blocks, maxBlocks: 4000);
            }

            box.Text = sb.ToString().TrimEnd();
            if (string.IsNullOrWhiteSpace(box.Text))
                box.Text = "(пустой документ)";
            AttachDocumentBox(box);
            PreviewLog.Info("DOCX preview OK: " + path + " blocks=" + blocks);
        }

        /// <summary>
        /// Не только Paragraph: карточки/реквизиты почти всегда в Table — раньше таблицы пропускались.
        /// </summary>
        private static void AppendDocxBlocks(OpenXmlElement container, StringBuilder sb, ref int blocks, int maxBlocks)
        {
            if (container == null || blocks >= maxBlocks) return;

            foreach (var el in container.Elements())
            {
                if (blocks >= maxBlocks) break;

                if (el is W.Paragraph para)
                {
                    AppendDocxParagraph(para, sb);
                    blocks++;
                }
                else if (el is W.Table table)
                {
                    AppendDocxTable(table, sb, ref blocks, maxBlocks);
                    sb.AppendLine();
                }
                else if (el is W.SectionProperties)
                {
                    // конец секции
                }
                else
                {
                    // SdtBlock / content controls / обёртки
                    AppendDocxBlocks(el, sb, ref blocks, maxBlocks);
                }
            }
        }

        private static void AppendDocxParagraph(W.Paragraph para, StringBuilder sb)
        {
            if (para == null) return;
            var line = new StringBuilder();
            foreach (var node in para.Descendants())
            {
                if (node is W.Text t)
                    line.Append(t.Text);
                else if (node is W.Break)
                    line.AppendLine();
                else if (node is W.TabChar)
                    line.Append('\t');
            }
            var s = line.ToString();
            if (string.IsNullOrWhiteSpace(s))
                sb.AppendLine();
            else
                sb.AppendLine(s.TrimEnd());
        }

        private static void AppendDocxTable(W.Table table, StringBuilder sb, ref int blocks, int maxBlocks)
        {
            if (table == null) return;
            foreach (var row in table.Elements<W.TableRow>())
            {
                if (blocks >= maxBlocks) break;
                var cells = new List<string>();
                foreach (var cell in row.Elements<W.TableCell>())
                {
                    var cellSb = new StringBuilder();
                    foreach (var p in cell.Elements<W.Paragraph>())
                    {
                        if (cellSb.Length > 0) cellSb.Append(' ');
                        var one = new StringBuilder();
                        foreach (var node in p.Descendants())
                        {
                            if (node is W.Text t)
                                one.Append(t.Text);
                            else if (node is W.TabChar)
                                one.Append(' ');
                        }
                        cellSb.Append(one.ToString().Trim());
                    }
                    foreach (var nested in cell.Elements<W.Table>())
                    {
                        if (cellSb.Length > 0) cellSb.Append(" | ");
                        foreach (var nr in nested.Elements<W.TableRow>())
                        {
                            var parts = nr.Elements<W.TableCell>()
                                .Select(c => string.Concat(c.Descendants<W.Text>().Select(x => x.Text)).Trim())
                                .Where(s => s.Length > 0);
                            cellSb.Append(string.Join(" / ", parts));
                        }
                    }
                    cells.Add(cellSb.ToString().Trim());
                }
                sb.AppendLine(string.Join("  ·  ", cells.Where(c => !string.IsNullOrWhiteSpace(c))));
                blocks++;
            }
        }

        private void ShowXlsx(string path)
        {
            _xlsxPath = path;

            _grid = new DataGridView { Dock = DockStyle.Fill };
            PreviewUi.StyleGrid(_grid);
            // Как Excel: ячейки + номера строк + буквы столбцов
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = true;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            _grid.RowHeadersVisible = true;
            _grid.RowHeadersWidth = 56;
            _grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            _grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.AdvancedRowHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;
            _grid.ShowRowErrors = false;
            _grid.ShowCellErrors = false;
            _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            _grid.RowHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = PreviewUi.HeaderBg,
                ForeColor = PreviewUi.TextMuted,
                Font = PreviewUi.FontMuted,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = PreviewUi.HeaderBg,
                SelectionForeColor = PreviewUi.TextMuted,
                Padding = new Padding(0)
            };
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.TopLeftHeaderCell.Style.BackColor = PreviewUi.HeaderBg;
            _grid.TopLeftHeaderCell.Style.SelectionBackColor = PreviewUi.HeaderBg;
            _grid.Resize -= GridOnResize;
            _grid.Resize += GridOnResize;
            _grid.KeyDown += GridOnKeyDown;
            _grid.ColumnHeaderMouseClick += GridOnColumnHeaderMouseClick;
            _grid.RowHeaderMouseClick += GridOnRowHeaderMouseClick;
            _grid.MouseDown += GridOnMouseDownExcel;
            _grid.CellMouseDown += GridOnCellMouseDown;
            _grid.RowPostPaint += GridOnRowPostPaint;
            _grid.CellPainting += GridOnHeaderCellPainting;
            AttachGridContextMenu(_grid);
            _host.Controls.Add(_grid);

            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var wb = doc.WorkbookPart;
                _sheets = wb?.Workbook?.Sheets?.Elements<Sheet>()
                    .Where(sh => sh.Id != null && sh.Name != null)
                    .Select(sh => new SheetInfo { Name = sh.Name.Value, RelId = sh.Id.Value })
                    .ToList() ?? new List<SheetInfo>();
            }

            if (_sheets.Count == 0)
            {
                ShowError("Пустая книга Excel");
                return;
            }

            if (_sheets.Count > 1)
                CreateSheetBar();

            FillSheetGrid(_sheets[0]);
            EnsureFindBar();
            PreviewLog.Info("XLSX preview OK: " + path + " sheets=" + _sheets.Count);
        }

        private void GridOnResize(object sender, EventArgs e)
        {
            if (_grid == null || _grid.Rows.Count == 0 || _fittingCols) return;
            FitGridColumns();
        }

        private void DisposeSheetBar()
        {
            if (_sheetBar == null) return;
            _host.Controls.Remove(_sheetBar);
            _sheetBar.Dispose();
            _sheetBar = null;
            _sheetName = null;
            _sheetMeta = null;
            _btnPrev = null;
            _btnNext = null;
        }

        /// <summary>Static top bar: ◀ name ▶ · n из N (PDF-like, always visible).</summary>
        private void CreateSheetBar()
        {
            DisposeSheetBar();

            _sheetBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = PreviewUi.HeaderBg,
                Padding = new Padding(4, 0, 8, 0)
            };
            PreviewUi.EnableDoubleBuffer(_sheetBar);
            var bottomLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = PreviewUi.Border
            };

            _btnPrev = MakeNavButton("‹");
            _btnPrev.Click += (s, e) => GoSheet(-1);

            _btnNext = MakeNavButton("›");
            _btnNext.Click += (s, e) => GoSheet(+1);

            _sheetName = new Label
            {
                AutoSize = false,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontTitle,
                ForeColor = PreviewUi.TextPrimary,
                Cursor = Cursors.Hand,
                Padding = new Padding(4, 0, 4, 0)
            };
            _sheetName.Click += (s, e) => ShowSheetPicker();

            // No Dock — width measured from text so "12 из 12" never clips
            _sheetMeta = new Label
            {
                AutoSize = false,
                Height = 28,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted,
                Padding = new Padding(4, 0, 2, 0)
            };

            var btnFind = PreviewFindBar.MakeToolbarButton();
            btnFind.Click += (s, e) => ShowFind();
            new ToolTip().SetToolTip(btnFind, "Найти (Ctrl+F)");

            _sheetBar.Controls.Add(bottomLine);
            _sheetBar.Controls.Add(_sheetMeta);
            _sheetBar.Controls.Add(_sheetName);
            _sheetBar.Controls.Add(_btnNext);
            _sheetBar.Controls.Add(_btnPrev);
            _sheetBar.Controls.Add(btnFind);
            _sheetBar.Tag = btnFind;
            _sheetBar.Resize += (s, e) => LayoutSheetBar();

            // Dock Top after Fill so bar sits above grid without overlay
            _host.Controls.Add(_sheetBar);
            RefreshSheetBar();
        }

        private static Button MakeNavButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                Width = 28,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(220, 224, 232);
            return btn;
        }

        private void LayoutSheetBar()
        {
            if (_sheetBar == null || _sheetMeta == null) return;
            const int btn = 28;
            int innerH = _sheetBar.ClientSize.Height - 1;
            int y = Math.Max(0, (innerH - btn) / 2);
            const int rightPad = 10;
            int metaW = Math.Max(56,
                TextRenderer.MeasureText(_sheetMeta.Text ?? "0 из 0", PreviewUi.FontMuted).Width + 12);
            _sheetMeta.SetBounds(
                _sheetBar.ClientSize.Width - metaW - rightPad,
                y,
                metaW,
                btn);

            _btnPrev.SetBounds(8, y, btn, btn);
            _btnNext.SetBounds(8 + btn + 4, y, btn, btn);
            int x = 8 + btn + 4 + btn + 4;
            var btnFind = _sheetBar.Tag as System.Windows.Forms.Control;
            if (btnFind != null)
            {
                int fw = Math.Max(btnFind.Width, 56);
                btnFind.SetBounds(x, y, fw, btn);
                x += fw + 8;
            }
            else x += 4;
            int nameRight = _sheetMeta.Left - 8;
            _sheetName.SetBounds(x, y, Math.Max(40, nameRight - x), btn);
        }

        private void RefreshSheetBar()
        {
            if (_sheets == null || _sheets.Count == 0) return;
            if (_sheetName != null)
                _sheetName.Text = _sheets[_activeSheet].Name;
            if (_sheetMeta != null)
                _sheetMeta.Text = (_activeSheet + 1) + " из " + _sheets.Count;
            if (_btnPrev != null) _btnPrev.Enabled = _activeSheet > 0;
            if (_btnNext != null) _btnNext.Enabled = _activeSheet < _sheets.Count - 1;
            LayoutSheetBar();
        }

        private void GoSheet(int delta)
        {
            if (_sheets == null || _sheets.Count == 0) return;
            int next = Math.Max(0, Math.Min(_sheets.Count - 1, _activeSheet + delta));
            if (next == _activeSheet) return;
            _activeSheet = next;
            FillSheetGrid(_sheets[_activeSheet]);
            RefreshSheetBar();
        }

        private void ShowSheetPicker()
        {
            if (_sheets == null || _sheetBar == null) return;
            var menu = new ContextMenuStrip();
            for (int i = 0; i < _sheets.Count; i++)
            {
                var idx = i;
                var item = new ToolStripMenuItem(_sheets[i].Name)
                {
                    Checked = i == _activeSheet
                };
                item.Click += (s, e) =>
                {
                    if (_activeSheet == idx) return;
                    _activeSheet = idx;
                    FillSheetGrid(_sheets[_activeSheet]);
                    RefreshSheetBar();
                };
                menu.Items.Add(item);
            }
            menu.Show(_sheetName, new Point(0, _sheetName.Height));
        }

        private void FillSheetGrid(SheetInfo sheet)
        {
            if (_grid == null || string.IsNullOrEmpty(_xlsxPath) || sheet == null)
                return;

            _columnFilters.Clear();
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            _grid.Columns.Clear();

            try
            {
                using (var doc = SpreadsheetDocument.Open(_xlsxPath, false))
                {
                    var wb = doc.WorkbookPart;
                    var shared = wb.SharedStringTablePart?.SharedStringTable;
                    var styles = wb.WorkbookStylesPart;
                    var part = (WorksheetPart)wb.GetPartById(sheet.RelId);
                    var rows = part.Worksheet.Descendants<Row>().Take(500).ToList();

                    int maxCol = 0;
                    var table = new List<string[]>();
                    var excelRowNums = new List<int>();
                    foreach (var row in rows)
                    {
                        int excelNum = row.RowIndex != null
                            ? (int)row.RowIndex.Value
                            : (excelRowNums.Count + 1);
                        var map = new Dictionary<int, string>();
                        foreach (var cell in row.Elements<Cell>())
                        {
                            int col = GetColumnIndex(cell.CellReference?.Value) ?? map.Count;
                            map[col] = GetCellText(cell, shared, styles);
                            if (col + 1 > maxCol) maxCol = col + 1;
                        }
                        if (map.Count == 0)
                        {
                            table.Add(new[] { "" });
                            excelRowNums.Add(excelNum);
                            maxCol = Math.Max(maxCol, 1);
                            continue;
                        }
                        var values = new string[maxCol];
                        for (int i = 0; i < maxCol; i++)
                            values[i] = map.ContainsKey(i) ? map[i] : "";
                        table.Add(values);
                        excelRowNums.Add(excelNum);
                    }

                    for (int r = 0; r < table.Count; r++)
                    {
                        if (table[r].Length >= maxCol) continue;
                        var enlarged = new string[maxCol];
                        Array.Copy(table[r], enlarged, table[r].Length);
                        for (int i = table[r].Length; i < maxCol; i++)
                            enlarged[i] = "";
                        table[r] = enlarged;
                    }

                    while (maxCol > 1)
                    {
                        bool any = false;
                        for (int r = 0; r < table.Count; r++)
                        {
                            if (!string.IsNullOrWhiteSpace(table[r][maxCol - 1]))
                            {
                                any = true;
                                break;
                            }
                        }
                        if (any) break;
                        maxCol--;
                    }

                    int colCount = Math.Min(Math.Max(maxCol, 1), 40);
                    // Заголовки как в Excel: A B C…; первая строка листа остаётся данными
                    for (int c = 0; c < colCount; c++)
                        AddGridColumn(ColumnLetter(c));

                    for (int r = 0; r < table.Count; r++)
                    {
                        bool empty = true;
                        for (int c = 0; c < colCount && c < table[r].Length; c++)
                        {
                            if (!string.IsNullOrWhiteSpace(table[r][c])) { empty = false; break; }
                        }
                        if (empty) continue;

                        var obj = new object[colCount];
                        for (int c = 0; c < colCount; c++)
                            obj[c] = c < table[r].Length ? table[r][c] : "";
                        int idx = _grid.Rows.Add(obj);
                        int num = r < excelRowNums.Count ? excelRowNums[r] : (r + 1);
                        // Номер рисуем в RowPostPaint — HeaderCell.Value в prevhost даёт «.» вместо цифр
                        _grid.Rows[idx].HeaderCell.Value = null;
                        _grid.Rows[idx].Tag = num;
                    }

                    RefreshColumnHeaderTexts();
                    ScheduleFitGridColumns();
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("XLSX sheet failed: " + sheet.Name, ex);
            }
            finally
            {
                _grid.ResumeLayout();
            }

            // новый лист — пересчитать поиск, если панель открыта
            if (_findBar != null && _findBar.Visible)
                RebuildMatches(resetIndex: true);
            else
            {
                _docMatchStarts.Clear();
                _gridHits.Clear();
                _matchIndex = -1;
            }
        }

        private void ScheduleFitGridColumns()
        {
            if (_grid == null) return;
            if (_grid.IsHandleCreated)
            {
                _grid.BeginInvoke(new Action(FitGridColumns));
            }
            else
            {
                EventHandler onCreated = null;
                onCreated = (s, e) =>
                {
                    _grid.HandleCreated -= onCreated;
                    _grid.BeginInvoke(new Action(FitGridColumns));
                };
                _grid.HandleCreated += onCreated;
            }
        }

        private void AddGridColumn(string title)
        {
            _grid.Columns.Add(PreviewUi.MakeColumn("c" + _grid.Columns.Count, title, 80));
        }

        /// <summary>
        /// Preferred widths from content; date/number stay compact; widest text takes leftover.
        /// Never shrink below content (HScroll if pane too narrow).
        /// </summary>
        private void FitGridColumns()
        {
            if (_grid == null || _grid.Columns.Count == 0 || _grid.Rows.Count == 0) return;
            if (_fittingCols) return;
            if (_grid.ClientSize.Width < 40) return;

            _fittingCols = true;
            try
            {
                int n = _grid.Columns.Count;
                var preferred = new int[n];
                var kind = new char[n]; // 'd' date, 'n' number, 't' text
                var font = _grid.DefaultCellStyle.Font ?? PreviewUi.FontUi;
                var headerFont = _grid.ColumnHeadersDefaultCellStyle.Font ?? PreviewUi.FontMuted;
                // Full date string must fit: "dd.MM.yyyy"
                // ~10% air around measured content (was too tight)
                const double pad = 1.10;
                int dateFloor = (int)Math.Ceiling(
                    (TextRenderer.MeasureText("00.00.0000", font).Width + 16) * pad);

                for (int c = 0; c < n; c++)
                {
                    string header = _grid.Columns[c].HeaderText ?? "";
                    // Заголовок — жёсткий минимум: числовые колонки раньше ужимались до 100px → «1 Б...»
                    int headerW = TextRenderer.MeasureText(header, headerFont).Width
                                  + PreviewUi.CellPadH * 2 + 16;
                    int w = headerW;
                    int sample = 0, textScore = 0, dateHits = 0, numHits = 0, nonEmpty = 0;

                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (sample++ > 120) break;
                        var val = row.Cells[c].Value as string ?? row.Cells[c].Value?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        nonEmpty++;
                        string measure = val.Length > 56 ? val.Substring(0, 56) + "…" : val;
                        w = Math.Max(w, TextRenderer.MeasureText(measure, font).Width + 16);
                        textScore += Math.Min(val.Length, 48);
                        if (LooksLikeDate(val)) dateHits++;
                        else if (LooksLikeNumber(val)) numHits++;
                    }

                    w = (int)Math.Ceiling(w * pad);

                    string h = header.ToLowerInvariant();
                    bool headerDate = h.Contains("дата") || h.Contains("date") || h == "число";
                    bool headerNum = h.Contains("сумм") || h.Contains("amount") ||
                                     h.Contains("кол") || h.Contains("шт");

                    if (headerDate || (nonEmpty > 0 && dateHits * 2 >= nonEmpty))
                    {
                        kind[c] = 'd';
                        // дата компактная, но не уже заголовка
                        preferred[c] = Math.Max(headerW, Math.Max(dateFloor, Math.Min(w, dateFloor + 24)));
                    }
                    else if (headerNum || (nonEmpty > 0 && numHits * 2 >= nonEmpty))
                    {
                        kind[c] = 'n';
                        // числа можно ограничить по ячейкам, заголовок — нет
                        int body = Math.Max(52, Math.Min(w, 140));
                        preferred[c] = Math.Max(headerW, body);
                        _grid.Columns[c].DefaultCellStyle.Alignment =
                            DataGridViewContentAlignment.MiddleRight;
                    }
                    else
                    {
                        kind[c] = 't';
                        preferred[c] = Math.Max(headerW, Math.Max(56, Math.Min(w, 360)));
                    }

                    _grid.Columns[c].Tag = textScore;
                    if (kind[c] != 't')
                        _grid.Columns[c].Tag = Math.Max(1, textScore / 5);
                }

                // Растягиваем последнее поле до правого края; остальные — по содержимому
                int last = n - 1;
                int avail = Math.Max(60, _grid.DisplayRectangle.Width);
                int others = 0;
                for (int c = 0; c < last; c++)
                    others += preferred[c];
                preferred[last] = Math.Max(preferred[last], avail - others);

                for (int c = 0; c < n; c++)
                {
                    _grid.Columns[c].MinimumWidth = kind[c] == 'd' ? dateFloor : 36;
                    _grid.Columns[c].Width = preferred[c];
                    _grid.Columns[c].Tag = null;
                }
            }
            finally
            {
                _fittingCols = false;
            }
        }

        private static bool LooksLikeDate(string s)
        {
            if (s == null) return false;
            // "03.01.2025" or leading date in "07.01.2025 РОЖДЕСТВО"
            string head = s.Length >= 10 ? s.Substring(0, 10) : s;
            DateTime dt;
            if (DateTime.TryParseExact(head, "dd.MM.yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt))
                return true;
            if (s.Length <= 19 && DateTime.TryParseExact(s,
                    new[] { "dd.MM.yyyy HH:mm", "yyyy-MM-dd", "dd/MM/yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt))
                return true;
            return false;
        }

        private static bool LooksLikeNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 14) return false;
            double d;
            return double.TryParse(s.Replace('\u00A0', ' ').Replace(" ", "").Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out d);
        }

        private static int? GetColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return null;
            int i = 0;
            int col = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            {
                col = col * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
                i++;
            }
            return col > 0 ? col - 1 : (int?)null;
        }

        private void ShowPptx(string path)
        {
            var box = PreviewUi.CreateDocumentBox();
            var sb = new StringBuilder();
            int n = 0;
            using (var doc = PresentationDocument.Open(path, false))
            {
                var pres = doc.PresentationPart;
                var ids = pres?.Presentation?.SlideIdList?.Elements<SlideId>().ToList()
                          ?? new List<SlideId>();
                foreach (var id in ids.Take(100))
                {
                    n++;
                    sb.AppendLine("── Слайд " + n + " ──");
                    var part = (SlidePart)pres.GetPartById(id.RelationshipId);
                    foreach (var text in part.Slide.Descendants<A.Text>().Select(t => t.Text))
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                            sb.AppendLine(text);
                    }

                    var notes = part.NotesSlidePart?.NotesSlide;
                    if (notes != null)
                    {
                        var noteTexts = notes.Descendants<A.Text>()
                            .Select(t => t.Text)
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList();
                        if (noteTexts.Count > 0)
                        {
                            sb.AppendLine("Заметки:");
                            foreach (var t in noteTexts)
                                sb.AppendLine("  · " + t);
                        }
                    }
                    sb.AppendLine();
                }
            }

            box.Text = sb.ToString();
            AttachDocumentBox(box);
            PreviewLog.Info("PPTX preview OK: " + path);
        }

        private void AttachDocumentBox(RichTextBox box)
        {
            _docBox = box;
            box.HideSelection = false;
            box.DetectUrls = false;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Найти…  Ctrl+F", null, (s, e) => ShowFind());
            menu.Items.Add("Далее  F3", null, (s, e) => FindNext(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Копировать", null, (s, e) => TryCopySelection());
            menu.Items.Add("Выделить всё", null, (s, e) =>
            {
                box.SelectAll();
                box.Focus();
            });
            box.ContextMenuStrip = menu;
            box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.F)
                {
                    ShowFind();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.F3)
                {
                    FindNext(forward: !e.Shift);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.C)
                {
                    TryCopySelection();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    box.SelectAll();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            _host.Controls.Add(box);
            EnsureFindBar();
        }

        private void AttachGridContextMenu(DataGridView grid)
        {
            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) =>
            {
                var pos = grid.PointToClient(System.Windows.Forms.Control.MousePosition);
                var hit = grid.HitTest(pos.X, pos.Y);
                if (hit.Type == DataGridViewHitTestType.ColumnHeader
                    || hit.Type == DataGridViewHitTestType.RowHeader
                    || hit.Type == DataGridViewHitTestType.TopLeftHeader)
                {
                    e.Cancel = true;
                    return;
                }
                bool has = grid.GetCellCount(DataGridViewElementStates.Selected) > 0;
                menu.Items[0].Enabled = has;
                menu.Items[1].Enabled = has;
            };
            menu.Items.Add("Найти…  Ctrl+F", null, (s, e) => ShowFind());
            menu.Items.Add("Далее  F3", null, (s, e) => FindNext(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Копировать", null, (s, e) => CopyGridSelection(withHeaders: false));
            menu.Items.Add("Копировать с заголовками", null, (s, e) => CopyGridSelection(withHeaders: true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выделить всё", null, (s, e) =>
            {
                grid.SelectAll();
                grid.Focus();
            });
            menu.Items.Add("Сбросить все фильтры", null, (s, e) => ClearAllFilters());
            grid.ContextMenuStrip = menu;
        }

        private void GridOnMouseDownExcel(object sender, MouseEventArgs e)
        {
            if (_grid == null || e.Button != MouseButtons.Left) return;
            var hit = _grid.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.TopLeftHeader)
            {
                if (!_grid.Focused) _grid.Focus();
                _grid.SelectAll();
            }
            else if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0)
            {
                // Первый клик по номеру строки (пока грид без фокуса)
                if (!_grid.Focused) _grid.Focus();
                SelectEntireRow(hit.RowIndex);
            }
            else if (hit.Type == DataGridViewHitTestType.ColumnHeader && hit.ColumnIndex >= 0)
            {
                if (!_grid.Focused) _grid.Focus();
                SelectEntireColumn(hit.ColumnIndex);
            }
        }

        /// <summary>
        /// В prevhost первый клик часто только даёт фокус — принудительно выделяем ячейку.
        /// </summary>
        private void GridOnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_grid == null || e.Button != MouseButtons.Left) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var mods = System.Windows.Forms.Control.ModifierKeys;
            if ((mods & Keys.Control) != 0 || (mods & Keys.Shift) != 0) return;

            try
            {
                if (!_grid.Focused) _grid.Focus();
                var cell = _grid[e.ColumnIndex, e.RowIndex];
                if (_grid.CurrentCell == cell && cell.Selected) return;
                _grid.ClearSelection();
                cell.Selected = true;
                _grid.CurrentCell = cell;
            }
            catch { /* ignore */ }
        }

        private void GridOnRowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            if (_grid == null || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[e.RowIndex];
            string text = row.Tag is int n ? n.ToString() : (e.RowIndex + 1).ToString();

            var style = _grid.RowHeadersDefaultCellStyle;
            var font = style.Font ?? PreviewUi.FontMuted;
            var fore = style.ForeColor.IsEmpty ? PreviewUi.TextMuted : style.ForeColor;
            var bounds = new Rectangle(
                e.RowBounds.Left,
                e.RowBounds.Top,
                _grid.RowHeadersWidth,
                e.RowBounds.Height);

            // Фон поверх системного «.» / треугольника
            using (var bg = new SolidBrush(style.BackColor.IsEmpty ? PreviewUi.HeaderBg : style.BackColor))
                e.Graphics.FillRectangle(bg, bounds);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                font,
                new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height),
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // Сетка как у ячеек (FillRectangle иначе съедает системные линии)
            var gridColor = _grid.GridColor.IsEmpty ? PreviewUi.Border : _grid.GridColor;
            using (var pen = new Pen(gridColor))
            {
                int x2 = bounds.Right - 1;
                int y2 = bounds.Bottom - 1;
                e.Graphics.DrawLine(pen, bounds.Left, y2, x2, y2);
                e.Graphics.DrawLine(pen, x2, bounds.Top, x2, y2);
            }
        }

        /// <summary>Угол и заголовки столбцов — те же линии сетки.</summary>
        private void GridOnHeaderCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (_grid == null) return;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) return; // обычные ячейки — стандарт

            e.PaintBackground(e.CellBounds, false);
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
                e.PaintContent(e.CellBounds);

            var gridColor = _grid.GridColor.IsEmpty ? PreviewUi.Border : _grid.GridColor;
            using (var pen = new Pen(gridColor))
            {
                var r = e.CellBounds;
                int x2 = r.Right - 1;
                int y2 = r.Bottom - 1;
                e.Graphics.DrawLine(pen, r.Left, y2, x2, y2);
                e.Graphics.DrawLine(pen, x2, r.Top, x2, y2);
                if (e.ColumnIndex < 0)
                    e.Graphics.DrawLine(pen, r.Left, r.Top, x2, r.Top);
            }
            e.Handled = true;
        }

        private void GridOnColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_grid == null || e.ColumnIndex < 0) return;
            if (e.Button == MouseButtons.Right)
            {
                ShowColumnFilterMenu(e.ColumnIndex);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (!_grid.Focused) _grid.Focus();
            SelectEntireColumn(e.ColumnIndex);
        }

        private void GridOnRowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_grid == null || e.RowIndex < 0) return;
            if (e.Button != MouseButtons.Left) return;
            if (!_grid.Focused) _grid.Focus();
            SelectEntireRow(e.RowIndex);
        }

        private void SelectEntireColumn(int col)
        {
            _grid.ClearSelection();
            DataGridViewCell first = null;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!row.Visible || row.IsNewRow) continue;
                if (col >= row.Cells.Count) continue;
                row.Cells[col].Selected = true;
                if (first == null) first = row.Cells[col];
            }
            try { if (first != null) _grid.CurrentCell = first; }
            catch { /* ignore */ }
            _grid.Focus();
        }

        private void SelectEntireRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex];
            if (!row.Visible) return;
            _grid.ClearSelection();
            DataGridViewCell first = null;
            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.Selected = true;
                if (first == null) first = cell;
            }
            try { if (first != null) _grid.CurrentCell = first; }
            catch { /* ignore */ }
            _grid.Focus();
        }

        private void ShowColumnFilterMenu(int col)
        {
            if (_grid == null || col < 0 || col >= _grid.Columns.Count) return;

            var menu = new ContextMenuStrip();
            string letter = ColumnLetter(col);
            string active = null;
            _columnFilters.TryGetValue(col, out active);

            var clear = new ToolStripMenuItem("Сбросить фильтр")
            {
                Enabled = !string.IsNullOrEmpty(active)
            };
            clear.Click += (s, e) => SetColumnFilter(col, null);
            menu.Items.Add(clear);
            menu.Items.Add(new ToolStripSeparator());

            var contains = new ToolStripMenuItem("Содержит…");
            contains.Click += (s, e) =>
            {
                string seed = active != null && active.StartsWith("~", StringComparison.Ordinal)
                    ? active.Substring(1) : "";
                string input = PromptText("Фильтр столбца " + letter, "Текст содержится в ячейке:", seed);
                if (input != null)
                    SetColumnFilter(col, string.IsNullOrEmpty(input) ? null : "~" + input);
            };
            menu.Items.Add(contains);
            menu.Items.Add(new ToolStripSeparator());

            var uniques = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow || col >= row.Cells.Count) continue;
                var v = Convert.ToString(row.Cells[col].Value) ?? "";
                if (v.Length > 80) v = v.Substring(0, 80) + "…";
                uniques.Add(v);
                if (uniques.Count >= 40) break;
            }

            foreach (var v in uniques)
            {
                string label = string.IsNullOrEmpty(v) ? "(пусто)" : v;
                var item = new ToolStripMenuItem(label)
                {
                    Checked = string.Equals(active, v, StringComparison.Ordinal)
                };
                string capture = v;
                item.Click += (s, e) => SetColumnFilter(col, capture);
                menu.Items.Add(item);
            }

            var headerRect = _grid.GetCellDisplayRectangle(col, -1, true);
            menu.Show(_grid, new Point(headerRect.Left, headerRect.Bottom));
        }

        private void SetColumnFilter(int col, string value)
        {
            if (string.IsNullOrEmpty(value))
                _columnFilters.Remove(col);
            else
                _columnFilters[col] = value;
            ApplyColumnFilters();
            RefreshColumnHeaderTexts();
        }

        private void ClearAllFilters()
        {
            if (_columnFilters.Count == 0) return;
            _columnFilters.Clear();
            ApplyColumnFilters();
            RefreshColumnHeaderTexts();
        }

        private void ApplyColumnFilters()
        {
            if (_grid == null) return;
            _grid.SuspendLayout();
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    bool show = true;
                    foreach (var kv in _columnFilters)
                    {
                        int c = kv.Key;
                        if (c < 0 || c >= row.Cells.Count) continue;
                        string cell = Convert.ToString(row.Cells[c].Value) ?? "";
                        string f = kv.Value ?? "";
                        if (f.StartsWith("~", StringComparison.Ordinal))
                        {
                            string needle = f.Substring(1);
                            if (cell.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0)
                            {
                                show = false;
                                break;
                            }
                        }
                        else if (!string.Equals(cell, f, StringComparison.CurrentCultureIgnoreCase))
                        {
                            show = false;
                            break;
                        }
                    }
                    row.Visible = show;
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }

            if (_findBar != null && _findBar.Visible && !string.IsNullOrEmpty(_findQuery))
                RebuildMatches(resetIndex: true);
        }

        private void RefreshColumnHeaderTexts()
        {
            if (_grid == null) return;
            for (int c = 0; c < _grid.Columns.Count; c++)
            {
                string letter = ColumnLetter(c);
                string f;
                if (_columnFilters.TryGetValue(c, out f) && !string.IsNullOrEmpty(f))
                    _grid.Columns[c].HeaderText = letter + " ▼";
                else
                    _grid.Columns[c].HeaderText = letter;
            }
        }

        private static string PromptText(string title, string prompt, string seed)
        {
            using (var form = new Form())
            using (var lbl = new Label())
            using (var box = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(360, 120);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;

                lbl.Text = prompt;
                lbl.SetBounds(12, 12, 336, 20);
                box.Text = seed ?? "";
                box.SetBounds(12, 36, 336, 24);
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(172, 76, 85, 28);
                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(263, 76, 85, 28);

                form.Controls.Add(lbl);
                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK ? box.Text : null;
            }
        }

        private void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                ShowFind();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F3)
            {
                FindNext(forward: !e.Shift);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyGridSelection(withHeaders: false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                _grid?.SelectAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                ShowFind();
                return true;
            }
            if (keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3))
            {
                FindNext(forward: keyData == Keys.F3);
                return true;
            }
            if (keyData == Keys.Escape && _findBar != null && _findBar.Visible)
            {
                ShowFindBar(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                if (TryCopySelection())
                    return true;
            }
            if (keyData == (Keys.Control | Keys.A))
            {
                if (_findBar?.Box != null && _findBar.Box.Focused)
                {
                    _findBar.Box.SelectAll();
                    return true;
                }
                if (_grid != null && _grid.Visible)
                {
                    _grid.SelectAll();
                    return true;
                }
                if (_docBox != null && _docBox.Visible)
                {
                    _docBox.SelectAll();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void EnsureFindBar()
        {
            if (_findDebounce == null)
            {
                _findDebounce = new Timer { Interval = 160 };
                _findDebounce.Tick += (s, e) =>
                {
                    _findDebounce.Stop();
                    RebuildMatches(resetIndex: true);
                };
            }

            if (_findBar != null && !_findBar.IsDisposed)
            {
                if (_findBar.Parent != _host)
                {
                    try { _host.Controls.Add(_findBar); } catch { }
                }
                try { _findBar.BringToFront(); } catch { }
                return;
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
            _host.Controls.Add(_findBar);
            _findBar.BringToFront();
        }

        private void ShowFindBar(bool show)
        {
            EnsureFindBar();
            if (_findBar == null) return;
            if (show)
            {
                _findBar.ShowBar(true, _host);
                PreviewLog.Info("Office ShowFindBar");
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(_findBar.Box.Text))
                                RebuildMatches(resetIndex: true);
                        }
                        catch { }
                    }));
                }
                catch { }
            }
            else
            {
                if (_findDebounce != null) _findDebounce.Stop();
                _findBar.ShowBar(false);
            }
        }

        private void FindBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StepMatch(forward: !e.Shift);
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowFindBar(false);
                return;
            }
            if (e.KeyCode == Keys.F3)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StepMatch(forward: !e.Shift);
            }
        }
        private void RebuildMatches(bool resetIndex)
        {
            if (_findBar?.Box == null) return;
            string q = _findBar.Box.Text ?? "";
            _findQuery = q;
            _docMatchStarts.Clear();
            _gridHits.Clear();
            _matchIndex = -1;

            if (q.Length == 0)
            {
                if (_findBar.Meta != null) _findBar.Meta.Text = "";
                return;
            }

            if (_grid != null && _grid.Visible)
                RebuildGridMatches(q);
            else if (_docBox != null && _docBox.Visible)
                RebuildDocMatches(q);
            else
            {
                if (_findBar.Meta != null) _findBar.Meta.Text = "";
                return;
            }

            int count = _gridHits.Count > 0 ? _gridHits.Count : _docMatchStarts.Count;
            if (count == 0)
            {
                if (_findBar.Meta != null) _findBar.Meta.Text = "нет совпадений";
                return;
            }

            if (resetIndex)
                _matchIndex = PickStartIndex();
            else if (_matchIndex < 0 || _matchIndex >= count)
                _matchIndex = 0;

            SelectCurrentMatch();
        }

        private int PickStartIndex()
        {
            if (_gridHits.Count > 0 && _grid != null)
            {
                int r = _grid.CurrentCell?.RowIndex ?? 0;
                int c = _grid.CurrentCell?.ColumnIndex ?? 0;
                for (int i = 0; i < _gridHits.Count; i++)
                {
                    var h = _gridHits[i];
                    if (h.Y > r || (h.Y == r && h.X >= c))
                        return i;
                }
                return 0;
            }
            if (_docMatchStarts.Count > 0 && _docBox != null)
            {
                int caret = _docBox.SelectionStart;
                for (int i = 0; i < _docMatchStarts.Count; i++)
                {
                    if (_docMatchStarts[i] >= caret)
                        return i;
                }
                return 0;
            }
            return 0;
        }

        private void RebuildDocMatches(string q)
        {
            string hay = _docBox.Text ?? "";
            int start = 0;
            while (start < hay.Length)
            {
                int i = hay.IndexOf(q, start, StringComparison.CurrentCultureIgnoreCase);
                if (i < 0) break;
                _docMatchStarts.Add(i);
                start = i + Math.Max(1, q.Length);
                if (_docMatchStarts.Count >= 20000) break;
            }
        }

        private void RebuildGridMatches(string q)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow || !row.Visible) continue;
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    string cell = Convert.ToString(row.Cells[c].Value) ?? "";
                    if (cell.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        _gridHits.Add(new Point(c, row.Index));
                        if (_gridHits.Count >= 20000) return;
                    }
                }
            }
        }

        private void StepMatch(bool forward)
        {
            if (_findBar?.Box != null &&
                !string.Equals(_findQuery, _findBar.Box.Text ?? "", StringComparison.Ordinal))
            {
                RebuildMatches(resetIndex: true);
                return;
            }
            int count = _gridHits.Count > 0 ? _gridHits.Count : _docMatchStarts.Count;
            if (count == 0)
            {
                RebuildMatches(resetIndex: true);
                return;
            }
            if (forward)
                _matchIndex = (_matchIndex + 1) % count;
            else
                _matchIndex = (_matchIndex - 1 + count) % count;
            SelectCurrentMatch();
        }

        private void SelectCurrentMatch()
        {
            try
            {
                if (_gridHits.Count > 0 && _grid != null)
                {
                    if (_matchIndex < 0 || _matchIndex >= _gridHits.Count) return;
                    var h = _gridHits[_matchIndex];
                    if (h.Y < 0 || h.Y >= _grid.Rows.Count) return;
                    if (!_grid.Rows[h.Y].Visible)
                    {
                        // строка скрыта фильтром — пересобрать
                        RebuildMatches(resetIndex: true);
                        return;
                    }
                    _grid.ClearSelection();
                    var cell = _grid[h.X, h.Y];
                    cell.Selected = true;
                    _grid.CurrentCell = cell;
                    try { _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, h.Y); } catch { }
                    if (_findBar.Meta != null)
                        _findBar.Meta.Text = (_matchIndex + 1) + " из " + _gridHits.Count;
                    return;
                }

                if (_docMatchStarts.Count > 0 && _docBox != null)
                {
                    if (_matchIndex < 0 || _matchIndex >= _docMatchStarts.Count) return;
                    int start = _docMatchStarts[_matchIndex];
                    int len = (_findQuery ?? "").Length;
                    _docBox.Select(start, len);
                    _docBox.ScrollToCaret();
                    if (_findBar.Meta != null)
                        _findBar.Meta.Text = (_matchIndex + 1) + " из " + _docMatchStarts.Count;
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Office SelectCurrentMatch", ex);
            }
        }

        private void DisposeFindUi()
        {
            if (_findDebounce != null)
            {
                _findDebounce.Stop();
                _findDebounce.Dispose();
                _findDebounce = null;
            }
            _findBar = null;
            _docMatchStarts.Clear();
            _gridHits.Clear();
            _matchIndex = -1;
            _findQuery = "";
        }

        /// <summary>Для PreviewAccel Ctrl+C в prevhost.</summary>
        public bool TryCopySelection()
        {
            try
            {
                if (_grid != null && _grid.Visible &&
                    _grid.GetCellCount(DataGridViewElementStates.Selected) > 0)
                {
                    CopyGridSelection(withHeaders: false);
                    return true;
                }
                if (_docBox != null && _docBox.Visible)
                {
                    if (_docBox.SelectionLength > 0)
                    {
                        Clipboard.SetText(_docBox.SelectedText);
                        return true;
                    }
                    if (!string.IsNullOrEmpty(_docBox.Text))
                    {
                        Clipboard.SetText(_docBox.Text);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Office TryCopySelection", ex);
            }
            return false;
        }

        private void CopyGridSelection(bool withHeaders)
        {
            if (_grid == null) return;
            try
            {
                if (_grid.GetCellCount(DataGridViewElementStates.Selected) == 0)
                    return;

                var prev = _grid.ClipboardCopyMode;
                _grid.ClipboardCopyMode = withHeaders
                    ? DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
                    : DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
                var data = _grid.GetClipboardContent();
                _grid.ClipboardCopyMode = prev;
                if (data != null)
                    Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Office CopyGridSelection", ex);
            }
        }

        private static string GetCellText(Cell cell, SharedStringTable shared, WorkbookStylesPart styles)
        {
            if (cell == null) return "";
            var value = cell.CellValue?.InnerText ?? "";
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString && shared != null)
            {
                if (int.TryParse(value, out var idx))
                {
                    var item = shared.ElementAt(idx);
                    return item?.InnerText ?? value;
                }
            }

            if (cell.DataType == null || cell.DataType.Value == CellValues.Number)
            {
                if (IsDateCell(cell, styles) &&
                    double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var oa) &&
                    oa > 200 && oa < 60000)
                {
                    try { return DateTime.FromOADate(oa).ToString("dd.MM.yyyy"); }
                    catch { /* keep raw */ }
                }
            }

            return value;
        }

        private static bool IsDateCell(Cell cell, WorkbookStylesPart styles)
        {
            if (styles?.Stylesheet?.CellFormats == null || cell.StyleIndex == null)
                return false;
            try
            {
                var idx = (int)cell.StyleIndex.Value;
                var formats = styles.Stylesheet.CellFormats.Elements<CellFormat>().ToList();
                if (idx < 0 || idx >= formats.Count) return false;
                var nfId = formats[idx].NumberFormatId?.Value ?? 0;
                if ((nfId >= 14 && nfId <= 22) || (nfId >= 45 && nfId <= 47))
                    return true;
                if (styles.Stylesheet.NumberingFormats == null) return false;
                var custom = styles.Stylesheet.NumberingFormats
                    .Elements<DocumentFormat.OpenXml.Spreadsheet.NumberingFormat>()
                    .FirstOrDefault(n => n.NumberFormatId != null && n.NumberFormatId.Value == nfId);
                string code = custom?.FormatCode?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(code)) return false;
                string lower = code.ToLowerInvariant();
                bool looksDate = lower.IndexOf('y') >= 0 || lower.IndexOf('d') >= 0 ||
                                 lower.Contains("mm") || lower.Contains("m/");
                return looksDate && lower.IndexOf('#') < 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ColumnLetter(int index)
        {
            var s = "";
            index++;
            while (index > 0)
            {
                index--;
                s = (char)('A' + (index % 26)) + s;
                index /= 26;
            }
            return s;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeFindUi();
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }
    }
}
