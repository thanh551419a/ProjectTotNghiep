using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Procedural volleyball net for native entity 6 (NET). The SimulationEntityReader places this
    /// root at the native AABB bottom-centre and calls SetSize with the native AABB size, so the
    /// post height and position always match the authoritative collision box (8 x 224 px = 0.1 x 2.8 m).
    /// Visual: post (native width), 1 m deep mesh panel under the top tape, white top/bottom tapes.
    /// </summary>
    public class NetView : MonoBehaviour
    {
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color postColor = new Color(0.80f, 0.84f, 0.90f, 1f);
        [SerializeField] private Color meshColor = new Color(0.92f, 0.95f, 1f, 0.55f);
        [SerializeField] private Color tapeColor = Color.white;
        [SerializeField] private float panelHalfWidth = 0.22f;
        [SerializeField] private float panelDepth = 1.0f;

        public const int SortNet = 500;

        private Transform root;
        private Vector2 builtSize = new Vector2(-1f, -1f);

        public Vector2 BuiltSize => builtSize;
        public int PartCount => root != null ? root.childCount : 0;

        public void SetSize(float widthWorld, float heightWorld)
        {
            if (Mathf.Approximately(builtSize.x, widthWorld) && Mathf.Approximately(builtSize.y, heightWorld)) return;
            Build(widthWorld, heightWorld);
        }

        private void Build(float w, float h)
        {
            if (root == null)
            {
                var existing = transform.Find("Generated");
                root = existing != null ? existing : new GameObject("Generated").transform;
                root.SetParent(transform, false);
            }
            LineGeometry.ClearChildren(root);
            builtSize = new Vector2(w, h);

            Material mat = LineGeometry.ResolveMaterial(lineMaterial);
            float postW = Mathf.Max(w, 0.06f);
            float top = h;
            float bottom = Mathf.Max(0f, h - panelDepth);
            float hw = panelHalfWidth;

            // Post: exactly the native AABB height from the floor.
            LineGeometry.Line(root, "Post", mat, postColor, postW, SortNet, false, new Vector3(0f, 0f, 0f), new Vector3(0f, top, 0f));
            // Base plate on the floor
            LineGeometry.Line(root, "Base", mat, postColor, 0.06f, SortNet, false, new Vector3(-0.18f, 0.03f, 0f), new Vector3(0.18f, 0.03f, 0f));

            // Mesh panel (grid)
            int cols = 4;
            for (int i = 0; i <= cols; i++)
            {
                float x = Mathf.Lerp(-hw, hw, i / (float)cols);
                LineGeometry.Line(root, "MeshV" + i, mat, meshColor, 0.018f, SortNet + 1, false, new Vector3(x, bottom, 0f), new Vector3(x, top, 0f));
            }
            int rows = Mathf.Max(2, Mathf.RoundToInt(panelDepth / 0.1f));
            for (int j = 0; j <= rows; j++)
            {
                float y = Mathf.Lerp(bottom, top, j / (float)rows);
                LineGeometry.Line(root, "MeshH" + j, mat, meshColor, 0.018f, SortNet + 1, false, new Vector3(-hw, y, 0f), new Vector3(hw, y, 0f));
            }
            // Tapes
            LineGeometry.Line(root, "TopTape", mat, tapeColor, 0.08f, SortNet + 2, false, new Vector3(-hw - 0.02f, top, 0f), new Vector3(hw + 0.02f, top, 0f));
            LineGeometry.Line(root, "BottomTape", mat, tapeColor, 0.04f, SortNet + 2, false, new Vector3(-hw, bottom, 0f), new Vector3(hw, bottom, 0f));
        }
    }
}
