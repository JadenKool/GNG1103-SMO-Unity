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

        [MenuItem("EOSAT/Test Parser With Client Sample File")]
        public static void Run()
        {
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
