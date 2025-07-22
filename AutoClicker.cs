using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace AutoClicker
{
    public class Config
    {
        public int clicksPerSecond { get; set; } = 10;
    }

    public class VersionInfo
    {
        public int major { get; set; } = 1;
        public int minor { get; set; } = 0;
        public int build { get; set; } = 1;
        public string lastBuildTime { get; set; } = "";

        public override string ToString()
        {
            return $"v{major}.{minor}.{build}";
        }
    }

    public class AutoClicker
    {
        // Windows API 导入
        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // 输入结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // 常量
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_F1 = 0x70;
        private const int VK_F2 = 0x71;
        private const int VK_CONTROL = 0x11;

        private volatile bool isClicking = false;
        private volatile bool shouldExit = false;
        private Point clickPosition;
        private Config config = null!;
        private VersionInfo version = null!;
        private Thread? clickThread;
        private IntPtr hookId = IntPtr.Zero;
        private LowLevelKeyboardProc hookProc;
        private readonly INPUT[] preAllocatedInputs = new INPUT[2];
        private static readonly int inputSize = Marshal.SizeOf(typeof(INPUT));

        public AutoClicker()
        {
            InitializeInputs();
            LoadConfig();
            LoadAndIncrementVersion();
        }

        private void InitializeInputs()
        {
            preAllocatedInputs[0] = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_LEFTDOWN,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            };
            
            preAllocatedInputs[1] = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_LEFTUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            };
        }

        private void LoadConfig()
        {
            try
            {
                string configPath = "config.json";
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<Config>(jsonString) ?? new Config();
                }
                else
                {
                    config = new Config();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置文件失败: {ex.Message}，使用默认配置");
                config = new Config();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("config.json", jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置文件失败: {ex.Message}");
            }
        }

        private void LoadAndIncrementVersion()
        {
            try
            {
                string versionPath = "version.json";
                if (File.Exists(versionPath))
                {
                    string jsonString = File.ReadAllText(versionPath);
                    version = JsonSerializer.Deserialize<VersionInfo>(jsonString) ?? new VersionInfo();
                }
                else
                {
                    version = new VersionInfo();
                }

                string currentBuildTime = GetCurrentBuildTime();
                
                if (version.lastBuildTime != currentBuildTime)
                {
                    version.build++;
                    version.lastBuildTime = currentBuildTime;
                    SaveVersion();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载版本信息失败: {ex.Message}，使用默认版本");
                version = new VersionInfo();
            }
        }

        private string GetCurrentBuildTime()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                if (string.IsNullOrEmpty(assemblyPath) || assemblyPath.Contains("dotnet"))
                {
                    string[] possiblePaths = {
                        Path.Combine("bin", "Debug", "net6.0-windows", "AutoClicker.exe"),
                        Path.Combine("bin", "Debug", "net6.0-windows", "AutoClicker.dll"),
                        Path.Combine("obj", "Debug", "net6.0-windows", "AutoClicker.dll")
                    };
                    
                    foreach (string path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }
                }
                else if (File.Exists(assemblyPath))
                {
                    return File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd HH:mm:ss");
                }
                
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        private void SaveVersion()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(version, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("version.json", jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存版本信息失败: {ex.Message}");
            }
        }

        public void Run()
        {
            Console.WriteLine($"自动点击器 {version}");
            Console.WriteLine($"点击速度: 每秒{config.clicksPerSecond}次");
            Console.WriteLine();
            Console.WriteLine("控制说明:");
            Console.WriteLine("  S - 开始点击 (在当前鼠标位置)");
            Console.WriteLine("  T - 停止点击");
            Console.WriteLine("  Q - 退出程序");
            Console.WriteLine();
            Console.WriteLine("全局热键 (如果注册成功):");
            Console.WriteLine("  Ctrl+F1 - 开始点击");
            Console.WriteLine("  Ctrl+F2 - 停止点击");
            Console.WriteLine();

            // 注册全局热键
            SetupGlobalHotkeys();

            // 键盘钩子不需要额外的消息循环

            while (!shouldExit)
            {
                Console.Write("请输入命令 (S/T/Q): ");
                var key = Console.ReadKey();
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.S:
                        StartClicking();
                        break;
                    case ConsoleKey.T:
                        StopClicking();
                        break;
                    case ConsoleKey.Q:
                        shouldExit = true;
                        StopClicking();
                        Console.WriteLine("程序退出");
                        break;
                    default:
                        Console.WriteLine("无效命令，请输入 S/T/Q");
                        break;
                }
            }

            // 清理资源
            Cleanup();
        }

        private void SetupGlobalHotkeys()
        {
            Console.WriteLine("启动按键监听: Ctrl+F1=开始, Ctrl+F2=停止 (全局有效)");
            
            // 启动按键监听线程
            var keyListenerThread = new Thread(KeyListenerLoop)
            {
                IsBackground = true,
                Name = "KeyListener"
            };
            keyListenerThread.Start();
        }

        private void KeyListenerLoop()
        {
            bool ctrlF1Pressed = false;
            bool ctrlF2Pressed = false;
            
            while (!shouldExit)
            {
                try
                {
                    // 检查Ctrl键是否按下
                    bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    
                    // 检查Ctrl+F1组合键
                    bool f1Current = (GetAsyncKeyState(VK_F1) & 0x8000) != 0;
                    bool ctrlF1Current = ctrlPressed && f1Current;
                    if (ctrlF1Current && !ctrlF1Pressed)
                    {
                        Console.WriteLine("\n[热键] Ctrl+F1 - 开始点击");
                        StartClicking();
                    }
                    ctrlF1Pressed = ctrlF1Current;
                    
                    // 检查Ctrl+F2组合键
                    bool f2Current = (GetAsyncKeyState(VK_F2) & 0x8000) != 0;
                    bool ctrlF2Current = ctrlPressed && f2Current;
                    if (ctrlF2Current && !ctrlF2Pressed)
                    {
                        Console.WriteLine("\n[热键] Ctrl+F2 - 停止点击");
                        StopClicking();
                    }
                    ctrlF2Pressed = ctrlF2Current;
                    
                    // 短暂休眠，避免过度占用CPU
                    Thread.Sleep(50);
                }
                catch
                {
                    // 忽略错误，继续监听
                    Thread.Sleep(100);
                }
            }
        }

        private void Cleanup()
        {
            // 按键监听线程会自动退出
        }

        private void StartClicking()
        {
            if (isClicking)
            {
                Console.WriteLine("已经在点击中...");
                return;
            }

            GetCursorPos(out clickPosition);
            
            isClicking = true;
            
            clickThread = new Thread(HighFrequencyClickLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "HighFrequencyClicker"
            };
            clickThread.Start();
            
            Console.WriteLine($"开始点击 - 位置: ({clickPosition.X}, {clickPosition.Y})，频率: {config.clicksPerSecond}次/秒");
        }

        private void StopClicking()
        {
            if (!isClicking)
            {
                Console.WriteLine("当前没有在点击");
                return;
            }

            isClicking = false;
            clickThread?.Join(1000);
            clickThread = null;
            
            Console.WriteLine("停止点击");
        }

        private void HighFrequencyClickLoop()
        {
            double intervalMs = 1000.0 / config.clicksPerSecond;
            long intervalTicks = (long)(intervalMs * Stopwatch.Frequency / 1000.0);
            
            Stopwatch stopwatch = Stopwatch.StartNew();
            long nextClickTime = intervalTicks;
            
            SetCursorPos(clickPosition.X, clickPosition.Y);
            
            while (isClicking)
            {
                while (stopwatch.ElapsedTicks < nextClickTime && isClicking)
                {
                    Thread.SpinWait(1);
                }
                
                if (!isClicking) break;
                
                // 方法1: 分别发送按下和抬起事件，确保完整性
                SendInput(1, new INPUT[] { preAllocatedInputs[0] }, inputSize); // 按下
                Thread.SpinWait(50); // 微小延迟
                SendInput(1, new INPUT[] { preAllocatedInputs[1] }, inputSize); // 抬起
                
                nextClickTime += intervalTicks;
            }
        }
    }

    class Program
    {
        static void Main()
        {
            var autoClicker = new AutoClicker();
            autoClicker.Run();
        }
    }
}