using System.Threading;

namespace ThreeCxKuando.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\ThreeCxKuandoConnector", out bool first);
        if (!first)
        {
            MessageBox.Show("Der 3CX kuando Connector läuft bereits in dieser Sitzung.",
                "3CX kuando Connector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
