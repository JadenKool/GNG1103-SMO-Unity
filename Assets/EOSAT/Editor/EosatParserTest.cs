using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Test Parser With Client Sample File
    /// Runs every line of the client's sample_vr_frames.ndjson through TelemetryParser (no Play mode needed)
    /// and checks it against the expected values in the CSV.
    /// </summary>
    public static class EosatParserTest
    {
        const string DefaultPath = @"C:\Users\yinja\Downloads\GNG1103_F2026\GNG1103_F2026\sample_vr_frames.ndjson";

        /// <summary>Checks for the 8103 power flattener, address parsing, IP octet stepping, replay search and pass prediction.</summary>
        [MenuItem("EOSAT/Run Self-Tests")]
        public static void SelfTests()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; Debug.LogError("[EOSAT self-test] FAILED: " + what); } }

            // 8103 power stream: nested JSON flattened, values found by name fragment.
            var p = new PowerSample();
            Check(FlatJson.TryFlatten("{\"battery\":{\"soc_pct\":81.5,\"voltage_v\":7.9},\"bus\":{\"voltage_v\":5.02},\"temp_c\":21,\"load_shed\":false,\"faces\":[{\"i\":0.1},{\"i\":0.2}]}", p.fields, out _),
                  "FlatJson parses nested objects / arrays");
            Check(Mathf.Approximately(p.BatterySocPct, 81.5f), "battery SoC");
            Check(Mathf.Approximately(p.BusVoltage, 5.02f), "bus voltage");
            Check(Mathf.Approximately(p.TemperatureC, 21f), "temperature");
            Check(p.LoadShed == "false", "load shed text");
            Check(p.fields.ContainsKey("faces[1].i"), "array paths");
            Check(!FlatJson.TryFlatten("{\"a\":", new System.Collections.Generic.Dictionary<string, string>(), out _), "truncated JSON rejected");

            // met_s -> MET
            const string met = "{\"type\":\"state\",\"met_s\":3725,\"orbit\":{\"pos_eci_km\":[6871,0,0]},\"attitude\":{\"q_body_to_eci\":[0,0,0,1]}}";
            Check(TelemetryParser.TryParse(met, true, out var ms, out _) && ms.hasMet && Math.Abs(ms.metSeconds - 3725) < 1e-6, "met_s parsed");

            // Address field: IP, IP/path, ops URL, bad input.
            Check(TelemetryHub.TryParseAddress("8.232.20.196/VR", "wss://ops.cgo.live/VR", out var h, out _, out var ws, out _)
                  && h == "8.232.20.196" && ws == "ws://8.232.20.196:8101/VR", "IP/path -> ws://IP:8101/VR (got " + ws + ")");
            Check(TelemetryHub.TryParseAddress("wss://ops.cgo.live/VR", "ws://127.0.0.1:8101/VR", out h, out _, out ws, out _)
                  && h == "ops.cgo.live" && ws == "wss://ops.cgo.live/VR", "ops URL kept (got " + ws + ")");
            Check(!TelemetryHub.TryParseAddress("300.1.1.1", "ws://x/VR", out _, out _, out _, out _), "octet > 255 rejected");
            Check(!TelemetryHub.TryParseAddress("1.2.3.4:9999", "ws://x/VR", out _, out _, out _, out _), "wrong port rejected");

            // Thumbstick octet stepping keeps the path.
            Check(Eosat.Meta.UiHoverNav.TrySplitIp("8.232.20.196/VR", out var oct, out var rest) && oct[3] == 196 && rest == "/VR", "IP split");
            Check(!Eosat.Meta.UiHoverNav.TrySplitIp("ops.cgo.live", out _, out _), "host name is not an IP");

            // Replay: binary search over the session log.
            var log = new System.Collections.Generic.List<LogEntry>();
            for (int i = 0; i < 10; i++) log.Add(new LogEntry { t = i, kind = LogKind.Sample });
            Check(Eosat.MR.ReplayController.LastAtOrBefore(log, 4.5f) == 4 && Eosat.MR.ReplayController.LastAtOrBefore(log, -1f) == -1
                  && Eosat.MR.ReplayController.LastAtOrBefore(log, 99f) == 9, "replay search");

            // Pass prediction: a polar 500 km orbit over 24 h passes every high-latitude station, and is in eclipse part of the time.
            float r = (float)EciMath.EarthRadiusKm + 500f;
            float rate = 360f / 5677f;                                   // ~94.6 min
            var pred = OrbitPredictor.Predict(new Vector3(r, 0, 0), new Vector3(0, 0, -1).normalized, rate, 1.7e9, 86400, 30);
            Check(pred.passes.Exists(x => x.Name == "Svalbard") && pred.passes.Exists(x => x.Name == "Troll"), "polar orbit sees Svalbard and Troll");
            Check(pred.eclipses.Count >= 10, "eclipse windows over 24 h (" + pred.eclipses.Count + ")");
            Check(pred.passes.TrueForAll(x => x.los > x.aos && x.maxElDeg > 0f && x.maxElDeg <= 90f), "pass AOS < LOS, max el in range");
            Vector2 ll = OrbitPredictor.LatLon(new Vector3(0, r, 0), 1.7e9);
            Check(Mathf.Abs(ll.x - 90f) < 0.01f, "north pole latitude");
            Check(Eosat.MR.FlightMapUI.MapPosition(90f, 180f, new Vector2(1000, 500)) == new Vector2(500, 250), "map corner");

            if (fails == 0) Debug.Log("[EOSAT self-test] all checks passed");
        }

        [MenuItem("EOSAT/Test Parser With Client Sample File")]
        public static void Run()
        {
            SelfTests();
            // Antenna with only the "deployed" bool (no state string) must still drive the animation.
            const string boolOnly = "{\"type\":\"state\",\"orbit\":{\"pos_eci_km\":[6871,0,0]},\"attitude\":{\"q_body_to_eci\":[0,0,0,1]},\"deploy\":{\"antenna\":{\"deployed\":true}}}";
            Debug.Assert(TelemetryParser.TryParse(boolOnly, true, out var bs, out _) && bs.antenna == AntennaState.Deployed, "[EOSAT test] antenna 'deployed' bool fallback FAILED");
            const string pct = "{\"type\":\"state\",\"orbit\":{\"pos_eci_km\":[6871,0,0]},\"attitude\":{\"q_body_to_eci\":[0,0,0,1]},\"deploy\":{\"antenna\":{\"state\":\"DEPLOYING\",\"percent\":45}}}";
            Debug.Assert(TelemetryParser.TryParse(pct, true, out var ps, out _) && ps.hasAntennaFraction && Mathf.Approximately(ps.antennaFraction, 0.45f), "[EOSAT test] antenna percent parse FAILED");
            string lang = LocalizationManager.Language;
            LocalizationManager.SetLanguage(LocalizationManager.French);
            Debug.Assert(LocalizationManager.Tr("<b>CONNECTION</b> ON") == "<b>CONNEXION</b> ACTIVÉ", "[EOSAT test] French translation FAILED: " + LocalizationManager.Tr("<b>CONNECTION</b> ON"));
            LocalizationManager.SetLanguage(lang);

            string path = File.Exists(DefaultPath) ? DefaultPath
                : EditorUtility.OpenFilePanel("Pick sample_vr_frames.ndjson", "", "ndjson");
            if (string.IsNullOrEmpty(path)) return;

            int good = 0, ignored = 0, bad = 0, nadirRows = 0, nadirOk = 0;
            int antennaChanges = 0, eclipseChanges = 0, pyChanges = 0, myChanges = 0;
            float altMin = float.MaxValue, altMax = float.MinValue;
            string firstError = null;
            TelemetrySample first = null, prev = null;
            var log = new StringBuilder();

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!TelemetryParser.TryParse(line, true, out var s, out var err, out bool ign))
                {
                    if (ign) ignored++; else { bad++; firstError ??= err; }
                    continue;
                }
                good++;
                first ??= s;

                float alt = s.posEciKm.magnitude - (s.earthRadiusKm > 0 ? s.earthRadiusKm : (float)EciMath.EarthRadiusKm);
                altMin = Mathf.Min(altMin, alt); altMax = Mathf.Max(altMax, alt);

                // Attitude check through the same ECI -> Unity conversion the scene uses (every frame, tumbling too):
                // the simulator's Earth direction in body axes, rotated by our Unity quaternion, must point at Earth.
                if (s.hasEarthDirBody)
                {
                    nadirRows++;
                    Quaternion q = EciMath.EciQuatToUnity(s.qw, s.qx, s.qy, s.qz);
                    Vector3 earthLocal = EciMath.EciToUnity(s.earthDirBody);     // body axes -> Unity local axes (same swap)
                    Vector3 nadir = -EciMath.EciToUnity(s.posEciKm).normalized;
                    if (Vector3.Dot(q * earthLocal, nadir) > 0.9999f) nadirOk++;
                }

                if (prev != null)
                {
                    if (s.antenna != prev.antenna) { antennaChanges++; log.AppendLine($"  {Utc(s)}  antenna -> {s.antenna}"); }
                    if (s.eclipse != prev.eclipse) { eclipseChanges++; log.AppendLine($"  {Utc(s)}  eclipse -> {s.eclipse}"); }
                    if (Step(prev.wingPyFraction, s.wingPyFraction)) { pyChanges++; log.AppendLine($"  {Utc(s)}  +Y wing {s.wingPyFraction:P0}"); }
                    if (Step(prev.wingMyFraction, s.wingMyFraction)) { myChanges++; log.AppendLine($"  {Utc(s)}  -Y wing {s.wingMyFraction:P0}"); }
                    if (s.aocsMode != prev.aocsMode) log.AppendLine($"  {Utc(s)}  AOCS {prev.aocsMode} -> {s.aocsMode} ({s.pointing})");
                }
                prev = s;
            }

            bool pass = good > 0 && bad == 0 && antennaChanges >= 1 && eclipseChanges >= 2
                        && pyChanges >= 1 && myChanges >= 1 && nadirRows > 0 && nadirOk == nadirRows
                        && altMax - altMin < 1f;

            string summary =
                $"{(pass ? "PASS" : "FAIL")}  {Path.GetFileName(path)}\n" +
                $"State frames parsed: {good}   ignored (hello): {ignored}   rejected: {bad}" + (firstError != null ? $" (first: {firstError})" : "") + "\n" +
                (first != null ? $"Row 1: {Utc(first)}  pos [{first.posEciKm.x:F3}, {first.posEciKm.y:F3}, {first.posEciKm.z:F3}] km  " +
                                 $"q(w,x,y,z) [{first.qw:F7}, {first.qx:F7}, {first.qy:F7}, {first.qz:F7}]\n" : "") +
                $"Altitude {altMin:F3} to {altMax:F3} km (expect 500.927 constant)\n" +
                $"Attitude check: {nadirOk}/{nadirRows} frames rotate the sim's Earth direction onto Earth (dot > 0.9999)\n" +
                $"Events:\n{log}";

            if (pass) Debug.Log("[EOSAT test] " + summary); else Debug.LogError("[EOSAT test] " + summary);
            EditorUtility.DisplayDialog("EOSAT parser test: " + (pass ? "PASS" : "FAIL"),
                summary.Length > 1500 ? summary.Substring(0, 1500) + "\n... (full log in Console)" : summary, "OK");
        }

        static bool Step(float a, float b) => (a < 0.001f && b >= 0.001f) || (a < 0.999f && b >= 0.999f);

        static string Utc(TelemetrySample s) => s.hasTime ? EciMath.FormatUtc(s.unixTime) : "seq " + s.seq;
    }
}
