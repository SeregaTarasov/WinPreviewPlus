using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>Редактируемый предпросмотр текста — Ctrl+S сохранить, Ctrl+F / F3 найти.</summary>
    public class TextPreviewControl : PreviewHandlerControl, IPreviewFindHost
    {
        private const int BarH = 48;
        private const int SaveBtnH = 34;

        private readonly Panel _host;
        private Panel _bar;
        private PreviewFindBar _findBar;
        private TextBox _editor;
        private Label _status;
        private Panel _btnSave;
        private Label _btnSaveLabel;
        private Panel _btnFind;
        private Label _btnFindLabel;
        private bool _saveEnabled;
        private PreviewSource _source;
        private Encoding _encoding = new UTF8Encoding(false);
        private bool _hasBom;
        private bool _dirty;
        private bool _loading;
        private string _savePath;
        private int _saveBtnW;
        private int _findBtnW;
        private string _flashStatus;
        private Timer _flashTimer;
        private Timer _findDebounce;
        private string _findQuery = "";
        private readonly List<int> _matchStarts = new List<int>();
        private int _matchIndex = -1;
        /// <summary>Клик по тексту — редактор активен, Backspace не уходит в Проводник.</summary>
        private bool _editorArmed;

        public TextPreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _host = new Panel { Dock = DockStyle.Fill, BackColor = PreviewUi.Surface };
            PreviewUi.EnableDoubleBuffer(_host);
            Controls.Add(_host);

            // Без handleKey на Backspace — иначе удаление срабатывает дважды
            // (TranslateAccelerator + фильтр). Ctrl+S/A/F — через ProcessCmdKey / Accel.
        }

        public void ShowFind() => ShowFindBar(true);

        public void FindNext(bool forward)
        {
            if (_findBar == null || !_findBar.Visible || string.IsNullOrEmpty(_findQuery))
            {
                ShowFindBar(true);
                return;
            }
            StepMatch(forward);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                Save();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                ShowFindBar(true);
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
            if (keyData == (Keys.Control | Keys.A))
            {
                if (_findBar?.Box != null && _findBar.Box.Focused)
                {
                    _findBar.Box.SelectAll();
                    return true;
                }
                if (_editor != null)
                {
                    _editor.Focus();
                    _editor.SelectAll();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void LoadText(PreviewSource source)
        {
            _source = source;
            _dirty = false;
            _host.Controls.Clear();

            _savePath = !string.IsNullOrEmpty(source.OriginalPath) && File.Exists(source.OriginalPath)
                ? source.OriginalPath
                : source.Path;

            CreateBar();
            _editor = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsTab = true,
                Font = new Font("Consolas", 10.5f),
                BorderStyle = BorderStyle.None,
                BackColor = PreviewUi.Surface,
                ForeColor = PreviewUi.TextPrimary,
                HideSelection = false,
                MaxLength = 0,
                ShortcutsEnabled = true
            };
            _editor.TextChanged += (s, e) =>
            {
                if (_loading) return;
                if (!_dirty)
                {
                    _dirty = true;
                    _flashStatus = null;
                    if (_flashTimer != null) _flashTimer.Stop();
                    RefreshStatus();
                }
            };
            _editor.KeyDown += EditorOnKeyDown;
            _editor.MouseDown += (s, e) => ArmEditor();
            _editor.GotFocus += (s, e) => ArmEditor();
            _editor.Leave += (s, e) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (!ContainsFocus) _editorArmed = false;
                }));
            };

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Найти…  Ctrl+F", null, (s, e) => ShowFindBar(true));
            ctx.Items.Add("Далее  F3", null, (s, e) => FindNext(true));
            ctx.Items.Add("Назад  Shift+F3", null, (s, e) => FindNext(false));
            _editor.ContextMenuStrip = ctx;

            CreateFindBar();
            _host.Controls.Add(_editor);
            _host.Controls.Add(_findBar);
            _host.Controls.Add(_bar);

            try
            {
                byte[] bytes = File.ReadAllBytes(source.Path);
                _loading = true;
                if (IsBinaryPlist(bytes))
                {
                    _encoding = new UTF8Encoding(false);
                    _hasBom = false;
                    _editor.ReadOnly = true;
                    _editor.Text =
                        "Бинарный Apple plist (bplist).\r\n\r\n" +
                        "Текстовый просмотр недоступен. XML-.plist открываются как обычно.\r\n" +
                        "На macOS/iOS: plutil -convert xml1 …";
                    _loading = false;
                    _dirty = false;
                    _savePath = null; // не предлагать Save поверх бинарника
                    RefreshStatus();
                }
                else
                {
                    _editor.ReadOnly = false;
                    DetectEncoding(bytes, out _encoding, out _hasBom, out int skip);
                    _editor.Text = _encoding.GetString(bytes, skip, bytes.Length - skip);
                    _loading = false;
                    _dirty = false;
                    RefreshStatus();
                }
                // Не ArmEditor при загрузке — иначе Backspace в списке файлов съедается превью
                PreviewLog.Info("Text preview OK: " + source.Path + " enc=" + _encoding.WebName +
                    " save=" + _savePath);
            }
            catch (Exception ex)
            {
                _loading = false;
                ShowError("Не удалось открыть: " + ex.Message);
                PreviewLog.Error("Text preview failed: " + source.Path, ex);
            }
        }

        private void EditorOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Save();
                return;
            }
            if (e.Control && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowFindBar(true);
                return;
            }
            if (e.KeyCode == Keys.F3)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FindNext(forward: !e.Shift);
                return;
            }
            // Multiline TextBox не обрабатывает Ctrl+A сам — и в prevhost Focused часто врёт
            if (e.Control && e.KeyCode == Keys.A)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _editor.SelectAll();
            }
        }

        private void ArmEditor()
        {
            _editorArmed = true;
            try
            {
                if (_editor != null && _editor.IsHandleCreated)
                    SetFocus(_editor.Handle);
            }
            catch { /* ignore */ }
        }

        /// <summary>Редактор в фокусе / после клика — Backspace не форвардить в Проводник.</summary>
        public bool IsEditorActive
        {
            get
            {
                if (_editor == null || _editor.IsDisposed || !_editor.Visible) return false;
                if (_editorArmed || _editor.Focused) return true;
                try
                {
                    IntPtr focus = GetFocus();
                    return focus != IntPtr.Zero
                           && (focus == _editor.Handle || IsChild(_editor.Handle, focus));
                }
                catch { return false; }
            }
        }

        public void TrySaveFromHost() => Save();

        public bool TrySelectAllFromHost()
        {
            if (_editor == null) return false;
            ArmEditor();
            _editor.SelectAll();
            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        private void CreateBar()
        {
            _bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = BarH,
                BackColor = PreviewUi.HeaderBg
            };
            PreviewUi.EnableDoubleBuffer(_bar);
            var bottomLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = PreviewUi.Border
            };

            // Ширина по тексту; высота больше — буквы не обрезаются сверху/снизу
            _saveBtnW = TextRenderer.MeasureText("Сохранить", PreviewUi.FontUi).Width + 24;
            _btnSave = new Panel
            {
                Width = _saveBtnW,
                Height = SaveBtnH,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand
            };
            PreviewUi.EnableDoubleBuffer(_btnSave);
            _btnSaveLabel = new Label
            {
                Text = "Сохранить",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                AutoEllipsis = false
            };
            void OnSaveClick(object s, EventArgs e)
            {
                if (_saveEnabled) Save();
            }
            _btnSave.Click += OnSaveClick;
            _btnSaveLabel.Click += OnSaveClick;
            _btnSave.MouseEnter += (s, e) =>
            {
                if (_saveEnabled) _btnSave.BackColor = Color.FromArgb(220, 224, 232);
            };
            _btnSave.MouseLeave += (s, e) => UpdateSaveVisual();
            _btnSave.Controls.Add(_btnSaveLabel);

            _status = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted
            };

            _flashTimer = new Timer { Interval = 2200 };
            _flashTimer.Tick += (s, e) =>
            {
                _flashTimer.Stop();
                _flashStatus = null;
                RefreshStatus();
            };

            _findBtnW = TextRenderer.MeasureText("Найти", PreviewUi.FontUi).Width + 24;
            _btnFind = new Panel
            {
                Width = _findBtnW,
                Height = SaveBtnH,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand
            };
            PreviewUi.EnableDoubleBuffer(_btnFind);
            _btnFindLabel = new Label
            {
                Text = "Найти",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = PreviewUi.FontUi,
                ForeColor = PreviewUi.TextPrimary,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            void OnFindClick(object s, EventArgs e) => ShowFindBar(true);
            _btnFind.Click += OnFindClick;
            _btnFindLabel.Click += OnFindClick;
            _btnFind.MouseEnter += (s, e) => _btnFind.BackColor = Color.FromArgb(220, 224, 232);
            _btnFind.MouseLeave += (s, e) => _btnFind.BackColor = PreviewUi.TabIdle;
            _btnFind.Controls.Add(_btnFindLabel);

            _bar.Controls.Add(bottomLine);
            _bar.Controls.Add(_status);
            _bar.Controls.Add(_btnFind);
            _bar.Controls.Add(_btnSave);
            _bar.Resize += (s, e) => LayoutBar();
            LayoutBar();
        }

        private void CreateFindBar()
        {
            if (_findDebounce == null)
            {
                _findDebounce = new Timer { Interval = 160 };
                _findDebounce.Tick += (s, e) =>
                {
                    _findDebounce.Stop();
                    RebuildMatchesAndSelect(resetIndex: true);
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
            if (show)
                _findBar.ShowBar(true, _host);
            else
            {
                if (_findDebounce != null) _findDebounce.Stop();
                _findBar.ShowBar(false);
                try { if (_editor != null) ArmEditor(); } catch { }
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

        private void RebuildMatchesAndSelect(bool resetIndex)
        {
            if (_editor == null || _findBar?.Box == null) return;
            string q = _findBar.Box.Text ?? "";
            _findQuery = q;
            _matchStarts.Clear();

            if (q.Length == 0)
            {
                _matchIndex = -1;
                if (_findBar.Meta != null) _findBar.Meta.Text = "";
                return;
            }

            string hay = _editor.Text ?? "";
            int start = 0;
            while (start < hay.Length)
            {
                int i = hay.IndexOf(q, start, StringComparison.CurrentCultureIgnoreCase);
                if (i < 0) break;
                _matchStarts.Add(i);
                start = i + Math.Max(1, q.Length);
                // защита от гигантских списков на очень частых буквах
                if (_matchStarts.Count >= 20000) break;
            }

            if (_matchStarts.Count == 0)
            {
                _matchIndex = -1;
                if (_findBar.Meta != null) _findBar.Meta.Text = "нет совпадений";
                return;
            }

            if (resetIndex)
            {
                int caret = _editor.SelectionStart;
                _matchIndex = 0;
                for (int i = 0; i < _matchStarts.Count; i++)
                {
                    if (_matchStarts[i] >= caret)
                    {
                        _matchIndex = i;
                        break;
                    }
                    _matchIndex = i;
                }
            }
            else if (_matchIndex < 0 || _matchIndex >= _matchStarts.Count)
            {
                _matchIndex = 0;
            }

            SelectCurrentMatch();
        }

        private void StepMatch(bool forward)
        {
            if (_findBar?.Box != null &&
                !string.Equals(_findQuery, _findBar.Box.Text ?? "", StringComparison.Ordinal))
            {
                RebuildMatchesAndSelect(resetIndex: true);
                return;
            }
            if (_matchStarts == null || _matchStarts.Count == 0)
            {
                RebuildMatchesAndSelect(resetIndex: true);
                return;
            }
            if (forward)
                _matchIndex = (_matchIndex + 1) % _matchStarts.Count;
            else
                _matchIndex = (_matchIndex - 1 + _matchStarts.Count) % _matchStarts.Count;
            SelectCurrentMatch();
        }

        private void SelectCurrentMatch()
        {
            if (_editor == null || _matchStarts == null || _matchIndex < 0 ||
                _matchIndex >= _matchStarts.Count)
                return;
            int start = _matchStarts[_matchIndex];
            int len = (_findQuery ?? "").Length;
            try
            {
                _editor.Select(start, len);
                _editor.ScrollToCaret();
            }
            catch { }
            if (_findBar?.Meta != null)
                _findBar.Meta.Text = (_matchIndex + 1) + " из " + _matchStarts.Count;
        }

        private void UpdateSaveVisual()
        {
            if (_btnSave == null) return;
            _btnSave.BackColor = _saveEnabled
                ? PreviewUi.TabIdle
                : Color.FromArgb(236, 238, 242);
            if (_btnSaveLabel != null)
            {
                _btnSaveLabel.ForeColor = _saveEnabled
                    ? PreviewUi.TextPrimary
                    : PreviewUi.TextMuted;
                _btnSave.Cursor = _saveEnabled ? Cursors.Hand : Cursors.Default;
                _btnSaveLabel.Cursor = _btnSave.Cursor;
            }
        }

        private void LayoutBar()
        {
            if (_bar == null) return;
            int y = Math.Max(0, (_bar.ClientSize.Height - 1 - SaveBtnH) / 2);
            int x = 8;
            _btnSave.SetBounds(x, y, _saveBtnW, SaveBtnH);
            x += _saveBtnW + 6;
            if (_btnFind != null)
            {
                _btnFind.SetBounds(x, y, _findBtnW, SaveBtnH);
                x += _findBtnW + 12;
            }
            _status.SetBounds(x, y, Math.Max(40, _bar.ClientSize.Width - x - 8), SaveBtnH);
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            string text = "Ctrl+S · Ctrl+F";
            if (!string.IsNullOrEmpty(_flashStatus))
                text += " · " + _flashStatus;
            else if (_dirty)
                text += " · Изменено";
            else if (string.IsNullOrEmpty(_savePath))
                text += " · Только чтение";
            _status.Text = text;
            _saveEnabled = _dirty && !string.IsNullOrEmpty(_savePath);
            UpdateSaveVisual();
        }

        private void FlashStatus(string message)
        {
            _flashStatus = message;
            if (_flashTimer != null)
            {
                _flashTimer.Stop();
                _flashTimer.Start();
            }
            RefreshStatus();
        }

        private void Save()
        {
            if (_editor == null) return;
            if (string.IsNullOrEmpty(_savePath))
            {
                MessageBox.Show("Нет пути к исходному файлу — сохранить нельзя.", "Текст",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(_savePath);
                var tmp = Path.Combine(
                    string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
                    Path.GetFileName(_savePath) + ".winpreviewplus.tmp");

                byte[] body = _encoding.GetBytes(_editor.Text ?? "");
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (_hasBom)
                    {
                        var preamble = _encoding.GetPreamble();
                        if (preamble != null && preamble.Length > 0)
                            fs.Write(preamble, 0, preamble.Length);
                    }
                    fs.Write(body, 0, body.Length);
                }

                File.Copy(tmp, _savePath, overwrite: true);
                try { File.Delete(tmp); } catch { /* ignore */ }

                _dirty = false;
                FlashStatus("Сохранено");
                PreviewLog.Info("Text saved: " + _savePath);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Text save failed: " + _savePath, ex);
                MessageBox.Show("Не удалось сохранить:\n" + ex.Message, "Текст",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsBinaryPlist(byte[] bytes)
        {
            // Apple binary property list magic: "bplist"
            return bytes != null && bytes.Length >= 6
                && bytes[0] == (byte)'b' && bytes[1] == (byte)'p'
                && bytes[2] == (byte)'l' && bytes[3] == (byte)'i'
                && bytes[4] == (byte)'s' && bytes[5] == (byte)'t';
        }

        private static void DetectEncoding(byte[] bytes, out Encoding encoding, out bool hasBom, out int skip)
        {
            encoding = new UTF8Encoding(false);
            hasBom = false;
            skip = 0;
            if (bytes == null || bytes.Length == 0) return;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                hasBom = true;
                skip = 3;
                return;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                hasBom = true;
                skip = 2;
                return;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                hasBom = true;
                skip = 2;
                return;
            }

            if (IsValidUtf8(bytes))
            {
                encoding = new UTF8Encoding(false);
                return;
            }

            try
            {
                encoding = Encoding.GetEncoding(1251);
            }
            catch
            {
                encoding = Encoding.Default;
            }
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
                utf8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_dirty && !string.IsNullOrEmpty(_savePath) && _editor != null)
                {
                    try
                    {
                        var name = _source?.DisplayName ?? Path.GetFileName(_savePath);
                        var r = MessageBox.Show(
                            "Сохранить изменения в «" + name + "»?",
                            "Текст",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button1);
                        if (r == DialogResult.Yes)
                            Save();
                    }
                    catch (Exception ex)
                    {
                        PreviewLog.Error("Text dispose save prompt", ex);
                    }
                }
                if (_flashTimer != null)
                {
                    _flashTimer.Stop();
                    _flashTimer.Dispose();
                    _flashTimer = null;
                }
                if (_findDebounce != null)
                {
                    _findDebounce.Stop();
                    _findDebounce.Dispose();
                    _findDebounce = null;
                }
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }
    }
}
