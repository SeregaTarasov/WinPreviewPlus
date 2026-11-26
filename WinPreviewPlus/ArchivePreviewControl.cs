using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SharpCompress.Archives;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    public class ArchivePreviewControl : PreviewHandlerControl
    {
        private readonly DataGridView _grid;
        private readonly Panel _nav;
        private readonly Button _btnUp;
        private readonly Label _pathLbl;
        private PreviewSource _source;
        private PreviewLiveSync _live;
        private Point _dragStart;
        private bool _dragPending;
        private bool _dragging;
        private string[] _dragCacheFiles;
        private string _dragCacheKey;
        private string _cwd = "";
        private List<RawEntry> _all;

        private sealed class RawEntry
        {
            public string Key;
            public bool IsDir;
            public long Size;
            public DateTime? Date;
        }

        public ArchivePreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            AllowDrop = false;

            _nav = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = PreviewUi.HeaderBg,
                Padding = new Padding(6, 4, 6, 4)
            };
            PreviewUi.EnableDoubleBuffer(_nav);
            _btnUp = new Button
            {
                Text = "↑",
                FlatStyle = FlatStyle.Flat,
                Font = PreviewUi.FontUi,
                Width = 32,
                Height = 26,
                Dock = DockStyle.Left,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _btnUp.FlatAppearance.BorderSize = 0;
            _btnUp.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            _btnUp.Click += (s, e) => TryGoUp();
            new ToolTip().SetToolTip(_btnUp, "На уровень выше (Backspace)");
            _pathLbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 0, 0)
            };
            _nav.Controls.Add(_pathLbl);
            _nav.Controls.Add(_btnUp);

            _grid = new DataGridView { Dock = DockStyle.Fill };
            PreviewUi.StyleGrid(_grid);
            _grid.MultiSelect = true;
            _grid.Columns.Add(PreviewUi.MakeColumn("name", "Имя", 200));
            _grid.Columns.Add(PreviewUi.MakeColumn("size", "Размер", 78,
                DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(PreviewUi.MakeColumn("date", "Дата", 118));
            _grid.Resize += (s, e) => ResizeColumns();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };
            _grid.KeyDown += GridOnKeyDown;
            _grid.MouseDown += GridOnMouseDown;
            _grid.MouseMove += GridOnMouseMove;
            _grid.MouseUp += (s, e) => { _dragPending = false; };

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Открыть", null, (s, e) => OpenSelected());
            ctx.Items.Add("Извлечь…", null, (s, e) => ExtractSelected());
            ctx.Items.Add("Извлечь всё…", null, (s, e) => ExtractAll());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Копировать файлы", null, (s, e) => BeginFileDragOrCopy(copyOnly: true));
            ctx.Items.Add("В проводнике", null, (s, e) =>
                ArchiveActions.RevealInExplorer(_source?.OriginalPath ?? _source?.Path));
            ctx.Items.Add("Копировать путь", null, (s, e) => CopySelectedPath());
            _grid.ContextMenuStrip = ctx;

            Controls.Add(_grid);
            Controls.Add(_nav);
        }

        public bool TryGoUp()
        {
            if (string.IsNullOrEmpty(_cwd)) return false;
            var trim = _cwd.TrimEnd('\\');
            int i = trim.LastIndexOf('\\');
            _cwd = i < 0 ? "" : trim.Substring(0, i + 1);
            FillGrid();
            return true;
        }

        private void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back)
            {
                if (TryGoUp()) e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                _grid.SelectAll();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                BeginFileDragOrCopy(copyOnly: true);
                e.Handled = true;
            }
        }

        private void GridOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = _grid.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.Cell && hit.Type != DataGridViewHitTestType.RowHeader)
            {
                _dragPending = false;
                return;
            }
            _dragStart = e.Location;
            _dragPending = true;
        }

        private void GridOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending || _dragging || e.Button != MouseButtons.Left) return;
            int dx = Math.Abs(e.X - _dragStart.X);
            int dy = Math.Abs(e.Y - _dragStart.Y);
            if (dx < SystemInformation.DragSize.Width * 2 &&
                dy < SystemInformation.DragSize.Height * 2)
                return;

            _dragPending = false;
            BeginInvoke(new Action(() => BeginFileDragOrCopy(copyOnly: false)));
        }

        private string SelectionCacheKey(string[] keys)
        {
            if (_source == null || keys == null || keys.Length == 0) return null;
            return _source.Path + "|" + string.Join("\n", keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        private void BeginFileDragOrCopy(bool copyOnly)
        {
            if (_source == null || _dragging) return;
            var keys = SelectedKeys(true);
            if (keys.Length == 0)
            {
                if (copyOnly)
                    MessageBox.Show("Выберите один или несколько файлов (не папки).", "Архив",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _dragging = true;
            try
            {
                var cacheKey = SelectionCacheKey(keys);
                string[] files = null;
                if (cacheKey != null && cacheKey == _dragCacheKey &&
                    _dragCacheFiles != null && _dragCacheFiles.Length > 0 &&
                    _dragCacheFiles.All(File.Exists))
                {
                    files = _dragCacheFiles;
                }
                else
                {
                    Cursor = Cursors.WaitCursor;
                    files = ArchiveActions.ExtractForDragDrop(_source.Path, keys);
                    if (files != null && files.Length > 0)
                    {
                        _dragCacheFiles = files;
                        _dragCacheKey = cacheKey;
                    }
                }

                if (files == null || files.Length == 0)
                {
                    MessageBox.Show("Не удалось подготовить файлы для переноса.", "Архив",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var list = new StringCollection();
                list.AddRange(files);
                var data = new DataObject();
                data.SetFileDropList(list);

                if (copyOnly)
                {
                    Clipboard.SetDataObject(data, copy: true);
                    PreviewLog.Info("Clipboard FileDrop: " + files.Length);
                }
                else
                {
                    var effect = _grid.DoDragDrop(data, DragDropEffects.Copy);
                    PreviewLog.Info("DoDragDrop effect=" + effect + " files=" + files.Length);
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Archive drag/copy failed", ex);
                MessageBox.Show("Ошибка переноса:\n" + ex.Message, "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _dragging = false;
            }
        }

        public void LoadArchive(PreviewSource source)
        {
            _source = source;
            BindLive();
            ReadEntries();
            if (!string.IsNullOrEmpty(_cwd) && !CwdStillExists())
                _cwd = "";
            FillGrid();
        }

        private void BindLive()
        {
            _live?.Dispose();
            _live = PreviewLiveSync.ForSource(
                this,
                () => _source,
                () =>
                {
                    ReadEntries();
                    if (!CwdStillExists()) _cwd = "";
                    FillGrid();
                },
                () => ResizeColumns());
        }

        private bool CwdStillExists()
        {
            if (string.IsNullOrEmpty(_cwd) || _all == null) return true;
            return _all.Any(e =>
                e.Key.StartsWith(_cwd, StringComparison.OrdinalIgnoreCase));
        }

        private void ReadEntries()
        {
            _all = new List<RawEntry>();
            if (_source == null || string.IsNullOrEmpty(_source.Path)) return;
            try
            {
                using (var archive = ArchiveFactory.Open(_source.Path))
                {
                    foreach (var e in archive.Entries)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                        var key = e.Key.Replace('/', '\\');
                        bool dir = e.IsDirectory || key.EndsWith("\\", StringComparison.Ordinal);
                        if (dir && !key.EndsWith("\\", StringComparison.Ordinal))
                            key += "\\";
                        _all.Add(new RawEntry
                        {
                            Key = key,
                            IsDir = dir,
                            Size = dir ? 0 : e.Size,
                            Date = e.LastModifiedTime
                        });
                    }
                }
                PreviewLog.Info("Archive index: " + _source.Path + " count=" + _all.Count);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Archive index failed: " + _source.Path, ex);
                _all = null;
                _grid.Rows.Clear();
                int idx = _grid.Rows.Add("Ошибка: " + ex.Message, "", "");
                _grid.Rows[idx].DefaultCellStyle.ForeColor = PreviewUi.Danger;
            }
        }

        private void FillGrid()
        {
            if (_all == null) return;
            _grid.Rows.Clear();
            _grid.SuspendLayout();
            try
            {
                var cwd = _cwd ?? "";
                if (!string.IsNullOrEmpty(cwd) && !cwd.EndsWith("\\", StringComparison.Ordinal))
                    cwd += "\\";

                _pathLbl.Text = string.IsNullOrEmpty(cwd)
                    ? (_source?.DisplayName ?? "Архив")
                    : (_source?.DisplayName ?? "Архив") + " \\ " + cwd.TrimEnd('\\');
                _btnUp.Enabled = !string.IsNullOrEmpty(cwd);

                if (!string.IsNullOrEmpty(cwd))
                {
                    int up = _grid.Rows.Add("..", "—", "—");
                    _grid.Rows[up].Tag = "..";
                    _grid.Rows[up].DefaultCellStyle.ForeColor = PreviewUi.Folder;
                }

                var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var files = new List<RawEntry>();

                foreach (var e in _all)
                {
                    if (!e.Key.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var rel = e.Key.Substring(cwd.Length);
                    if (string.IsNullOrEmpty(rel)) continue;
                    int slash = rel.IndexOf('\\');
                    if (slash >= 0)
                    {
                        var name = rel.Substring(0, slash);
                        if (name.Length == 0) continue;
                        var full = cwd + name + "\\";
                        if (!folders.ContainsKey(name))
                            folders[name] = full;
                    }
                    else if (e.IsDir)
                    {
                        var name = rel.TrimEnd('\\');
                        if (name.Length == 0) continue;
                        folders[name] = e.Key.EndsWith("\\") ? e.Key : e.Key + "\\";
                    }
                    else
                        files.Add(e);
                }

                foreach (var kv in folders.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    int idx = _grid.Rows.Add(kv.Key + "\\", "—", "—");
                    _grid.Rows[idx].Tag = kv.Value;
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = PreviewUi.Folder;
                }

                foreach (var e in files.OrderBy(f => Path.GetFileName(f.Key), StringComparer.OrdinalIgnoreCase).Take(4000))
                {
                    string dateText = e.Date.HasValue
                        ? e.Date.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                        : "—";
                    int idx = _grid.Rows.Add(Path.GetFileName(e.Key), PreviewUi.FormatSize(e.Size), dateText);
                    _grid.Rows[idx].Tag = e.Key;
                }

                if (_grid.Rows.Count == 0 || (_grid.Rows.Count == 1 && Equals(_grid.Rows[0].Tag, "..")))
                {
                    int empty = _grid.Rows.Add(string.IsNullOrEmpty(cwd) ? "Архив пуст" : "Папка пуста", "", "");
                    _grid.Rows[empty].Tag = null;
                    _grid.Rows[empty].DefaultCellStyle.ForeColor = PreviewUi.TextMuted;
                }

                ResizeColumns();
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }

        private string[] SelectedKeys(bool filesOnly)
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s) && s != ".." && (!filesOnly || !s.EndsWith("\\")))
                .ToArray();
        }

        private void OpenSelected()
        {
            if (_source == null) return;
            var row = _grid.CurrentRow ?? (_grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null);
            var tag = row?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;
            if (tag == "..")
            {
                TryGoUp();
                return;
            }
            if (tag.EndsWith("\\", StringComparison.Ordinal))
            {
                _cwd = tag;
                FillGrid();
                return;
            }
            ArchiveActions.OpenEntry(_source.Path, tag);
        }

        private void ExtractSelected()
        {
            if (_source == null) return;
            var keys = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s) && s != "..")
                .ToArray();
            if (keys.Length == 0)
            {
                MessageBox.Show("Ничего не выбрано.", "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Куда извлечь выбранное";
                var suggest = SuggestExtractDir();
                if (!string.IsNullOrEmpty(suggest))
                    dlg.SelectedPath = suggest;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    ArchiveActions.ExtractEntries(_source.Path, keys, dlg.SelectedPath);
                    var open = MessageBox.Show(
                        "Извлечено в:\n" + dlg.SelectedPath + "\n\nОткрыть папку?",
                        "Архив", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                        System.Diagnostics.Process.Start("explorer.exe", dlg.SelectedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка извлечения:\n" + ex.Message, "Архив",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    PreviewLog.Error("ExtractSelected failed", ex);
                }
            }
        }

        private void ExtractAll()
        {
            if (_source == null) return;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Куда распаковать весь архив";
                var suggest = SuggestExtractDir();
                if (!string.IsNullOrEmpty(suggest))
                    dlg.SelectedPath = suggest;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var dest = Path.Combine(dlg.SelectedPath,
                    Path.GetFileNameWithoutExtension(_source.DisplayName ?? "archive"));
                try
                {
                    ArchiveActions.ExtractAll(_source.Path, dest);
                    var open = MessageBox.Show(
                        "Архив распакован:\n" + dest + "\n\nОткрыть папку?",
                        "Архив", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                        System.Diagnostics.Process.Start("explorer.exe", dest);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка распаковки:\n" + ex.Message, "Архив",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    PreviewLog.Error("ExtractAll failed", ex);
                }
            }
        }

        private string SuggestExtractDir()
        {
            var orig = _source?.OriginalPath;
            if (!string.IsNullOrEmpty(orig) && File.Exists(orig))
                return Path.GetDirectoryName(orig);
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void CopySelectedPath()
        {
            var keys = SelectedKeys(false);
            if (keys.Length == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, keys)); }
            catch { /* ignore */ }
        }

        private void ResizeColumns()
        {
            PreviewUi.FitColumnsContentThenFillLast(_grid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _live?.Dispose();
                _live = null;
                DeleteDragCache();
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }

        private void DeleteDragCache()
        {
            var files = _dragCacheFiles;
            _dragCacheFiles = null;
            _dragCacheKey = null;
            if (files == null || files.Length == 0) return;
            try
            {
                var dir = Path.GetDirectoryName(files[0]);
                var marker = Path.Combine("WinPreviewPlus", "drag");
                if (!string.IsNullOrEmpty(dir) &&
                    dir.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Drag cache cleanup", ex);
            }
        }
    }
}
