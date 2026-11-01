using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NAudio.Wave;
using SharpShell.SharpPreviewHandler;

namespace WinPreviewPlus
{
    public class AudioPreviewControl : PreviewHandlerControl
    {
        private readonly Panel _stage;
        private readonly Panel _card;
        private Label _title;
        private Label _time;
        private SeekBar _seek;
        private Button _btnPlay;
        private Timer _tick;

        private PreviewSource _source;
        private WaveStream _reader;
        private IWavePlayer _player;
        private bool _playing;

        public AudioPreviewControl()
        {
            Dock = DockStyle.Fill;
            BackColor = PreviewUi.Surface;
            Font = PreviewUi.FontUi;
            PreviewUi.EnableDoubleBuffer(this);
            TabStop = true;

            _stage = new Panel { Dock = DockStyle.Fill, BackColor = PreviewUi.Surface };
            PreviewUi.EnableDoubleBuffer(_stage);
            _stage.Resize += (s, e) => LayoutCard();
            Controls.Add(_stage);

            _card = new Panel
            {
                Width = 720,
                Height = 320,
                BackColor = PreviewUi.Surface
            };
            PreviewUi.EnableDoubleBuffer(_card);
            _stage.Controls.Add(_card);
            BuildCard();
            LayoutCard();

            _tick = new Timer { Interval = 200 };
            _tick.Tick += (s, e) => UpdateProgress();
        }

        private void LayoutCard()
        {
            if (_card == null || _stage == null) return;
            // ~3× прежний блок 440×120
            int w = Math.Min(720, Math.Max(420, _stage.ClientSize.Width - 40));
            int h = Math.Min(320, Math.Max(260, _stage.ClientSize.Height - 40));
            _card.Width = w;
            _card.Height = h;
            _card.Left = Math.Max(0, (_stage.ClientSize.Width - _card.Width) / 2);
            _card.Top = Math.Max(0, (_stage.ClientSize.Height - _card.Height) / 2);
            LayoutCardInner();
        }

        private void BuildCard()
        {
            _title = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                ForeColor = PreviewUi.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Text = "Аудио"
            };
            _time = new Label
            {
                AutoSize = false,
                Font = new Font("Consolas", 16f, FontStyle.Regular),
                ForeColor = PreviewUi.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "0:00 / 0:00"
            };
            _btnPlay = new Button
            {
                Text = "▶",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 22f, FontStyle.Regular),
                ForeColor = PreviewUi.TextPrimary,
                BackColor = PreviewUi.TabIdle,
                Cursor = Cursors.Hand,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnPlay.FlatAppearance.BorderSize = 0;
            _btnPlay.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 224, 232);
            _btnPlay.Click += (s, e) => TogglePlayPause();
            new ToolTip().SetToolTip(_btnPlay, "Play / Pause (Пробел)");

            _seek = new SeekBar
            {
                TrackThickness = 10f,
                ThumbRadius = 14f
            };
            _seek.ValueChanged += (s, e) =>
            {
                if (_seek.IsDragging) RefreshTimeLabel(fromBar: true);
            };
            _seek.SeekCommitted += (s, e) => SeekToBar();

