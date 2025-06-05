using Google.Protobuf.WellKnownTypes;
using ntx20.api.proto;
using System;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    
    public class WsConsoleWriter : IAsyncSink<api.proto.Payload>
    {
        
        string prevOutput = "";
        double lastPrintTime = -10000.0;
        bool sepPrinted = true;
        string track;
        private WebSocket socket;
        double lts = 0.0;
        Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");
        public WsConsoleWriter(WebSocket socket, string track)
        {
            this.track = track;
            this.socket = socket;
            WriteSocket("c:i").Wait();
        }

        private async Task WriteSocket(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        async Task IAsyncSink<Payload>.WriteAsync(Payload item, CancellationToken cancelationToken)
        {
            if (item.Track != track)
                return ;
            if (!sepPrinted && (lts - lastPrintTime) > 5000)
            {
                await WriteSocket($"c:p");
                sepPrinted = true;  
            }
            string output = "";
            string la_output = "";
            
            foreach (var e in item.Chunk)
            {
                if (e.Key == "ts")
                    lts = e.D;

                if (e.Key!="txt")
                    continue;
                if (e.Tags.Contains("noise"))
                    continue;
                
                var s = e.S;
                if (e.Tags.Contains("sos"))
                {

                    s = firstalpha.Replace(s, m =>
                    m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                    );

                }

                if (e.Tags.Contains("la"))
                    la_output += s;
                else
                    output += s;
            }
            
            if((output+la_output).Trim().Length >0)
            {
                lastPrintTime = lts;
                sepPrinted = false;
            }

            if(output.Length> 0)
                await WriteSocket($"f:{output}");
            if (la_output.Length > 0)
            {
                await WriteSocket($"l:{la_output}");
            }
            prevOutput = la_output;
            return ;
        }

        async Task IAsyncSink<Payload>.CompleteAsync(CancellationToken cancelationToken)
        {
            if (!sepPrinted)
            {
                await WriteSocket($"c:p");
                sepPrinted = true;
            }


        }

        Task IAsyncSink<Payload>.FlushAsync(CancellationToken cancelationToken)
        {
            return Task.CompletedTask;
        }
    }
}
