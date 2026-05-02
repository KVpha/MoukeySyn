using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Runtime.InteropServices;

namespace WindowsHID;
public static class Window
{
    // 消息常量
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_QUIT = 0x0012;

    // Import the necessary functions from user32.dll
    [DllImport("user32.dll")]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam
    );

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    // Import the GetMessage, TranslateMessage, and DispatchMessage functions
    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Define constants for window styles
    const int WS_OVERLAPPED = 0x00000000;
    const int WS_SYSMENU = 0x00080000;
    const int WS_CAPTION = 0x00C00000;
    const int WS_MINIMIZEBOX = 0x00020000;
    const int WS_MAXIMIZEBOX = 0x00010000;
    const int WS_EX_TOOLWINDOW = 0x00000080;

    const int SW_SHOWNORMAL = 1;

    // Define the MSG structure
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    // Define the POINT structure
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    static Thread thread;
    private static IntPtr hWnd;
    private static bool useRawInput = false;

    /// <summary>
    /// 创建窗口并启动消息循环
    /// </summary>
    /// <param name="enableRawInput">是否启用 Raw Input 捕捉</param>
    public static void Create(bool enableRawInput = false)
    {
        useRawInput = enableRawInput;
        thread = new(Loop);
        thread.Start();
    }

    public static void Loop()
    {
        // hide()报错修改对应raw input 窗口使用隐藏任务栏方式，宽高设为0
        hWnd = CreateWindowEx(
            WS_EX_TOOLWINDOW,
            "Static",
            "MouseSync Hidden Window (Raw Input)",
            WS_OVERLAPPED | WS_SYSMENU | WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
            100, 100, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );

        if (hWnd == IntPtr.Zero)
        {
            Console.WriteLine("[✗] Failed to create window");
            return;
        }

        Console.WriteLine($"[✓] Window created: {hWnd}");

        // 初始化 Raw Input（如果启用）
        if (useRawInput)
        {
            if (RawInputCapture.Initialize(hWnd))
            {
                Console.WriteLine("[✓] Raw Input initialized");
            }
            else
            {
                Console.WriteLine("[✗] Failed to initialize Raw Input");
                useRawInput = false;
            }
        }

        Hook.StartAll();
        UpdateWindow(hWnd);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            // 处理 WM_INPUT 消息
            if (msg.message == WM_INPUT && useRawInput)
            {
                try
                {
                    RawInputCapture.ProcessRawInput(msg.lParam);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Error processing WM_INPUT: {ex.Message}");
                }
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // 清理 Raw Input
        if (useRawInput)
        {
            RawInputCapture.Cleanup();
        }
    }

    public static void Destroy()
    {
        thread?.Interrupt();
        if (hWnd != IntPtr.Zero)
        {
            DestroyWindow(hWnd);
        }
    }

    static void Main()
    {
        IntPtr hWnd = CreateWindowEx(
            0,
            "Static",
            "Sample Window",
            WS_OVERLAPPED | WS_SYSMENU | WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
            100, 100, 400, 300,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );

        if (hWnd != IntPtr.Zero)
        {
            ShowWindow(hWnd, SW_SHOWNORMAL);
            UpdateWindow(hWnd);

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            DestroyWindow(hWnd);
        }
    }
}
