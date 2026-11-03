using Assets.Table;
using UnityEngine;

public class UIInputHandler : MonoBehaviour
{
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float groundY = 0f;

    private Rigidbody playerRigidbody;
    private bool leftHeld;
    private bool rightHeld;
    private Animator playerAnimator;
    private UIButtonInputTest[] buttons;
    private Transform player;
    private CharacterStateTable stateTable;

    [SerializeField] private float moveSpeed = 5f;


    private void Awake()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("UIInputHandler: Không tìm thấy Canvas!");
            return;
        }

        buttons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
        stateTable = FindFirstObjectByType<CharacterStateTable>();

        Debug.Log($"UIInputHandler: Found {buttons.Length} buttons in Canvas.");

        foreach (var button in buttons)
        {
            Debug.Log($"Found: {button.name}");
        }

        GameObject playerObject = GameObject.Find("Player");

        if (playerObject == null)
        {
            Debug.LogError("UIInputHandler: Không tìm thấy Player!");
            return;
        }

        player = playerObject.transform;
        playerAnimator = playerObject.GetComponent<Animator>();
        playerRigidbody = playerObject.GetComponent<Rigidbody>();
        Debug.Log($"UIInputHandler: Found {buttons.Length} buttons.");
        Debug.Log($"Player: {player.name}");
    }

    private void OnEnable()
    {
        if (buttons == null)
            return;

        foreach (var button in buttons)
        {
            if (button != null)
                button.OnInput += HandleInput;
        }
    }

    private void OnDisable()
    {
        if (buttons == null)
            return;

        foreach (var button in buttons)
        {
            if (button != null)
                button.OnInput -= HandleInput;
        }
    }

    [SerializeField] private bool useAuthoritativeSimulation = true;

    private void HandleInput(int buttonId, bool isPressed)
    {
        if (useAuthoritativeSimulation)
        {
            // Simulation is authoritatively handled by SpikeSimulationManager
            return;
        }

        Debug.Log(
            $"HANDLER CATCH: ID = {buttonId}, Pressed = {isPressed}"
        );

        if (buttonId == 0)
        {
            //FinishCurrentAnimation();
            leftHeld = isPressed;

            stateTable.SetDirectionValue(0, -1);
            stateTable.SetStateValue(0, 20);

            player.position += Vector3.left * moveSpeed * Time.deltaTime;



        }
        else if (buttonId == 1)
        {
            //FinishCurrentAnimation();
            rightHeld = isPressed;

            stateTable.SetDirectionValue(0, 1);
            stateTable.SetStateValue(0, 20);

            player.position += Vector3.right * moveSpeed * Time.deltaTime;
        }
        else if (buttonId == 2 && isPressed)
        {
            if (player.position.y <= groundY + 0.01f)
            {
                playerRigidbody.AddForce(
                    Vector2.up * jumpForce,
                    ForceMode.Impulse
                );

                stateTable.SetStateValue(0, 30);
            }
        }
        if (!leftHeld && !rightHeld)
        {
            stateTable.SetStateValue(0, 10);

            FinishCurrentAnimation();
        }
    }
    private void Update()
    {
        if (useAuthoritativeSimulation || playerRigidbody == null)
            return;

        Debug.Log($"Rigidbody2D Position: {playerRigidbody.position}");

        if (playerRigidbody.position.y <= groundY)
        {
            Vector2 position = playerRigidbody.position;
            position.y = groundY;

            playerRigidbody.position = position;

            Vector2 velocity = playerRigidbody.linearVelocity;

            if (velocity.y < 0f)
                velocity.y = 0f;

            playerRigidbody.linearVelocity = velocity;
        }
    }
    private void FinishCurrentAnimation()
    {
        if (playerAnimator == null)
            return;

        AnimatorStateInfo currentState =
            playerAnimator.GetCurrentAnimatorStateInfo(0);

        playerAnimator.Play(
            currentState.fullPathHash,
            0,
            1f
        );
    }
}