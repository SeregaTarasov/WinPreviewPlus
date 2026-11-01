using System;
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
        private PreviewSource _source;
        private Point _dragStart;
        private bool _dragPending;
        private bool _dragging;
        private string[] _dragCacheFiles;
        private string _dragCacheKey;

        public ArchivePreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            AllowDrop = false;

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
            _grid.SelectionChanged += (s, e) => WarmDragCache();

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
        }

        private void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
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
            // заранее готовим файлы, пока тянут мышь
            WarmDragCache();
        }

        private void GridOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending || _dragging || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height)
                return;

            _dragPending = false;
            // DoDragDrop нельзя из глубины MouseMove DGV — откладываем
            BeginInvoke(new Action(() => BeginFileDragOrCopy(copyOnly: false)));
        }

        private string SelectionCacheKey(string[] keys)
        {
            if (_source == null || keys == null || keys.Length == 0) return null;
            return _source.Path + "|" + string.Join("\n", keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        private void WarmDragCache()
        {
            if (_source == null || _dragging) return;
            var keys = SelectedKeys(true);
            if (keys.Length == 0) return;
            var key = SelectionCacheKey(keys);
            if (key == null || key == _dragCacheKey) return;
            try
            {
                var files = ArchiveActions.ExtractForDragDrop(_source.Path, keys);
                if (files != null && files.Length > 0 && files.All(File.Exists))
                {
                    _dragCacheFiles = files;
                    _dragCacheKey = key;
                    PreviewLog.Info("Drag cache ready: " + files.Length);
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("WarmDragCache failed", ex);
            }
        }

        /// <summary>
        /// FileDrop в Explorer / буфер — как несколько ярлыков с рабочего стола.
        /// </summary>
        private void BeginFileDragOrCopy(bool copyOnly)
        {
            if (_source == null || _dragging) return;
            var keys = SelectedKeys(true);
            if (keys.Length == 0)
            {
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
            _grid.Rows.Clear();
            _grid.SuspendLayout();

            try
            {
                using (var archive = ArchiveFactory.Open(source.Path))
                {
                    var entries = archive.Entries
                        .Where(e => e != null && !string.IsNullOrEmpty(e.Key))
                        .OrderBy(e => e.IsDirectory ? 0 : 1)
                        .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                        .Take(5000)
                        .ToList();

                    foreach (var entry in entries)
                    {
                        var name = entry.Key.Replace('/', '\\');
                        if (entry.IsDirectory && !name.EndsWith("\\", StringComparison.Ordinal))
                            name += "\\";

                        string sizeText = entry.IsDirectory ? "—" : FormatSize(entry.Size);
                        string dateText = entry.LastModifiedTime.HasValue
                            ? entry.LastModifiedTime.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                            : "—";

                        int idx = _grid.Rows.Add(name, sizeText, dateText);
                        var row = _grid.Rows[idx];
                        row.Tag = name;
                        if (entry.IsDirectory)
                            row.DefaultCellStyle.ForeColor = PreviewUi.Folder;
                    }

                    ResizeColumns();
                    PreviewLog.Info("Archive preview OK: " + source.Path + " count=" + entries.Count);
                }
            }
            catch (Exception ex)
            {
                _grid.Rows.Clear();
                int idx = _grid.Rows.Add("Ошибка: " + ex.Message, "", "");
                _grid.Rows[idx].DefaultCellStyle.ForeColor = PreviewUi.Danger;
                PreviewLog.Error("Archive preview failed: " + source.Path, ex);
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
                .Where(s => !string.IsNullOrEmpty(s) && (!filesOnly || !s.EndsWith("\\")))
                .ToArray();
        }

        private void OpenSelected()
        {
            if (_source == null) return;
            var keys = SelectedKeys(true);
            if (keys.Length != 1)
            {
                MessageBox.Show("Выберите один файл в списке.", "Архив",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ArchiveActions.OpenEntry(_source.Path, keys[0]);
        }

        private void ExtractSelected()
        {
            if (_source == null) return;
            var keys = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s))
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
            // Имя/Размер по содержимому; Дата — до правого края панели
            PreviewUi.FitColumnsContentThenFillLast(_grid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " Б";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " КБ";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.##") + " МБ";
            return (mb / 1024.0).ToString("0.##") + " ГБ";
        }
    }
}
