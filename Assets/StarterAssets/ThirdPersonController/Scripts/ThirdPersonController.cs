using UnityEngine;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using System.Diagnostics;

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        public static ThirdPersonController instance;
        public List<GameObject> renderers = new List<GameObject>();
        public List<Image> UIboxes = new List<Image>();
        List<GameObject> jewelsInStorage = new List<GameObject>();

        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        LayerMask[] GroundLayers = new LayerMask[3];

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDDeath;

#if ENABLE_INPUT_SYSTEM 
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM 
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }

        bool dead = false;
        Trapdoor[] allTraps;
        MovingSpike[] allMovers;

        public ParticleSystem blueParticles;
        public ParticleSystem yellowParticles;

        public AudioClip jumpSound;
        public AudioClip jewel;
        public AudioClip changeColor;
        public AudioClip bossSwitch;
        public AudioClip deathSound;

        private void Awake()
        {
            instance = this;
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM 
            _playerInput = GetComponent<PlayerInput>();
#else
#endif
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }

            allTraps = FindObjectsOfType(typeof(Trapdoor)) as Trapdoor[];
            allMovers = FindObjectsOfType(typeof(MovingSpike)) as MovingSpike[];
            GroundLayers[0] = LayerMask.GetMask("Default");
            GroundLayers[1] = LayerMask.GetMask("Blue");
            GroundLayers[2] = LayerMask.GetMask("Yellow");
        }

        public void SetToColor(int n)
        {
            if (n == 1)
            {
                blueParticles.Clear();
                blueParticles.Play();
            }
            else if (n == 2)
            {
                yellowParticles.Clear();
                yellowParticles.Play();
            }

            for (int i = 0; i<renderers.Count; i++)
            {
                if (i == n)
                    renderers[i].SetActive(true);
                else
                    renderers[i].SetActive(false);
            }

            for (int i = 0; i<UIboxes.Count; i++)
            {
                if (n == 0)
                    UIboxes[i].color = new Color(0, 0, 0, 0.5f);
                else if (n == 1)
                    UIboxes[i].color = new Color(0, 0.4f, 1, 0.3f);
                else
                    UIboxes[i].color = new Color(1, 0.7f, 0, 0.3f);      
            }
        }

        private void Start()
        {
            if (Challenges.instance == null)
                SceneManager.LoadScene("Main Menu");

            SetToColor(0);
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            AssignAnimationIDs();

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            if (Challenges.instance.timed)
                StartCoroutine(Died(false, true));

            for (int i = 0; i < CameraManager.instance.timePerLevel.Length; i++)
                CameraManager.instance.timePerLevel[i] = new Stopwatch();

            if (Challenges.instance.checkpointLoaded > 0)
            {
                GameObject x = CheckpointManager.instance.allCheckpoints[Challenges.instance.checkpointLoaded - 1];
                CheckpointManager.instance.transform.position =
                new Vector3(x.transform.position.x, x.transform.position.y + 3, x.transform.position.z);
            }
            StartCoroutine(Died(false, true));
        }

        private void Update()
        {
            _hasAnimator = TryGetComponent(out _animator);

            if (dead)
            {
                //transform.position = CheckpointManager.instance.transform.position;
            }
            else
            {
                JumpAndGravity();
                GroundedCheck();
                Move();
                Restart();

                if (Challenges.instance.timed && Challenges.instance.stopwatch.Elapsed.Seconds >= 15)
                {
                    Challenges.instance.levelDeath[CameraManager.instance.currentZone]++;
                    StartCoroutine(Died(true, false));
                }
            }

        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        public IEnumerator Died(bool count, bool deathFloor)
        {
            dead = true;
            _playerInput.enabled = false;
            //AudioManager.instance.StopSounds();

            if (count)
                AudioManager.instance.PlaySound(deathSound, 0.4f);

            if (!deathFloor)
            {
                _animator.SetBool(_animIDDeath, true);
                yield return new WaitForSeconds(0.7f);
            }

            this.gameObject.layer = 7;
            _animator.SetBool(_animIDDeath, false);

            Challenges.instance.stopwatch.Restart();
            Challenges.instance.jumpsLeft = Challenges.instance.oneJump ? 1 : 3;

            if (count)
            {
                UIManager.instance.deaths++;
                if (Challenges.instance.oneLife)
                {
                    for (int i = 0; i < CameraManager.instance.timePerLevel.Length; i++)
                        CameraManager.instance.timePerLevel[i] = new Stopwatch();

                    CheckpointManager.instance.NewCheckpoint(null);
                    UIManager.instance.stopwatch.Restart();

                    for (int i = 0; i < UIManager.instance.allCollectibles.Length; i++)
                        UIManager.instance.allCollectibles[i].SetActive(true);
                }
            }

            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < jewelsInStorage.Count; j++)
                {
                    UnityEngine.Debug.Log($"disable {jewelsInStorage[j].name}");
                    jewelsInStorage[j].SetActive(true);
                    UIManager.instance.DisableJewel(jewelsInStorage[j].name);
                }
                jewelsInStorage.Clear();

                SetToColor(0);
                FinalBoss.instance.Restart();
                this.transform.position = CheckpointManager.instance.transform.position;
                for (int j = 0; j < allTraps.Length; j++)
                    allTraps[j].Reset();
                for (int j = 0; j < allMovers.Length; j++)
                    allMovers[j].Reset();
                yield return new WaitForSeconds(0.01f);
            }
            dead = false;
            _playerInput.enabled = true;
        }

        public void OnTriggerEnter(Collider other)
        {
            if (!dead)
            {
                if (other.CompareTag("Rock"))
                {
                    if (other.gameObject.name == "Death Floor")
                    {
                        StartCoroutine(Died(true, true));
                    }
                    else
                    {
                        StartCoroutine(Died(true, false));
                    }
                    Challenges.instance.levelDeath[CameraManager.instance.currentZone]++;
                }

                else if (other.CompareTag("Spike"))
                {
                    Challenges.instance.levelDeath[CameraManager.instance.currentZone]++;
                    StartCoroutine(Died(true, false));
                }

                else if (other.CompareTag("Knight"))
                {
                    Challenges.instance.levelDeath[CameraManager.instance.currentZone]++;
                    StartCoroutine(Died(true, false));
                }

                else if (other.CompareTag("Checkpoint"))
                {
                    SetToColor(0);
                    this.gameObject.layer = 7;
                    CheckpointManager.instance.NewCheckpoint(other.gameObject);

                    if (!Challenges.instance.oneLife)
                        jewelsInStorage.Clear();

                    if (other.transform.parent.name == "END")
                        _playerInput.enabled = false;
                }

                else if (other.CompareTag("BossSwitch"))
                {
                    other.gameObject.SetActive(false);
                    AudioManager.instance.PlaySound(bossSwitch, 0.4f);
                }

                else if (other.CompareTag("Jewel"))
                {
                    other.gameObject.SetActive(false);
                    jewelsInStorage.Add(other.gameObject);
                    UIManager.instance.EnableJewel(other.name);
                    AudioManager.instance.PlaySound(jewel, 0.4f);
                }

                else if (other.CompareTag("Color Capsule"))
                {
                    AudioManager.instance.PlaySound(changeColor, 0.4f);
                    this.gameObject.layer = other.gameObject.layer;
                    switch (gameObject.layer)
                    {
                        case 0:
                            this.gameObject.layer = 7;
                            SetToColor(0);
                            break;
                        case 3: //blue
                            this.gameObject.layer = 8;
                            SetToColor(1);
                            break;
                        case 6: //yellow
                            this.gameObject.layer = 9;
                            SetToColor(2);
                            break;
                    }
                }
            }
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDDeath = Animator.StringToHash("Die");
        }

        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);

            Grounded = false;
            for (int i = 0; i<GroundLayers.Length; i++)
            {
                if (Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers[i],
                QueryTriggerInteraction.Ignore))
                {
                    Grounded = true;
                    break;
                }
            }

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            // if there is an input and camera position is not fixed
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                //Don't multiply mouse input by Time.deltaTime;
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // clamp our rotations so our values are limited 360 degrees
            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // Cinemachine will follow this target
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Restart()
        {
            if (_input.restart)
            {
                _input.restart = false;
                StartCoroutine(Died(true, false));
            }
        }

        private void Move()
        {
            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            if (_input.move == Vector2.zero)
                targetSpeed = 0.0f;

            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f)
                _animationBlend = 0f;

            // normalise input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                // rotate to face input direction relative to camera position
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // move the player
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // update animator if using character
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    Grounded = false;
                    _input.jump = false;

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        AudioManager.instance.PlaySound(jumpSound, 0.1f);
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f)
                lfAngle += 360f;
            if (lfAngle > 360f)
                lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = UnityEngine.Random.Range(0, FootstepAudioClips.Length);
                    AudioManager.instance.PlaySound(FootstepAudioClips[index], 0.1f);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioManager.instance.PlaySound(LandingAudioClip, 0.1f);
            }
        }
    }
}