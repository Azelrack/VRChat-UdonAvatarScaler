
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ScaleDebugScript : UdonSharpBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI _debugDisplay;
    [SerializeField] private AvatarScalerScript _avatarScalerScript;

    public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters)
    {
        OnScaleApplied();
    }

    public void OnScaleApplied()
    {
        if (!_avatarScalerScript)
        {
            _debugDisplay.text = $"Missing {nameof(AvatarScalerScript)} reference.";
            return;
        }

        var localPlayer = Networking.LocalPlayer;
        var size = localPlayer.GetAvatarEyeHeightAsMeters();
        var ratio = _avatarScalerScript.GetSizeRatio(size);
        var factor = _avatarScalerScript.GetScaleFactor(ratio);
        var walk = localPlayer.GetWalkSpeed();
        var run = localPlayer.GetRunSpeed();
        var strafe = localPlayer.GetStrafeSpeed();
        var jump = localPlayer.GetJumpImpulse();
        var gravity = localPlayer.GetGravityStrength();
        var voiceDistanceFar = localPlayer.GetVoiceDistanceFar();
        var voiceDistanceNear = localPlayer.GetVoiceDistanceNear();
        var voiceVolumetricRadius = localPlayer.GetVoiceVolumetricRadius();
        // Apparently we can set audio far but not get it ??????

        var resume = $"[{localPlayer.playerId}] {localPlayer.displayName}\n" +
            $"--- Current Size ---\n" +
            $"{size}m\n" +
            $"Ratio {ratio}\n" +
            $"Factor {factor}\n" +
            $"--- Speeds ---\n" +
            $"Walk: {walk}\n" +
            $"Run: {run}\n" +
            $"Strafe: {strafe}\n" +
            $"Jump: {jump}\n" +
            $"Gravity: {gravity}\n" +
            $"--- Voice ---\n" +
            $"Voice Distance Far: {voiceDistanceFar}\n" +
            $"Voice Distance Near: {voiceDistanceNear}\n" +
            $"Voice Volumetric Radius: {voiceVolumetricRadius}";

        _debugDisplay.text = resume;
    }
}
