using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
            // Set the console title.
            Console.Title = "P to EXIT";

            // Force administrator privileges.
            if (!IsAdministrator())
            {
                RelaunchAsAdmin();
                return;
            }

            using CancellationTokenSource cts = new CancellationTokenSource();

            Logger.Log("Initializing Gamepad Emulator (QWERTZ optimized)...");

            // Set up the virtual gamepad using ViGEmClient.
            using var client = new ViGEmClient();
            var gamepad = client.CreateXbox360Controller();
            gamepad.Connect();
            Logger.Log("Virtual gamepad is now online and ready for input!");

            // Send an initial neutral state report so that the controller is detected.
            ResetControllerState(gamepad);

            // Create and verify the interception context.
            using var interceptor = new InterceptorContext();
            if (!interceptor.IsValid)
            {
                Logger.Log("ERROR: Failed to create interception context.");
                return;
            }

            Logger.Log("Interception context established successfully.");

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
                    | InterceptorConstants.INTERCEPTION_FILTER_MOUSE_RIGHT_BUTTON_DOWN
                    | InterceptorConstants.INTERCEPTION_FILTER_MOUSE_RIGHT_BUTTON_UP
            );

            // Initialize input processing with asynchronous loops.
            var inputProcessor = new InputProcessor(interceptor, gamepad);
            var tasks = new List<Task>
            {
                inputProcessor.ProcessKeyboardAsync(cts.Token),
                inputProcessor.ProcessMouseAsync(cts.Token),
                inputProcessor.MonitorMouseInactivityAsync(cts.Token),
                inputProcessor.MaintainControllerConnectionAsync(cts.Token),
            };

            Logger.Log(
                @"==============================
      Welcome, username!
  
 - P      : Panic key (Shutdown)
=============================="
            );

            // Focus the Apex Legends window.
            FocusApexLegends();

            // Wait for any tasks to complete (or panic exit).
            await Task.WhenAny(tasks);
            cts.Cancel();
            Logger.Log("Program terminating normally.");
            Logger.Close();
        }

        private static bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

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
                Logger.Log("Failed to relaunch as administrator: " + ex.Message);
            }

            Environment.Exit(0);
        }

        static void ResetControllerState(IXbox360Controller gamepad)
        {
            gamepad.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            gamepad.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            gamepad.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            gamepad.SetAxisValue(Xbox360Axis.RightThumbY, 0);
            gamepad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            gamepad.SubmitReport();
            Logger.Log("Controller state reset to neutral.");
        }

        private static void FocusApexLegends()
        {
            const string apexWindowTitle = "Apex Legends";
            IntPtr hWnd = FindWindow(null, apexWindowTitle);
            if (hWnd != IntPtr.Zero)
            {
                SetForegroundWindow(hWnd);
                Logger.Log("Apex Legends window focused.");
            }
            else
            {
                Logger.Log("Apex Legends window not found.");
            }
        }
    }

    public static class InterceptorConstants
    {
        public const ushort INTERCEPTION_FILTER_KEY_DOWN = 0x01;
        public const ushort INTERCEPTION_FILTER_KEY_UP = 0x02;
        public const ushort INTERCEPTION_FILTER_MOUSE_MOVE = 0x01;

        // Assumed constants for mouse button events (not coming via flags).
        public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_DOWN = 0x02;
        public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_BUTTON_UP = 0x04;
        public const ushort INTERCEPTION_FILTER_MOUSE_RIGHT_BUTTON_DOWN = 0x08;
        public const ushort INTERCEPTION_FILTER_MOUSE_RIGHT_BUTTON_UP = 0x10;
    }

    public enum InterceptionType
    {
        Keyboard,
        Mouse,
    }

    public class InterceptorContext : IDisposable
    {
        public InterceptorContext()
        {
            Context = interception_create_context();
        }

        public IntPtr Context { get; private set; }
        public bool IsValid => Context != IntPtr.Zero;

        public void Dispose()
        {
            if (Context != IntPtr.Zero)
            {
                interception_destroy_context(Context);
                Context = IntPtr.Zero;
            }
        }

        public void SetFilter(InterceptionType type, ushort filter)
        {
            if (type == InterceptionType.Keyboard)
                interception_set_filter(Context, IsKeyboard, filter);
            else if (type == InterceptionType.Mouse)
                interception_set_filter(Context, IsMouse, filter);
        }

        public int WaitForDevice()
        {
            return interception_wait(Context);
        }

        public int ReceiveKeyboardStroke(ref InterceptionStroke stroke)
        {
            var device = WaitForDevice();
            return interception_receive(Context, device, ref stroke, 1);
        }

        public int ReceiveMouseStroke(ref InterceptionMouseStroke stroke)
        {
            var device = WaitForDevice();
            return interception_receive(Context, device, ref stroke, 1);
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
    }

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

    public class InputProcessor
    {
        private const double Sensitivity = 1.05;
        private const double AimSensitivityFactor = 0.5; // Lower sensitivity when aiming.
        private const int KEY_INSERT = 82;
        private const int KEY_PANIC = 25;
        private readonly HashSet<int> _activeKeys = new();
        private readonly object _activeKeysLock = new();
        private readonly IXbox360Controller _gamepad;
        private readonly InterceptorContext _interceptor;

        private readonly Dictionary<int, Xbox360Button> _keyToButton = new Dictionary<
            int,
            Xbox360Button
        >
        {
            { 57, Xbox360Button.A },
            { 29, Xbox360Button.B },
            { 19, Xbox360Button.X },
            { 44, Xbox360Button.Y },
            { 15, Xbox360Button.Back },
            { 1, Xbox360Button.Start },
            { 16, Xbox360Button.LeftShoulder },
            { 18, Xbox360Button.RightShoulder },
            { 56, Xbox360Button.Up },
            { 58, Xbox360Button.Down },
            { 46, Xbox360Button.Left },
            { 4, Xbox360Button.Right },
            { 42, Xbox360Button.LeftThumb },
            { 47, Xbox360Button.RightThumb },
        };

        // Movement keys: W (17), S (31), D (32), A (30).
        private readonly Dictionary<int, Tuple<int, double>> _movementKeys = new Dictionary<
            int,
            Tuple<int, double>
        >
        {
            { 17, Tuple.Create(0, 1.0) },
            { 31, Tuple.Create(0, -1.0) },
            { 32, Tuple.Create(1, 1.0) },
            { 30, Tuple.Create(1, -1.0) },
        };

        // Flag for aiming mode.
        private bool _isAiming;
        private double _lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // Controller is active by default.
        private volatile bool _paused;

        public InputProcessor(InterceptorContext interceptor, IXbox360Controller gamepad)
        {
            _interceptor = interceptor;
            _gamepad = gamepad;
        }

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
                    Logger.Log($"Keyboard processing error: {ex.Message}");
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

                        Logger.Log(
                            $"DEBUG: Mouse event - state: 0x{mouseStroke.state:X}, flags: 0x{mouseStroke.flags:X}, information: 0x{mouseStroke.information:X}"
                        );

                        // If no movement is detected, use the state field to detect button events.
                        if (mouseStroke.x == 0 && mouseStroke.y == 0 && mouseStroke.rolling == 0)
                        {
                            // Interpret the state field:
                            // 0x1: left button down, 0x2: left button up, 0x4: right button down, 0x8: right button up.
                            if (mouseStroke.state == 0x1)
                            {
                                Logger.Log("NOW THE LEFT MOUSE BUTTON SHOULD HAVE BEEN PRESSED.");
                                _gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 255);
                                _gamepad.SubmitReport();
                            }
                            else if (mouseStroke.state == 0x2)
                            {
                                Logger.Log("NOW THE LEFT MOUSE BUTTON SHOULD HAVE BEEN RELEASED.");
                                _gamepad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
                                _gamepad.SubmitReport();
                            }

                            if (mouseStroke.state == 0x4)
                            {
                                if (!_isAiming)
                                {
                                    Logger.Log(
                                        "NOW THE RIGHT MOUSE BUTTON SHOULD HAVE BEEN PRESSED (aim mode activated)."
                                    );
                                    _isAiming = true;
                                    _gamepad.SetSliderValue(Xbox360Slider.LeftTrigger, 255);
                                    _gamepad.SubmitReport();
                                }
                            }
                            else if (mouseStroke.state == 0x8)
                            {
                                if (_isAiming)
                                {
                                    Logger.Log(
                                        "NOW THE RIGHT MOUSE BUTTON SHOULD HAVE BEEN RELEASED (aim mode deactivated)."
                                    );
                                    _isAiming = false;
                                    _gamepad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                                    _gamepad.SubmitReport();
                                }
                            }
                        }
                        else
                        {
                            // Otherwise, treat it as a movement event.
                            Logger.Log($"MOUSE EVENT: Movement detected from device {device}");
                            HandleMouseMovement(mouseStroke);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Mouse processing error: {ex.Message}");
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

            // Optional pause toggle.
            if (stroke.code == KEY_INSERT && stroke.state == 0)
            {
                _paused = !_paused;
                Logger.Log(
                    _paused ? "SYSTEM PAUSED: Input disabled." : "SYSTEM RESUMED: Ready for input."
                );
                if (_paused)
                {
                    _lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() - 100;
                    Logger.Log("Mouse inputs temporarily disabled.");
                }

                return;
            }

            // Panic key (P) triggers shutdown.
            if (stroke.code == KEY_PANIC && stroke.state == 0)
            {
                Logger.Log("PANIC: Emergency shutdown triggered. Exiting application...");
                Logger.Close();
                Environment.Exit(0);
            }

            if (_paused)
                return;

            if (_keyToButton.ContainsKey(stroke.code))
            {
                var button = _keyToButton[stroke.code];
                Logger.Log(
                    $"KEYBOARD BUTTON: {stroke.code} -> Controller Button: {button} STATE: {keyState}"
                );
                _gamepad.SetButtonState(button, stroke.state == 0);
                _gamepad.SubmitReport();
            }

            if (_movementKeys.ContainsKey(stroke.code))
            {
                lock (_activeKeysLock)
                {
                    if (stroke.state == 0)
                    {
                        _activeKeys.Add(stroke.code);
                        Logger.Log($"MOVEMENT KEY: {stroke.code} PRESSED.");
                    }
                    else if (stroke.state == 1)
                    {
                        _activeKeys.Remove(stroke.code);
                        Logger.Log($"MOVEMENT KEY: {stroke.code} RELEASED.");
                    }
                }

                UpdateJoystick();
            }
        }

        private void HandleMouseMovement(InterceptionMouseStroke mouseStroke)
        {
            if (_paused)
                return;

            Logger.Log($"MOUSE MOVEMENT: X: {mouseStroke.x}, Y: {mouseStroke.y}");

            // Use reduced sensitivity when aiming.
            var effectiveSensitivity = _isAiming ? Sensitivity * AimSensitivityFactor : Sensitivity;
            var rjoystickX = mouseStroke.x / 10.0 * effectiveSensitivity;
            var rjoystickY = mouseStroke.y / 10.0 * effectiveSensitivity;
            rjoystickX = Math.Max(Math.Min(rjoystickX, 1.0), -1.0);
            rjoystickY = Math.Max(Math.Min(rjoystickY, 1.0), -1.0);

            Logger.Log($"ADJUSTED VALUES: X: {rjoystickX:F2}, Y: {rjoystickY:F2}");
            short joystickX = (short)(rjoystickX * short.MaxValue);
            short joystickY = (short)(-rjoystickY * short.MaxValue); // Invert for right thumbstick.

            Logger.Log($"CONTROLLER RIGHT THUMB: X: {joystickX}, Y: {joystickY}");
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
                    if (movement.Item1 == 0)
                        y += movement.Item2;
                    else
                        x += movement.Item2;
                }
            }

            short leftX = (short)(x * short.MaxValue);
            short leftY = (short)(y * short.MaxValue);
            Logger.Log($"MOVEMENT UPDATE: Left joystick set to X: {leftX}, Y: {leftY}");
            _gamepad.SetAxisValue(Xbox360Axis.LeftThumbX, leftX);
            _gamepad.SetAxisValue(Xbox360Axis.LeftThumbY, leftY);
            _gamepad.SubmitReport();
        }
    }

    // Logger class that writes logs to a file and echoes them to the console.
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly StreamWriter _writer;

        static Logger()
        {
            var fileName = "MoEmuLog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            _writer = new StreamWriter(fileName, false);
            _writer.AutoFlush = true;
            Log("Log started.");
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                _writer.WriteLine(logEntry);
                Console.WriteLine(message);
            }
        }

        public static void Close()
        {
            lock (_lock)
            {
                Log("Log closed.");
                _writer.Close();
            }
        }
    }
}
