using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;

if (args.Contains("--child")) { await Task.Delay(TimeSpan.FromMinutes(2)); return; }
if (args.Contains("-nostdin") && File.Exists(Path.Combine(AppContext.BaseDirectory, "hang-preview.flag")))
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview.pid"), Environment.ProcessId.ToString());
    await Task.Delay(TimeSpan.FromMinutes(2)); return;
}
var root = Environment.CurrentDirectory;
if (args.Contains("--spawn-immediately"))
{
    var immediateChild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--child" }, UseShellExecute = false, CreateNoWindow = true })!;
    File.WriteAllText(Path.Combine(root, "child.pid"), immediateChild.Id.ToString());
    return;
}
if (File.Exists(Path.Combine(root, "fail.flag"))) { Environment.Exit(42); return; }
if (File.Exists(Path.Combine(root, "hang.flag"))) { await Task.Delay(TimeSpan.FromMinutes(2)); return; }
var port = int.Parse(args[Array.IndexOf(args, "-p") + 1]);
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
File.WriteAllText(Path.Combine(root, "parent.pid"), Environment.ProcessId.ToString());
if (File.Exists(Path.Combine(root, "spawn-child.flag")))
{
    var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--child" }, UseShellExecute = false, CreateNoWindow = true })!;
    File.WriteAllText(Path.Combine(root, "child.pid"), child.Id.ToString());
}
while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () => {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }
            var body = File.Exists(Path.Combine(root, "bad-schema.flag")) ? "{}" : "{\"paths\":{\"/tts\":{\"post\":{}}},\"components\":{\"schemas\":{\"TTS_Request\":{\"properties\":{\"text\":{},\"ref_audio_path\":{},\"prompt_lang\":{}}}}}}";
            var bytes = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(bytes);
        }
    });
}
