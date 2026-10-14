using System.Runtime.InteropServices;
using TouchPanel.Actions;

namespace TouchPanel.Interop.Audio;

/// <summary>
/// Production <see cref="IAudioController"/> (RF-048, RF-053): applies an absolute master volume
/// level through the Core Audio <c>IAudioEndpointVolume</c> COM interface. No NuGet dependency; all
/// COM/native declarations live under <c>Interop/</c> (RNF-010).
/// </summary>
/// <remarks>
/// <para><b>Why not SendInput (documented DD-8 deviation, T-09).</b> DD-8 routed all volume
/// actions through <c>VK_VOLUME_UP/DOWN</c>, but a key press only advances by a device-defined
/// increment and the current level is unknown, so a configured absolute level could not be reached
/// accurately. Absolute set therefore uses the endpoint API; step and mute remain on the SendInput
/// path in <c>VolumeHandler</c>/<c>MuteHandler</c>.</para>
/// <para><b>Failure containment.</b> Every failure (no default output device, Core Audio service
/// unavailable, HRESULT error) is returned as a failed result with a reason and never thrown, so
/// the action layer can surface it (RF-050).</para>
/// <para><b>Lifetime.</b> The COM objects are created per call and released immediately; the
/// controller is stateless and safe to call from any thread.</para>
/// </remarks>
internal sealed class CoreAudioController : IAudioController
{
    /// <inheritdoc />
    public bool TrySetVolumeLevel(int level, out string? error)
    {
        var scalar = LevelToScalar(level);

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpointVolume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            var result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            if (result < 0 || device is null)
            {
                error = $"No default audio output device is available (HRESULT 0x{result:X8}).";
                return false;
            }

            var interfaceId = AudioEndpointVolumeIid;
            result = device.Activate(ref interfaceId, ClsCtxAll, nint.Zero, out var instance);
            if (result < 0 || instance is not IAudioEndpointVolume volume)
            {
                error = $"The audio endpoint volume interface is unavailable (HRESULT 0x{result:X8}).";
                return false;
            }

            endpointVolume = volume;

            var eventContext = Guid.Empty;
            result = endpointVolume.SetMasterVolumeLevelScalar(scalar, ref eventContext);
            if (result < 0)
            {
                error = $"Setting the master volume failed (HRESULT 0x{result:X8}).";
                return false;
            }

            error = null;
            return true;
        }
        catch (COMException exception)
        {
            error = $"Core Audio is unavailable: {exception.Message}";
            return false;
        }
        catch (InvalidCastException exception)
        {
            error = $"Core Audio returned an unexpected interface: {exception.Message}";
            return false;
        }
        finally
        {
            ReleaseComObject(endpointVolume);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    /// <summary>Maps a configured absolute level (0-100) to the Core Audio scalar (0.0-1.0).</summary>
    /// <param name="level">The level in percent; out-of-range values are clamped.</param>
    /// <returns>The scalar volume accepted by <c>SetMasterVolumeLevelScalar</c>.</returns>
    internal static float LevelToScalar(int level) => Math.Clamp(level, 0, 100) / 100f;

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(comObject);
        }
        catch (Exception)
        {
            // Release is best-effort; the GC would still collect the RCW.
        }
    }

    private static readonly Guid AudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private const int ClsCtxAll = 0x17;
}

/// <summary>Core Audio data-flow direction; only rendering (output) is used.</summary>
internal enum EDataFlow
{
    /// <summary>Audio rendering (output) devices.</summary>
    Render = 0,
}

/// <summary>Core Audio device role; the multimedia role is the default output used by volume keys.</summary>
internal enum ERole
{
    /// <summary>The default multimedia output device.</summary>
    Multimedia = 1,
}

/// <summary>The <c>MMDeviceEnumerator</c> coclass (<c>BCDE0395-E52F-467C-8E3D-C4579291692E</c>).</summary>
[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

/// <summary>
/// <c>IMMDeviceEnumerator</c> (<c>A95664D2-9614-4F35-A746-DE8DB63617E6</c>). Declared in native
/// vtable order; methods not used by this controller are kept for completeness.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    /// <summary><c>EnumAudioEndpoints</c> (not used).</summary>
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out nint devices);

    /// <summary><c>GetDefaultAudioEndpoint</c>.</summary>
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? endpoint);

    /// <summary><c>GetDevice</c> (not used).</summary>
    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out nint device);

    /// <summary><c>RegisterEndpointNotificationCallback</c> (not used).</summary>
    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint client);

    /// <summary><c>UnregisterEndpointNotificationCallback</c> (not used).</summary>
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint client);
}

/// <summary><c>IMMDevice</c> (<c>D666063F-1587-4E43-81F1-B948E807363F</c>).</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    /// <summary><c>Activate</c>: creates the requested interface on the device.</summary>
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        int classContext,
        nint activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    /// <summary><c>OpenPropertyStore</c> (not used).</summary>
    [PreserveSig]
    int OpenPropertyStore(int storageAccess, out nint properties);

    /// <summary><c>GetId</c> (not used).</summary>
    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);

    /// <summary><c>GetState</c> (not used).</summary>
    [PreserveSig]
    int GetState(out int state);
}

/// <summary>
/// <c>IAudioEndpointVolume</c> (<c>5CDF2C82-841E-4546-9722-0CF74078229A</c>). Only
/// <c>SetMasterVolumeLevelScalar</c> is called; the remaining methods preserve the native vtable
/// order and document the contract.
/// </summary>
[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    /// <summary><c>RegisterControlChangeNotify</c> (not used).</summary>
    [PreserveSig]
    int RegisterControlChangeNotify(nint notify);

    /// <summary><c>UnregisterControlChangeNotify</c> (not used).</summary>
    [PreserveSig]
    int UnregisterControlChangeNotify(nint notify);

    /// <summary><c>GetChannelCount</c> (not used).</summary>
    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    /// <summary><c>SetMasterVolumeLevel</c> (not used; the scalar form is applied instead).</summary>
    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    /// <summary><c>SetMasterVolumeLevelScalar</c>: the call used by this controller.</summary>
    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    /// <summary><c>GetMasterVolumeLevel</c> (not used).</summary>
    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    /// <summary><c>GetMasterVolumeLevelScalar</c> (not used).</summary>
    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    /// <summary><c>SetChannelVolumeLevel</c> (not used).</summary>
    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

    /// <summary><c>SetChannelVolumeLevelScalar</c> (not used).</summary>
    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    /// <summary><c>GetChannelVolumeLevel</c> (not used).</summary>
    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);

    /// <summary><c>GetChannelVolumeLevelScalar</c> (not used).</summary>
    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    /// <summary><c>SetMute</c> (not used; mute is a <c>VK_VOLUME_MUTE</c> key action per DD-8).</summary>
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    /// <summary><c>GetMute</c> (not used).</summary>
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    /// <summary><c>GetVolumeStepInfo</c> (not used).</summary>
    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    /// <summary><c>VolumeStepUp</c> (not used).</summary>
    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    /// <summary><c>VolumeStepDown</c> (not used).</summary>
    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    /// <summary><c>QueryHardwareSupport</c> (not used).</summary>
    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    /// <summary><c>GetVolumeRange</c> (not used).</summary>
    [PreserveSig]
    int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}
