using System;
using System.Windows.Forms;
using EngineerNotebook.UI;

namespace EngineerNotebook
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
