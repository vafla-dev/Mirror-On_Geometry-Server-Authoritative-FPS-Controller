using UnityEngine;

namespace Mirror_Server_Authoritative_FPS_Controller
{
    public class CameraShake : MonoBehaviour
    {
        [SerializeField] private MirrorFPSController m_Controller;
        [Space]
        [SerializeField] private float speed = 2;
        [SerializeField] private float amplitude = 0.02f;

        private Vector3 lastPos;

        private float distance;

        private void Update()
        {
            if (distance > float.MaxValue - 100)
                distance = 0;

            distance += (transform.position - lastPos).magnitude;

            float y = Mathf.Sin(distance * speed * (((transform.position - lastPos).magnitude == 0) ? 0 : 1)) * amplitude;

            if (m_Controller != null && m_Controller.noclip)
            {
                transform.localPosition = new Vector3(0, 0, 0);
                return;
            }

            transform.localPosition = new Vector3(0, y, 0);

            lastPos = transform.position;
        }
    }
}
