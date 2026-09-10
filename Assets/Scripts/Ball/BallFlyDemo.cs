using UnityEngine;

public class BallFlyDemo : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField] private float flightTime = 2.0f;

    [Header("Position Limits")]
    [SerializeField] private float minX = -10f;
    [SerializeField] private float maxX = 10f;
    [SerializeField] private float maxY = 15f;

    [Header("Random")]
    [SerializeField] private float minPeakHeight = 2f;
    [SerializeField] private float maxPeakHeight = 8f;

    private Vector3 startPosition;
    private Vector3 targetPosition;

    private float elapsed;
    private float peakHeight;

    private void Start()
    {
        startPosition = transform.position;

        targetPosition = new Vector3(
            maxX,
            Random.Range(0f, maxY),
            transform.position.z
        );

        peakHeight = Random.Range(
            minPeakHeight,
            maxPeakHeight
        );
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float t = Mathf.Clamp01(elapsed / flightTime);

        float x = Mathf.Lerp(
            startPosition.x,
            targetPosition.x,
            t
        );

        float baseY = Mathf.Lerp(
            startPosition.y,
            targetPosition.y,
            t
        );

        // Parabol
        float arc = 4f * peakHeight * t * (1f - t);

        float y = baseY + arc;

        // Đảm bảo không vượt giới hạn
        x = Mathf.Clamp(x, minX, maxX);
        y = Mathf.Clamp(y, -10f, maxY);

        transform.position = new Vector3(
            x,
            y,
            transform.position.z
        );

        if (elapsed >= flightTime)
        {
            StartNewFlight();
        }
    }

    private void StartNewFlight()
    {
        startPosition = transform.position;

        float nextX =
            Mathf.Approximately(targetPosition.x, maxX)
            ? minX
            : maxX;

        targetPosition = new Vector3(
            nextX,
            Random.Range(0f, maxY),
            transform.position.z
        );

        peakHeight = Random.Range(
            minPeakHeight,
            maxPeakHeight
        );

        elapsed = 0f;
    }
}