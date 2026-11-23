using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace VirtualTourBox;

public partial class App : System.Windows.Application
{
    readonly string instanceName = "VirtualTourBox-" + WindowsIdentity.GetCurrent().User!.Value;
    Mutex? instanceMutex;
    bool ownsMutex;
    readonly CancellationTokenSource stopping = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex = new Mutex(false, "Local\\" + instanceName);
        try { ownsMutex = instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex)
        {
            NotifyExistingInstance();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        _ = ListenForActivation();
    }

    void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", instanceName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client);
            writer.WriteLine("show");
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show("Virtual TourBox 已在运行。请从任务栏或通知区域打开；若旧版本没有响应，请先彻底退出旧版本。");
        }
    }

    async Task ListenForActivation()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(instanceName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stopping.Token);
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(stopping.Token);
                if (command == "show")
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MainWindow.Show();
                        MainWindow.WindowState = WindowState.Normal;
                        MainWindow.Activate();
                    });
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        stopping.Cancel();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
