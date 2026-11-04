using UnityEngine;

namespace DanceFlow.Unity.WebGLDemo
{
    public sealed class KandiCameraOrbit : MonoBehaviour
    {
        [SerializeField] private Vector3 target = new Vector3(-5, 2, 0);
        [SerializeField] private float distance = 12;
        [SerializeField] private float yaw = -6;
        [SerializeField] private float pitch = 17;
        private Vector3 _previous;

        public void Configure(Vector3 lookAt, float initialDistance)
        {
            target = lookAt;
            distance = initialDistance;
        }
        private void LateUpdate()
        {
            if (Input.GetMouseButtonDown(0)) _previous = Input.mousePosition;
            if (Input.GetMouseButton(0))
            {
                Vector3 delta = Input.mousePosition - _previous;
                _previous = Input.mousePosition;
                yaw += delta.x * 0.16f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -22, 80);
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, 4, 36);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
