namespace TouchPanel.Interop.Input;

/// <summary>
/// Win32 virtual-key codes used by the M1 action catalog (RF-048). Kept beside the SendInput
/// wrapper so all native input constants live under <c>Interop/</c> (RNF-010); handlers map their
/// semantic action values onto these.
/// </summary>
internal static class VirtualKeys
{
    /// <summary><c>VK_MEDIA_NEXT_TRACK</c> (0xB0).</summary>
    internal const ushort MediaNextTrack = 0xB0;

    /// <summary><c>VK_MEDIA_PREV_TRACK</c> (0xB1).</summary>
    internal const ushort MediaPreviousTrack = 0xB1;

    /// <summary><c>VK_MEDIA_STOP</c> (0xB2).</summary>
    internal const ushort MediaStop = 0xB2;

    /// <summary><c>VK_MEDIA_PLAY_PAUSE</c> (0xB3).</summary>
    internal const ushort MediaPlayPause = 0xB3;

    /// <summary><c>VK_VOLUME_MUTE</c> (0xAD).</summary>
    internal const ushort VolumeMute = 0xAD;

    /// <summary><c>VK_VOLUME_DOWN</c> (0xAE).</summary>
    internal const ushort VolumeDown = 0xAE;

    /// <summary><c>VK_VOLUME_UP</c> (0xAF).</summary>
    internal const ushort VolumeUp = 0xAF;
}
