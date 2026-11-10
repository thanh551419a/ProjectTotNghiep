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

            // Post: exactly the native AABB height from the floor + protective post pad.
            LineGeometry.Line(root, "Post", mat, postColor, postW, SortNet, false, new Vector3(0f, 0f, 0f), new Vector3(0f, top, 0f));
            LineGeometry.Line(root, "PostPad", mat, new Color(0.14f, 0.38f, 0.68f, 0.95f), postW * 1.45f, SortNet + 1, false,
                new Vector3(0f, 0.08f, 0f), new Vector3(0f, Mathf.Min(top * 0.72f, bottom), 0f));
            // Base plate on the floor
            LineGeometry.Line(root, "Base", mat, postColor, 0.06f, SortNet, false, new Vector3(-0.18f, 0.03f, 0f), new Vector3(0.18f, 0.03f, 0f));

            // Mesh panel (fine championship grid)
            int cols = 6;
            for (int i = 0; i <= cols; i++)
            {
                float x = Mathf.Lerp(-hw, hw, i / (float)cols);
                LineGeometry.Line(root, "MeshV" + i, mat, meshColor, 0.016f, SortNet + 1, false, new Vector3(x, bottom, 0f), new Vector3(x, top, 0f));
            }
            int rows = Mathf.Max(3, Mathf.RoundToInt(panelDepth / 0.085f));
            for (int j = 0; j <= rows; j++)
            {
                float y = Mathf.Lerp(bottom, top, j / (float)rows);
                LineGeometry.Line(root, "MeshH" + j, mat, meshColor, 0.016f, SortNet + 1, false, new Vector3(-hw, y, 0f), new Vector3(hw, y, 0f));
            }
            // Top & bottom canvas tapes
            LineGeometry.Line(root, "TopTape", mat, tapeColor, 0.085f, SortNet + 2, false, new Vector3(-hw - 0.02f, top, 0f), new Vector3(hw + 0.02f, top, 0f));
            LineGeometry.Line(root, "BottomTape", mat, tapeColor, 0.045f, SortNet + 2, false, new Vector3(-hw, bottom, 0f), new Vector3(hw, bottom, 0f));

            // Championship red-and-white striped side antenna extending above the top tape
            float antennaBottom = bottom;
            float antennaTop = top + 0.55f;
            int stripes = 6;
            Color antennaRed = new Color(0.92f, 0.18f, 0.18f, 1f);
            for (int s = 0; s < stripes; s++)
            {
                float sy0 = Mathf.Lerp(antennaBottom, antennaTop, s / (float)stripes);
                float sy1 = Mathf.Lerp(antennaBottom, antennaTop, (s + 1) / (float)stripes);
                Color sc = (s % 2 == 0) ? tapeColor : antennaRed;
                LineGeometry.Line(root, "AntennaStripe_" + s, mat, sc, 0.028f, SortNet + 3, false,
                    new Vector3(0f, sy0, 0f), new Vector3(0f, sy1, 0f));
            }
        }
    }
}
