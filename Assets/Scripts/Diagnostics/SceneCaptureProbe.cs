using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Presentation.Character;
using Simulation;
using UnityEngine;

namespace Spike.Diagnostics
{
    /// <summary>
    /// Development-only screen-composition probe. It renders Camera.main into off-screen
    /// RenderTextures at fixed viewport sizes AFTER the camera framing for that viewport was applied,
    /// saves PNG screenshots, and measures in screen pixels:
    ///   * each character's projected SpriteRenderer bounding box (current frame) and, in the Editor,
    ///     the worst case over every sprite frame referenced by its Animator clips;
    ///   * whether the whole play area (CourtGeometry.RequiredVisibleWorld) is inside the viewport;
    ///   * whether the ball and the net are visible on screen.
    /// Limits per character: width <= 10% W, height <= 20% H, box area <= 5% W*H.
    /// A missing / invisible / off-screen character FAILS the check.
    ///
    /// Standalone activation: SPIKE_CAPTURE_OUT (dir) when SPIKE_SELFTEST_OUT is not set (the
    /// self-test calls CaptureAll itself). Other env vars: SPIKE_CAPTURE_DELAY (s, default 2),
    /// SPIKE_CAPTURE_TAG (prefix), SPIKE_CAPTURE_QUIT ("1"), SPIKE_CAPTURE_SIZES ("1920x1080,2400x1080").
    /// </summary>
    public class SceneCaptureProbe : MonoBehaviour
    {
        public const float MaxWidthFraction = 0.10f;
        public const float MaxHeightFraction = 0.20f;
        public const float MaxAreaFraction = 0.05f;

        private string outDir;
        private string filePrefix;
        private float delay;
        private bool quitAfter;
        private readonly List<Vector2Int> sizes = new List<Vector2Int>();

        public static string LastReportPath { get; private set; }
        public static bool LastAllCharactersPass { get; private set; }
        public static string LastSummary { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoActivate()
        {
            string dir = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_OUT");
            if (string.IsNullOrEmpty(dir)) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPIKE_SELFTEST_OUT"))) return;
            if (!(Application.isEditor || Debug.isDebugBuild)) return;
            if (FindAnyObjectByType<SceneCaptureProbe>() != null) return;
            new GameObject("[SceneCaptureProbe]").AddComponent<SceneCaptureProbe>();
        }

        private void Awake()
        {
            outDir = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_OUT");
            filePrefix = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_TAG");
            if (string.IsNullOrEmpty(filePrefix)) filePrefix = "capture";
            string d = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_DELAY");
            if (!float.TryParse(d, NumberStyles.Float, CultureInfo.InvariantCulture, out delay)) delay = 2f;
            quitAfter = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_QUIT") == "1";
            string s = Environment.GetEnvironmentVariable("SPIKE_CAPTURE_SIZES");
            if (string.IsNullOrEmpty(s)) s = "1920x1080,2400x1080";
            foreach (var part in s.Split(','))
            {
                var wh = part.Trim().Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h) && w > 0 && h > 0)
                    sizes.Add(new Vector2Int(w, h));
            }
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(delay);
            bool ok;
            try { ok = CaptureAll(outDir, filePrefix, sizes); }
            catch (Exception ex) { ok = false; Debug.LogError("[SceneCaptureProbe] Capture failed: " + ex); }
            if (quitAfter) DevRunControl.Finish(ok ? 0 : 3, "SceneCaptureProbe");
        }

        /// <summary>Captures + measures at each size. True if both characters, the court, ball and net pass at every size.</summary>
        public static bool CaptureAll(string dir, string prefix, IList<Vector2Int> viewportSizes)
        {
            Directory.CreateDirectory(dir);
            Camera cam = Camera.main;
            if (cam == null)
            {
                LastSummary = "no main camera";
                Debug.LogError("[SceneCaptureProbe] No main camera.");
                return false;
            }

            var json = new StringBuilder();
            var summary = new StringBuilder();
            json.Append("{\"tag\":\"").Append(prefix).Append("\",\"limits\":{\"widthPct\":10,\"heightPct\":20,\"areaPct\":5},\"captures\":[");
            bool allPass = true;
            for (int i = 0; i < viewportSizes.Count; i++)
            {
                if (i > 0) json.Append(',');
                allPass &= CaptureOne(cam, viewportSizes[i].x, viewportSizes[i].y, dir, prefix, json, summary);
            }
            json.Append("],\"allPass\":").Append(allPass ? "true" : "false").Append('}');
            string reportPath = Path.Combine(dir, prefix + "_measurements.json");
            File.WriteAllText(reportPath, json.ToString());
            LastReportPath = reportPath;
            LastAllCharactersPass = allPass;
            LastSummary = summary.ToString();
            Debug.Log("[SceneCaptureProbe] Wrote " + reportPath + " allPass=" + allPass + " :: " + LastSummary);
            return allPass;
        }

