using Assets.Table;
using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Presentation-only floor shadow for the ball (readability aid at full-court zoom).
    /// Reads the BALL record (id 7) from the Data Table; it never writes the ball Transform and
    /// never computes any trajectory: x = native ball centre x, y = floor line, size/alpha shrink
    /// with the native ball height.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class BallShadowView : MonoBehaviour
    {
        [SerializeField] private Material material;
        [SerializeField] private float baseWidth = 0.42f;
        [SerializeField] private float baseHeight = 0.12f;
        [SerializeField] private float maxAlpha = 0.45f;

        private SpriteRenderer sr;
        private static Sprite ellipse;

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Ellipse;
            sr.sharedMaterial = LineGeometry.ResolveMaterial(material);
            sr.sortingOrder = CourtLineRenderer.SortLines + 5; // above court lines, below characters/net/ball
            sr.enabled = false;
        }

        private void LateUpdate()
        {
            var table = SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized || !table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var b) || !b.hasPosition)
            {
                sr.enabled = false;
                return;
            }
            float heightM = Mathf.Max(0f, CourtGeometry.ToWorldY(b.posY));
            float k = 1f / (1f + heightM * 0.25f);
            sr.enabled = !(b.hasBallGameplayState && b.stateFrame > 0 && b.posY < CourtGeometry.FloorY);
            transform.position = new Vector3(CourtGeometry.ToWorldX(b.posX + b.sizeX * 0.5f), 0f, 0f);
            transform.localScale = new Vector3(baseWidth * k, baseHeight * k, 1f);
            sr.color = new Color(0f, 0f, 0f, maxAlpha * k);
        }

        private static Sprite Ellipse
        {
            get
            {
                if (ellipse != null) return ellipse;
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - (N - 1) * 0.5f) / (N * 0.5f), dy = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.5f)));
                }
                tex.Apply();
                ellipse = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
                ellipse.hideFlags = HideFlags.DontSave;
                return ellipse;
            }
        }
    }
}
