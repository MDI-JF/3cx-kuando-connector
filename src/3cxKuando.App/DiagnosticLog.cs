using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace KuandoConnector.App;

internal static class DiagnosticLog
{
    private static readonly object Guard = new();
    private static readonly string Root = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Folder = Path.Combine(Root, "3cxKuando");
    private static readonly string FilePath = Path.Combine(Folder,
        $"events-session-{Process.GetCurrentProcess().SessionId}.log");

    public static void Write(string category, string state)
    {
        try
        {
            lock (Guard)
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
                    " | " + category + " | " + state + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch { /* Diagnostic logging must never disrupt call monitoring. */ }
    }

    public static string SafeError(Exception error)
    {
        if (error is HttpRequestException { StatusCode: { } status })
            return "HTTP " + (int)status;
        if (error is HttpRequestException http &&
            http.InnerException is SocketException socket)
            return "Socket " + socket.SocketErrorCode;
        if (error is SocketException directSocket)
            return "Socket " + directSocket.SocketErrorCode;
        if (error is TaskCanceledException)
            return "HTTP timeout";
        return error.GetType().Name;
    }

    public static void Open()
    {
        try
        {
            if (!File.Exists(FilePath)) Write("Connector", "Diagnostic logging enabled");
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Diagnostic log", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
