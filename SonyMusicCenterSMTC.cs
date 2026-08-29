using System;
using System.Net;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.Media;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

class SmtcServer
{
    static MediaPlayer player;
    static string lastAction = "";
    static object actionLock = new object();
    static TimeSpan currentVirtualPos = TimeSpan.Zero;
    static double lastDuration = 0;

    [MTAThread]
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

    static string logFile = Path.Combine(Path.GetTempPath(), "smtc_debug_full.txt");
    static void Log(string message) {
        try {
            File.AppendAllText(logFile, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, message));
        } catch {}
    }

    static void Run()
    {
        player = new MediaPlayer();
        player.CommandManager.IsEnabled = true;
        player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;

        player.CommandManager.PlayReceived += (sender, args) => { lock (actionLock) { lastAction = "play"; } };
        player.CommandManager.PauseReceived += (sender, args) => { lock (actionLock) { lastAction = "pause"; } };
        player.CommandManager.NextReceived += (sender, args) => { lock (actionLock) { lastAction = "next"; } };
        player.CommandManager.PreviousReceived += (sender, args) => { lock (actionLock) { lastAction = "prev"; } };

        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:9999/");
        listener.Start();
        
        var js = new JavaScriptSerializer();
        js.MaxJsonLength = 50 * 1024 * 1024;

        while (true)
        {
            var context = listener.GetContext();
            var req = context.Request;
            var res = context.Response;
            res.AppendHeader("Access-Control-Allow-Origin", "*");

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
                    
                    string title = data.ContainsKey("title") ? data["title"] as string : null;
                    string artist = data.ContainsKey("artist") ? data["artist"] as string : null;
                    string album = data.ContainsKey("album") ? data["album"] as string : null;
                    string state = data.ContainsKey("state") ? data["state"] as string : null;
                    string position = data.ContainsKey("position") ? data["position"] as string : null;
                    string duration = data.ContainsKey("duration") ? data["duration"] as string : null;
                    string cover = data.ContainsKey("cover") ? data["cover"] as string : null; try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "smtc_log.txt"), string.Format("[{0}] Title: {1}, Cover: {2}\r\n", DateTime.Now, title, cover == null ? "NULL" : (cover.Length > 100 ? cover.Substring(0, 100) + "..." : cover))); } catch {}
                    bool metaChanged = data.ContainsKey("metaChanged") ? (bool)data["metaChanged"] : true;

                    double durSec = 0;
                    if (duration != null) double.TryParse(duration, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out durSec);
                    double posSec = 0;
                    if (position != null) double.TryParse(position, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posSec);

                    if (metaChanged || player.Source == null || Math.Abs(durSec - lastDuration) > 1.0)
                    {
                        lastDuration = durSec;
                        var desc = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(44100, 2, 16));
                        var mss = new MediaStreamSource(desc);
                        mss.CanSeek = true;
                        mss.Duration = TimeSpan.FromSeconds(durSec > 0 ? durSec : 300);

                        mss.Starting += (s, e) =>
                        {
                            var def = e.Request.GetDeferral();
                            if (e.Request.StartPosition.HasValue)
                                currentVirtualPos = e.Request.StartPosition.Value;
                            e.Request.SetActualStartPosition(currentVirtualPos);
                            def.Complete();
                        };

                        mss.SampleRequested += (s, e) =>
                        {
                            var def = e.Request.GetDeferral();
                            var dw = new DataWriter();
                            dw.WriteBytes(new byte[17640]); // 0.1s
                            var buf = dw.DetachBuffer();
                            e.Request.Sample = MediaStreamSample.CreateFromBuffer(buf, currentVirtualPos);
                            currentVirtualPos += TimeSpan.FromSeconds(0.1);
                            def.Complete();
                        };

                        var source = MediaSource.CreateFromMediaStreamSource(mss);
                        var item = new MediaPlaybackItem(source);
                        
                        var props = item.GetDisplayProperties();
                        props.Type = MediaPlaybackType.Music;
                        if (title != null) props.MusicProperties.Title = title;
                        if (artist != null) props.MusicProperties.Artist = artist;
                        if (album != null) props.MusicProperties.AlbumTitle = album;
                        
                        if (!string.IsNullOrEmpty(cover)) {
                            try {
                                if (cover.StartsWith("data:")) {
                                    int comma = cover.IndexOf(',');
                                    if (comma != -1) {
                                        byte[] imgData = Convert.FromBase64String(cover.Substring(comma + 1));
                                        string tmpImg = Path.Combine(Path.GetTempPath(), "music_center_cover_" + Guid.NewGuid().ToString() + ".jpg");
                                        File.WriteAllBytes(tmpImg, imgData);
                                        props.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri("file:///" + tmpImg.Replace('\\', '/')));
                                    }
                                } else {
                                    if (cover.StartsWith("file:///")) {
                                        cover = "file:///" + Uri.UnescapeDataString(cover.Substring(8)).Replace('\\', '/');
                                    }
                                    props.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri(cover));
                                }
                            } catch {}
                        }
                        item.ApplyDisplayProperties(props);
                        player.Source = item;
                    }

                    if (position != null) {
                        try {
                            // If difference is large, seek the player
                            if (Math.Abs(player.PlaybackSession.Position.TotalSeconds - posSec) > 3) {
                                player.PlaybackSession.Position = TimeSpan.FromSeconds(posSec);
                            }
                        } catch {}
                    }

                    if (state == "playing") player.Play();
                    else player.Pause();

                    res.StatusCode = 200;
                    res.Close();
                }
                else if (req.Url.AbsolutePath == "/poll")
                {
                    string action = "";
                    lock (actionLock)
                    {
                        action = lastAction;
                        lastAction = "";
                    }
                    byte[] buffer = Encoding.UTF8.GetBytes(action);
                    res.ContentLength64 = buffer.Length;
                    res.OutputStream.Write(buffer, 0, buffer.Length);
                    res.Close();
                }
                else
                {
                    res.StatusCode = 404;
                    res.Close();
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "smtc_req_err.txt"), e.ToString());
                try { res.Close(); } catch { }
            }
        }
    }
}









