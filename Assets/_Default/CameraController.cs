using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float m_MovementSpeed = 2.0f;
    public float m_RotationSpeed = 1.0f;

    private void Update()
    {
        if (!Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        float x = 0f, y = 0f, z = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            z = 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            z = -1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            x = -1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            x = 1;
        if (Input.GetKey(KeyCode.E))
            y = 1;
        if (Input.GetKey(KeyCode.Q))
            y = -1;

        Vector3 direction = new Vector3(x, y, z) * m_MovementSpeed * (Input.GetKey(KeyCode.LeftShift) ? 2f : Input.GetKey(KeyCode.LeftControl) ? 0.5f : 1f) * Time.deltaTime;
        transform.position += transform.TransformDirection(new Vector3(direction.x, direction.y, direction.z));

        transform.RotateAround(transform.position, transform.up, Input.GetAxis("Mouse X") * m_RotationSpeed);
        transform.RotateAround(transform.position, transform.right, -Input.GetAxis("Mouse Y") * m_RotationSpeed);
        transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, 0f);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}