using ntx20.api.proto;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    
    public class ConsoleWriter : IAsyncSink<api.proto.Payload>
    {
        class Position
        {
            public int Left { get; set; }
            public int Top { get; set; }
        }
        string prevOutput = "";
        private Position fixPos=null;
        double lastPrintTime = -10000.0;
        bool sepPrinted = false;
        string track;
        double lts = 0.0;
        Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");
        public ConsoleWriter(string track)
        {
            this.track = track;
        }

        Task IAsyncSink<Payload>.WriteAsync(Payload item, CancellationToken cancelationToken)
        {
            if (item.Track != track)
                return Task.CompletedTask;
            if (!sepPrinted && (lts - lastPrintTime) > 5000)
            {
                Console.WriteLine();
                Console.WriteLine("====");
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


            


            fixPos = new Position { Left = Console.CursorLeft, Top = Console.CursorTop };
            if(prevOutput.Length > 0)
            {
                var empty = new String(' ', prevOutput.Length);
                Console.Write(empty);
                Console.CursorTop = fixPos.Top; Console.CursorLeft = fixPos.Left;
            }
            Console.Write(output);
            if (la_output.Length > 0)
            {
                fixPos = new Position { Left = Console.CursorLeft, Top = Console.CursorTop };
                Console.Write(la_output);
                Console.CursorTop = fixPos.Top; Console.CursorLeft = fixPos.Left;
            }
            prevOutput = la_output;
            return Task.CompletedTask;
        }

        Task IAsyncSink<Payload>.CompleteAsync(CancellationToken cancelationToken)
        {
            if (!sepPrinted)
            {
                Console.WriteLine();
                Console.WriteLine("====");
                sepPrinted = true;
            }


            return Task.CompletedTask;
        }

        Task IAsyncSink<Payload>.FlushAsync(CancellationToken cancelationToken)
        {
            return Task.CompletedTask;
        }
    }
}
