using System.Runtime.InteropServices;
using TouchPanel.Actions;

namespace TouchPanel.Interop.Input;

/// <summary>
/// The only key-synthesis implementation (RF-048, RF-053): injects one virtual-key press (key down
/// followed by key up) through <c>SendInput</c>. All P/Invoke for input lives here (RNF-010).
/// </summary>
/// <remarks>
/// <para><b>Failure containment.</b> A blocked injection (for example a higher-integrity window
/// owning the input queue) is reported as <see langword="false"/> with a reason instead of an
/// exception, so the action layer turns it into a widget error state and a log entry (RF-050/F-14).</para>
/// <para><b>Foreground independence.</b> Media/volume virtual keys are handled by the shell, so
/// they work without the panel ever taking focus (RF-018/RF-057 context).</para>
/// </remarks>
internal sealed class SendInputHelper : IKeyInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    /// <inheritdoc />
    public bool TrySendKey(ushort virtualKey, out string? error)
    {
        var inputs = new INPUT[2];
        inputs[0] = KeyboardInput(virtualKey, keyUp: false);
        inputs[1] = KeyboardInput(virtualKey, keyUp: true);
        return TrySend(inputs, out error);
    }

    /// <inheritdoc />
    public bool TryKeyDown(ushort virtualKey, out string? error) =>
        TrySend([KeyboardInput(virtualKey, keyUp: false)], out error);

    /// <inheritdoc />
    public bool TryKeyUp(ushort virtualKey, out string? error) =>
        TrySend([KeyboardInput(virtualKey, keyUp: true)], out error);

    private static INPUT KeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new INPUTUNION
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    private static bool TrySend(INPUT[] inputs, out string? error)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == inputs.Length)
        {
            error = null;
            return true;
        }

        var win32Error = Marshal.GetLastWin32Error();
        error = sent == 0
            ? $"Input synthesis was blocked (Win32 error {win32Error})."
            : $"Input synthesis was only partially delivered ({sent} of {inputs.Length} events, Win32 error {win32Error}).";
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    /// <summary>
    /// The native union; the mouse member is declared only so the structure keeps the native size
    /// (the keyboard member alone would under-size it on x64).
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, [In] INPUT[] inputs, int sizeOfInputStructure);
}
