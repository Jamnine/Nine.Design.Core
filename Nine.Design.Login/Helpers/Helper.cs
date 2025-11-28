using System.IO;
using System.Security.Permissions;
using System.Windows.Threading;


namespace Nine.Design.Login.Helpers
{
    static class Helper
    {

        public static void Delay(double ms)  //不假死的延时函数
        {
            DateTime current = DateTime.Now;
            while (current.AddMilliseconds(ms) > DateTime.Now)
            {
                DispatcherHelper.DoEvents();
            }
            return;
        }

        public static class DispatcherHelper //延时函数主体方法
        {
            [SecurityPermissionAttribute(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
            public static void DoEvents()
            {
                DispatcherFrame frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrames), frame);
                try { Dispatcher.PushFrame(frame); }
                catch (InvalidOperationException) { }
            }
            private static object ExitFrames(object frame)
            {
                ((DispatcherFrame)frame).Continue = false;
                return null;
            }
        }

        //---------------------->>>保存写出文件方法
        public static void SaveToFile(string Input_Path, string OutPut_Path)
        {
            Stream data = new FileStream(Input_Path, FileMode.Open);
            byte[] data2 = new Byte[data.Length];
            data.Read(data2, 0, (int)data.Length);
            data.Seek(0, SeekOrigin.Begin);

            FileStream fs = new FileStream(OutPut_Path, FileMode.Create);
            BinaryWriter bw = new BinaryWriter(fs);

            bw.Write(data2);
            bw.Close();
            fs.Close();
        }

    }
}
