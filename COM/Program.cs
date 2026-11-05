using System;
using System.Windows.Forms;

namespace CanDienTu
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormCauHinh());
        }
    }
}
