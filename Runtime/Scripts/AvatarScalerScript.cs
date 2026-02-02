
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDK3.Rendering;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace Azelrack.AvatarScaler
{
    public class EnumToIntAttribute : PropertyAttribute { }

    public enum ScalingMode
    {
        [Tooltip("Simply applies a ratio compared to base size.")]
        Linear = 0,
        [Tooltip("[Recommended] Applies a power to scaling ratio, to define how the scaling affects growing and shrinking differently.")]
        NonLinear = 1,
        [Tooltip("Lets you define your own curve.")]
        CustomCurve = 2,
    }

    /// <summary>
    /// Controls avatar size, speed and voice range, scaling them appropriately depending on the Avatar Eye Height in use.
    /// </summary>
    /// <remarks>
    /// To use this script, remove from your world object the AvatarScalingSettings and VRCWorldSettings Udon Graph objects, as this one aims to replace them.
    /// See the <see cref="_extraLogging"/> in local game test mode with "Right Shift + ~ + 3" or "Right Maj + ² + 3 on Azerty).
    /// </remarks>
    public class AvatarScalerScript : UdonSharpBehaviour
    {
        #region Constants

        /// <summary>
        /// Size we will consider as "base" for all scaling features.
        /// </summary>
        public const float REFERENCE_SIZE = 1.8f;

        /// <summary>
        /// Key used to persist avatar size in <see cref="PlayerData"/>.
        /// </summary>
        private const string PLAYER_SIZE_KEY = "PlayerSize";

        /// <summary>
        /// Key used to persist user's toggle of size gesture.
        /// </summary>
        private const string PLAYER_TOGGLE_SIZE_GESTURE_KEY = "SizeGesture";

        #endregion

        #region Network Size Sync

        /// <summary>
        /// Time to wait before the next network sync event can be sent.
        /// </summary>
        private readonly double NETWORK_SYNC_INTERVAL_SEC = 0.5;

        /// <summary>
        /// Controls the update of <see cref="_networkSyncCountDown"/> countdown."/>
        /// </summary>
        private bool _waitForSync = false;

        /// <summary>
        /// Timer used to reduce the amount of network sync calls,
        /// as rescaling can quickly overflow events limits and create strange delays.
        /// </summary>
        private double _networkSyncCountDown = 0f;

        /// <summary>
        /// Last size coefficient applied to local player.
        /// </summary>
        private float _sizeCoef;

        #endregion

        #region Input fields

        // Controller scaling
        private bool _inputGrabLeft = false;
        private bool _inputUseLeft = false;
        private bool _inputGrabRight = false;
        private bool _inputUseRight = false;
        private float _initialHandDistance = 0;

        #endregion

        #region Customizable fields

        #region Default movement Speeds

        [Header("--- Default movement speeds ---")] // default is VRChat's defaults.
        [SerializeField] private float _baseStrafeSpeed = 2f;
        [SerializeField] private float _baseJumpImpulse = 2f;
        [SerializeField] private float _baseWalkSpeed = 3f;
        [SerializeField] private float _baseRunSpeed = 4f;
        [SerializeField] private float _baseGravityStrength = 1f;
        [SerializeField]
        [Tooltip("Used to multiply all base speeds in one place. 1.0 means no boost, 1.5 means 50% boost."), Min(0.1f)]
        private float _speedBoost = 1.0f;

        #endregion

        #region Voice Control

        [Header("--- Voice control ---")]
        [SerializeField]
        [Tooltip("Distance in meters player voice will start to fall off. VRChat default is 0, we need just a little bit to improve hearing players on big sizes.")]
        private float _baseVoiceDistanceNear = 150f;

        [SerializeField]
        [Tooltip("Distance in meters player voice can be heard. VRChat default is 25, I recommend 150 for size worlds, " +
            "if you want to be able to hear small people on the ground.")]
        private float _baseVoiceDistanceFar = 350f;

        [SerializeField]
        [Tooltip("Area width of the player voice emitter. 0 = just a point on space, 1 = 1m width. " +
            "Useful to make very big player's voices origin cover more space.")]
        private float _baseVoiceVolumetricRadius = 0.1f;

        [SerializeField]
        [Tooltip("Maximum audio multiplier when growing. Prevents players from having crazy voice range when very big. " +
            "Default will limit increase to 3 times the far/near values for any scaling mode. Input 1 to effectively remove audio scaling for big players.")]
        private float _maxAudioMultiplier = 3f;

        [SerializeField]
        [Tooltip("When shrinking, player voice and avatar audio will shrink too, forcing bigger people to get their head close to hear them. " +
            "Can feel somewhat realistic, but really do not recommend for hangout worlds.")]
        private bool _shrinkingAffectsAudio = false;

        #endregion

        #region Scaling mode

        [Header("--- Scaling mode ---")]
        [SerializeField, EnumToInt, Tooltip("Choose how much size affects character features")]
        private int _scalingMode = 1;

        /// <summary>
        /// Used by <see cref="ScalingMode.NonLinear"/> to reduce the impact of shrinking.
        /// Default is 0.5f, reducing the slowdown when shrinking.
        /// </summary>
        [SerializeField]
        [Tooltip("Controls how size impacts shrinking, reducing the number below 1.0 means losing less speed and other aspects.")]
        private float _nonLinearDownscaleExponent = 0.5f;

        /// <summary>
        /// Used by <see cref="ScalingMode.NonLinear"/> to reduce the impact of growing.
        /// Default is 0.8f, reduces speeds a bit when growing, avoiding the player to be too fast (personal preference).
        /// </summary>
        [SerializeField]
        [Tooltip("Controls how size impacts growth, reducing the number below 1.0 means gaining less speed and other aspects.")]
        private float _nonLinearUpscaleExponent = 0.8f;

        /// <summary>
        /// Uses unity <see cref="AnimationCurve"/> to allow world creator to define a custom scaling curve using GUI.
        /// </summary>
        [SerializeField]
        AnimationCurve _customScalingCurve =
            new AnimationCurve(new Keyframe(0, 0.25f), new Keyframe(REFERENCE_SIZE, 1f), new Keyframe(100f, 80f));

        #endregion

        #region Scaling keys and speed

        [Header("--- Scaling keys and speed ---")]
        [SerializeField]
        [Tooltip("Desktop players can keep this key pressed to shrink.")]
        private KeyCode _keyboardShrinkKey = KeyCode.K;

        [SerializeField]
        [Tooltip("Desktop players can keep this key pressed to grow.")]
        private KeyCode _keyboardGrowKey = KeyCode.L;

        [SerializeField]
        [Tooltip("Defines the speed at which players will get resized, when using resize keys/controls. Value will be added/removed from size every frame.")]
        private float _playerScalingSpeedKeyboard = 0.025f;

        [SerializeField]
        [Tooltip("Defines the speed at which players will get resized, when using resize keys/controls. Value will be added/removed from size every frame.")]
        private float _playerScalingSpeedVR = 0.025f;

        #endregion

        #region Size limits & rules

        [Header("--- Size limits & rules ---")]
        [SerializeField]
        [Tooltip("Prevents player to get out of 'Min Height' and 'Max Height' bounds.")]
        private bool _enforceLimits = true;

        [SerializeField]
        [Tooltip("Allows players to use VRChat's size menu. Please note this menu is limited by VRChat.")]
        private bool _allowSizeMenu = true;

        [SerializeField]
        [Tooltip("Enables the size gesture and keys, allowing players to go from MinHeight to MaxHeight without being limited by VRChat's size menu.")]
        private bool _allowSizeGestureAndKeys = true;

        [SerializeField]
        [Tooltip("Maximum allowed height. Even if VRChat scale menu only goes to 5, SDK allows to go up to 100.")]
        [Range(0.1f, 100f)]
        private float _maxHeight = 100f;

        [SerializeField]
        [Tooltip("Minimum allowed height. VRChat height menu can go as down as 0.1. Camera near plane will be adjusted automatically.")]
        [Range(0.1f, 100f)]
        private float _minHeight = 0.1f;

        #endregion

        #region Extra

        [Header("--- Extra ---")]

        [SerializeField]
        [Tooltip("Use VRChat persistance to save user size and toggles, and restore them when they log in.")]
        private bool _usePersistence = false;

        [SerializeField]
        [Tooltip("Log every action this script takes on size.")]
        private bool _extraLogging = false;

        [SerializeField]
        [Tooltip("Optionnal toggle for Size Gesture And Keys. Script will ensure its IsOn status will match the default state & player persistance.")]
        private Toggle _sizeGestureToggle;

        #endregion

        #endregion

        #region Private properties

        private bool IsScalingGestureActive =>
            _inputUseLeft && _inputUseRight && _inputGrabLeft && _inputGrabRight;

        #endregion

        #region Unity Messages

        private void Start()
        {
            // Failsafe until I do a custom editor.
            if (_maxHeight < _minHeight)
            {
                var temp = _maxHeight;
                _maxHeight = _minHeight;
                _minHeight = temp;
                Debug.LogWarning("Your max height was inferior to min height, values have been swapped.");
            }
        }

        private void Update()
        {
            if (!_allowSizeGestureAndKeys)
                return;

            // Keyboard size controls (can be kept pressed).
            if (Input.GetKey(_keyboardShrinkKey))
                AddToSize(Networking.LocalPlayer, -_playerScalingSpeedKeyboard, true);
            else if (Input.GetKey(_keyboardGrowKey))
                AddToSize(Networking.LocalPlayer, _playerScalingSpeedKeyboard, true);
            else // Controller size controls
                ProcessVRScalingInput(Networking.LocalPlayer);

            UpdateNetworkSize();
        }

        /// <summary>
        /// When player uses the rescaling gesture, this processes the scaling direction and speed.
        /// </summary>
        private void ProcessVRScalingInput(VRCPlayerApi playerApi)
        {
            if (IsScalingGestureActive)
            {
                var sizeRatio = GetSizeRatio(playerApi);
                var leftHandPos = playerApi.GetBonePosition(HumanBodyBones.LeftHand);
                var rightHandPos = playerApi.GetBonePosition(HumanBodyBones.RightHand);
                var currentHandDistance = Vector3.Distance(leftHandPos, rightHandPos) / sizeRatio;

                if (_initialHandDistance == 0)
                    _initialHandDistance = currentHandDistance;
                else
                {
                    var increaseSpeed = _playerScalingSpeedVR * sizeRatio;
                    AddToSize(playerApi, _initialHandDistance > currentHandDistance ? -increaseSpeed : increaseSpeed);
                }
            }
            else if (_initialHandDistance != 0)
                _initialHandDistance = 0;
        }

        #endregion

        #region UdonSharpBehaviour events

        /// <summary>
        /// Restores previous player size, if <see cref="_usePersistence"/> is enabled.
        /// </summary>
        public override void OnPlayerRestored(VRCPlayerApi localPlayer)
        {
            if (localPlayer.isLocal && _usePersistence)
            {
                float previousSize = PlayerData.GetFloat(localPlayer, PLAYER_SIZE_KEY);
                if (previousSize > 0)
                    localPlayer.SetAvatarEyeHeightByMeters(previousSize);
                _allowSizeGestureAndKeys = PlayerData.GetBool(localPlayer, PLAYER_TOGGLE_SIZE_GESTURE_KEY);
            }
            else // Force re-application of current size to ensure proper scaling on join.
                localPlayer.SetAvatarEyeHeightByMeters(localPlayer.GetAvatarEyeHeightAsMeters());

            // Update gesture display with current status.
            if (_sizeGestureToggle)
            {
                _sizeGestureToggle.isOn = _allowSizeGestureAndKeys;
                // Changing toggle status will change _allowSizeGesture, so we need to set it back.
                // It feels like a lazy workaround, might do better later if I add more options.
                _allowSizeGestureAndKeys = _sizeGestureToggle.isOn;
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi localPlayer)
        {
            // Height management methods only works for local player, basically each client manages its own size.
            if (!localPlayer.isLocal)
                return;

            if (_enforceLimits)
            {
                localPlayer.SetAvatarEyeHeightMaximumByMeters(_maxHeight);
                localPlayer.SetAvatarEyeHeightMinimumByMeters(_minHeight);
            }

            localPlayer.SetManualAvatarScalingAllowed(_allowSizeMenu);

            TryWriteExtraLog(localPlayer,
                $"VRChat size menu is " + (_allowSizeMenu ? "allowed" : "unallowed") +
                $"Size gesture use is " + (_allowSizeGestureAndKeys ? "allowed" : "unallowed") +
                $" Max height: {_maxHeight}m, Min height: {_minHeight}m");
        }

        public override void OnAvatarEyeHeightChanged(VRCPlayerApi localPlayer, float prevEyeHeightAsMeters)
        {
            if (!localPlayer.isLocal)
                return;

            float currentHeight = localPlayer.GetAvatarEyeHeightAsMeters();

            if (_enforceLimits)
            {
                float clampedHeight = Mathf.Clamp(currentHeight, _minHeight, _maxHeight);
                bool outOfBounds = Mathf.Abs(clampedHeight - currentHeight) > float.Epsilon;

                // If we detect a mismatch in height bigger than float value margin error, enforce height.
                if (outOfBounds)
                {
                    TryWriteExtraLog(localPlayer, $"went from {prevEyeHeightAsMeters}m to {currentHeight}m. Enforcing height limit to {clampedHeight}m");
                    localPlayer.SetAvatarEyeHeightByMeters(clampedHeight);
                    currentHeight = clampedHeight;
                }
            }

            // Adjust camera near clip plane to avoid clipping issues when very small.
            if (currentHeight < 0.4f)
                VRCCameraSettings.ScreenCamera.NearClipPlane = 0.001f;
            else
                VRCCameraSettings.ScreenCamera.NearClipPlane = 0.05f;

            ApplyScaling(localPlayer, currentHeight);

            if (_usePersistence)
                PlayerData.SetFloat(PLAYER_SIZE_KEY, currentHeight);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
                _inputUseLeft = args.boolValue;
            if (args.handType == HandType.RIGHT)
                _inputUseRight = args.boolValue;
        }

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
                _inputGrabLeft = args.boolValue;
            if (args.handType == HandType.RIGHT)
                _inputGrabRight = args.boolValue;
        }

        #endregion

        #region Scaling methods
        //--- Some of these methods are public to allow potential use in your scripts.

        /// <summary>
        /// Get player size ratio compared to <see cref="REFERENCE_SIZE"/>.
        /// </summary>
        public float GetSizeRatio(VRCPlayerApi player) => GetSizeRatio(player.GetAvatarEyeHeightAsMeters());

        /// <summary>
        /// currentSize divided by <see cref="REFERENCE_SIZE"/>.
        /// </summary>
        public float GetSizeRatio(float currentSize) => currentSize / REFERENCE_SIZE;

        /// <summary>
        /// Will create the right scaling ratio depending on the <see cref="ScalingMode"/> in use.
        /// </summary>
        /// <param name="ratio">Current size divided by <see cref="REFERENCE_SIZE"/></param>.
        public float GetScaleFactor(float ratio)
        {
            // Using assemblies with U# forces to use int instead of enum in serialized fields.
            switch (_scalingMode)
            {
                // NonLinear
                case 1:
                    float power = ratio < 1f ? _nonLinearDownscaleExponent : _nonLinearUpscaleExponent;
                    return Mathf.Pow(ratio, power);
                // CustomCurve
                case 2:
                    return _customScalingCurve.Evaluate(ratio);
                // Linear
                case 0:
                default:
                    return ratio;
            }
        }

        /// <summary>
        /// Scale avatar features depending on <see cref="_scalingMode"/> and <see cref="VRCPlayerApi.GetAvatarEyeHeightAsMeters"/>.
        /// </summary>
        private void ApplyScaling(VRCPlayerApi localPlayer, float currentSize)
        {
            float ratio = GetSizeRatio(currentSize);
            _sizeCoef = GetScaleFactor(ratio);
            float boostedCoef = _sizeCoef * _speedBoost;

            if (_extraLogging)
                TryWriteExtraLog(localPlayer, $"Size coef is: {_sizeCoef}");

            // Movement scaling
            localPlayer.SetStrafeSpeed(_baseStrafeSpeed * boostedCoef);
            localPlayer.SetWalkSpeed(_baseWalkSpeed * boostedCoef);
            localPlayer.SetRunSpeed(_baseRunSpeed * boostedCoef);
            localPlayer.SetJumpImpulse(_baseJumpImpulse * boostedCoef);
            localPlayer.SetGravityStrength(_baseGravityStrength * boostedCoef);

            ApplyAudioScaleChange(localPlayer.playerId, _sizeCoef);
            // Audio scaling has to be synced to each client and is delayed to avoid network event flood.
            StartNetworkScaleEventCountdown();
        }

        /// <summary>
        /// Sets up the countdown to send a network event to sync scaling.
        /// </summary>
        private void StartNetworkScaleEventCountdown()
        {
            _waitForSync = true;

            // Ensures at least one sync every NETWORK_SYNC_INTERVAL_SEC seconds.
            if (_networkSyncCountDown <= double.Epsilon)
                _networkSyncCountDown = NETWORK_SYNC_INTERVAL_SEC;
        }

        /// <summary>
        /// Handles the countdown update and sending of network size sync events.
        /// </summary>
        private void UpdateNetworkSize()
        {
            // Sync size network events
            if (_networkSyncCountDown > double.Epsilon)
                _networkSyncCountDown -= Time.deltaTime;
            else if (_waitForSync)
            {
                // Sync size to other clients every second when resizing.
                SendCustomNetworkEvent(
                    NetworkEventTarget.Others,
                    nameof(ApplyAudioScaleChange),
                    Networking.LocalPlayer.playerId,
                    _sizeCoef
                );
                _waitForSync = false;
            }
        }

        /// <summary>
        /// Applies a scale ratio to any existing <see cref="VRCPlayerApi.playerId"/>.
        /// </summary>
        [NetworkCallable]
        public void ApplyAudioScaleChange(int playerID, float ratio)
        {
            var playerToEdit = VRCPlayerApi.GetPlayerById(playerID);

            if (playerToEdit == null)
                return;

            // Limits audio scaling to avoid crazy ranges on big players.
            ratio = Mathf.Min(ratio, _maxAudioMultiplier);

            // Voice and audio scaling needs to be linear, otherwise it could affect too much player's ability to be heard.
            // Near
            playerToEdit.SetVoiceDistanceNear(ApplyAudioScaling(_baseVoiceDistanceNear, ratio));

            // Far
            float audioFar = ApplyAudioScaling(_baseVoiceDistanceFar, ratio);
            playerToEdit.SetVoiceDistanceFar(audioFar);
            playerToEdit.SetAvatarAudioFarRadius(audioFar);

            // Volumetric Radius
            float audioVolumetricRadius = ApplyAudioScaling(_baseVoiceVolumetricRadius, ratio);
            playerToEdit.SetVoiceVolumetricRadius(audioVolumetricRadius);
            playerToEdit.SetAvatarAudioVolumetricRadius(audioVolumetricRadius);

            if (_extraLogging)
            {
                Debug.Log(
                    $"Applied audio scaling to player [{playerToEdit.playerId}] {playerToEdit.displayName}: " +
                    $"Voice Near: {playerToEdit.GetVoiceDistanceNear()}m, " +
                    $"Voice Far: {playerToEdit.GetVoiceDistanceFar()}m, " +
                    $"Voice Volumetric Radius: {playerToEdit.GetVoiceVolumetricRadius()}m"
                );
            }
        }

        /// <summary>
        /// Adapts audio ranges to <paramref name="ratio"/>, but does not reduces shrunk people voice radius,
        /// as it would make them almost impossible to hear.
        /// </summary>
        private float ApplyAudioScaling(float baseDistance, float ratio)
        {
            if (_shrinkingAffectsAudio || ratio > 1)
                return baseDistance * ratio;
            return baseDistance;
        }

        /// <summary>
        /// Simply add an increment to player's current size.
        /// </summary>
        /// <param name="localPlayer">Local player.</param>
        /// <param name="increment">Value to add to current size.</param>
        /// <param name="ratioIncrement">Apply the current size ratio to increment or not.</param>
        public void AddToSize(VRCPlayerApi localPlayer, float increment, bool ratioIncrement = false)
        {
            var currentSize = localPlayer.GetAvatarEyeHeightAsMeters();
            if (ratioIncrement)
                increment *= GetSizeRatio(currentSize);
            var newHeight = currentSize + increment;
            if (_enforceLimits)
                newHeight = Mathf.Clamp(newHeight, _minHeight, _maxHeight);
            localPlayer.SetAvatarEyeHeightByMeters(newHeight);
        }

        #endregion

        #region Toggles

        /// <summary>
        /// Toggle the use of gesture and keys.
        /// </summary>
        /// <returns>Current state of <see cref="_allowSizeGestureAndKeys"/></returns>
        public bool ToggleLocalPlayerScalingGesture()
        {
            _allowSizeGestureAndKeys = !_allowSizeGestureAndKeys;
            Debug.Log(_allowSizeGestureAndKeys);
            if (_usePersistence)
                PlayerData.SetBool(PLAYER_TOGGLE_SIZE_GESTURE_KEY, _allowSizeGestureAndKeys);
            return _allowSizeGestureAndKeys;
        }

        #endregion

        #region Logging

        private void TryWriteExtraLog(VRCPlayerApi localPlayer, string message)
        {
            if (_extraLogging)
                Debug.Log($"[{nameof(AvatarScalerScript)}] Player {localPlayer.playerId} '{localPlayer.displayName}': {message}");
        }

        #endregion
    }
}