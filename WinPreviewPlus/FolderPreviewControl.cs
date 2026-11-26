using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>Превью папки: вход внутрь, открытие файлов, копирование.</summary>
    public class FolderPreviewControl : PreviewHandlerControl
    {
        private readonly DataGridView _grid;
        private readonly Panel _nav;
        private readonly Button _btnUp;
        private readonly Label _pathLbl;
        private string _root;
        private string _cwd;
        private PreviewLiveSync _live;
        private Point _dragStart;
        private bool _dragPending;
        private bool _dragging;

        public FolderPreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);

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
            _grid.Resize += (s, e) => PreviewUi.FitColumnsContentThenFillLast(_grid);
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };
            _grid.KeyDown += GridOnKeyDown;
            _grid.MouseDown += GridOnMouseDown;
            _grid.MouseMove += GridOnMouseMove;
            _grid.MouseUp += (s, e) => { _dragPending = false; };

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Открыть", null, (s, e) => OpenSelected());
            ctx.Items.Add("В проводнике", null, (s, e) => RevealSelected());
            ctx.Items.Add("Копировать файлы", null, (s, e) => BeginFileDragOrCopy(copyOnly: true));
            ctx.Items.Add("Копировать путь", null, (s, e) => CopyPaths());
            _grid.ContextMenuStrip = ctx;

            Controls.Add(_grid);
            Controls.Add(_nav);
        }

        public void LoadFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new DirectoryNotFoundException("Папка не найдена: " + path);
            _root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
            _cwd = _root;
            FillGrid();
            BindLive();
        }

        public bool TryGoUp()
        {
            if (string.IsNullOrEmpty(_cwd) || string.IsNullOrEmpty(_root)) return false;
            var parent = Directory.GetParent(_cwd.TrimEnd('\\', '/'));
            if (parent == null) return false;
            var next = parent.FullName.TrimEnd('\\') + "\\";
            if (!next.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                return false;
            _cwd = next;
            FillGrid();
            BindLive();
            return true;
        }

        private void BindLive()
        {
            _live?.Dispose();
            _live = PreviewLiveSync.ForPath(
                this,
                () => _cwd,
                () => FillGrid(),
                () => PreviewUi.FitColumnsContentThenFillLast(_grid));
        }

        private void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back) { if (TryGoUp()) e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { _grid.SelectAll(); e.Handled = true; }
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

        private string[] SelectedDropPaths()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s) && s != "..")
                .Select(s => s.EndsWith("\\", StringComparison.Ordinal) ? s.TrimEnd('\\') : s)
                .Where(s => File.Exists(s) || Directory.Exists(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void BeginFileDragOrCopy(bool copyOnly)
        {
            if (_dragging) return;
            var paths = SelectedDropPaths();
            if (paths.Length == 0)
            {
                if (copyOnly)
                    MessageBox.Show("Выберите файлы или папки.", "Папка",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _dragging = true;
            try
            {
                var list = new StringCollection();
                list.AddRange(paths);
                var data = new DataObject();
                data.SetFileDropList(list);

                if (copyOnly)
                {
                    Clipboard.SetDataObject(data, copy: true);
                    PreviewLog.Info("Folder clipboard FileDrop: " + paths.Length);
                }
                else
                {
                    var effect = _grid.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
                    PreviewLog.Info("Folder DoDragDrop effect=" + effect + " items=" + paths.Length);
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Folder drag/copy failed", ex);
                MessageBox.Show("Ошибка переноса:\n" + ex.Message, "Папка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _dragging = false;
            }
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();
            if (string.IsNullOrEmpty(_cwd) || !Directory.Exists(_cwd))
            {
                int miss = _grid.Rows.Add("Папка недоступна", "", "");
                _grid.Rows[miss].DefaultCellStyle.ForeColor = PreviewUi.Danger;
                return;
            }

            _pathLbl.Text = _cwd.TrimEnd('\\');
            _btnUp.Enabled = !_cwd.Equals(_root, StringComparison.OrdinalIgnoreCase);

            if (!_cwd.Equals(_root, StringComparison.OrdinalIgnoreCase))
            {
                int up = _grid.Rows.Add("..", "—", "—");
                _grid.Rows[up].Tag = "..";
                _grid.Rows[up].DefaultCellStyle.ForeColor = PreviewUi.Folder;
            }

            try
            {
                var dir = new DirectoryInfo(_cwd);
                int n = 0;
                foreach (var d in dir.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (n++ >= 2000) break;
                    int idx = _grid.Rows.Add(d.Name + "\\", "—", d.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
                    _grid.Rows[idx].Tag = d.FullName + "\\";
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = PreviewUi.Folder;
                }
                foreach (var f in dir.EnumerateFiles().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (n++ >= 2000) break;
                    int idx = _grid.Rows.Add(f.Name, PreviewUi.FormatSize(f.Length),
                        f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
                    _grid.Rows[idx].Tag = f.FullName;
                }
            }
            catch (Exception ex)
            {
                int idx = _grid.Rows.Add("Ошибка: " + ex.Message, "", "");
                _grid.Rows[idx].DefaultCellStyle.ForeColor = PreviewUi.Danger;
                PreviewLog.Error("Folder listing " + _cwd, ex);
            }

            PreviewUi.FitColumnsContentThenFillLast(_grid);
        }

        private string SelectedPath()
        {
            var row = _grid.CurrentRow ?? (_grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null);
            return row?.Tag as string;
        }

        private void OpenSelected()
        {
            var tag = SelectedPath();
            if (string.IsNullOrEmpty(tag)) return;
            if (tag == "..") { TryGoUp(); return; }
            if (tag.EndsWith("\\", StringComparison.Ordinal) && Directory.Exists(tag.TrimEnd('\\')))
            {
                _cwd = Path.GetFullPath(tag.TrimEnd('\\')) + "\\";
                FillGrid();
                BindLive();
                return;
            }
            if (!File.Exists(tag)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = tag, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть:\n" + ex.Message, "Папка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                PreviewLog.Error("Folder open " + tag, ex);
            }
        }

        private void RevealSelected()
        {
            var tag = SelectedPath();
            if (tag == ".." || string.IsNullOrEmpty(tag))
            {
                ArchiveActions.RevealInExplorer(_cwd.TrimEnd('\\'));
                return;
            }
            if (tag.EndsWith("\\"))
                Process.Start("explorer.exe", "/select,\"" + tag.TrimEnd('\\') + "\"");
            else
                ArchiveActions.RevealInExplorer(tag);
        }

        private void CopyPaths()
        {
            var paths = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s) && s != "..")
                .ToArray();
            if (paths.Length == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _live?.Dispose();
                _live = null;
            }
            base.Dispose(disposing);
        }
    }
}
