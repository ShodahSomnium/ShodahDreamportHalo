using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The map's ambience, started when the world is built: the structure BSP's background sound (the palette entry of the cluster the
    /// listener stands in - HaloWorld.ClusterAt, polled twice a second; Danger Canyon / Death Island / Icefields have several, most MP
    /// maps one) as a 2D loop, crossfaded by the tracks' own fades when the cluster's sound changes; and the scenario's sound scenery
    /// (ssce objects: their lsnd attachments, e.g. Blood Gulch's teleporter hum, Timberland's streams) looping at their places.
    /// </summary>
    public sealed partial class HaloAudioSystem
    {
        HaloTag[] _bgPalette = new HaloTag[0];
        int[] _bgOfCluster = new int[0];
        int _bgDefault = -1, _bgNow = -2;
        HaloAudioLoop _bg;
        readonly List<HaloAudioLoop> _scenery = new List<HaloAudioLoop>();
        float _bgPollAt;
        bool _ambienceOn;

        void OnWorldBuilt()
        {
            try { StartAmbience(HaloSession.Map, HaloSession.Scenario, HaloSession.WorldRoot); }
            catch (Exception e) { HaloLog.Warn("Audio: ambience failed: " + e.Message); }
        }

        internal void StartAmbience(HaloMap map, HaloScenario sc, Transform root)
        {
            StopAmbience();
            if (map == null || sc == null) return;
            // background sound palette of the active BSP (multiplayer maps have one; the campaign switches - OnBspSwitched)
            _ambienceOn = true;
            int bsp = 0;
            try { if (HaloCampaign.Active) bsp = HaloCampaign.BspIndex; } catch (Exception) { }
            LoadBackground(sc, bsp);

            // sound scenery
            int n = 0;
            foreach (var p in sc.SoundScenery)
            {
                if (p.Type == null || (p.NotPlaced & 1) != 0) continue;
                Vector3 pos = HaloSpace.Pos(p.Position);
                if (root != null) pos = root.TransformPoint(pos);
                foreach (var at in p.Type.Root.Block(320, 72))
                {
                    var t = at.Ref(0);
                    if (t == null || t.ClassName != "lsnd") continue;
                    var l = StartLoop(t, pos, null, true, 1f, GAmbient);
                    if (l != null) { _scenery.Add(l); n++; }
                }
            }
            HaloLog.Info("Audio: ambience " + map.Name + " - background " + (_bgDefault >= 0 ? _bgPalette[_bgDefault].Name : "none") + " (" + _bgPalette.Length
                + " in the palette, " + _bgOfCluster.Length + " clusters), " + n + " sound scenery loops");
        }

        void LoadBackground(HaloScenario sc, int bsp)
        {
            var pal = new List<HaloTag>();
            var ofCluster = new List<int>();
            if (sc != null && bsp >= 0 && bsp < sc.Bsps.Count)
            {
                var r = sc.BspRoot(bsp);
                foreach (var e in r.Block(508, 116)) pal.Add(e.Ref(32));
                foreach (var c in r.Block(308, 104)) ofCluster.Add(c.I16(4));
            }
            _bgPalette = pal.ToArray(); _bgOfCluster = ofCluster.ToArray();
            var votes = new int[_bgPalette.Length];
            foreach (int i in _bgOfCluster) if (i >= 0 && i < votes.Length && _bgPalette[i] != null) votes[i]++;
            _bgDefault = -1; int best = 0;
            for (int i = 0; i < votes.Length; i++) if (votes[i] > best) { best = votes[i]; _bgDefault = i; }
            _bgNow = -2;
            SwitchBackground(_bgDefault);
        }

        void SwitchBackground(int index)
        {
            if (index == _bgNow) return;
            _bgNow = index;
            if (_bg != null) { _bg.Stop(false, -1f); _bg = null; }
            if (index < 0 || index >= _bgPalette.Length || _bgPalette[index] == null) return;
            _bg = StartLoop(_bgPalette[index], Vector3.zero, null, false, 1f, GAmbient);
        }

        void TickAmbience()
        {
            if (!_ambienceOn || Time.unscaledTime < _bgPollAt) return;
            _bgPollAt = Time.unscaledTime + 0.5f;
            if (_bgOfCluster.Length <= 1 || !HaloWorld.Ready) return;
            int c;
            try { c = HaloWorld.ClusterAt(ListenerPosition()); } catch (Exception) { return; }
            if (c < 0 || c >= _bgOfCluster.Length) return;   // outside the BSP: keep what plays
            SwitchBackground(_bgOfCluster[c]);
        }

        internal void StopAmbience()
        {
            _ambienceOn = false;
            if (_bg != null) { _bg.Stop(true, 0.5f); _bg = null; }
            foreach (var l in _scenery) l.Stop(true, 0.2f);
            _scenery.Clear();
            _bgNow = -2;
        }

        internal string AmbienceInfo => (_bg != null && _bg.Tag != null ? _bg.Tag.Name : "no background") + ", " + _scenery.Count + " scenery loops";
    }
}
