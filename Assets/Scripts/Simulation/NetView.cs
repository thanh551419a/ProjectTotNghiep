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
            float nearY = -CourtGeometry.CourtBandHalfDepth - 0.12f;
            float bluePostTop = Mathf.Min(top * 0.68f, bottom + 0.15f);
            float bluePostHalfW = Mathf.Max(0.14f, postW * 1.35f);

            // Back post & net spine from floor to net top
            var postLr = LineGeometry.Line(root, "Post", mat, new Color(0.28f, 0.32f, 0.38f, 1f), postW * 1.15f, SortNet, false, new Vector3(0f, 0f, 0f), new Vector3(0f, top, 0f));
            if (postLr != null) postLr.useWorldSpace = false;

            // Prominent Royal Blue Front Net Post (from Near Sideline -d up to lower net tape, matching The Spike Cross)
            Color frontPostBlue = new Color(0.145f, 0.39f, 0.92f, 1f);
            Color frontPostHighlight = new Color(0.26f, 0.52f, 0.98f, 1f);
            LineGeometry.Quad(root, "FrontBluePost", mat, frontPostBlue,
                Rect.MinMaxRect(-bluePostHalfW, nearY, bluePostHalfW, bluePostTop), SortNet + 4);
            LineGeometry.Quad(root, "FrontBluePostHighlight", mat, frontPostHighlight,
                Rect.MinMaxRect(-bluePostHalfW * 0.65f, nearY, -bluePostHalfW * 0.15f, bluePostTop), SortNet + 5);
            LineGeometry.Quad(root, "FrontBluePostCap", mat, tapeColor,
                Rect.MinMaxRect(-bluePostHalfW, bluePostTop - 0.06f, bluePostHalfW, bluePostTop + 0.04f), SortNet + 5);

            // Base plate on the floor
            var baseLr = LineGeometry.Line(root, "Base", mat, postColor, 0.06f, SortNet, false, new Vector3(-0.18f, 0.03f, 0f), new Vector3(0.18f, 0.03f, 0f));
            if (baseLr != null) baseLr.useWorldSpace = false;

            // Mesh panel (fine championship grid)
            int cols = 6;
            for (int i = 0; i <= cols; i++)
            {
                float x = Mathf.Lerp(-hw, hw, i / (float)cols);
                var meshVLr = LineGeometry.Line(root, "MeshV" + i, mat, meshColor, 0.016f, SortNet + 1, false, new Vector3(x, bottom, 0f), new Vector3(x, top, 0f));
                if (meshVLr != null) meshVLr.useWorldSpace = false;
            }
            int rows = Mathf.Max(3, Mathf.RoundToInt(panelDepth / 0.085f));
            for (int j = 0; j <= rows; j++)
            {
                float y = Mathf.Lerp(bottom, top, j / (float)rows);
                var meshHLr = LineGeometry.Line(root, "MeshH" + j, mat, meshColor, 0.016f, SortNet + 1, false, new Vector3(-hw, y, 0f), new Vector3(hw, y, 0f));
                if (meshHLr != null) meshHLr.useWorldSpace = false;
            }
            // Top & bottom canvas tapes
            var topTapeLr = LineGeometry.Line(root, "TopTape", mat, tapeColor, 0.085f, SortNet + 2, false, new Vector3(-hw - 0.02f, top, 0f), new Vector3(hw + 0.02f, top, 0f));
            if (topTapeLr != null) topTapeLr.useWorldSpace = false;
            var bottomTapeLr = LineGeometry.Line(root, "BottomTape", mat, tapeColor, 0.045f, SortNet + 2, false, new Vector3(-hw, bottom, 0f), new Vector3(hw, bottom, 0f));
            if (bottomTapeLr != null) bottomTapeLr.useWorldSpace = false;

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
                var antLr = LineGeometry.Line(root, "AntennaStripe_" + s, mat, sc, 0.028f, SortNet + 3, false,
                    new Vector3(0f, sy0, 0f), new Vector3(0f, sy1, 0f));
                if (antLr != null) antLr.useWorldSpace = false;
            }
        }
    }
}
