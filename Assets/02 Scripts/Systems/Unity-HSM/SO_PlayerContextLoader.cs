using UnityEngine;

[CreateAssetMenu(menuName = "PlayerContextLoader")]
public class SO_PlayerContextLoader : ScriptableObject
{
    [Header("Movement")]
    public float moveSpeed = 6f;

    public float accel = 40f;
    public float decel = 50f;

    [Header("Airborn")]
    public float jumpForce = 7f;

    public float gravForce = 9.81f;
    public float drag = 1;
    
    [Header("Modifiers")]
    public float sprintMod = 2;
    public float crouchMod = .5f;

    [Header("Technical")]
    public float groundCheckLength = .5f;
}
