using System.IO;
using System.Linq;
using Assets.Table;
using Presentation.CameraSystem;
using Presentation.Character;
using Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Spike.EditorTools
{
    /// <summary>
    /// Idempotent correction of the existing gameplay scene (Assets/Resources/New Scene.unity).
    /// Keeps the existing objects/assets and only fixes what the audit proved wrong:
    ///   * removes the static court image, the Player's physics Rigidbody and missing-script components;
    ///   * binds the existing Player prefab instance to native entity 0 and adds ONE opponent instance of
    ///     the second existing Player prefab (Assets/Resources/Prefabs/Player.prefab) bound to entity 3;
    ///   * gives the Ball a real sprite and binds it to entity 7; adds ONE procedural Net (entity 6);
    ///   * adds the procedural line court, ONE SpikeSimulation object (manager + input adapter + Data Table);
    ///   * fixes UI buttons (hold/release for every button, adds a Serve button with ui id 3);
    ///   * Android landscape-only orientation and product name.
    /// Run: Unity -batchmode -projectPath ... -executeMethod Spike.EditorTools.GameplaySceneSetup.Run -quit
    /// </summary>
    public static class GameplaySceneSetup
    {
        public const string ScenePath = "Assets/Resources/New Scene.unity";
        public const string OpponentPrefabPath = "Assets/Resources/Prefabs/Player.prefab";
        public const string GeneratedDir = "Assets/Art/Generated";
        public const string LineMaterialPath = GeneratedDir + "/UnlitVertexColor.mat";
        public const string BallSpritePath = GeneratedDir + "/Volleyball.png";
        public const string ServeIconPath = "Assets/Photo/Serve.png";

        [MenuItem("Spike/Correct Gameplay Scene")]
        public static void Run()
        {
            Directory.CreateDirectory(GeneratedDir);
            Material lineMat = EnsureLineMaterial();
            Sprite ballSprite = EnsureBallSprite();
            Sprite serveIcon = EnsureServeIcon();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log("Opened " + ScenePath);

            // ---- remove static court image ------------------------------------------------
            var bg = GameObject.Find("Background");
            if (bg != null) { Object.DestroyImmediate(bg); Log("Removed static 'Background' sprite (court is now line geometry)"); }

            // ---- missing scripts everywhere -------------------------------------------------
            int removedMissing = 0;
            foreach (var go in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject))
                removedMissing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            Log($"Removed {removedMissing} missing-script components");

            // ---- characters ----------------------------------------------------------------
            var views = Object.FindObjectsByType<CharacterView>(FindObjectsInactive.Include);
            CharacterView player = views.FirstOrDefault(v => v.Id == 0);
            if (player == null) { Debug.LogError("[SceneSetup] Player CharacterView (id 0) not found"); EditorApplication.Exit(2); return; }
            PreparePhysicsFree(player.gameObject);
            player.gameObject.name = "Player";
            ConfigureReader(player.gameObject, SimulationDataTable.ENTITY_PLAYER, null);
            player.GetComponent<SpriteRenderer>().sortingOrder = 999;

            CharacterView opponent = views.FirstOrDefault(v => v.Id == 3);
            if (opponent == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OpponentPrefabPath);
                if (prefab == null) { Debug.LogError("[SceneSetup] Opponent prefab missing: " + OpponentPrefabPath); EditorApplication.Exit(2); return; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.name = "Opponent_1";
                opponent = inst.GetComponent<CharacterView>();
                Log("Instantiated opponent from " + OpponentPrefabPath);
            }
            opponent.gameObject.name = "Opponent_1";
            SetSerialized(opponent, "id", 3);
            PreparePhysicsFree(opponent.gameObject);
            var oppSr = opponent.GetComponent<SpriteRenderer>();
            oppSr.color = new Color(1f, 0.66f, 0.58f, 1f); // coral team tint (sprite art unchanged)
            oppSr.sortingOrder = 998;
            ConfigureReader(opponent.gameObject, SimulationDataTable.ENTITY_OPPONENT_1, null);
            // Remove any further character duplicates (only ids 0 and 3 are simulated).
            foreach (var v in Object.FindObjectsByType<CharacterView>(FindObjectsInactive.Include))
                if (v != player && v != opponent) { Log("Removed extra character " + v.name); Object.DestroyImmediate(v.gameObject); }
            player.transform.position = new Vector3(CourtGeometry.ToWorldX(330f), 0f, 0f);
            opponent.transform.position = new Vector3(CourtGeometry.ToWorldX(2410f), 0f, 0f);

            // ---- ball ----------------------------------------------------------------------
            var ball = GameObject.Find("Ball");
            if (ball == null) ball = new GameObject("Ball");
            PreparePhysicsFree(ball);
            foreach (var demo in ball.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().Name == "BallFlyDemo").ToArray()) Object.DestroyImmediate(demo);
            var ballSr = ball.GetComponent<SpriteRenderer>();
            if (ballSr == null) ballSr = ball.AddComponent<SpriteRenderer>();
            ballSr.sprite = ballSprite;
            ballSr.sharedMaterial = lineMat;
            ballSr.color = Color.white;
            ballSr.sortingOrder = 1000;
            ballSr.enabled = true;
            ConfigureReader(ball, SimulationDataTable.ENTITY_BALL, null);
            ball.transform.localScale = Vector3.one * 0.2f;

            // ---- net -----------------------------------------------------------------------
            foreach (var n in Object.FindObjectsByType<NetView>(FindObjectsInactive.Include).Skip(1).ToArray()) Object.DestroyImmediate(n.gameObject);
            var netView = Object.FindAnyObjectByType<NetView>(FindObjectsInactive.Include);
            if (netView == null) netView = new GameObject("Net").AddComponent<NetView>();
            netView.gameObject.name = "Net";
            SetSerialized(netView, "lineMaterial", lineMat);
            ConfigureReader(netView.gameObject, SimulationDataTable.ENTITY_NET, netView);

            // ---- ball floor shadow (presentation only, reads ball record) -------------------
            foreach (var s in Object.FindObjectsByType<BallShadowView>(FindObjectsInactive.Include).Skip(1).ToArray()) Object.DestroyImmediate(s.gameObject);
            var shadow = Object.FindAnyObjectByType<BallShadowView>(FindObjectsInactive.Include);
            if (shadow == null) shadow = new GameObject("BallShadow").AddComponent<BallShadowView>();
            SetSerialized(shadow, "material", lineMat);

            // ---- court ---------------------------------------------------------------------
            foreach (var c in Object.FindObjectsByType<CourtLineRenderer>(FindObjectsInactive.Include).Skip(1).ToArray()) Object.DestroyImmediate(c.gameObject);
            var court = Object.FindAnyObjectByType<CourtLineRenderer>(FindObjectsInactive.Include);
            if (court == null) court = new GameObject("Court").AddComponent<CourtLineRenderer>();
            court.gameObject.name = "Court";
            SetSerialized(court, "lineMaterial", lineMat);
            var gen = court.transform.Find("Generated");
            if (gen != null) Object.DestroyImmediate(gen.gameObject); // built at runtime from native constants

            // ---- simulation root (manager + adapter + data table) --------------------------
            var managers = Object.FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Include);
            foreach (var extra in managers.Skip(1)) Object.DestroyImmediate(extra.gameObject);
            var mgr = managers.FirstOrDefault();
            GameObject simGo = mgr != null ? mgr.gameObject : new GameObject("SpikeSimulation");
            simGo.name = "SpikeSimulation";
            var adapter = simGo.GetComponent<SpikeInputAdapter>();
            if (adapter == null) adapter = simGo.AddComponent<SpikeInputAdapter>();
            foreach (var t in Object.FindObjectsByType<SimulationDataTable>(FindObjectsInactive.Include))
                if (t.gameObject != simGo) Object.DestroyImmediate(t);
            var dataTable = simGo.GetComponent<SimulationDataTable>();
            if (dataTable == null) dataTable = simGo.AddComponent<SimulationDataTable>();
            mgr = simGo.GetComponent<SpikeSimulationManager>();
            if (mgr == null) mgr = simGo.AddComponent<SpikeSimulationManager>();
            if (simGo.GetComponent<SimSnapshotDiagnosticsOverlay>() == null) simGo.AddComponent<SimSnapshotDiagnosticsOverlay>();
            var stateTable = Object.FindAnyObjectByType<CharacterStateTable>(FindObjectsInactive.Include);
            SetSerialized(mgr, "dataTable", dataTable);
            SetSerialized(mgr, "stateTable", stateTable);
            SetSerialized(mgr, "inputAdapter", adapter);

            // ---- camera ----------------------------------------------------------------------
            var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                if (cam.GetComponent<CameraFollow>() == null) cam.gameObject.AddComponent<CameraFollow>();
                cam.orthographic = true;
                cam.rect = new Rect(0, 0, 1, 1);
                cam.transform.position = new Vector3(0f, 6f, -10f);
                cam.orthographicSize = 10f;
            }

            // ---- UI buttons ------------------------------------------------------------------
            FixUiButtons(serveIcon);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { Debug.LogError("[SceneSetup] Saving scene failed"); EditorApplication.Exit(3); return; }
            Log("Saved " + ScenePath);

            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            Log("DONE");
        }

        private static void PreparePhysicsFree(GameObject go)
        {
            foreach (var rb in go.GetComponents<Rigidbody>()) { Object.DestroyImmediate(rb); Log($"Removed Rigidbody from {go.name} (native simulation is the only physics)"); }
            foreach (var rb in go.GetComponents<Rigidbody2D>()) { Object.DestroyImmediate(rb); Log($"Removed Rigidbody2D from {go.name}"); }
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<Collider2D>()) Object.DestroyImmediate(c);
        }

        private static void ConfigureReader(GameObject go, uint entityId, NetView net)
        {
            var readers = go.GetComponents<SimulationEntityReader>();
            for (int i = 1; i < readers.Length; i++) Object.DestroyImmediate(readers[i]);
            var r = readers.Length > 0 ? readers[0] : go.AddComponent<SimulationEntityReader>();
            SetSerialized(r, "entityId", (int)entityId);
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) SetSerialized(r, "spriteRenderer", sr);
            if (net != null) SetSerialized(r, "netView", net);
            Log($"Bound {go.name} -> native entity {entityId}");
        }

        private static void SetSerialized(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[SceneSetup] Field '{field}' not found on {target}"); return; }
            switch (value)
            {
                case int i: if (p.propertyType == SerializedPropertyType.Integer) p.longValue = i; break;
                case bool b: p.boolValue = b; break;
                case Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FixUiButtons(Sprite serveIcon)
        {
            var buttons = Object.FindObjectsByType<UIButtonInputTest>(FindObjectsInactive.Include);
            UIButtonInputTest bump = null, serve = null;
            foreach (var b in buttons)
            {
                var so = new SerializedObject(b);
                int id = so.FindProperty("buttonId").intValue;
                so.FindProperty("allowHold").boolValue = true; // every button reports press AND release
                so.ApplyModifiedPropertiesWithoutUndo();
                if (id == 2) bump = b;
                if (id == 3) serve = b;
            }
            if (bump != null) bump.gameObject.name = "Bump";
            if (serve == null && bump != null)
            {
                var copy = Object.Instantiate(bump.gameObject, bump.transform.parent);
                copy.name = "Serve";
                var so = new SerializedObject(copy.GetComponent<UIButtonInputTest>());
                so.FindProperty("buttonId").intValue = 3;
                so.FindProperty("allowHold").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                var rt = copy.GetComponent<RectTransform>();
                var src = bump.GetComponent<RectTransform>();
                rt.anchoredPosition = src.anchoredPosition + new Vector2(-(src.rect.width * src.localScale.x + 40f), 0f);
                var img = copy.GetComponent<Image>();
                if (img != null && serveIcon != null) img.sprite = serveIcon;
                Log("Added Serve UI button (ui id 3) next to Bump");
            }
            LayoutTouchButtons();
        }

        /// <summary>
        /// D15: the original buttons were 140 px opaque icons centred at y=-342 (on the court floor) and covered the
        /// player. Move them into the bottom band reserved by CameraFollow.bottomControlsReserve (16 %):
        /// Left/Right bottom-left, Serve/Bump bottom-right, 110 px, semi-transparent.
        /// </summary>
        private static void LayoutTouchButtons()
        {
            const float size = 110f, margin = 20f, gap = 36f;
            foreach (var b in Object.FindObjectsByType<UIButtonInputTest>(FindObjectsInactive.Include))
            {
                int id = new SerializedObject(b).FindProperty("buttonId").intValue;
                var rt = b.GetComponent<RectTransform>();
                bool leftSide = id == 0 || id == 1;
                // slot 0 = outermost; left side: Left(0) outer, Right(1) inner; right side: Bump(2) outer, Serve(3) inner
                int slot = (id == 0 || id == 2) ? 0 : 1;
                float x = margin + size * 0.5f + slot * (size + gap);
                rt.anchorMin = rt.anchorMax = leftSide ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(leftSide ? x : -x, margin + size * 0.5f);
                var img = b.GetComponent<Image>();
                if (img != null)
                {
                    // Legibility: the original icons are black shapes that disappear on the dark backdrop.
                    // Button root becomes a light translucent disc; the original icon moves to an opaque child.
                    Transform icon = b.transform.Find("Icon");
                    if (icon == null)
                    {
                        var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                        go.layer = b.gameObject.layer;
                        go.transform.SetParent(b.transform, false);
                        var irt = (RectTransform)go.transform;
                        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
                        irt.offsetMin = new Vector2(20f, 20f); irt.offsetMax = new Vector2(-20f, -20f);
                        var iimg = go.GetComponent<Image>();
                        iimg.sprite = img.sprite;
                        iimg.preserveAspect = true;
                        iimg.raycastTarget = false;
                        iimg.color = Color.white;
                        icon = go.transform;
                    }
                    var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                    if (knob != null) img.sprite = knob;
                    img.preserveAspect = true;
                    img.color = new Color(0.85f, 0.9f, 1f, 0.55f);
                }
                Log($"Touch button '{b.gameObject.name}' (ui id {id}) -> {(leftSide ? "bottom-left" : "bottom-right")} slot {slot}, {size}px, translucent disc + icon");
            }
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "The Spike Cross";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.projecttotnghiep.thespikecross");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Log("PlayerSettings: landscape only, productName 'The Spike Cross', gameplay scene first in Build Settings");
        }

        // ---------------------------------------------------------------- generated assets
        private static Material EnsureLineMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
            if (mat != null) return mat;
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh) { name = "UnlitVertexColor" };
            AssetDatabase.CreateAsset(mat, LineMaterialPath);
            Log($"Created {LineMaterialPath} with shader {sh.name}");
            return mat;
        }

        private static Sprite EnsureBallSprite()
        {
            if (!File.Exists(BallSpritePath))
            {
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var c = new Vector2((N - 1) * 0.5f, (N - 1) * 0.5f);
                float R = N * 0.5f - 2f;
                Color white = new Color(0.98f, 0.98f, 0.95f), blue = new Color(0.16f, 0.36f, 0.82f), yellow = new Color(1f, 0.80f, 0.15f), outline = new Color(0.08f, 0.10f, 0.18f);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Vector2 p = new Vector2(x, y) - c;
                    float d = p.magnitude;
                    Color col = Color.clear;
                    if (d <= R)
                    {
                        float ang = Mathf.Atan2(p.y, p.x) + 0.35f * Mathf.Sin(d / R * 3.1f);
                        int panel = Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI / 3f)) % 3;
                        col = panel == 0 ? white : panel == 1 ? blue : yellow;
                        float shade = Mathf.Lerp(1.05f, 0.75f, Mathf.Clamp01((p + new Vector2(R * 0.35f, -R * 0.35f)).magnitude / (R * 1.6f)));
                        col = new Color(col.r * shade, col.g * shade, col.b * shade, 1f);
                        if (d > R - 5f) col = outline;
                    }
                    else if (d <= R + 1f) col = new Color(outline.r, outline.g, outline.b, R + 1f - d);
                    tex.SetPixel(x, y, col);
                }
                tex.Apply();
                File.WriteAllBytes(BallSpritePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(BallSpritePath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(BallSpritePath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = 128;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Bilinear;
                imp.SaveAndReimport();
                Log("Generated " + BallSpritePath);
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(BallSpritePath);
        }

        /// <summary>Serve icon in the same flat black-on-white style as the existing MoveLeft/Bump icons.</summary>
        private static Sprite EnsureServeIcon()
        {
            if (!File.Exists(ServeIconPath))
            {
                const int W = 800, H = 600;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                var px = new Color32[W * H];
                Vector2 ballC = new Vector2(400, 215);
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool black = false;
                    Vector2 p = new Vector2(x, y);
                    float d = (p - ballC).magnitude;
                    if (d <= 85f && d >= 70f) black = true;              // ball outline
                    if (d < 70f && Mathf.Abs((p - ballC).x * 0.6f + (p - ballC).y) < 6f) black = true; // seam
                    if (d < 70f && Mathf.Abs((p - ballC).x - (p - ballC).y * 0.6f) < 6f) black = true;
                    // upward chevron above the ball
                    float cx = Mathf.Abs(x - 400f);
                    float top = 470f - cx;
                    if (cx < 110f && y <= top && y >= top - 55f && y > 330f) black = true;
                    px[y * W + x] = black ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(ServeIconPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ServeIconPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(ServeIconPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.SaveAndReimport();
                Log("Generated " + ServeIconPath);
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(ServeIconPath);
        }

        private static void Log(string s) => Debug.Log("[SceneSetup] " + s);
    }
}
