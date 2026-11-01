using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// Видео через WebView2 / HTML5 &lt;video&gt;.
    /// Не требует Windows Media Player (на Win11 его часто нет → CLASSNOTREG / «нужен WMP 10»).
    /// Кодеки — из Microsoft Edge.
    /// </summary>
    public class VideoPreviewControl : PreviewHandlerControl
    {
        private readonly Panel _root;
        private WebView2 _web;
        private Label _status;
        private PreviewSource _source;
        private string _mappedFolder;
        private bool _ready;

        public VideoPreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Black;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _root = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            PreviewUi.EnableDoubleBuffer(_root);
            Controls.Add(_root);
        }

        public void LoadVideo(PreviewSource source)
        {
            DisposeWeb();
            _source = source;
            _ready = false;
            _root.Controls.Clear();
            ShowStatus("Загрузка…");

            try
            {
                if (string.IsNullOrEmpty(source?.Path) || !File.Exists(source.Path))
                    throw new FileNotFoundException("Файл не найден", source?.Path);

                _web = new WebView2
                {
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.Black
                };
                _root.Controls.Clear();
                _root.Controls.Add(_web);

                _web.CoreWebView2InitializationCompleted += WebOnInitCompleted;
                var task = _web.EnsureCoreWebView2Async(null);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                var msg = t.Exception?.GetBaseException().Message ?? "WebView2 init failed";
                                ShowError("WebView2 не запустился:\n" + msg +
                                          "\n\nУстановите «WebView2 Runtime» (обычно уже есть с Edge).");
                                PreviewLog.Error("WebView2 Ensure failed", t.Exception);
                            }));
                        }
                        catch { /* disposed */ }
                    }
                });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть видео:\n" + ex.Message);
                PreviewLog.Error("Video LoadVideo failed: " + source?.Path, ex);
            }
        }

        private void WebOnInitCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                ShowError("WebView2 не инициализировался:\n" +
                          (e.InitializationException?.Message ?? "unknown") +
                          "\n\nУстановите Microsoft Edge WebView2 Runtime.");
                PreviewLog.Error("WebView2 init not success", e.InitializationException);
                return;
            }

            try
            {
                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;

                var path = _source.Path;
                _mappedFolder = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);

                // Виртуальный хост — иначе file:// для video часто блокируется
                core.SetVirtualHostNameToFolderMapping(
                    "winpreview.media",
                    _mappedFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                string src = "https://winpreview.media/" + Uri.EscapeDataString(fileName);
                string html = BuildPlayerHtml(src, fileName);
                core.NavigateToString(html);
                _ready = true;
                PreviewLog.Info("Video WebView2 OK: " + path);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось показать видео:\n" + ex.Message);
                PreviewLog.Error("Video WebView2 navigate failed", ex);
            }
        }

        private static string BuildPlayerHtml(string videoSrc, string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.Append("<title>").Append(HtmlEncode(title)).Append("</title>");
            sb.Append("<style>");
            sb.Append("html,body{margin:0;height:100%;background:#000;overflow:hidden;font-family:Segoe UI,sans-serif}");
            sb.Append("video{width:100%;height:100%;object-fit:contain;outline:none;background:#000}");
            sb.Append("</style></head><body>");
            sb.Append("<video id='v' controls playsinline preload='metadata' src='");
            sb.Append(HtmlEncode(videoSrc));
            sb.Append("'></video>");
            sb.Append("<script>");
            sb.Append("document.addEventListener('keydown',function(e){");
            sb.Append("if(e.code==='Space'){e.preventDefault();var v=document.getElementById('v');");
            sb.Append("if(v.paused)v.play();else v.pause();}");
            sb.Append("});");
            sb.Append("</script></body></html>");
            return sb.ToString();
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;")
                    .Replace("<", "&lt;").Replace(">", "&gt;");
        }

        public void TogglePlayPause()
        {
            if (!_ready || _web?.CoreWebView2 == null) return;
            try
            {
                _ = _web.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var v=document.getElementById('v');if(!v)return;" +
                    "if(v.paused)v.play();else v.pause();})()");
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Video TogglePlayPause", ex);
            }
        }

        private void ShowStatus(string text)
        {
            _root.Controls.Clear();
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = PreviewUi.TextMuted,
                BackColor = Color.Black,
                Font = PreviewUi.FontUi,
                Text = text
            };
            _root.Controls.Add(_status);
        }

        private void ShowError(string message)
        {
            _root.Controls.Clear();
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = PreviewUi.Danger,
                BackColor = PreviewUi.Surface,
                Font = PreviewUi.FontUi,
                Text = message,
                Padding = new Padding(16)
            };
            _root.Controls.Add(_status);
        }

        private void DisposeWeb()
        {
            _ready = false;
            try
            {
                if (_web != null)
                {
                    _web.CoreWebView2InitializationCompleted -= WebOnInitCompleted;
                    _root.Controls.Remove(_web);
                    _web.Dispose();
                    _web = null;
                }
            }
            catch { /* ignore */ }
            _mappedFolder = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeWeb();
                if (_source != null)
                {
                    _source.Dispose();
                    _source = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                TogglePlayPause();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
