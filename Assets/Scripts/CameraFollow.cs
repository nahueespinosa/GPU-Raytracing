using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform objectToFollow;
    Vector3 offsetDirection;
    float offsetMagnitude;

    void updateOffset(Vector3 offset) {
        offsetDirection = offset.normalized;
        offsetMagnitude = offset.magnitude;
    }

    void updateYaw(float val) {
        Vector3 offset = offsetMagnitude * offsetDirection;
        offset += -0.1f * val * offsetMagnitude * transform.right;
        offsetDirection = offset.normalized;
    }

    void updatePitch(float val) {
        Vector3 offset = offsetMagnitude * offsetDirection;
        offset += -0.1f * val * offsetMagnitude * transform.up;
        offsetDirection = offset.normalized;
    }

    void updateZoom(float val) {
        offsetMagnitude = Mathf.Clamp(offsetMagnitude - val * 4.0f, -1000.0f, 1000.0f);
    }

    void Start()
    {
        if (objectToFollow == null)
            objectToFollow = new GameObject().transform;

        updateOffset(transform.position - objectToFollow.position);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) {
            Cursor.lockState = CursorLockMode.Locked;
        }
        else if (Input.GetMouseButton(0)) {
            float mouseX = Input.GetAxis("Mouse X");
            mouseX = Mathf.Clamp(mouseX, -4.0f, 4.0f);
            updateYaw(mouseX);
            float mouseY = Input.GetAxis("Mouse Y");
            mouseY = Mathf.Clamp(mouseY, -4.0f, 4.0f);
            updatePitch(mouseY);
        }
        else {
            Cursor.lockState = CursorLockMode.None;
        }

        updateZoom(Input.GetAxis("Mouse ScrollWheel"));

        transform.position = objectToFollow.position + offsetMagnitude * offsetDirection;
        transform.LookAt(objectToFollow);
    }
}
