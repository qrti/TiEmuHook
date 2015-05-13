using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

using TiEmuHook.Properties;                 // Settings -> C:\Users\%USER%\AppData\Local\TiEmuHook
using System.Runtime.InteropServices;       // DLLImport
using System.Threading;                     // Thread.Sleep()
using System.IO;                            // Path Directory File

namespace TiEmuHook
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        internal struct INPUT{
            public UInt32 Type;
            public MOUSEKEYBDHARDWAREINPUT Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct MOUSEKEYBDHARDWAREINPUT{
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;
        }

        #pragma warning disable 649

        internal struct MOUSEINPUT{
            public Int32 X;
            public Int32 Y;
            public UInt32 MouseData;
            public UInt32 Flags;
            public UInt32 Time;
            public IntPtr ExtraInfo;
        }

        #pragma warning restore 649
      
        public IntPtr topHwnd = IntPtr.Zero;
        public volatile bool listen = true;
        Process process;

        INPUT[] mouseDown, mouseUp;
        System.Windows.Forms.Timer timer;
        int phase;

        int ii;

        private void Event(object sender, EventArgs e) 
        {
            phase = 0;
            timer.Start();
        }

        public Form1()
        {
            InitializeComponent();
            MouseHook.start(this);
            MouseHook.MouseAction += new EventHandler(Event);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            INPUT mouseDownInput = new INPUT();
            mouseDownInput.Type = 0;                    
            mouseDownInput.Data.Mouse.Flags = 0x0002;           // left button down  
            mouseDown = new INPUT[] { mouseDownInput };

            INPUT mouseUpInput = new INPUT();
            mouseUpInput.Type = 0;                    
            mouseUpInput.Data.Mouse.Flags = 0x0004;             // left button up  
            mouseUp = new INPUT[] { mouseUpInput };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 25; 
            timer.Tick += new EventHandler(timer_Tick);

            txtPath.Text = Settings.Default.path;
            txtTitle.Text = Settings.Default.title;
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
           if(Settings.Default.formPosX<0 || Settings.Default.formPosX>System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width)
                Settings.Default.formPosX = 0;
                
            if(Settings.Default.formPosY<0 || Settings.Default.formPosY>System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height)
                Settings.Default.formPosY = 0;
                
            //if(Settings.Default.formWidth<=0 || Settings.Default.formWidth>System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width)
            //    Settings.Default.formWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / 2;

            //if(Settings.Default.formHeight<=0 || Settings.Default.formHeight>System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height)
            //    Settings.Default.formHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / 2;

            this.Location = new Point(Settings.Default.formPosX, Settings.Default.formPosY); 

            //this.Width = Settings.Default.formWidth;
            //this.Height = Settings.Default.formHeight;

            startApp(false);
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            MouseHook.stop();

            Settings.Default.formPosX = this.Location.X;
            Settings.Default.formPosY = this.Location.Y;
            //Settings.Default.formWidth = this.Width;
            //Settings.Default.formHeight = this.Height;

            Settings.Default.path = txtPath.Text;
            Settings.Default.title = txtTitle.Text;
            Settings.Default.Save();

            if(process!=null && !process.HasExited)
                process.CloseMainWindow();
        }

        private void btnRestart_Click(object sender, EventArgs e)
        {
            startApp(true);
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void startApp(bool restart)
        {
            if(restart)
                txtStatus.Clear();

            if(process!=null && !process.HasExited){
                process.CloseMainWindow();
                process = null;
            }

            topHwnd = findWindow(false);

            if(topHwnd==IntPtr.Zero && File.Exists(txtPath.Text)){
                process = new Process{
                    StartInfo = new ProcessStartInfo{ FileName = txtPath.Text }
                };

                process.Start();
                int t = 0;    

                while((topHwnd = findWindow(false))==IntPtr.Zero && t<50){
                    Thread.Sleep(100);
                    t++;
                }
            }

            txtStatus.AppendText("TiEmuHook V1.0 qland.de\n");
            topHwnd = findWindow(true);

            if(topHwnd != IntPtr.Zero)
                this.WindowState = FormWindowState.Minimized;
        }

        private IntPtr findWindow(bool message)
        {
            IntPtr h = IntPtr.Zero;

            foreach(Process p in Process.GetProcesses()){
                string title = p.MainWindowTitle;

                if(title.Contains(txtTitle.Text)){
                    h = p.MainWindowHandle;
                    process = p;
                    break;
                }
            }

            ii = 0;
            
            if(message)
                txtStatus.AppendText(string.Format("Target Window {0}found\n", h==IntPtr.Zero ? "not " : ""));

            return h;
        }

        void timer_Tick(object sender, EventArgs e)
        {
            switch(phase){
                case 0:
                    SendInput((uint)mouseDown.Length, mouseDown, Marshal.SizeOf(typeof(INPUT)));
                    phase++;
                    break;

                case 1:
                    SendInput((uint)mouseUp.Length, mouseUp, Marshal.SizeOf(typeof(INPUT)));           
                    phase++;
                    timer.Stop();
                    listen = true;
                    txtStatus.AppendText("-> " + ii++ + "\n");
                    break;
            }
        }
    }

    
    public static class MouseHook
    {
        public static event EventHandler MouseAction = delegate { };
        private static LowLevelMouseProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static Form1 _form = null;

        public static void start(Form1 form)
        {
            _form = form;
            _hookID = SetHook(_proc);
        }

        public static void stop()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using(Process curProcess = Process.GetCurrentProcess())
            using(ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            IntPtr h = GetForegroundWindow();

            if(h == _form.topHwnd){
                if(nCode >= 0 && (MouseMessages)wParam == MouseMessages.WM_LBUTTONUP  && _form.listen){
                    _form.listen = false;

                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                    MouseEventArgs e = new MouseEventArgs(0,
                                                          0,
                                                          hookStruct.pt.x,
                                                          hookStruct.pt.y,
                                                          0);
                    
                    MouseAction(null, e);
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);          // return (IntPtr)0x01;     // do not pass event to next hook or application
        }

        private const int WH_MOUSE_LL = 14;

        private enum MouseMessages{
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
            WM_MOUSEMOVE = 0x0200,
            WM_MOUSEWHEEL = 0x020A,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT{
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT{
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
    }
}

//[DllImport("user32.dll")]
//internal static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

//internal struct INPUT{
//    public UInt32 Type;
//    public MOUSEKEYBDHARDWAREINPUT Data;
//}

//[StructLayout(LayoutKind.Explicit)]
//internal struct MOUSEKEYBDHARDWAREINPUT{
//    [FieldOffset(0)]
//    public MOUSEINPUT Mouse;
//}

//#pragma warning disable 649

//internal struct MOUSEINPUT{
//    public Int32 X;
//    public Int32 Y;
//    public UInt32 MouseData;
//    public UInt32 Flags;
//    public UInt32 Time;
//    public IntPtr ExtraInfo;
//}

//#pragma warning restore 649

//var inputMouseDown = new INPUT();
//inputMouseDown.Type = 0;                    
//inputMouseDown.Data.Mouse.Flags = 0x0002;   // left button down

//var inputMouseUp = new INPUT();
//inputMouseUp.Type = 0; 
//inputMouseUp.Data.Mouse.Flags = 0x0004;     // left button up

//var inputs = new INPUT[] { inputMouseDown, inputMouseUp };
//SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));


//[DllImport("user32.dll", EntryPoint = "PostMessageA", SetLastError = true)]
//public static extern bool PostMessage(IntPtr hwnd, uint Msg, IntPtr wParam, IntPtr lParam);

//[DllImport("user32.dll", CharSet = CharSet.Auto)]
//static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

//private const int WM_LBUTTONDOWN = 0x201; 
//private const int WM_LBUTTONUP = 0x202;   

//int coordinates =  x | (y << 16);
//SendMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)0x00, (IntPtr)coordinates);
//SendMessage(hWnd, WM_LBUTTONUP, (IntPtr)0x00, (IntPtr)coordinates);
//SendMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)0x00, (IntPtr)coordinates);
//SendMessage(hWnd, WM_LBUTTONUP, (IntPtr)0x00, (IntPtr)coordinates);


//private const UInt32 MOUSEEVENTF_LEFTDOWN = 0x0002;
//private const UInt32 MOUSEEVENTF_LEFTUP = 0x0004;

//[DllImport("user32.dll")]
//private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, uint dwExtraInf);

//mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
//Thread.Sleep(100);
//mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);