using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    /// <summary>
    /// Превью растров (GDI+) и SVG (WebView2). Ctrl+колёсико — зум, drag — pan.
    /// HEIC/AVIF — через WIC, если в системе есть кодек (часто «Расширения для изображений HEIF»).
    /// </summary>
    public class ImagePreviewControl : PreviewHandlerControl
    {
        private const int MaxDecodeSide = 8192;
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 32f;

        private readonly Panel _bar;
        private readonly Panel _view;
        private readonly ZoomCanvas _canvas;
        private readonly Label _meta;
        private readonly Label _zoomLbl;
        private Button _btnFit;
        private Button _btn100;
        private Button _btnZoomOut;
        private Button _btnZoomIn;

        private PreviewSource _source;
        private Image _image;
        private int _naturalW;
        private int _naturalH;
        private bool _downscaled;
        private bool _svgMode;
        private WebView2 _web;
        private bool _webReady;

        private float _zoom = 1f;
        private float _ox;
        private float _oy;
        private bool _fitMode = true;

        private bool _panning;
        private Point _panLast;

        public ImagePreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = PreviewUi.HeaderBg,
                Padding = new Padding(6, 4, 6, 4)
            };
            PreviewUi.EnableDoubleBuffer(_bar);

            _btnZoomOut = MakeBtn("−");
            _btnZoomIn = MakeBtn("+");
            _btnFit = MakeBtn("⌂");
            _btn100 = MakeBtn("1:1");
            _btnZoomOut.Click += (s, e) =>
            {
                if (_svgMode) SvgCmd("zoomBy", 1f / 1.2f);
                else ZoomBy(1f / 1.2f, null);
            };
            _btnZoomIn.Click += (s, e) =>
            {
                if (_svgMode) SvgCmd("zoomBy", 1.2f);
                else ZoomBy(1.2f, null);
            };
            _btnFit.Click += (s, e) =>
            {
                if (_svgMode) SvgCmd("fit", 0);
                else FitToWindow();
            };
            _btn100.Click += (s, e) =>
            {
                if (_svgMode) SvgCmd("zoomAbs", 1f);
                else ZoomAbsolute(1f, null);
            };

            _zoomLbl = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted,
                Text = "100%"
            };
            _meta = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PreviewUi.FontMuted,
                ForeColor = PreviewUi.TextMuted,
                AutoEllipsis = true,
                Text = ""
            };

            var tip = new ToolTip();
            tip.SetToolTip(_btnZoomOut, "Уменьшить (Ctrl+колёсико)");
            tip.SetToolTip(_btnZoomIn, "Увеличить (Ctrl+колёсико)");
            tip.SetToolTip(_btnFit, "Вписать в окно");
            tip.SetToolTip(_btn100, "100%");

            _bar.Controls.Add(_btnZoomOut);
            _bar.Controls.Add(_btnZoomIn);
            _bar.Controls.Add(_btnFit);
            _bar.Controls.Add(_btn100);
            _bar.Controls.Add(_zoomLbl);
            _bar.Controls.Add(_meta);
            _bar.Resize += (s, e) => LayoutBar();

            _view = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 34, 38) };
            PreviewUi.EnableDoubleBuffer(_view);

            _canvas = new ZoomCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 34, 38),
                TabStop = true
            };
            PreviewUi.EnableDoubleBuffer(_canvas);
            _canvas.Paint += CanvasOnPaint;
            _canvas.Resize += (s, e) =>
            {
                if (_fitMode && !_svgMode) FitToWindow();
                else _canvas.Invalidate();
            };
            _canvas.MouseEnter += (s, e) =>
            {
                try { if (!_canvas.Focused) _canvas.Focus(); } catch { }
            };
            _canvas.MouseDown += CanvasOnMouseDown;
            _canvas.MouseMove += CanvasOnMouseMove;
            _canvas.MouseUp += CanvasOnMouseUp;
            _canvas.Wheel = CanvasOnMouseWheel;

            _view.Controls.Add(_canvas);
            Controls.Add(_view);
            Controls.Add(_bar);
            LayoutBar();
        }

        public void LoadImage(PreviewSource source)
        {
            DisposeImage();
            DisposeWeb();
            _source = source;
            _svgMode = false;
            _webReady = false;
            _canvas.Visible = true;

            string ext = (Path.GetExtension(source?.Path) ?? "").ToLowerInvariant();
            if (ext == ".svg" || ext == ".svgz")
            {
                LoadSvg(source);
                return;
            }

            try
            {
                _image = DecodeImage(source.Path, out _naturalW, out _naturalH, out _downscaled);
                string name = source.DisplayName ?? Path.GetFileName(source.Path) ?? "Изображение";
                string sizeNote = _downscaled
                    ? (" · показ до " + _image.Width + "×" + _image.Height + " (исх. " + _naturalW + "×" + _naturalH + ")")
                    : (" · " + _image.Width + "×" + _image.Height);
                _meta.Text = name + sizeNote;
                FitToWindow();
                PreviewLog.Info("Image preview OK: " + source.Path +
                    " " + _image.Width + "x" + _image.Height +
                    (_downscaled ? " (downscaled)" : ""));
            }
            catch (Exception ex)
            {
                string hint = "";
                if (ext == ".heic" || ext == ".heif" || ext == ".heics" || ext == ".avif")
                    hint = " Установите «Расширения для изображений HEIF / AV1» из Microsoft Store.";
                _meta.Text = "Не удалось открыть" + (string.IsNullOrEmpty(hint) ? "" : " — кодек?");
                PreviewLog.Error("Image preview failed: " + source?.Path + hint, ex);
                _canvas.Invalidate();
            }
            LayoutBar();
        }

        private void LoadSvg(PreviewSource source)
        {
            _svgMode = true;
            _canvas.Visible = false;
            string name = source.DisplayName ?? Path.GetFileName(source.Path) ?? "SVG";
            _meta.Text = name + " · SVG";
            _zoomLbl.Text = "—";
            LayoutBar();

            try
            {
                if (string.IsNullOrEmpty(source?.Path) || !File.Exists(source.Path))
                    throw new FileNotFoundException("Файл не найден", source?.Path);

                _web = new WebView2
                {
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.FromArgb(32, 34, 38)
                };
                _view.Controls.Add(_web);
                _web.BringToFront();
                _web.CoreWebView2InitializationCompleted += SvgOnInitCompleted;
                var task = _web.EnsureCoreWebView2Async(null);
                task.ContinueWith(t =>
                {
                    if (!t.IsFaulted) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _meta.Text = "SVG: нужен WebView2 Runtime";
                            PreviewLog.Error("SVG WebView2 Ensure failed", t.Exception);
                        }));
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                _meta.Text = "SVG не открыт";
                PreviewLog.Error("SVG Load failed: " + source?.Path, ex);
            }
        }

        private void SvgOnInitCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _web == null)
            {
                _meta.Text = "SVG: WebView2 не стартовал";
                PreviewLog.Error("SVG WebView2 init", e.InitializationException);
                return;
            }
            try
            {
                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                // Ctrl+колёсико зума страницы Edge — включаем + свой JS pan
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;

                var path = _source.Path;
                var folder = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);
                core.SetVirtualHostNameToFolderMapping(
                    "winpreview.image",
                    folder,
                    CoreWebView2HostResourceAccessKind.Allow);

                string src = "https://winpreview.image/" + Uri.EscapeDataString(fileName);
                core.NavigateToString(BuildSvgHtml(src, fileName));
                _webReady = true;
                _zoomLbl.Text = "fit";
                LayoutBar();
                PreviewLog.Info("SVG WebView2 OK: " + path);
            }
            catch (Exception ex)
            {
                _meta.Text = "SVG ошибка";
                PreviewLog.Error("SVG navigate failed", ex);
            }
        }

        private static string BuildSvgHtml(string src, string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.Append("<title>").Append(HtmlEnc(title)).Append("</title><style>");
            sb.Append("html,body{margin:0;height:100%;overflow:hidden;background:#202226;cursor:grab}");
            sb.Append("body.drag{cursor:grabbing}");
            sb.Append("#stage{position:absolute;left:0;top:0;transform-origin:0 0}");
            sb.Append("img,object,svg{display:block;max-width:none;user-select:none;-webkit-user-drag:none}");
            sb.Append("</style></head><body>");
            sb.Append("<div id='stage'><img id='pic' alt='' src='").Append(HtmlEnc(src)).Append("'/></div>");
            sb.Append("<script>");
            sb.Append("var z=1,ox=0,oy=0,fit=true,drag=false,lx=0,ly=0;");
            sb.Append("var stage=document.getElementById('stage'),pic=document.getElementById('pic');");
            sb.Append("function apply(){stage.style.transform='translate('+ox+'px,'+oy+'px) scale('+z+');}");
            sb.Append("function doFit(){fit=true;var vw=innerWidth,vh=innerHeight,w=pic.naturalWidth||pic.width,h=pic.naturalHeight||pic.height;");
            sb.Append("if(!w||!h){z=1;ox=oy=0;apply();return;}");
            sb.Append("z=Math.min(vw/w,vh/h);ox=(vw-w*z)/2;oy=(vh-h*z)/2;apply();}");
            sb.Append("pic.onload=function(){doFit();};");
            sb.Append("window.addEventListener('resize',function(){if(fit)doFit();});");
            sb.Append("window.addEventListener('wheel',function(e){");
            sb.Append("if(!e.ctrlKey){oy+=e.deltaY*-0.5;fit=false;apply();e.preventDefault();return;}");
            sb.Append("e.preventDefault();fit=false;var f=e.deltaY<0?1.12:1/1.12;");
            sb.Append("var nx=e.clientX,ny=e.clientY,ix=(nx-ox)/z,iy=(ny-oy)/z;");
            sb.Append("z=Math.max(0.05,Math.min(32,z*f));ox=nx-ix*z;oy=ny-iy*z;apply();");
            sb.Append("},{passive:false});");
            sb.Append("window.addEventListener('mousedown',function(e){if(e.button!==0)return;drag=true;lx=e.clientX;ly=e.clientY;document.body.classList.add('drag');});");
            sb.Append("window.addEventListener('mousemove',function(e){if(!drag)return;fit=false;ox+=e.clientX-lx;oy+=e.clientY-ly;lx=e.clientX;ly=e.clientY;apply();});");
            sb.Append("window.addEventListener('mouseup',function(){drag=false;document.body.classList.remove('drag');});");
            sb.Append("window.wpp={fit:doFit,zoomBy:function(f){fit=false;var cx=innerWidth/2,cy=innerHeight/2,ix=(cx-ox)/z,iy=(cy-oy)/z;z=Math.max(0.05,Math.min(32,z*f));ox=cx-ix*z;oy=cy-iy*z;apply();},");
            sb.Append("zoomAbs:function(v){fit=false;var cx=innerWidth/2,cy=innerHeight/2,ix=(cx-ox)/z,iy=(cy-oy)/z;z=Math.max(0.05,Math.min(32,v));ox=cx-ix*z;oy=cy-iy*z;apply();}};");
            sb.Append("</script></body></html>");
            return sb.ToString();
        }

        private static string HtmlEnc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;")
                    .Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void SvgCmd(string name, float arg)
        {
            if (!_webReady || _web?.CoreWebView2 == null) return;
            try
            {
                string js;
                if (name == "fit") js = "window.wpp&&wpp.fit()";
                else if (name == "zoomBy")
                    js = "window.wpp&&wpp.zoomBy(" + arg.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                else
                    js = "window.wpp&&wpp.zoomAbs(" + arg.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("SVG cmd " + name, ex);
            }
        }

        private static Image DecodeImage(string path, out int naturalW, out int naturalH, out bool downscaled)
        {
            naturalW = naturalH = 0;
            downscaled = false;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var raw = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false))
            {
                naturalW = raw.Width;
                naturalH = raw.Height;
                try { raw.RotateFlip(ExifRotate(raw)); } catch { }

                int maxSide = Math.Max(raw.Width, raw.Height);
                if (maxSide <= MaxDecodeSide)
                    return new Bitmap(raw);

                float scale = (float)MaxDecodeSide / maxSide;
                int w = Math.Max(1, (int)Math.Round(raw.Width * scale));
                int h = Math.Max(1, (int)Math.Round(raw.Height * scale));
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(raw, new Rectangle(0, 0, w, h));
                }
                downscaled = true;
                return bmp;
            }
        }

        private static RotateFlipType ExifRotate(Image img)
        {
            const int OrientationId = 0x0112;
            if (Array.IndexOf(img.PropertyIdList, OrientationId) < 0)
                return RotateFlipType.RotateNoneFlipNone;
            try
            {
                var p = img.GetPropertyItem(OrientationId);
                switch (p.Value[0])
                {
                    case 2: return RotateFlipType.RotateNoneFlipX;
                    case 3: return RotateFlipType.Rotate180FlipNone;
                    case 4: return RotateFlipType.RotateNoneFlipY;
                    case 5: return RotateFlipType.Rotate90FlipX;
                    case 6: return RotateFlipType.Rotate90FlipNone;
                    case 7: return RotateFlipType.Rotate270FlipX;
                    case 8: return RotateFlipType.Rotate270FlipNone;
                    default: return RotateFlipType.RotateNoneFlipNone;
                }
            }
            catch { return RotateFlipType.RotateNoneFlipNone; }
        }

        private void FitToWindow()
        {
            _fitMode = true;
            if (_image == null || _canvas.ClientSize.Width < 2 || _canvas.ClientSize.Height < 2)
            {
                RefreshZoomLabel();
                _canvas.Invalidate();
                return;
            }
            float zx = (float)_canvas.ClientSize.Width / _image.Width;
            float zy = (float)_canvas.ClientSize.Height / _image.Height;
            _zoom = Math.Max(MinZoom, Math.Min(zx, zy));
            float dw = _image.Width * _zoom;
            float dh = _image.Height * _zoom;
            _ox = (_canvas.ClientSize.Width - dw) / 2f;
            _oy = (_canvas.ClientSize.Height - dh) / 2f;
            RefreshZoomLabel();
            _canvas.Invalidate();
        }

        private void ZoomAbsolute(float zoom, Point? pivotClient)
        {
            _fitMode = false;
            ApplyZoom(Math.Max(MinZoom, Math.Min(MaxZoom, zoom)), pivotClient ?? CenterClient());
        }

        private void ZoomBy(float factor, Point? pivotClient)
        {
            ZoomAbsolute(_zoom * factor, pivotClient);
        }

        private Point CenterClient()
        {
            return new Point(_canvas.ClientSize.Width / 2, _canvas.ClientSize.Height / 2);
        }

        private void ApplyZoom(float newZoom, Point pivot)
        {
            if (_image == null) return;
            float imgX = (pivot.X - _ox) / _zoom;
            float imgY = (pivot.Y - _oy) / _zoom;
            _zoom = newZoom;
            _ox = pivot.X - imgX * _zoom;
            _oy = pivot.Y - imgY * _zoom;
            ClampPan();
            RefreshZoomLabel();
            _canvas.Invalidate();
        }

        private void ClampPan()
        {
            if (_image == null) return;
            float dw = _image.Width * _zoom;
            float dh = _image.Height * _zoom;
            int cw = _canvas.ClientSize.Width;
            int ch = _canvas.ClientSize.Height;
            if (dw <= cw) _ox = (cw - dw) / 2f;
            else { if (_ox > 0) _ox = 0; if (_ox + dw < cw) _ox = cw - dw; }
            if (dh <= ch) _oy = (ch - dh) / 2f;
            else { if (_oy > 0) _oy = 0; if (_oy + dh < ch) _oy = ch - dh; }
        }

        private void CanvasOnMouseWheel(MouseEventArgs e)
        {
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            if (ctrl)
            {
                float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
                ZoomBy(factor, e.Location);
            }
            else if (_image != null)
            {
                _fitMode = false;
                _oy += e.Delta / 3f;
                ClampPan();
                _canvas.Invalidate();
            }
        }

        private void CanvasOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _panning = true;
                _panLast = e.Location;
                _canvas.Capture = true;
                _canvas.Cursor = Cursors.SizeAll;
                try { _canvas.Focus(); } catch { }
            }
        }

        private void CanvasOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning || _image == null) return;
            _fitMode = false;
            _ox += e.X - _panLast.X;
            _oy += e.Y - _panLast.Y;
            _panLast = e.Location;
            ClampPan();
            _canvas.Invalidate();
        }

        private void CanvasOnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _panning = false;
                _canvas.Capture = false;
                _canvas.Cursor = Cursors.Default;
            }
        }

        private void CanvasOnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_canvas.BackColor);
            if (_image == null) return;
            var dest = new RectangleF(_ox, _oy, _image.Width * _zoom, _image.Height * _zoom);
            if (_zoom >= 3f)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.DrawImage(_image, dest);
        }

        private void RefreshZoomLabel()
        {
            if (_zoomLbl != null)
                _zoomLbl.Text = (int)Math.Round(_zoom * 100) + "%";
            LayoutBar();
        }

        private Button MakeBtn(string text)
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
                Size = new Size(32, 26)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            return b;
        }

        private void LayoutBar()
        {
            if (_bar == null) return;
            int x = 6;
            int y = (_bar.ClientSize.Height - 26) / 2;
            void Place(Control c, int w)
            {
                c.SetBounds(x, y, w, 26);
                x += w + 4;
            }
            Place(_btnZoomOut, 32);
            Place(_btnZoomIn, 32);
            Place(_btnFit, 32);
            Place(_btn100, 40);
            int zl = TextRenderer.MeasureText(_zoomLbl.Text ?? "100%", PreviewUi.FontMuted).Width + 8;
            Place(_zoomLbl, zl);
            x += 8;
            _meta.SetBounds(x, y, Math.Max(40, _bar.ClientSize.Width - x - 8), 26);
        }

        private void DisposeImage()
        {
            if (_image != null)
            {
                try { _image.Dispose(); } catch { }
                _image = null;
            }
        }

        private void DisposeWeb()
        {
            _webReady = false;
            if (_web == null) return;
            try
            {
                _web.CoreWebView2InitializationCompleted -= SvgOnInitCompleted;
                _view.Controls.Remove(_web);
                _web.Dispose();
            }
            catch { }
            _web = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeWeb();
                DisposeImage();
                _source?.Dispose();
                _source = null;
            }
            base.Dispose(disposing);
        }

        private sealed class ZoomCanvas : Panel
        {
            public Action<MouseEventArgs> Wheel;

            public ZoomCanvas()
            {
                SetStyle(ControlStyles.Selectable | ControlStyles.UserMouse, true);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                Wheel?.Invoke(e);
                if (e is HandledMouseEventArgs he)
                    he.Handled = true;
                base.OnMouseWheel(e);
            }
        }
    }
}
