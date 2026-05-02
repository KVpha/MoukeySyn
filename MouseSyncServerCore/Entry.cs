using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MouseSyncServerCore;

public enum ServerFlags
{
    debug
}
public class Entry
{
    static Entry()
    {
        
    }
    public static bool isDebug = false;
    //Entry
    public static void Main(string[] args)
    {
        Info.load();
        if (args.Contains(ServerFlags.debug.ToString()))
        {
            isDebug = true;
        }
        // hide()报错修改对应rawinput模式不hide()，跳过hide
        bool useRawInput = true;
        if (Info.instance.IsHideOnStart && !useRawInput)
        {
            HideWindow.Hide();
        }
        ServerCore.start();
        


    }
}
