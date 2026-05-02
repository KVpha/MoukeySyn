using MouseSyncServerCore;
using WindowsHID;

public class Programe
{
    public static void Main(string[] args)
    {
        // 检查是否启用 Raw Input 模式
        bool useRawInput = args.Contains("--rawinput") || args.Contains("-r");
        
        if (useRawInput)
        {
            Console.WriteLine("[⚙] Raw Input Mode ENABLED");
        }
        else
        {
            Console.WriteLine("[⚙] Standard Hook Mode (default)");
        }

        Entry.Main(args);
        
        var instance = ServerCore.instance;
        bool isCreateFakeWindowAndHook = true;
        bool isHook = false;

        // 注册钩子回调
        if ((isCreateFakeWindowAndHook) || (isHook))
        {
            MouseHook.addCallback(instance.mouseHandler);
            KeyboardHook.addCallback(instance.keyHandler);
        }

        // 创建窗口（并可选启用 Raw Input）
        if (isCreateFakeWindowAndHook)
        {
            if (useRawInput)
            {
                Hook.SetRawInputMode(true);
            }
            Window.Create(enableRawInput: useRawInput);
        }

        // 启用传统钩子（如需要）
        if (isHook)
        {
            Hook.StartAll();
        }

        ServerCore.wait();
    }
}
