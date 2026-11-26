using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

class SmtcServer
{
    static MediaPlayer player;
    static SystemMediaTransportControls smtc;
    static JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static IRandomAccessStream lastThumbStream = null;
    static readonly object smtcLock = new object();
    static readonly ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();
    static long pendingSeekMs = -1;
    static long lastSeekTicks = 0;
    static double lastDuration = 0;
    static string lastTitle = null;
    static string lastCover = "";

    static void Main()
    {
        try {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "music_center_cover_*.jpg")) {
                File.Delete(f);
            }
        } catch {}

        try { Run(); }
        catch (Exception e) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "smtc_err.txt"), e.ToString()); }
    }

    static void Run()
    {
        player = new MediaPlayer();
        player.CommandManager.IsEnabled = false;

        smtc = player.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsNextEnabled = true;
        smtc.IsPreviousEnabled = true;
        smtc.PlaybackRate = 1.0;
        smtc.ButtonPressed += OnButtonPressed;
        smtc.PlaybackPositionChangeRequested += OnSeekRequested;

        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:9999/");
        listener.Start();

        while (true)
        {
            var ctx = listener.GetContext();
            var req = ctx.Request;
            var res = ctx.Response;

            try
            {
                if (req.Url.AbsolutePath == "/update" && req.HttpMethod == "POST")
                {
                    string json = "";
                    using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }

                    var data = js.Deserialize<Dictionary<string, object>>(json);
                    if (data != null)
                    {
                        UpdateSmtc(data);
                    }

                    var responseBytes = Encoding.UTF8.GetBytes("OK");
                    res.ContentLength64 = responseBytes.Length;
                    res.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                }
                else if (req.Url.AbsolutePath == "/poll" && req.HttpMethod == "GET")
                {
                    string command = "";
                    long seekMs = Interlocked.Exchange(ref pendingSeekMs, -1);
                    if (seekMs >= 0)
                    {
                        command = "seek:" + seekMs.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (!commandQueue.TryDequeue(out command))
                    {
                        command = "";
                    }
                    res.AddHeader("Access-Control-Allow-Origin", "*");
                    var responseBytes = Encoding.UTF8.GetBytes(command);
                    res.ContentLength64 = responseBytes.Length;
                    res.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                }
                else
                {
                    res.StatusCode = 404;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "smtc_req_err.txt"), e.ToString());
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }
    }

    static void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                commandQueue.Enqueue("play");
                break;
            case SystemMediaTransportControlsButton.Pause:
                commandQueue.Enqueue("pause");
                break;
            case SystemMediaTransportControlsButton.Next:
                Interlocked.Exchange(ref pendingSeekMs, -1);
                Interlocked.Exchange(ref lastSeekTicks, 0);
                commandQueue.Enqueue("next");
                break;
            case SystemMediaTransportControlsButton.Previous:
                Interlocked.Exchange(ref pendingSeekMs, -1);
                Interlocked.Exchange(ref lastSeekTicks, 0);
                commandQueue.Enqueue("prev");
                break;
        }
    }

    static void OnSeekRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        long ms = (long)args.RequestedPlaybackPosition.TotalMilliseconds;
        if (ms < 0) ms = 0;
        Interlocked.Exchange(ref pendingSeekMs, ms);
        Interlocked.Exchange(ref lastSeekTicks, DateTime.UtcNow.Ticks);

        lock (smtcLock)
        {
            if (lastDuration > 0)
            {
                double posSec = ms / 1000.0;
                if (posSec > lastDuration) posSec = lastDuration;
                ApplyTimeline(posSec, lastDuration);
            }
        }
    }

    static void ApplyTimeline(double posSec, double durSec)
    {
        if (!(durSec > 0)) return;
        if (posSec < 0) posSec = 0;
        if (posSec > durSec) posSec = durSec;

        var timeline = new SystemMediaTransportControlsTimelineProperties();
        timeline.StartTime = TimeSpan.Zero;
        timeline.MinSeekTime = TimeSpan.Zero;
        timeline.Position = TimeSpan.FromSeconds(posSec);
        timeline.MaxSeekTime = TimeSpan.FromSeconds(durSec);
        timeline.EndTime = TimeSpan.FromSeconds(durSec);
        smtc.UpdateTimelineProperties(timeline);
    }

    static void UpdateSmtc(Dictionary<string, object> data)
    {
        string title = data.ContainsKey("title") ? data["title"] as string : "";
        string artist = data.ContainsKey("artist") ? data["artist"] as string : "";
        string album = data.ContainsKey("album") ? data["album"] as string : "";
        string state = data.ContainsKey("state") ? data["state"] as string : "";
        string position = data.ContainsKey("position") ? Convert.ToString(data["position"], CultureInfo.InvariantCulture) : "0";
        string duration = data.ContainsKey("duration") ? Convert.ToString(data["duration"], CultureInfo.InvariantCulture) : "0";
        string cover = data.ContainsKey("cover") ? data["cover"] as string : "";
        bool metaChanged = data.ContainsKey("metaChanged") && data["metaChanged"] is bool ? (bool)data["metaChanged"] : true;

        double durSec = 0;
        double.TryParse(duration, NumberStyles.Any, CultureInfo.InvariantCulture, out durSec);
        if (!(durSec > 0)) durSec = 0;

        double posSec = 0;
        double.TryParse(position, NumberStyles.Any, CultureInfo.InvariantCulture, out posSec);
        if (!(posSec >= 0)) posSec = 0;

        lock (smtcLock)
        {
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.PlaybackRate = 1.0;

            if (state == "playing") smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            else if (state == "paused") smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            else smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;

            bool titleChanged = title != lastTitle;
            if (titleChanged)
            {
                Interlocked.Exchange(ref lastSeekTicks, 0);
            }

            if (metaChanged || titleChanged || cover != lastCover)
            {
                lastTitle = title;
                var updater = smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = title ?? "";
                updater.MusicProperties.Artist = artist ?? "";
                updater.MusicProperties.AlbumTitle = album ?? "";

                if (cover != lastCover || titleChanged)
                {
                    lastCover = cover ?? "";
                    updater.Thumbnail = CreateThumbnail(lastCover);
                }
                updater.Update();
            }

            if (durSec > 0)
            {
                bool durChanged = Math.Abs(durSec - lastDuration) > 1.0;
                lastDuration = durSec;
                bool recentlySeeked = (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastSeekTicks)) < TimeSpan.FromMilliseconds(1500).Ticks;
                if (!recentlySeeked || titleChanged || durChanged)
                {
                    ApplyTimeline(posSec, durSec);
                }
            }
        }
    }

    static RandomAccessStreamReference CreateThumbnail(string cover)
    {
        if (string.IsNullOrEmpty(cover)) return null;
        try
        {
            byte[] imgData = null;
            if (cover.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = cover.IndexOf(',');
                if (comma != -1) imgData = Convert.FromBase64String(cover.Substring(comma + 1));
            }
            else if (cover.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                string clean = StripQuery(cover);
                imgData = File.ReadAllBytes(new Uri(clean).LocalPath);
            }
            else if (cover.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     cover.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return RandomAccessStreamReference.CreateFromUri(new Uri(cover));
            }
            else
            {
                string clean2 = StripQuery(cover);
                if (File.Exists(clean2)) imgData = File.ReadAllBytes(clean2);
            }

            if (imgData != null && imgData.Length > 0)
            {
                try { File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "music_center_cover_current.jpg"), imgData); } catch {}
                var stream = new InMemoryRandomAccessStream();
                var dwb = new DataWriter();
                dwb.WriteBytes(imgData);
                var wop = stream.WriteAsync(dwb.DetachBuffer());
                while (wop.Status == AsyncStatus.Started) Thread.Sleep(1);
                wop.GetResults();
                stream.Seek(0);
                if (lastThumbStream != null) { try { lastThumbStream.Dispose(); } catch {} }
                lastThumbStream = stream;
                return RandomAccessStreamReference.CreateFromStream(stream);
            }
        }
        catch {}
        return null;
    }

    static string StripQuery(string s)
    {
        int q = s.IndexOf('?');
        if (q >= 0) s = s.Substring(0, q);
        int h = s.IndexOf('#');
        if (h >= 0) s = s.Substring(0, h);
        return s;
    }
}
