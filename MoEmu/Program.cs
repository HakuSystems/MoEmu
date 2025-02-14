using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MoEmu
{
    class Program
    {
        // Win32 API functions for focusing a window.
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        static async Task Main(string[] args)
        {
            // Force the application to run with administrator privileges.
            if (!IsAdministrator())
            {
                RelaunchAsAdmin();
                return;
            }

            using CancellationTokenSource cts = new CancellationTokenSource();

            Console.WriteLine("Initializing Gamepad Emulator (QWERTZ optimized)...");

            // Set up the virtual gamepad using ViGEmClient.
            using var client = new ViGEmClient();
            var gamepad = client.CreateXbox360Controller();
            gamepad.Connect();
            Console.WriteLine("Virtual gamepad is now online and ready for input!");

            // Send an initial neutral state report so that the controller is detected.
            ResetControllerState(gamepad);

            // Create and verify the interception context.
            using var interceptor = new InterceptorContext();
            if (!interceptor.IsValid)
            {
                Console.WriteLine("ERROR: Failed to create interception context.");
                return;
            }
            Console.WriteLine("Interception context established successfully.");

            // Set filters for keyboard and mouse.
            interceptor.SetFilter(
                InterceptionType.Keyboard,
                InterceptorConstants.INTERCEPTION_FILTER_KEY_DOWN
                    | InterceptorConstants.INTERCEPTION_FILTER_KEY_UP
            );
            interceptor.SetFilter(
                InterceptionType.Mouse,
                InterceptorConstants.INTERCEPTION_FILTER_MOUSE_MOVE
                    | InterceptorConstants.INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_DOWN
                    | InterceptorConstants.INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_UP
            );

            // Initialize input processing with our asynchronous loops.
            var inputProcessor = new InputProcessor(interceptor, gamepad);
            var tasks = new List<Task>
            {
                inputProcessor.ProcessKeyboardAsync(cts.Token),
                inputProcessor.ProcessMouseAsync(cts.Token),
                inputProcessor.MonitorMouseInactivityAsync(cts.Token),
                inputProcessor.MaintainControllerConnectionAsync(cts.Token),
            };

            Console.WriteLine(
                @"
==============================
      Welcome, username!
  
 - P      : Panic key (Shutdown)
==============================
"
            );

            // Focus the Apex Legends window.
            FocusApexLegends();

            // Wait for any of the tasks to complete (e.g., panic exit).
            await Task.WhenAny(tasks);
            cts.Cancel();
        }

        // Checks if the current process is running with administrator privileges.
        private static bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // Relaunches the current process with elevated privileges.
        private static void RelaunchAsAdmin()
        {
            ProcessStartInfo procInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                UseShellExecute = true,
                Verb = "runas",
            };

            try
            {
                Process.Start(procInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to relaunch as administrator: " + ex.Message);
            }
            Environment.Exit(0);
        }

        // Resets the virtual controller state to neutral values.
        static void ResetControllerState(IXbox360Controller gamepad)
        {
            gamepad.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            gamepad.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            gamepad.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            gamepad.SetAxisValue(Xbox360Axis.RightThumbY, 0);
            gamepad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            gamepad.SubmitReport();
        }

        // Attempts to focus the Apex Legends window.
        private static void FocusApexLegends()
        {
            // Adjust the window title if needed.
            const string apexWindowTitle = "Apex Legends";
            IntPtr hWnd = FindWindow(null, apexWindowTitle);
            if (hWnd != IntPtr.Zero)
            {
                SetForegroundWindow(hWnd);
                Console.WriteLine("Apex Legends window focused.");
            }
            else
            {
                Console.WriteLine("Apex Legends window not found.");
            }
        }
    }

    // Constants and types used for interception filters and scan codes.
    public static class InterceptorConstants
    {
        public const ushort INTERCEPTION_FILTER_KEY_DOWN = 0x01;
        public const ushort INTERCEPTION_FILTER_KEY_UP = 0x02;
        public const ushort INTERCEPTION_FILTER_MOUSE_MOVE = 0x01;

        // Assumed flags for mouse button events:
        public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_DOWN = 0x02;
        public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_UP = 0x04;
    }

    // Enumeration for device type in the interception context.
    public enum InterceptionType
    {
        Keyboard,
        Mouse,
    }

    // Disposable wrapper for the unmanaged interception context.
    public class InterceptorContext : IDisposable
    {
        public IntPtr Context { get; private set; }
        public bool IsValid => Context != IntPtr.Zero;

        public InterceptorContext()
        {
            Context = interception_create_context();
        }

        public void Dispose()
        {
            if (Context != IntPtr.Zero)
            {
                interception_destroy_context(Context);
                Context = IntPtr.Zero;
            }
        }

        #region P/Invoke Definitions

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_receive(
            IntPtr context,
            int device,
            ref InterceptionStroke stroke,
            uint nstroke
        );

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_receive(
            IntPtr context,
            int device,
            ref InterceptionMouseStroke stroke,
            uint nstroke
        );

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_send(
            IntPtr context,
            int device,
            ref InterceptionStroke stroke,
            uint nstroke
        );

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(
            IntPtr context,
            InterceptionPredicate predicate,
            ushort filter
        );

        [DllImport("libs\\interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_wait(IntPtr context);

        public delegate int InterceptionPredicate(int device);

        public static int IsKeyboard(int device) => (device >= 1 && device <= 10) ? 1 : 0;

        public static int IsMouse(int device) => (device >= 11 && device <= 20) ? 1 : 0;

        #endregion

        public void SetFilter(InterceptionType type, ushort filter)
        {
            if (type == InterceptionType.Keyboard)
                interception_set_filter(Context, IsKeyboard, filter);
            else if (type == InterceptionType.Mouse)
                interception_set_filter(Context, IsMouse, filter);
        }

        public int WaitForDevice() => interception_wait(Context);

        public int ReceiveKeyboardStroke(ref InterceptionStroke stroke)
        {
            int device = WaitForDevice();
            return interception_receive(Context, device, ref stroke, 1);
        }

        public int ReceiveMouseStroke(ref InterceptionMouseStroke stroke)
        {
            int device = WaitForDevice();
            return interception_receive(Context, device, ref stroke, 1);
        }
    }

    // Struct definitions for keyboard and mouse strokes.
    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionStroke
    {
        public ushort code;
        public ushort state;
        public uint information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionMouseStroke
    {
        public ushort state;
        public ushort flags;
        public short rolling;
        public int x;
        public int y;
        public uint information;
    }

    // Processes keyboard and mouse input asynchronously.
    public class InputProcessor
    {
        private readonly InterceptorContext _interceptor;
        private readonly IXbox360Controller _gamepad;

        // Internal state variables.
        // Controller is active by default.
        private volatile bool _paused = false;
        private readonly object _activeKeysLock = new object();
        private readonly HashSet<int> _activeKeys = new HashSet<int>();
        private double _lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        private const double Sensitivity = 1.05;

        // Scan codes for special keys.
        private const int KEY_INSERT = 82;
        private const int KEY_PANIC = 25;

        // Mapping from keyboard scan codes to Xbox360Controller buttons (QWERTZ optimized).
        private readonly Dictionary<int, Xbox360Button> _keyToButton = new Dictionary<
            int,
            Xbox360Button
        >
        {
            { 57, Xbox360Button.A }, // Space key -> A
            { 29, Xbox360Button.B }, // Left Ctrl -> B
            { 19, Xbox360Button.X }, // R key -> X
            { 44, Xbox360Button.Y }, // QWERTZ: physical key "Y"
            { 15, Xbox360Button.Back }, // Tab -> Back
            { 1, Xbox360Button.Start }, // ESC -> Start
            { 16, Xbox360Button.LeftShoulder }, // Q -> Left Shoulder
            { 18, Xbox360Button.RightShoulder }, // E -> Right Shoulder
            { 56, Xbox360Button.Up }, // Left Alt -> Up
            { 58, Xbox360Button.Down }, // Caps Lock -> Down
            { 46, Xbox360Button.Left }, // C -> Left
            { 4, Xbox360Button.Right }, // '3' key -> Right
            { 42, Xbox360Button.LeftThumb }, // Left Shift -> Left Thumb
            {
                47,
                Xbox360Button.RightThumb
            } // V -> Right Thumb
            ,
        };

        // Mapping for movement keys (W, A, S, D) to joystick input values.
        // First item indicates axis (0: Y, 1: X), second is the multiplier.
        private readonly Dictionary<int, Tuple<int, double>> _movementKeys = new Dictionary<
            int,
            Tuple<int, double>
        >
        {
            { 17, Tuple.Create(0, 1.0) }, // W
            { 31, Tuple.Create(0, -1.0) }, // S
            { 32, Tuple.Create(1, 1.0) }, // D
            {
                30,
                Tuple.Create(1, -1.0)
            } // A
            ,
        };

        public InputProcessor(InterceptorContext interceptor, IXbox360Controller gamepad)
        {
            _interceptor = interceptor;
            _gamepad = gamepad;
        }

        // Continuously submits the controller state as a keep-alive.
        public async Task MaintainControllerConnectionAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _gamepad.SubmitReport();
                await Task.Delay(1000, token);
            }
        }

        public async Task ProcessKeyboardAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int device = _interceptor.WaitForDevice();
                    if (InterceptorContext.IsKeyboard(device) == 1)
                    {
                        InterceptionStroke stroke = new InterceptionStroke();
                        _interceptor.ReceiveKeyboardStroke(ref stroke);
                        HandleKeyStroke(stroke);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Keyboard processing error: {ex.Message}");
                }
                await Task.Delay(5, token);
            }
        }

        public async Task ProcessMouseAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int device = _interceptor.WaitForDevice();
                    if (InterceptorContext.IsMouse(device) == 1)
                    {
                        InterceptionMouseStroke mouseStroke = new InterceptionMouseStroke();
                        _interceptor.ReceiveMouseStroke(ref mouseStroke);
                        // Process mouse button events for shooting.
                        if (
                            (
                                mouseStroke.flags
                                & InterceptorConstants.INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_DOWN
                            ) != 0
                        )
                        {
                            Console.WriteLine("SHOOT: Left mouse button pressed.");
                            _gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 255);
                            _gamepad.SubmitReport();
                        }
                        else if (
                            (
                                mouseStroke.flags
                                & InterceptorConstants.INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_UP
                            ) != 0
                        )
                        {
                            Console.WriteLine("SHOOT: Left mouse button released.");
                            _gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
                            _gamepad.SubmitReport();
                        }
                        // Process mouse movement.
                        else if (
                            (
                                mouseStroke.flags
                                & InterceptorConstants.INTERCEPTION_FILTER_MOUSE_MOVE
                            ) != 0
                        )
                        {
                            Console.WriteLine(
                                $"MOUSE EVENT: Movement detected from device {device}"
                            );
                            HandleMouseMovement(mouseStroke);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Mouse processing error: {ex.Message}");
                }
                await Task.Delay(5, token);
            }
        }

        public async Task MonitorMouseInactivityAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                double currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (currentTime - _lastMouseMoveTime > 50)
                {
                    _gamepad.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                    _gamepad.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                    _gamepad.SubmitReport();
                }
                await Task.Delay(50, token);
            }
        }

        private void HandleKeyStroke(InterceptionStroke stroke)
        {
            string keyState = (stroke.state == 0) ? "PRESSED" : "RELEASED";

            // Still allow pausing if needed (optional).
            if (stroke.code == KEY_INSERT && stroke.state == 0)
            {
                _paused = !_paused;
                Console.WriteLine(
                    _paused ? "SYSTEM PAUSED: Input disabled." : "SYSTEM RESUMED: Ready for input."
                );
                if (_paused)
                {
                    _lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() - 100;
                    Console.WriteLine("Mouse inputs temporarily disabled.");
                }
                return;
            }

            // Panic key (P) triggers emergency shutdown.
            if (stroke.code == KEY_PANIC && stroke.state == 0)
            {
                Console.WriteLine("PANIC: Emergency shutdown triggered. Exiting application...");
                Environment.Exit(0);
            }

            if (_paused)
                return;

            // Process gamepad button mapping.
            if (_keyToButton.ContainsKey(stroke.code))
            {
                var button = _keyToButton[stroke.code];
                Console.WriteLine(
                    $"KEYBOARD BUTTON: {stroke.code} -> Controller Button: {button} STATE: {keyState}"
                );
                _gamepad.SetButtonState(button, stroke.state == 0);
                _gamepad.SubmitReport();
            }

            // Process movement keys for left thumbstick.
            if (_movementKeys.ContainsKey(stroke.code))
            {
                lock (_activeKeysLock)
                {
                    if (stroke.state == 0)
                    {
                        _activeKeys.Add(stroke.code);
                        Console.WriteLine($"MOVEMENT KEY: {stroke.code} PRESSED.");
                    }
                    else if (stroke.state == 1)
                    {
                        _activeKeys.Remove(stroke.code);
                        Console.WriteLine($"MOVEMENT KEY: {stroke.code} RELEASED.");
                    }
                }
                UpdateJoystick();
            }
        }

        private void HandleMouseMovement(InterceptionMouseStroke mouseStroke)
        {
            if (_paused)
                return;

            Console.WriteLine($"MOUSE MOVEMENT: X: {mouseStroke.x}, Y: {mouseStroke.y}");

            // Adjust sensitivity and calculate right thumbstick input.
            double rjoystickX = (mouseStroke.x / 10.0) * Sensitivity;
            double rjoystickY = (mouseStroke.y / 10.0) * Sensitivity;
            rjoystickX = Math.Max(Math.Min(rjoystickX, 1.0), -1.0);
            rjoystickY = Math.Max(Math.Min(rjoystickY, 1.0), -1.0);

            Console.WriteLine($"ADJUSTED VALUES: X: {rjoystickX:F2}, Y: {rjoystickY:F2}");

            short joystickX = (short)(rjoystickX * short.MaxValue);
            short joystickY = (short)(-rjoystickY * short.MaxValue); // Invert for right thumbstick.

            Console.WriteLine($"CONTROLLER RIGHT THUMB: X: {joystickX}, Y: {joystickY}");

            _gamepad.SetAxisValue(Xbox360Axis.RightThumbX, joystickX);
            _gamepad.SetAxisValue(Xbox360Axis.RightThumbY, joystickY);
            _lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _gamepad.SubmitReport();
        }

        private void UpdateJoystick()
        {
            double x = 0,
                y = 0;
            lock (_activeKeysLock)
            {
                foreach (var key in _activeKeys)
                {
                    var movement = _movementKeys[key];
                    // For Y axis, we no longer invert the value.
                    if (movement.Item1 == 0)
                        y += movement.Item2;
                    else
                        x += movement.Item2;
                }
            }

            short leftX = (short)(x * short.MaxValue);
            short leftY = (short)(y * short.MaxValue);

            Console.WriteLine($"MOVEMENT UPDATE: Left joystick set to X: {leftX}, Y: {leftY}");
            _gamepad.SetAxisValue(Xbox360Axis.LeftThumbX, leftX);
            _gamepad.SetAxisValue(Xbox360Axis.LeftThumbY, leftY);
            _gamepad.SubmitReport();
        }
    }
}
