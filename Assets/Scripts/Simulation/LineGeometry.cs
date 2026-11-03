using UnityEngine;

namespace Simulation
{
    /// <summary>Small helpers for procedural 2D line / quad geometry (court, net).</summary>
    public static class LineGeometry
    {
        private static Material fallbackMaterial;
        private static Sprite whiteSprite;

        /// <summary>Unlit vertex-colour material. Prefer the serialized asset (guaranteed to be in builds).</summary>
        public static Material ResolveMaterial(Material assigned)
        {
            if (assigned != null) return assigned;
            if (fallbackMaterial == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                fallbackMaterial = new Material(sh) { name = "RuntimeLineMaterial", hideFlags = HideFlags.DontSave };
            }
            return fallbackMaterial;
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "WhitePixel", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    whiteSprite.name = "WhiteUnitSprite";
                    whiteSprite.hideFlags = HideFlags.DontSave;
                }
                return whiteSprite;
            }
        }

        public static LineRenderer Line(Transform parent, string name, Material mat, Color color, float width, int sortingOrder, bool loop, params Vector3[] points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.sharedMaterial = mat;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.TransformZ;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = sortingOrder;
            return lr;
        }

        public static SpriteRenderer Quad(Transform parent, string name, Material mat, Color color, Rect worldRect, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(worldRect.center.x, worldRect.center.y, 0f);
            go.transform.localScale = new Vector3(worldRect.width, worldRect.height, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.sharedMaterial = mat;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
            }
        }
    }
}
