using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinPreviewPlus.Setup
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _log;
        private readonly Button _btnInstall;
        private readonly Button _btnUninstall;
        private readonly Label _status;
        private readonly Label _deps;
        private readonly Label _state;
        private readonly CheckBox _chkNet48;
        private readonly CheckBox _chkWebView2;
        private readonly Label _depsHint;
        private readonly bool _silentInstall;
        private readonly bool _silentUninstall;

        public MainForm(bool silentInstall, bool silentUninstall)
        {
            _silentInstall = silentInstall;
            _silentUninstall = silentUninstall;

            Text = "WinPreviewPlus";
            ClientSize = new Size(560, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "WinPreviewPlus",
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 32, 40),
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(520, 30)
            };

            var sub = new Label
            {
                Text = "Бесплатное превью в Проводнике (Alt+P): архивы, Office, PDF, фото/SVG,\r\n" +
                       "текст, аудио, видео. Продукт — в профиль; зависимости могут запросить UAC.",
                AutoSize = false,
                ForeColor = Color.FromArgb(110, 118, 132),
                Location = new Point(20, 48),
                Size = new Size(520, 40)
            };

            _deps = new Label
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(80, 90, 100),
                Location = new Point(20, 92),
                Size = new Size(520, 22)
            };

            _state = new Label
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(0, 103, 192),
                Location = new Point(20, 114),
                Size = new Size(520, 22)
            };

            _depsHint = new Label
            {
                Text = "Если чего-то нет — оставьте галочки: установщик скачает и поставит, затем продолжит.",
                AutoSize = false,
                ForeColor = Color.FromArgb(110, 118, 132),
                Location = new Point(20, 138),
                Size = new Size(520, 20)
            };

            _chkNet48 = new CheckBox
            {
                AutoSize = false,
                Location = new Point(24, 160),
                Size = new Size(520, 24),
                Text = ".NET Framework 4.8 (обязательно)",
                ForeColor = Color.FromArgb(28, 32, 40)
            };

            _chkWebView2 = new CheckBox
            {
                AutoSize = false,
                Location = new Point(24, 184),
                Size = new Size(520, 24),
                Text = "WebView2 Runtime (видео / SVG)",
                ForeColor = Color.FromArgb(28, 32, 40)
            };

            _btnInstall = new Button
            {
                Text = "Установить",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 103, 192),
                ForeColor = Color.White,
                Location = new Point(20, 220),
                Size = new Size(150, 36),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += async (s, e) => await RunInstallAsync();

            _btnUninstall = new Button
            {
                Text = "Удалить",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(236, 238, 242),
                ForeColor = Color.FromArgb(28, 32, 40),
                Location = new Point(182, 220),
                Size = new Size(130, 36),
                Cursor = Cursors.Hand
            };
            _btnUninstall.FlatAppearance.BorderSize = 0;
            _btnUninstall.Click += async (s, e) => await RunUninstallAsync();

            var btnManual = new Button
            {
                Text = "Ссылки…",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(236, 238, 242),
                ForeColor = Color.FromArgb(28, 32, 40),
                Location = new Point(324, 220),
                Size = new Size(110, 36),
                Cursor = Cursors.Hand
            };
            btnManual.FlatAppearance.BorderSize = 0;
            btnManual.Click += (s, e) =>
            {
                var r = MessageBox.Show(this,
                    "Открыть страницу .NET Framework 4.8?\n\n" +
                    "Нет — открыть загрузку WebView2 Runtime.",
                    "Ручная установка зависимостей",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (r == DialogResult.Yes) Installer.OpenNet48Download();
                else if (r == DialogResult.No) Installer.OpenWebView2Download();
            };

            _status = new Label
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(60, 70, 80),
                Text = Installer.HasPayload() ? "Готов." : "Ошибка сборки: нет payload.",
                Location = new Point(20, 268),
                Size = new Size(520, 22)
            };

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5f),
                BackColor = Color.FromArgb(248, 249, 251),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(20, 294),
                Size = new Size(520, 200)
            };

            Controls.Add(title);
            Controls.Add(sub);
            Controls.Add(_deps);
            Controls.Add(_state);
            Controls.Add(_depsHint);
            Controls.Add(_chkNet48);
            Controls.Add(_chkWebView2);
            Controls.Add(_btnInstall);
            Controls.Add(_btnUninstall);
            Controls.Add(btnManual);
            Controls.Add(_status);
            Controls.Add(_log);

            RefreshDepsAndState();

            if (!Installer.HasPayload())
                _btnInstall.Enabled = false;

            Installer.Log = line =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() => _log.AppendText(line + Environment.NewLine)));
                }
                catch { }
            };

            Shown += async (s, e) =>
            {
                if (_silentUninstall)
                {
                    await RunUninstallAsync();
                    Close();
                }
                else if (_silentInstall)
                {
                    await RunInstallAsync();
                    Close();
                }
            };
        }

        private void RefreshDepsAndState()
        {
            bool netOk = Installer.IsNet48Installed();
            bool wv2Ok = Installer.IsWebView2Installed();

            if (_deps != null)
            {
                _deps.Text = ".NET 4.8: " + (netOk ? "OK" : "нет") +
                             "   ·   WebView2 Runtime: " + (wv2Ok ? "OK" : "нет (видео/SVG)");
            }

            if (_chkNet48 != null)
            {
                if (netOk)
                {
                    _chkNet48.Checked = true;
                    _chkNet48.Enabled = false;
                    _chkNet48.Text = ".NET Framework 4.8 — установлено";
                }
                else
                {
                    _chkNet48.Enabled = true;
                    _chkNet48.Checked = true; // обязательно — галка по умолчанию
                    _chkNet48.Text = ".NET Framework 4.8 (обязательно) — скачать и установить";
                }
            }

            if (_chkWebView2 != null)
            {
                if (wv2Ok)
                {
                    _chkWebView2.Checked = true;
                    _chkWebView2.Enabled = false;
                    _chkWebView2.Text = "WebView2 Runtime — установлено";
                }
                else
                {
                    _chkWebView2.Enabled = true;
                    _chkWebView2.Checked = true; // по умолчанию ставим
                    _chkWebView2.Text = "WebView2 Runtime (видео / SVG) — скачать и установить";
                }
            }

            if (_state == null) return;

            if (Installer.IsInstalled())
            {
                _state.Text = "Статус: установлено  →  %LOCALAPPDATA%\\WinPreviewPlus";
                if (_btnInstall != null) _btnInstall.Text = "Переустановить";
            }
            else
            {
                _state.Text = "Статус: не установлено";
                if (_btnInstall != null) _btnInstall.Text = "Установить";
            }
        }

        private async Task RunInstallAsync()
        {
            bool needNet = _chkNet48 != null && _chkNet48.Checked && !Installer.IsNet48Installed();
            bool needWv2 = _chkWebView2 != null && _chkWebView2.Checked && !Installer.IsWebView2Installed();

            if (!Installer.IsNet48Installed() && (_chkNet48 == null || !_chkNet48.Checked))
            {
                MessageBox.Show(this,
                    ".NET Framework 4.8 обязателен.\nОтметьте галочку — установщик скачает и поставит его.",
                    "WinPreviewPlus",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!_silentInstall && (needNet || needWv2))
            {
                var parts = new System.Collections.Generic.List<string>();
                if (needNet) parts.Add("• .NET Framework 4.8");
                if (needWv2) parts.Add("• WebView2 Runtime");
                var r = MessageBox.Show(this,
                    "Будут скачаны и установлены зависимости:\n\n" +
                    string.Join("\n", parts) +
                    "\n\nПотребуется подтверждение UAC. Затем установка WinPreviewPlus продолжится.\n\nПродолжить?",
                    "WinPreviewPlus — зависимости",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            SetBusy(true);
            _status.Text = (needNet || needWv2) ? "Зависимости и установка…" : "Установка…";
            _log.Clear();
            try
            {
                bool ensureNet = _chkNet48 == null || _chkNet48.Checked;
                bool ensureWv2 = _chkWebView2 == null || _chkWebView2.Checked;
                await Task.Run(() => Installer.Install(
                    restartExplorer: true,
                    ensureNet48: ensureNet,
                    ensureWebView2: ensureWv2,
                    silentDeps: _silentInstall));
                RefreshDepsAndState();
                _status.Text = "Установлено. Проводник → файл → Alt+P.";
                if (!_silentInstall)
                {
                    MessageBox.Show(this,
                        "WinPreviewPlus установлен.\n\n" +
                        "• Проводник → выделите файл → Alt+P\n" +
                        "• Удаление: эта программа, меню Пуск или Параметры → Приложения",
                        "WinPreviewPlus",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка установки";
                Installer.Log(MaskPath(ex.ToString()));
                if (!_silentInstall)
                    MessageBox.Show(this, MaskPath(ex.Message), "WinPreviewPlus", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                RefreshDepsAndState();
            }
        }

        private static string MaskPath(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    s = s.Replace(home, "%USERPROFILE%");
            }
            catch { }
            return s;
        }

        private async Task RunUninstallAsync()
        {
            if (!_silentUninstall)
            {
                var r = MessageBox.Show(this,
                    "Полностью удалить WinPreviewPlus?\n\n" +
                    "Будут сняты preview handlers, ассоциации файлов,\n" +
                    "запись в списке приложений и файлы из профиля.\n" +
                    "(.NET и WebView2 не удаляются.)",
                    "WinPreviewPlus — удаление",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            SetBusy(true);
            _status.Text = "Удаление…";
            _log.Clear();
            try
            {
                await Task.Run(() => Installer.Uninstall(restartExplorer: true));
                RefreshDepsAndState();
                _status.Text = "Удалено полностью.";
                if (!_silentUninstall)
                {
                    MessageBox.Show(this,
                        "WinPreviewPlus удалён.\nПроводник перезапущен — Alt+P больше не использует этот обработчик.",
                        "WinPreviewPlus",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка удаления";
                Installer.Log(MaskPath(ex.ToString()));
                if (!_silentUninstall)
                    MessageBox.Show(this, MaskPath(ex.Message), "WinPreviewPlus", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                RefreshDepsAndState();
            }
        }

        private void SetBusy(bool busy)
        {
            _btnInstall.Enabled = !busy && Installer.HasPayload();
            _btnUninstall.Enabled = !busy;
            if (_chkNet48 != null && !Installer.IsNet48Installed())
                _chkNet48.Enabled = !busy;
            if (_chkWebView2 != null && !Installer.IsWebView2Installed())
                _chkWebView2.Enabled = !busy;
            UseWaitCursor = busy;
        }
    }
}
