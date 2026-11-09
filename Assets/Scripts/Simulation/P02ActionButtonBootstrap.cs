using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Simulation
{
    /// <summary>Creates the four additional controls without replacing serialized legacy controls.</summary>
    public static class P02ActionButtonBootstrap
    {
        public const string RootName = "SpikeActionButtons";
        private const float MinWidthPixels = 96f;
        private const float MinHeightPixels = 72f;
        private const float GapPixels = 8f;
        private const float BottomReservation = 0.16f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            if (!EnsureButtons(SceneManager.GetActiveScene(), out string error))
                Debug.LogError("[P02ActionButtonBootstrap] " + error);
        }

        /// <summary>Public for the Editor acceptance test; does not tick or alter simulation state.</summary>
        public static bool EnsureButtons(Scene scene, out string error)
        {
            error = null;
            if (!scene.IsValid() || !scene.isLoaded)
                return Fail("Active gameplay scene is invalid or unloaded.", out error);

            SpikeSimulationManager manager = null;
            int managerCount = 0;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Exclude))
            {
                if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled) continue;
                manager = candidate;
                managerCount++;
            }
            if (managerCount != 1)
                return Fail($"Expected exactly one active simulation manager in scene '{scene.name}', found {managerCount}.", out error);

            EventSystem eventSystem = null;
            int eventSystemCount = 0;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude))
            {
                if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled) continue;
                var module = candidate.GetComponent<BaseInputModule>();
                if (module == null || !module.isActiveAndEnabled) continue;
                eventSystem = candidate;
                eventSystemCount++;
            }
            if (eventSystemCount != 1)
                return Fail($"Expected exactly one active EventSystem with an enabled input module in scene '{scene.name}', found {eventSystemCount}.", out error);

            var candidates = new List<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (canvas.gameObject.scene != scene || !canvas.isActiveAndEnabled) continue;
                var buttons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
                if (CountId(buttons, 0) == 1 && CountId(buttons, 1) == 1 &&
                    CountId(buttons, 2) == 1 && CountId(buttons, 3) == 1)
                    candidates.Add(canvas);
            }
            if (candidates.Count != 1)
                return Fail($"Expected exactly one active-scene Canvas containing one each of legacy button IDs 0-3, found {candidates.Count}.", out error);

            var selectedCanvas = candidates[0];
            if (selectedCanvas.GetComponent<GraphicRaycaster>() == null ||
                !selectedCanvas.GetComponent<GraphicRaycaster>().isActiveAndEnabled)
                return Fail("Gameplay Canvas has no enabled GraphicRaycaster.", out error);
            if (eventSystem.gameObject.scene != selectedCanvas.gameObject.scene)
                return Fail("EventSystem and gameplay Canvas belong to different scenes.", out error);

            Transform actionRoot = selectedCanvas.transform.Find(RootName);
            if (actionRoot == null)
            {
                var current = selectedCanvas.GetComponentsInChildren<UIButtonInputTest>(true);
                if (CountId(current, 4) + CountId(current, 5) + CountId(current, 6) + CountId(current, 7) != 0)
                    return Fail("Action button ID 4-7 already exists outside the named action root.", out error);
                actionRoot = CreateRoot(selectedCanvas);
                CreateButton(actionRoot, selectedCanvas.gameObject.layer, 4, "Jump");
                CreateButton(actionRoot, selectedCanvas.gameObject.layer, 5, "Spike Light");
                CreateButton(actionRoot, selectedCanvas.gameObject.layer, 6, "Spike Medium");
                CreateButton(actionRoot, selectedCanvas.gameObject.layer, 7, "Spike Strong");
            }
            else if (!ValidateActionRoot(actionRoot, selectedCanvas, out error))
            {
                return false;
            }

            if (!ValidateAllIds(selectedCanvas, actionRoot, out error))
                return false;

            Canvas.ForceUpdateCanvases();
            if (!ValidateScreenGeometry(selectedCanvas, actionRoot, out error))
                return false;

            var adapter = manager.GetComponent<SpikeInputAdapter>();
            if (adapter == null)
                return Fail("Simulation manager has no SpikeInputAdapter.", out error);
            adapter.BindUiButtons(scene);
            return true;
        }

        public static bool TryComputeRowRect(int width, int height, Rect safeArea, out Rect row)
        {
            float rowWidth = 4f * MinWidthPixels + 3f * GapPixels;
            float x = safeArea.center.x - rowWidth * 0.5f;
            float y = Mathf.Max(safeArea.yMin, GapPixels);
            row = new Rect(x, y, rowWidth, MinHeightPixels);
            return width > 0 && height > 0 && row.xMin >= safeArea.xMin && row.xMax <= safeArea.xMax &&
                   row.yMin >= safeArea.yMin && row.yMax <= safeArea.yMax &&
                   row.yMax <= height * BottomReservation;
        }

        private static Transform CreateRoot(Canvas canvas)
        {
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            var rectCanvas = (RectTransform)canvas.transform;
            var rootObject = new GameObject(RootName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rootObject.layer = canvas.gameObject.layer;
            var root = (RectTransform)rootObject.transform;
            root.SetParent(canvas.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);

            Vector2 safeBottomLocal;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect safeArea = Screen.safeArea;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectCanvas,
                new Vector2(safeArea.center.x, safeArea.yMin),
                camera,
                out safeBottomLocal);
            root.anchoredPosition = new Vector2(
                safeBottomLocal.x,
                safeBottomLocal.y - rectCanvas.rect.yMin + GapPixels / scale);
            root.sizeDelta = new Vector2((4f * MinWidthPixels + 3f * GapPixels) / scale, MinHeightPixels / scale);

            var layout = rootObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = GapPixels / scale;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return root;
        }

        private static void CreateButton(Transform root, int canvasLayer, int id, string label)
        {
            float scale = Mathf.Max(0.01f, root.GetComponentInParent<Canvas>().scaleFactor);
            var buttonObject = new GameObject(
                "Action_" + id,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement),
                typeof(UIButtonInputTest));
            buttonObject.layer = canvasLayer;
            buttonObject.transform.SetParent(root, false);
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.sizeDelta = new Vector2(MinWidthPixels / scale, MinHeightPixels / scale);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.85f, 0.9f, 1f, 0.9f);
            image.raycastTarget = true;
            var element = buttonObject.GetComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = MinWidthPixels / scale;
            element.minHeight = element.preferredHeight = MinHeightPixels / scale;
            buttonObject.GetComponent<UIButtonInputTest>().Configure(id, true);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.layer = canvasLayer;
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);
            var text = labelObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = 16;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.color = Color.black;
            text.raycastTarget = false;
        }

        private static bool ValidateActionRoot(Transform root, Canvas canvas, out string error)
        {
            error = null;
            if (root.parent != canvas.transform || root.GetComponent<HorizontalLayoutGroup>() == null ||
                root.gameObject.layer != canvas.gameObject.layer)
                return Fail("Existing action root has an incorrect parent, layout, or layer.", out error);
            var buttons = root.GetComponentsInChildren<UIButtonInputTest>(true);
            if (buttons.Length != 4)
                return Fail("Existing action root must contain exactly four action controls.", out error);
            for (int id = 4; id <= 7; id++)
            {
                var found = FindById(buttons, id);
                if (found == null || found.gameObject.layer != canvas.gameObject.layer ||
                    found.transform.parent != root || FindLabel(found) != ExpectedLabel(id))
                    return Fail($"Existing action root has invalid button ID {id}, label, parent, or layer.", out error);
                foreach (var child in found.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.layer != canvas.gameObject.layer)
                        return Fail($"Existing action button ID {id} has a child on the wrong Canvas layer.", out error);
            }
            return true;
        }

        private static bool ValidateAllIds(Canvas canvas, Transform actionRoot, out string error)
        {
            var buttons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
            for (int id = 0; id < 8; id++)
            {
                if (CountId(buttons, id) != 1)
                    return Fail($"Expected exactly one gameplay button with ID {id}, found {CountId(buttons, id)}.", out error);
            }
            for (int id = 4; id <= 7; id++)
            {
                var button = FindById(buttons, id);
                if (button.transform.parent != actionRoot)
                    return Fail($"Action button ID {id} is not a direct child of {RootName}.", out error);
            }
            error = null;
            return true;
        }

        private static bool ValidateScreenGeometry(Canvas canvas, Transform root, out string error)
        {
            error = null;
            var row = ScreenRect((RectTransform)root, canvas);
            if (!TryComputeRowRect(Screen.width, Screen.height, Screen.safeArea, out var expected))
                return Fail($"Four-button action row cannot fit the safe-area bottom band at {Screen.width}x{Screen.height}.", out error);
            if (row.width < expected.width - 2f || row.height < MinHeightPixels - 2f ||
                row.xMin < Screen.safeArea.xMin || row.xMax > Screen.safeArea.xMax ||
                row.yMin < Screen.safeArea.yMin || row.yMax > Screen.safeArea.yMax ||
                row.yMax > Screen.height * BottomReservation)
                return Fail("Action root violates screen-space safe-area or reserved-band geometry.", out error);

            float hudScale = Mathf.Max(0.75f, Screen.height / 1080f * 1.4f);
            float hudWidth = 360f * hudScale;
            float hudHeight = 82f * hudScale;
            var hudRect = new Rect((Screen.width - hudWidth) * 0.5f, Screen.height - hudHeight - 10f * hudScale, hudWidth, hudHeight);
            if (row.Overlaps(hudRect))
                return Fail("Action row overlaps the score/status HUD bounds.", out error);

            var buttons = root.GetComponentsInChildren<UIButtonInputTest>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var r = ScreenRect((RectTransform)buttons[i].transform, canvas);
                if (r.width < MinWidthPixels - 2f || r.height < MinHeightPixels - 2f)
                    return Fail($"Action button ID {buttons[i].ButtonId} is below minimum touch size.", out error);
                if (r.xMin < Screen.safeArea.xMin || r.xMax > Screen.safeArea.xMax ||
                    r.yMin < Screen.safeArea.yMin || r.yMax > Screen.safeArea.yMax)
                    return Fail($"Action button ID {buttons[i].ButtonId} is outside Screen.safeArea.", out error);
                foreach (var legacy in canvas.GetComponentsInChildren<UIButtonInputTest>(true))
                {
                    if (legacy.ButtonId > 3) continue;
                    if (r.Overlaps(ScreenRect((RectTransform)legacy.transform, canvas)))
                        return Fail($"Action button ID {buttons[i].ButtonId} overlaps legacy button ID {legacy.ButtonId}.", out error);
                }
                for (int j = i + 1; j < buttons.Length; j++)
                {
                    var other = ScreenRect((RectTransform)buttons[j].transform, canvas);
                    if (r.Overlaps(other) || Mathf.Abs(r.xMax - other.xMin) < GapPixels - 1f)
                        return Fail("Action buttons overlap or violate the minimum clear gap.", out error);
                }
            }
            var allButtons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
            for (int i = 0; i < allButtons.Length; i++)
            {
                var left = ScreenRect((RectTransform)allButtons[i].transform, canvas);
                for (int j = i + 1; j < allButtons.Length; j++)
                {
                    var right = ScreenRect((RectTransform)allButtons[j].transform, canvas);
                    if (left.Overlaps(right))
                        return Fail($"Gameplay buttons {allButtons[i].ButtonId} and {allButtons[j].ButtonId} overlap.", out error);
                }
            }
            return true;
        }

        private static Rect ScreenRect(RectTransform rect, Canvas canvas)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static int CountId(UIButtonInputTest[] buttons, int id)
        {
            int count = 0;
            foreach (var button in buttons) if (button.ButtonId == id) count++;
            return count;
        }

        private static UIButtonInputTest FindById(UIButtonInputTest[] buttons, int id)
        {
            foreach (var button in buttons) if (button.ButtonId == id) return button;
            return null;
        }

        private static string FindLabel(UIButtonInputTest button)
        {
            var label = button.GetComponentInChildren<Text>(true);
            return label == null ? null : label.text;
        }

        private static string ExpectedLabel(int id)
        {
            switch (id)
            {
                case 4: return "Jump";
                case 5: return "Spike Light";
                case 6: return "Spike Medium";
                case 7: return "Spike Strong";
                default: return null;
            }
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
