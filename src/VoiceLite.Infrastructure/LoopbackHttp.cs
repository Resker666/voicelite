using System.Net;
using System.Net.Sockets;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

internal static class LoopbackHttp
{
    internal static void Validate(IEngineSession session)
    {
        var uri = session.Endpoint;
        if (!uri.IsAbsoluteUri || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port < 1 || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0)
            throw new VoiceLiteException("仅允许连接本应用管理的 127.0.0.1 引擎。");
        session.VerifyOwnership();
    }
    internal static HttpClient Create(IEngineSession session, HttpMessageHandler? injected = null)
    {
        Validate(session);
        if (injected is not null) return new(injected, false) { BaseAddress = session.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
        handler.ConnectCallback = async (_, token) => {
            Validate(session);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, session.Endpoint.Port), token);
                Validate(session); // Recheck the OS owner after connect, before sending HTTP bytes.
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        };
        return new(handler) { BaseAddress = session.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
    }
}
