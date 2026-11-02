//#define DEBUG_MOVEMENT

using Mirror;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


namespace Mirror_Server_Authoritative_FPS_Controller
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class MirrorFPSController : NetworkBehaviour
    {
        #region Data

        #region Structures

        private struct PhysicsNetPacket
        {
            public bool isGrounded;

            public float gravity;

            public PhysicsNetPacket(bool isGrounded, float gravity)
            {
                this.isGrounded = isGrounded;

                this.gravity = gravity;
            }
        }

        #endregion

        [Header("Data")]
        public Transform rotMainCamera;

        [Space, Header("NetSettings")]
        [SerializeField, SyncVar] public float speedMovement = 6;
        [SerializeField, SyncVar] public float increaseDuringSprinting = 1.333334f;
        [SerializeField, SyncVar] public bool noclip = false;
        [SerializeField, SyncVar] public float jumpForce = 7f;
        [SerializeField] private float maximumPermissibleDifferenceInPosition = 45;
        [SerializeField, Min(1)] private float maximumAllowableDifferenceInGravitationalAcceleration = 1;

        [Space, Header("Settings")]
        [SerializeField] private float sensitivity = 2;
        [SerializeField] private KeyCode sprintingKey = KeyCode.LeftShift;
        [SerializeField] public KeyCode useKey = KeyCode.E;
        [Space]
        [SerializeField] private LayerMask layerUse = ~0;
        [SerializeField] private float distanceUse = 4;

        [Space, Header("Physics")]
        [SerializeField] private float initialFallRate = -1;
        [SerializeField] private float accelerationOfGravity = 24.88f;


#if DEBUG_MOVEMENT

    [Space, Header("Debug")]
    [SerializeField] private GameObject prefabPlayerDebug;
    [NonSerialized] private GameObject playerDebug;

    [SerializeField] private GameObject prefabPlayerDebugTarget;
    [NonSerialized] private GameObject playerDebugTarget;
    [Space]
    [SerializeField] private bool isDebug;

#endif


        private CharacterController characterController;

        private Character character;

        protected float rotX;
        protected float rotY;

        private Vector3 currentPosForServer;
        private Vector3 targetForServer;
        public Vector2 rotServer { private set; get; }

        private List<Vector3> moveMotions = new List<Vector3>();

        private Vector3 MoveMotion
        {
            get
            {
                Vector3 end = Vector3.zero;

                foreach (Vector3 vector in moveMotions)
                {
                    end += vector;
                }

                return end;
            }
        }

        /// <summary>
        /// Shows the player's movement direction and speed—on the server!
        /// </summary>
        public Vector3 velocity { private set; get; }

        public ClientPhysicalData clientPhysicalData;

        private float isSprinting;

        protected float noclipSpeed = 10;

        private float gravity = -1;

        #endregion

        #region MonoBehaviour

        protected virtual void Start()
        {
            if (!isLocalPlayer)
            {
                Destroy(rotMainCamera.gameObject);
            }

#if DEBUG_MOVEMENT

        if (isLocalPlayer && isDebug)
        {
            playerDebug = Instantiate(prefabPlayerDebug, transform.position, Quaternion.Euler(0, 0, 0));
            playerDebugTarget = Instantiate(prefabPlayerDebugTarget, transform.position, Quaternion.Euler(0, 0, 0));
        }

#endif

            character = GetComponent<Character>();

            if (!isLocalPlayer && !NetworkServer.active) return;

            characterController = GetComponent<CharacterController>();
        }

        protected virtual void Update()
        {
            if (!isLocalPlayer && NetworkServer.active) MoveByServer();

            if (!isLocalPlayer) return;

            Vector3 motion = Vector3.zero;

            CheckUse();

            InputMove(ref motion);

            CalculateGravity(ref motion);

            Rotate();

            MoveByClient(motion);
        }

        #endregion

        #region Movement

        [Client]
        private void InputMove(ref Vector3 motion)
        {
            if (!CanMove()) return;

            if (!noclip)
                motion = transform.right * Input.GetAxis("Horizontal") + transform.forward * Input.GetAxis("Vertical");
            else
                motion = rotMainCamera.right * Input.GetAxis("Horizontal") + rotMainCamera.forward * Input.GetAxis("Vertical");


            if (Vector3.Distance(motion, Vector3.zero) > 1)
            {
                motion = motion.normalized;
            }


            motion = motion * Time.deltaTime * ((noclip) ? (noclipSpeed * (Input.GetKey(sprintingKey) ? 2 : 1)) : (Input.GetKey(KeyCode.LeftShift) ? (speedMovement * increaseDuringSprinting) : speedMovement));
        }

        [Client]
        private void MoveByClient(Vector3 motion)
        {
            if (noclip)
            {
                gravity = initialFallRate;

                characterController.enabled = false;

                transform.position += motion;

                noclipSpeed += Input.GetAxis("Mouse ScrollWheel") * 3;
                noclipSpeed = Mathf.Clamp(noclipSpeed, 0.1f, 200);

                motion = Vector3.zero;

                moveMotions.Clear();
            }
            else
            {
                Vector3 startPos = transform.position;

                characterController.enabled = true;

                if (motion.y == 0 && GravityCanBeCalculated())
                    motion.y = -0.01f;

                characterController.Move(motion + MoveMotion);

                moveMotions.Clear();

                motion = transform.position - startPos;
            }

            if (isServer)
            {
                velocity = motion / Time.deltaTime;

                RpcSetPos(transform.position, velocity, new PhysicsNetPacket(characterController.isGrounded, 0));
            }
            else
            {
                Vector3 vector = motion / Time.deltaTime / speedMovement;
                vector.y = motion.y;

                CmdSetMoveTarget(transform.position, transform.position + vector, Input.GetKey(sprintingKey) ? Time.deltaTime : 0);
#if DEBUG_MOVEMENT
            playerDebugTarget.transform.position = transform.position + vector;
#endif
            }
        }

        private float smoothMovementOnServer;

        [Server]
        private void MoveByServer()
        {
            if (!CanMove(true))
            {
                targetForServer = transform.position;
            }
            if (noclip)
            {
                gravity = initialFallRate;
                return;
            }

            characterController.enabled = true;

            Vector3 directional = targetForServer - transform.position;

            if (Vector3.Distance(directional, Vector3.zero) > 1)
                directional.Normalize();

            float to = 0.1f;

            if (Vector3.Distance(targetForServer, transform.position) > 0.01)
            {
                to = Vector3.Distance(directional, Vector3.zero);
            }

            smoothMovementOnServer = Mathf.MoveTowards(smoothMovementOnServer, to, 3 * Time.deltaTime);

            directional = directional * ((isSprinting > 0) ? (speedMovement * increaseDuringSprinting) : speedMovement) * smoothMovementOnServer * Time.deltaTime;

            isSprinting -= Time.deltaTime;

            isSprinting = Mathf.Min(isSprinting, 0);

            if (Vector3.Distance(directional, Vector3.zero) < Vector3.Distance(targetForServer - transform.position, Vector3.zero))
                P(transform.position,
                    currentPosForServer,
                    targetForServer,
                    Vector3.Distance(directional, Vector3.zero),
                    ref directional);

            if (GravityCanBeCalculated(true))
                CalculateGravityByServer(ref directional);

            Vector3 startPos = transform.position;

            characterController.Move(directional + MoveMotion);

            moveMotions.Clear();

            velocity = (transform.position - startPos) / Time.deltaTime;

            RpcSetPos(transform.position, velocity, new PhysicsNetPacket(characterController.isGrounded, gravity));

            if (!NetworkClient.active)
            {
                Vector3 delta3 = transform.InverseTransformDirection(velocity);
                Vector2 delta2 = new Vector2(delta3.x, delta3.z);

                character?.SetTarget(delta2);
            }

            bool P(Vector3 point, Vector3 startPoint, Vector3 endPoint, float radius, ref Vector3 delta)
            {
                Vector3 M = startPoint - point;
                Vector3 N = endPoint - startPoint;

                float a = (N.x * N.x) + (N.y * N.y) + (N.z * N.z);
                float b = (2 * M.x * N.x) + (2 * M.y * N.y) + (2 * M.z * N.z);
                float c = (M.x * M.x) + (M.y * M.y) + (M.z * M.z) - radius * radius;

                //at² + bt + c = 0

                float d = b * b - 4 * a * c;

                if (d < 0)
                    return false;

                d = Mathf.Sqrt(d);

                float[] x = new float[2];

                x[0] = (-b - d) / (2 * a);
                x[1] = (-b + d) / (2 * a);

                float t = -1;

                foreach (float value in x)
                {
                    if (value >= 0 && value <= 1)
                        if (value > t)
                            t = value;
                }
                if (t >= 0 && t <= 1)
                {
                    delta = startPoint + N * t - point;

                    return true;
                }

                t = Vector3.Dot(point - startPoint, N) / N.sqrMagnitude;

                if (t >= 0 && t <= 1)
                {
                    delta = startPoint + N * t - point;

                    delta.Normalize();

                    delta *= radius;

                    return true;
                }

                return false;
            }
        }

        [Command]
        private void CmdSetMoveTarget(Vector3 currentPos, Vector3 target, float isSprinting)
        {
            if (noclip)
            {
                characterController.enabled = false;

                transform.position = currentPos;

                RpcSetPos(transform.position, Vector3.zero, new PhysicsNetPacket(true, 0));

                moveMotions.Clear();

                return;
            }

            isSprinting = Mathf.Clamp(isSprinting, 0, int.MaxValue);

            this.isSprinting += isSprinting;

            this.isSprinting = Mathf.Clamp(this.isSprinting, 0, int.MaxValue);

            currentPosForServer = currentPos;
            targetForServer = target;
        }

        [ClientRpc]
        private void RpcSetPos(Vector3 pos, Vector3 delta, PhysicsNetPacket physicsPacket)
        {
            if (character != null)
            {
                Vector3 delta3 = transform.InverseTransformDirection(delta);
                Vector2 delta2 = new Vector2(delta3.x, delta3.z);

                character?.SetTarget(delta2);

                character?.SetBoolIsGrounded(physicsPacket.isGrounded);
            }

            if (isLocalPlayer)
            {
#if DEBUG_MOVEMENT
            if (isDebug && playerDebug != null)
                playerDebug.transform.position = pos;
#endif


                Vector3 diff = pos - transform.position;

                float maximumPermissibleDifferenceInPosition = this.maximumPermissibleDifferenceInPosition * ((Input.GetKey(sprintingKey)) ? (speedMovement * increaseDuringSprinting) : speedMovement) * Time.deltaTime;

                bool thatIsCorrect = (diff.x * diff.x + diff.z * diff.z) <= (maximumPermissibleDifferenceInPosition * maximumPermissibleDifferenceInPosition);

                if (!NetworkServer.active)
                    if ((this.gravity * physicsPacket.gravity > 0
                        && (Mathf.Abs(this.gravity - physicsPacket.gravity) > maximumAllowableDifferenceInGravitationalAcceleration + jumpForce))
                        || !thatIsCorrect)
                    {
                        this.gravity = physicsPacket.gravity;

                        clientPhysicalData.fall = Mathf.Clamp(-gravity, -0.9f, 0.9f);
                    }

                if (thatIsCorrect || noclip) return;

                Debug.LogWarning("corrective teleporter!");

                if (characterController != null)
                    characterController.enabled = false;

                transform.position = pos;

                if (characterController != null)
                    characterController.enabled = true;

                return;
            }

            SetPos(pos);
        }

        [ClientRpc]
        private void RpcSetPos(Vector3 pos)
            => SetPos(pos);

        private void SetPos(Vector3 pos)
        {
            if (characterController != null)
                characterController.enabled = false;

            transform.position = pos;

            if (characterController != null)
                characterController.enabled = true;
        }

        [TargetRpc]
        private void TargetAddMoveMotion(NetworkConnectionToClient conn, Vector3 motion)
        {
            moveMotions.Add(motion);
        }

        #endregion

        #region Rotation

        [Client]
        private void Rotate()
        {
            if (!CanRotate()) return;

            rotX += Input.GetAxis("Mouse X") * sensitivity;
            rotY -= Input.GetAxis("Mouse Y") * sensitivity;

            rotY = Mathf.Clamp(rotY, -90, 90);

            rotMainCamera.localRotation = Quaternion.Euler(rotY, 0, 0);

            transform.rotation = Quaternion.Euler(0, rotX, 0);

            CmdSetRot(new Vector2(rotX, rotY));
        }

        [Command]
        private void CmdSetRot(Vector2 rot)
        {
            if (!CanRotate(true)) return;

            rot.y = Mathf.Clamp(rot.y, -90, 90);

            this.rotServer = rot;

            RpcSetRot(rot);

            if (!NetworkClient.active)
            {
                character?.SetRotHed(rot.y);

                transform.rotation = Quaternion.Euler(0, rot.x, 0);
            }
        }

        [ClientRpc]
        protected virtual void RpcSetRot(Vector2 rot)
        {
            character?.SetRotHed(rot.y);

            if (isLocalPlayer)
            {
#if DEBUG_MOVEMENT
            if (!isDebug || playerDebug == null) return;

            playerDebug.transform.rotation = Quaternion.Euler(0, rot.x, 0);
#endif
                return;
            }

            transform.rotation = Quaternion.Euler(0, rot.x, 0);
        }

        #endregion

        #region Physics

        [Client]
        private void CalculateGravity(ref Vector3 vector)
        {
            if (!GravityCanBeCalculated()) return;

            if (noclip)
            {
                clientPhysicalData.fall = 0;
                clientPhysicalData.isGrounded = false;

                if (!CanMove()) return;

                float add = 0;

                add += (Input.GetKey(KeyCode.Space)) ? 1 : 0;
                add -= (Input.GetKey(KeyCode.C)) ? 1 : 0;

                add *= Time.deltaTime * (noclipSpeed * (Input.GetKey(sprintingKey) ? 2 : 1));

                vector.y += add;

                return;
            }

            clientPhysicalData.isGrounded = characterController.isGrounded;

            if (characterController.isGrounded)
            {
                gravity = initialFallRate;
                clientPhysicalData.fall = 0;

                if (CanJump())
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        if (!NetworkServer.active)
                            CmdJump();
                        else
                            RpcPlayAnimJump();
                        gravity = jumpForce;
                    }

                vector.y = gravity * Time.deltaTime;

                return;
            }

            gravity -= accelerationOfGravity * Time.deltaTime;

            clientPhysicalData.fall = Mathf.Clamp(-gravity, -0.9f, 0.9f);

            vector.y = gravity * Time.deltaTime;
        }

        private bool isJumping = false;

        [Server]
        private void CalculateGravityByServer(ref Vector3 vector)
        {
            if (characterController.isGrounded)
            {
                gravity = initialFallRate;

                if (isJumping)
                {
                    gravity = jumpForce;

                    isJumping = false;

                    RpcPlayAnimJump();
                }

                vector.y = gravity * Time.deltaTime;

                return;
            }

            gravity -= accelerationOfGravity * Time.deltaTime;

            vector.y = gravity * Time.deltaTime;

            return;
        }

        [Command]
        private void CmdJump()
        {
            if (!CanJump(true)) return;

            if (!GravityCanBeCalculated(true)) return;

            isJumping = true;
        }

        [ClientRpc]
        private void RpcPlayAnimJump()
        {
            if (character == null) return;

            character.PlayAnumJump();
            character.SetBoolIsGrounded(false);
        }

        #endregion

        #region Use

        private void CheckUse()
        {
            if (!CanUse()) return;

            if (!Input.GetKeyDown(useKey)) return;

            if (Physics.Raycast(rotMainCamera.position, rotMainCamera.forward, out RaycastHit hit, distanceUse, layerUse))
            {
                if (hit.transform.TryGetComponent<IInteractable>(out IInteractable used))
                {
                    used.Use();
                }
            }
        }

        #endregion

        #region Methods

        public void SetPosition(Vector3 vector, bool notNecessarily = false)
        {
            if (notNecessarily && NetworkServer.active)
            {
                Vector3 startPos = transform.position;

                SetPos(vector);

                RpcSetPos(vector, transform.position - startPos, new PhysicsNetPacket(characterController.isGrounded, gravity));

                return;
            }

            SetPos(vector);

            if (NetworkServer.active)
            {
                RpcSetPos(vector);

                return;
            }
        }

        [Server]
        public void Move(Vector3 motion)
        {
            if (connectionToClient == null)
            {
                Debug.LogError("The object is missing `connectionToClient`. You might have added \"MirrorFPSController\" to something other than the player prefab.");

                return;
            }

            moveMotions.Add(motion);

            if (isServer && !isLocalPlayer)
                TargetAddMoveMotion(connectionToClient, motion);
        }

        /// <summary>
        /// Client can't use this method because hi has protected fields rotX end rotY
        /// </summary>
        /// <param name="newRot"></param>
        [Server]
        public void SetRotation(Vector2 newRot)
        {
            rotServer = newRot;

            RpcSetRot(newRot);
        }

        #endregion

        #region Cen

        /// <summary>
        /// Return false if player movement should be blocked.
        /// </summary>
        protected virtual bool CanMove(bool isServer = false)
        {
            return true;
        }

        /// <summary>
        /// Return false if camera rotation should be blocked.
        /// </summary>
        protected virtual bool CanRotate(bool isServer = false)
        {
            return true;
        }

        /// <summary>
        /// Return false if gravity calculations should be stopped.
        /// </summary>
        protected virtual bool GravityCanBeCalculated(bool isServer = false)
        {
            return true;
        }

        /// <summary>
        /// Return false if the ability to jump needs to be disabled.
        /// </summary>
        protected virtual bool CanJump(bool isServer = false)
        {
            return true;
        }

        /// <summary>
        /// Return false if you need to disable interaction.
        /// </summary>
        /// <param name="isServer">The script uses only client-side validation, so you will need to invoke it from the server side yourself when necessary.</param>
        /// <returns></returns>
        public virtual bool CanUse(bool isServer = false)
        {
            return true;
        }

        #endregion
    }

    public interface IInteractable
    {
        void Use();
    }

    public struct ClientPhysicalData
    {
        /// <summary>
        /// were 0 is On Ground
        /// </summary>
        public float fall { get; internal set; }

        public bool isGrounded { get; internal set; }
    }
}
