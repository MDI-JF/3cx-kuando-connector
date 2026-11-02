using System.Threading;

namespace KuandoConnector.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\3cxKuandoConnector", out bool first);
        if (!first)
        {
            MessageBox.Show("3cxKuando is already running in this session.",
                "3cxKuando", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
