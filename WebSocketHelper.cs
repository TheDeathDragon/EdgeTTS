using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace EdgeTTS;

internal static class WebSocketHelper
{
    public static void SendText(this WebSocket ws, string msg, CancellationTokenSource cancellationTokenSource)
    {
        ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)),
                     WebSocketMessageType.Text, true,
                     cancellationTokenSource.Token).Wait();
    }

    public class Message
    {
        public static readonly Message Close = new();

        public readonly WebSocketMessageType Type;

        public readonly string? MessageStr;

        public readonly byte[]? MessageBinary;

        public Message(string message)
        {
            Type = WebSocketMessageType.Text;
            MessageStr = message;
            MessageBinary = null;
        }

        public Message(byte[] message)
        {
            Type = WebSocketMessageType.Binary;
            MessageStr = null;
            MessageBinary = message;
        }

        private Message()
        {
            Type = WebSocketMessageType.Close;
            MessageStr = null;
            MessageBinary = null;
        }

        public override string ToString() 
            => $"{nameof(Type)}: {Type}, {nameof(MessageStr)}: {MessageStr}, {nameof(MessageBinary)}: byte[{MessageBinary?.Length ?? -1}]";
    }

    public class Session(WebSocket ws)
    {
        public readonly WebSocket ws = ws;
        public readonly StringBuilder sb = new();
        public readonly MemoryStream buffer = new();
        public readonly byte[] array = new byte[5 * 1024];
    }

    public static Message ReceiveNextMessage(Session session, CancellationTokenSource cancellationTokenSource)
    {
        var ws = session.ws;
        var sb = session.sb;
        var buffer = session.buffer;
        var array = session.array;

        sb.Clear();
        buffer.Position = 0;
        buffer.SetLength(0);

        WebSocketMessageType? previousMessageType = null;

        while (true)
        {
            var result = ws.ReceiveAsync(new ArraySegment<byte>(array), cancellationTokenSource.Token).Result;
            switch (result.MessageType)
            {
                case WebSocketMessageType.Text:
                    if (previousMessageType != null && previousMessageType != WebSocketMessageType.Text)
                        throw new IOException("Unexpected WebSocketMessageType");

                    if (result.Count > 0) sb.Append(Encoding.UTF8.GetString(array, 0, result.Count));
                    break;
                case WebSocketMessageType.Binary:
                    if (previousMessageType != null && previousMessageType != WebSocketMessageType.Binary)
                        throw new IOException("Unexpected WebSocketMessageType");

                    if (result.Count > 0) buffer.Write(array, 0, result.Count);
                    break;
                case WebSocketMessageType.Close:
                    Debug.Assert(sb.Length == 0);
                    Debug.Assert(buffer.Length == 0);
                    return Message.Close;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            previousMessageType = result.MessageType;

            if (result.EndOfMessage)
                return previousMessageType == WebSocketMessageType.Text ? new Message(sb.ToString()) : new Message(buffer.ToArray());
        }
    }
}