            _card.Controls.Add(_title);
            _card.Controls.Add(_btnPlay);
            _card.Controls.Add(_seek);
            _card.Controls.Add(_time);
            LayoutCardInner();
        }

        private void LayoutCardInner()
        {
            if (_card == null || _title == null) return;
            int w = _card.ClientSize.Width;
            int h = _card.ClientSize.Height;
            // Вертикально по центру карточки: title → play+seek → time
            int blockH = 72 + 96 + 40;
            int top = Math.Max(8, (h - blockH) / 2);
            _title.SetBounds(8, top, w - 16, 56);
            int rowY = top + 72;
            int btn = 96;
            _btnPlay.SetBounds(8, rowY, btn, btn);
            _seek.SetBounds(8 + btn + 16, rowY + (btn - 48) / 2, Math.Max(120, w - btn - 40), 48);
            _time.SetBounds(8, rowY + btn + 12, w - 16, 36);
        }

        public void LoadAudio(PreviewSource source)
        {
            StopPlayback(reset: true);
            DisposePlayer();
            _source = source;

            _title.Text = source.DisplayName ?? Path.GetFileName(source.Path) ?? "Аудио";

            try
            {
                _reader = OpenStream(source.Path);
                _player = new WaveOutEvent();
                _player.Init(_reader);
                _player.PlaybackStopped += PlayerOnPlaybackStopped;
                try { ((WaveOutEvent)_player).Volume = 0.85f; } catch { /* ignore */ }
                _seek.Value = 0;
                RefreshTimeLabel(fromBar: false);
                _btnPlay.Enabled = true;
                PreviewLog.Info("Audio preview OK: " + source.Path +
                    " dur=" + FormatTime(_reader.TotalTime));
            }
            catch (Exception ex)
            {
                _btnPlay.Enabled = false;
                _time.Text = "Не удалось открыть";
                PreviewLog.Error("Audio open failed: " + source.Path, ex);
            }
        }

        private static WaveStream OpenStream(string path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            Exception last = null;

            if (ext == ".m4r" || ext == ".m4a" || ext == ".aac" || ext == ".wma" ||
                ext == ".flac" || ext == ".mp4")
            {
                try { return new MediaFoundationReader(path); }
                catch (Exception ex) { last = ex; }
            }
            if (ext == ".wav")
            {
                try { return new WaveFileReader(path); }
                catch (Exception ex) { last = ex; }
            }
            if (ext == ".mp3")
            {
                try { return new Mp3FileReader(path); }
                catch (Exception ex) { last = ex; }
            }
            if (ext == ".aiff" || ext == ".aif")
            {
                try { return new AiffFileReader(path); }
                catch (Exception ex) { last = ex; }
            }

            try { return new AudioFileReader(path); }
            catch (Exception ex) { last = ex; }

            try { return new MediaFoundationReader(path); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Формат не поддерживается или нет кодека. " + (ex.Message ?? last?.Message),
                    ex);
            }
        }

        public void TogglePlayPause()
        {
            if (_player == null || _reader == null) return;
            try
            {
                if (_playing)
                {
                    _player.Pause();
                    _playing = false;
                    _btnPlay.Text = "▶";
                    _tick.Stop();
                }
                else
                {
                    if (_reader.Position >= _reader.Length && _reader.Length > 0)
                        _reader.Position = 0;
                    _player.Play();
                    _playing = true;
                    _btnPlay.Text = "⏸";
                    _tick.Start();
                }
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Audio play/pause", ex);
                _time.Text = "Ошибка воспроизведения";
            }
        }

        private void StopPlayback(bool reset)
        {
            try { _player?.Stop(); } catch { /* ignore */ }
            _playing = false;
            if (_btnPlay != null) _btnPlay.Text = "▶";
            _tick?.Stop();
            if (reset && _reader != null)
            {
                try { _reader.Position = 0; } catch { /* ignore */ }
                if (_seek != null) _seek.Value = 0;
                RefreshTimeLabel(fromBar: false);
            }
        }

        private void PlayerOnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _playing = false;
                    _btnPlay.Text = "▶";
                    _tick.Stop();
                    if (e.Exception != null)
                    {
                        PreviewLog.Error("Audio PlaybackStopped", e.Exception);
                        _time.Text = "Ошибка";
                    }
                    else if (_reader != null && _reader.Position >= _reader.Length)
                    {
                        try { _reader.Position = 0; } catch { /* ignore */ }
                        _seek.Value = 0;
                        RefreshTimeLabel(fromBar: false);
                    }
                }));
            }
            catch { /* disposed */ }
        }

        private void SeekToBar()
        {
            if (_reader == null || _reader.Length <= 0) return;
            try
            {
                double t = _seek.Value / (double)_seek.Maximum;
                long pos = (long)(t * _reader.Length);
                int align = Math.Max(1, _reader.WaveFormat.BlockAlign);
                pos -= pos % align;
                if (pos < 0) pos = 0;
                if (pos > _reader.Length) pos = _reader.Length;
                _reader.Position = pos;
                RefreshTimeLabel(fromBar: false);
            }
            catch (Exception ex)
            {
                PreviewLog.Error("Audio seek", ex);
            }
        }

        private void UpdateProgress()
        {
            if (_seek == null || _seek.IsDragging || _reader == null || _reader.Length <= 0) return;
            try
            {
                int v = (int)Math.Round(_reader.Position * (double)_seek.Maximum / _reader.Length);
                v = Math.Max(0, Math.Min(_seek.Maximum, v));
                if (_seek.Value != v) _seek.Value = v;
                RefreshTimeLabel(fromBar: false);
            }
            catch { /* ignore */ }
        }

        private void RefreshTimeLabel(bool fromBar)
        {
            if (_time == null) return;
            if (_reader == null)
            {
                _time.Text = "0:00 / 0:00";
                return;
            }
            TimeSpan cur;
            if (fromBar && _reader.Length > 0)
            {
                double t = _seek.Value / (double)_seek.Maximum;
                cur = TimeSpan.FromSeconds(t * _reader.TotalTime.TotalSeconds);
            }
            else
            {
                try { cur = _reader.CurrentTime; }
                catch { cur = TimeSpan.Zero; }
            }
            TimeSpan total;
            try { total = _reader.TotalTime; }
            catch { total = TimeSpan.Zero; }
            _time.Text = FormatTime(cur) + " / " + FormatTime(total);
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1)
                return ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }

        private void DisposePlayer()
        {
            _tick?.Stop();
            try
            {
                if (_player != null)
                {
                    _player.PlaybackStopped -= PlayerOnPlaybackStopped;
                    _player.Stop();
                    _player.Dispose();
                    _player = null;
                }
            }
            catch { /* ignore */ }
            try
            {
                _reader?.Dispose();
                _reader = null;
            }
            catch { /* ignore */ }
            _playing = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePlayer();
                if (_tick != null)
                {
                    _tick.Dispose();
                    _tick = null;
                }
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