        private static bool CaptureOne(Camera cam, int w, int h, string dir, string prefix, StringBuilder json, StringBuilder summary)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.ResetAspect();
            cam.gameObject.SendMessage("ApplyFramingNow", SendMessageOptions.DontRequireReceiver); // fit for THIS viewport first
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            string png = Path.Combine(dir, $"{prefix}_{w}x{h}.png");
            File.WriteAllBytes(png, tex.EncodeToPNG());

            Rect pr = cam.pixelRect;
            float W = pr.width, H = pr.height;
            json.Append("{\"image\":\"").Append(Path.GetFileName(png)).Append("\",");
            json.AppendFormat(CultureInfo.InvariantCulture, "\"viewport\":[{0},{1}],\"orthoSize\":{2:F4},\"cameraPos\":[{3:F3},{4:F3}],\"aspect\":{5:F4},",
                (int)W, (int)H, cam.orthographicSize, cam.transform.position.x, cam.transform.position.y, cam.aspect);

            // Whole play area visible?
            Rect req = CourtGeometry.RequiredVisibleWorld;
            Rect reqPx = ProjectBounds(cam, new Bounds(new Vector3(req.center.x, req.center.y, 0f), new Vector3(req.width, req.height, 0f)));
            bool courtVisible = reqPx.xMin >= pr.xMin - 0.5f && reqPx.yMin >= pr.yMin - 0.5f && reqPx.xMax <= pr.xMax + 0.5f && reqPx.yMax <= pr.yMax + 0.5f;
            json.AppendFormat(CultureInfo.InvariantCulture, "\"playAreaPx\":[{0:F1},{1:F1},{2:F1},{3:F1}],\"playAreaFullyVisible\":{4},",
                reqPx.xMin - pr.x, reqPx.yMin - pr.y, reqPx.width, reqPx.height, courtVisible ? "true" : "false");

