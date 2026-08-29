using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.MediaProperties;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class SmtcServer
{
    static MediaPlayer player;
    static double lastDuration = 0;
    static TimeSpan currentVirtualPos = TimeSpan.Zero;
    static JavaScriptSerializer js = new JavaScriptSerializer();

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
        player.CommandManager.IsEnabled = true;

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
                    string title = data.ContainsKey("title") ? data["title"] as string : null;
                    string artist = data.ContainsKey("artist") ? data["artist"] as string : null;
                    string album = data.ContainsKey("album") ? data["album"] as string : null;
                    string state = data.ContainsKey("state") ? data["state"] as string : null;
                    string position = data.ContainsKey("position") ? data["position"] as string : null;
                    string duration = data.ContainsKey("duration") ? data["duration"] as string : null;
                    string cover = data.ContainsKey("cover") ? data["cover"] as string : null;
                    bool metaChanged = data.ContainsKey("metaChanged") ? (bool)data["metaChanged"] : true;

                    double durSec = 0;
                    double.TryParse(duration, out durSec);
                    double posSec = 0;
                    double.TryParse(position, out posSec);

                    bool recreateSource = player.Source == null || Math.Abs(durSec - lastDuration) > 1.0;
                    MediaPlaybackItem newItem = null;

                    if (recreateSource)
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
                        newItem = new MediaPlaybackItem(source);
                    }

                    if (metaChanged || recreateSource)
                    {
                        var item = recreateSource ? newItem : (player.Source as MediaPlaybackItem);
                        if (item != null)
                        {
                            var props = item.GetDisplayProperties();
                            props.Type = MediaPlaybackType.Music;
                            if (title != null) props.MusicProperties.Title = title;
                            if (artist != null) props.MusicProperties.Artist = artist;
                            if (album != null) props.MusicProperties.AlbumTitle = album;
                            
                            if (!string.IsNullOrEmpty(cover)) {
                                try {
                                    string tmpImg = Path.Combine(Path.GetTempPath(), "music_center_cover_" + Guid.NewGuid().ToString() + ".jpg");
                                    bool imageReady = false;
                                    
                                    if (cover.StartsWith("data:")) {
                                        int comma = cover.IndexOf(',');
                                        if (comma != -1) {
                                            byte[] imgData = Convert.FromBase64String(cover.Substring(comma + 1));
                                            File.WriteAllBytes(tmpImg, imgData);
                                            imageReady = true;
                                        }
                                    } else if (cover.StartsWith("file:///")) {
                                        string sourcePath = Uri.UnescapeDataString(cover.Substring(8)).Replace('/', '\\');
                                        if (File.Exists(sourcePath)) {
                                            File.Copy(sourcePath, tmpImg, true);
                                            imageReady = true;
                                        } else {
                                            props.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri(cover));
                                        }
                                    } else {
                                        props.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri(cover));
                                    }
                                    
                                    if (imageReady) {
                                        props.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri("file:///" + tmpImg.Replace('\\', '/')));
                                    }
                                } catch {}
                            }
                            item.ApplyDisplayProperties(props);
                        }
                    }

                    if (recreateSource && newItem != null) {
                        player.Source = newItem;
                    }

                    if (position != null) {
                        try {
                            if (Math.Abs(player.PlaybackSession.Position.TotalSeconds - posSec) > 3) {
                                player.PlaybackSession.Position = TimeSpan.FromSeconds(posSec);
                            }
                        } catch {}
                    }

                    if (state == "playing") player.Play();
                    else if (state == "paused") player.Pause();

                    var responseBytes = Encoding.UTF8.GetBytes("OK");
                    res.ContentLength64 = responseBytes.Length;
                    res.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                }
                else if (req.Url.AbsolutePath == "/poll" && req.HttpMethod == "GET")
                {
                    string command = "";
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
}

