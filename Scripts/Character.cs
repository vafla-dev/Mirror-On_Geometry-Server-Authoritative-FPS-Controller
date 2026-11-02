using Mirror;
using Unity.VisualScripting;
using UnityEditor.Rendering;
using UnityEngine;

namespace Mirror_Server_Authoritative_FPS_Controller
{
    public class Character : NetworkBehaviour
    {
        #region Data

        [SerializeField] public Animator _animator;
        [Space]
        [SerializeField] private float speedFilling = 5;
        [SerializeField] private float multiplier = 1;
        [SerializeField] private float maxValue = float.MaxValue;
        [Space]
        [SerializeField] public bool debugMod = false;

        private Vector2 current = Vector2.zero;
        private Vector2 target = Vector2.zero;

        #endregion

        #region MonoBehaviour

        private void Update()
        {
            if (isLocalPlayer && !debugMod && !NetworkServer.active) return;

            current.y = Mathf.MoveTowards(current.y, target.y, Time.deltaTime * speedFilling);
            current.x = Mathf.MoveTowards(current.x, target.x, Time.deltaTime * speedFilling);

            if (_animator == null) return;

            _animator.SetFloat("IY", current.y);
            _animator.SetFloat("IX", current.x);
        }

        protected virtual void Start()
        {
            if (isLocalPlayer && !debugMod && _animator != null)
            {
                if (NetworkServer.active)
                    HideMesh(_animator.gameObject);
                else
                    Destroy(_animator.gameObject);
            }
        }

        #endregion

        #region Method

        public void SetTarget(Vector2 target)
        {
            target *= multiplier;
            target.y = Mathf.Clamp(target.y, -maxValue, maxValue);
            target.x = Mathf.Clamp(target.x, -maxValue, maxValue);

            this.target = target;
        }

        public void SetRotHed(float rot)
        {
            if (_animator == null) return;

            rot /= 90;

            _animator.SetFloat("rot head", rot);
        }

        public void PlayAnumJump()
        {
            if (_animator == null) return;

            _animator.Play("Jump", 0);
            _animator.Play("Jump", 1);
        }

        public void SetBoolIsGrounded(bool b)
        {
            if (_animator == null) return;

            _animator.SetBool("isGrounded", b);
        }

        protected void HideMesh(GameObject obj)
        {
            if (obj == null) return;

            if (obj.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer skinnedMeshRenderer))
            {
                skinnedMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            if (obj.TryGetComponent<MeshRenderer>(out MeshRenderer meshRenderer))
            {
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            foreach (Transform child in obj.transform)
            {
                HideMesh(child.gameObject);
            }
        }

        #endregion
    }
}