            bool pass = courtVisible;
            var seenIds = new HashSet<int>();
            json.Append("\"characters\":[");
            bool first = true;
            foreach (var view in FindObjectsByType<CharacterView>(FindObjectsInactive.Include))
            {
                var sr = view.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                Rect box = ProjectBounds(cam, sr.bounds);
                bool visible = sr.enabled && sr.gameObject.activeInHierarchy && sr.sprite != null && sr.color.a > 0.01f;
                bool onScreen = Intersects(box, pr) && box.xMin >= pr.xMin && box.xMax <= pr.xMax && box.yMin >= pr.yMin && box.yMax <= pr.yMax;
                float wf = box.width / W, hf = box.height / H, af = box.width * box.height / (W * H);

                // Worst case over all animation frames (uniform scale, so frame size * scale).
                Vector2 worstSize = WorstFrameSize(view, sr);
                float unitsToPx = H / (cam.orthographicSize * 2f);
                float s = Mathf.Abs(view.transform.lossyScale.y);
                float wwf = worstSize.x * s * unitsToPx / W, whf = worstSize.y * s * unitsToPx / H;
                float waf = wwf * whf;

                bool charPass = visible && onScreen && wf <= MaxWidthFraction && hf <= MaxHeightFraction && af <= MaxAreaFraction &&
                                wwf <= MaxWidthFraction && whf <= MaxHeightFraction && waf <= MaxAreaFraction;
                if (visible && onScreen) seenIds.Add(view.Id);
                pass &= charPass;

                if (!first) json.Append(',');
                first = false;
                json.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"name\":\"{0}\",\"characterId\":{1},\"visible\":{2},\"onScreen\":{3},\"sprite\":\"{4}\",\"boxPx\":[{5:F1},{6:F1},{7:F1},{8:F1}]," +
                    "\"widthPct\":{9:F3},\"heightPct\":{10:F3},\"areaPct\":{11:F4},\"worstFramePx\":[{12:F1},{13:F1}],\"worstWidthPct\":{14:F3},\"worstHeightPct\":{15:F3},\"worstAreaPct\":{16:F4},\"pass\":{17}}}",
                    view.name, view.Id, visible ? "true" : "false", onScreen ? "true" : "false", sr.sprite != null ? sr.sprite.name : "null",
                    box.x - pr.x, box.y - pr.y, box.width, box.height, wf * 100f, hf * 100f, af * 100f,
                    worstSize.x * s * unitsToPx, worstSize.y * s * unitsToPx, wwf * 100f, whf * 100f, waf * 100f, charPass ? "true" : "false");
                summary.AppendFormat(CultureInfo.InvariantCulture, "{0}x{1} id{2}: {3:F2}%W {4:F2}%H {5:F3}%A (worst {6:F2}/{7:F2}/{8:F3}) {9}; ",
                    (int)W, (int)H, view.Id, wf * 100f, hf * 100f, af * 100f, wwf * 100f, whf * 100f, waf * 100f, charPass ? "PASS" : "FAIL");
            }
            json.Append("],");
            bool bothCharacters = seenIds.Contains(0) && seenIds.Contains(3);
            pass &= bothCharacters;

            // Ball and net
            bool ballVisible = false, netVisible = false;
            Rect ballPx = Rect.zero, netPx = Rect.zero;
            foreach (var reader in FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Exclude))
            {
                if (reader.EntityId == 7)
                {
                    var sr = reader.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null && sr.enabled)
                    {
                        ballPx = ProjectBounds(cam, sr.bounds);
                        ballVisible = Intersects(ballPx, pr) && sr.color.a > 0.01f;
                    }
                }
                else if (reader.EntityId == 6)
                {
                    var rs = reader.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        Bounds b = rs[0].bounds;
                        foreach (var r in rs) b.Encapsulate(r.bounds);
                        netPx = ProjectBounds(cam, b);
                        netVisible = Intersects(netPx, pr);
                    }
                }
            }
            pass &= ballVisible && netVisible;
            json.AppendFormat(CultureInfo.InvariantCulture,
                "\"bothCharactersVisible\":{0},\"ball\":{{\"visible\":{1},\"boxPx\":[{2:F1},{3:F1},{4:F1},{5:F1}]}},\"net\":{{\"visible\":{6},\"boxPx\":[{7:F1},{8:F1},{9:F1},{10:F1}]}},\"pass\":{11}}}",
                bothCharacters ? "true" : "false", ballVisible ? "true" : "false", ballPx.x - pr.x, ballPx.y - pr.y, ballPx.width, ballPx.height,
                netVisible ? "true" : "false", netPx.x - pr.x, netPx.y - pr.y, netPx.width, netPx.height, pass ? "true" : "false");
            summary.AppendFormat("{0}x{1} court={2} ball={3} net={4}; ", (int)W, (int)H, courtVisible, ballVisible, netVisible);

            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            cam.gameObject.SendMessage("ApplyFramingNow", SendMessageOptions.DontRequireReceiver);
            RenderTexture.active = prevActive;
            Destroy(tex);
            rt.Release();
            Destroy(rt);
            Debug.Log($"[SceneCaptureProbe] Captured {png} viewport={W}x{H} pass={pass}");
            return pass;
        }

        /// <summary>Largest sprite frame (local units, scale 1) the character can display.</summary>
        private static Vector2 WorstFrameSize(CharacterView view, SpriteRenderer sr)
        {
            Vector2 worst = sr.sprite != null ? (Vector2)sr.sprite.bounds.size : Vector2.zero;
#if UNITY_EDITOR
            var anim = view.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    foreach (var binding in UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite") continue;
                        foreach (var key in UnityEditor.AnimationUtility.GetObjectReferenceCurve(clip, binding))
                        {
                            if (key.value is Sprite sp)
                                worst = new Vector2(Mathf.Max(worst.x, sp.bounds.size.x), Mathf.Max(worst.y, sp.bounds.size.y));
                        }
                    }
                }
            }
#endif
            return worst;
        }

        public static Rect ProjectBounds(Camera cam, Bounds b)
        {
            Vector3 min = b.min, max = b.max;
            float xmin = float.MaxValue, ymin = float.MaxValue, xmax = float.MinValue, ymax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 s = cam.WorldToScreenPoint(p);
                xmin = Mathf.Min(xmin, s.x); xmax = Mathf.Max(xmax, s.x);
                ymin = Mathf.Min(ymin, s.y); ymax = Mathf.Max(ymax, s.y);
            }
            return Rect.MinMaxRect(xmin, ymin, xmax, ymax);
        }

        private static bool Intersects(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) > Mathf.Max(a.xMin, b.xMin) && Mathf.Min(a.yMax, b.yMax) > Mathf.Max(a.yMin, b.yMin);
    }
}
