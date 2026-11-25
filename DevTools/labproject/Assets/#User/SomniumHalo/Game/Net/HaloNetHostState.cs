using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The campaign HOST STATE and the hand-over (user decision: "if the master leaves mid-mission, hand hosting to the next player and keep the mission
    /// going"). Modules register a part (a capture to bytes, an adopt from bytes); the host captures every part every <see cref="Every"/> seconds and at
    /// every checkpoint and streams the blob ("Halo Net Host 0..3", HaloNetStream); followers keep the last complete blob. When the master leaves, the
    /// next master (Net's master rule) promotes itself: Net turns its puppets back into real units with the same serials and places (so the other
    /// followers' puppets stay), calls every part's adopt in order (adopt(null) when the blob has no such part: the part falls back), then raises
    /// <see cref="Promoted"/> (the Mission module sets HaloCampaign.Host and starts the host-only ticks in place: no reload, no teleport, nobody leaves).
    /// </summary>
    public static class HaloNetHostState
    {
        /// <summary>Seconds between two captures on the host (also at every checkpoint).</summary>
        public static float Every = 15f;

        private sealed class Part { public string Key; public int Order; public Func<byte[]> Capture; public Action<byte[]> Adopt; }

        private static readonly List<Part> Parts = new List<Part>();

        /// <summary>This client became the host of the running mission (after the parts adopted the blob).</summary>
        public static event Action Promoted;

        /// <summary>
        /// A module's part of the host state: <paramref name="capture"/> (host, every 15 s and at checkpoints) returns its bytes; <paramref name="adopt"/>
        /// (the new host after a hand-over) takes them back - null when the blob had none. Lower <paramref name="order"/> adopts first
        /// (mission 10, script 20, ai 30). The same key again replaces the part.
        /// </summary>
        public static void Register(string key, int order, Func<byte[]> capture, Action<byte[]> adopt)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < Parts.Count; i++) if (Parts[i].Key == key) { Parts.RemoveAt(i); break; }
            Parts.Add(new Part { Key = key, Order = order, Capture = capture, Adopt = adopt });
            Parts.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public static void Unregister(string key)
        {
            for (int i = 0; i < Parts.Count; i++) if (Parts[i].Key == key) { Parts.RemoveAt(i); return; }
        }

        /// <summary>A follower holds a complete host-state blob of the running mission.</summary>
        public static bool HaveBlob { get { return HaloNetGame.Net != null && HaloNetGame.Net.HostStream.Complete != null; } }

        /// <summary>Seconds since the last complete blob arrived (or was captured), -1 when none.</summary>
        public static float BlobAge { get { return _blobAt < 0f ? -1f : Time.unscaledTime - _blobAt; } }

        /// <summary>Hand-overs this client took (lab, log).</summary>
        public static int Promotions { get; private set; }
        /// <summary>Captures streamed by this client as the host.</summary>
        public static int Captures { get; private set; }

        private static float _nextCapture, _blobAt = -1f;
        private static int _seenBlob = -1;
        private static bool _captureNow;

        /// <summary>A checkpoint: capture at once.</summary>
        internal static void CheckpointTaken() { _captureNow = true; }

        // ------------------------------------------------------------------ the blob

        /// <summary>The parts as one blob: 'HS01', count, then per part its key (UTF-8, length-prefixed) and its bytes (length-prefixed).</summary>
        public static byte[] Pack(IList<KeyValuePair<string, byte[]>> parts)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0x31305348);      // 'HS01'
                w.Write(parts.Count);
                for (int i = 0; i < parts.Count; i++)
                {
                    byte[] k = Encoding.UTF8.GetBytes(parts[i].Key ?? "");
                    w.Write(k.Length); w.Write(k);
                    byte[] v = parts[i].Value ?? new byte[0];
                    w.Write(v.Length); w.Write(v);
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        public static Dictionary<string, byte[]> Unpack(byte[] blob)
        {
            var d = new Dictionary<string, byte[]>();
            if (blob == null || blob.Length < 8) return d;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(blob)))
                {
                    if (r.ReadInt32() != 0x31305348) return d;
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        string key = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                        d[key] = r.ReadBytes(r.ReadInt32());
                    }
                }
            }
            catch (Exception e)
            {
                HaloLog.Warn("Net: the host state blob is damaged: " + e.Message);
            }

            return d;
        }

        // ------------------------------------------------------------------ the host (HaloNetGame.Coop, every frame)

        internal static void HostTick(HaloNet net)
        {
            float now = Time.unscaledTime;
            if (!_captureNow && now < _nextCapture) return;
            _captureNow = false;
            _nextCapture = now + Every;
            var list = new List<KeyValuePair<string, byte[]>>(Parts.Count);
            for (int i = 0; i < Parts.Count; i++)
            {
                byte[] b = null;
                try { b = Parts[i].Capture != null ? Parts[i].Capture() : null; }
                catch (Exception e) { HaloLog.Warn("Net: capturing the host state part '" + Parts[i].Key + "' failed: " + e.Message); }
                list.Add(new KeyValuePair<string, byte[]>(Parts[i].Key, b));
            }

            byte[] blob = Pack(list);
            net.HostStream.SetBlob(blob);
            _blobAt = now;
            Captures++;
            if (Captures == 1 || Captures % 20 == 0) HaloLog.Info("Net: host state captured (" + Parts.Count + " part(s), " + blob.Length + " bytes, " + net.HostStream.ChunkCount + " chunks; capture " + Captures + ")");
        }

        internal static void FollowerTick(HaloNet net)
        {
            if (net.HostStream.CompleteId != _seenBlob && net.HostStream.Complete != null)
            {
                _seenBlob = net.HostStream.CompleteId;
                _blobAt = Time.unscaledTime;
            }
        }

        /// <summary>The hand-over: every part adopts what the old host last streamed, then Promoted.</summary>
        internal static void Promote(HaloNet net)
        {
            Promotions++;
            byte[] blob = net.HostStream.Complete;
            Dictionary<string, byte[]> parts = Unpack(blob);
            HaloLog.Info("Net: HOST HAND-OVER - this client hosts the mission now (" + (blob != null ? "host state of " + BlobAge.ToString("0") + " s ago, " + parts.Count + " part(s)" : "no host state received: the parts fall back") + ")");
            for (int i = 0; i < Parts.Count; i++)
            {
                byte[] b;
                parts.TryGetValue(Parts[i].Key, out b);
                try { if (Parts[i].Adopt != null) Parts[i].Adopt(b); }
                catch (Exception e) { HaloLog.Error("Net: the part '" + Parts[i].Key + "' failed to adopt the host state: " + e); }
                HaloLog.Info("Net: hand-over part '" + Parts[i].Key + "': " + (b != null ? b.Length + " bytes adopted" : "nothing in the blob (fallback)"));
            }

            _nextCapture = Time.unscaledTime + 2f;      // the new host streams its own state soon
            Action h = Promoted;
            if (h == null) return;
            foreach (Action d in h.GetInvocationList())
            {
                try { d(); }
                catch (Exception e) { HaloLog.Error("Net: a Promoted listener failed: " + e); }
            }
        }

        internal static void Reset()
        {
            _seenBlob = -1;
            _blobAt = -1f;
            _nextCapture = 0f;
            _captureNow = false;
        }
    }
}
