using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>Picks the transport for the platform: the browser WebSocket bridge in a WebGL player, <c>ClientWebSocket</c> everywhere else.</summary>
    public static class TransportFactory
    {
        public static ITransport Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLTransport();
#else
            return new NativeTransport();
#endif
        }
    }
}
