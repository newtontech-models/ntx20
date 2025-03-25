using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.util
{

    public class ProcessingMode
    {
        public string Mode { get; set; }
        public int Parallelism { get; set; }
        public static ProcessingMode Parse(string cmd)
        {
            if (cmd == "one")
            {
                return new ProcessingMode { Mode=cmd, Parallelism = 1 };
            }
            var parts = cmd.Trim().Split(':');
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid processing mode {cmd}");
            }
            return new ProcessingMode { Mode = parts[0], Parallelism = (int)uint.Parse(parts[1]) };
        }
    }

    public class RetryWithBackoff
    {
        int count;
        int initMs;
        float mult;
        int cCount;

        public static RetryWithBackoff ParseFromCmd(string cmd)
        {
            int count;
            int initMs=250;
            float mult = 1.5f;
            var parsed = cmd.Split(new char[] { ':' },3);
            count = (int)uint.Parse(parsed[0]);
            if (parsed.Length >1)
                initMs= (int)uint.Parse(parsed[1]);
            if (parsed.Length > 2)
                mult = float.Parse(parsed[2]);

            return new RetryWithBackoff(count,initMs,mult); 
        }

        public RetryWithBackoff(int count, int initMs=250, float mult = 1.5f)
        {
            this.count = count;
            this.initMs = initMs;
            this.mult = mult;
            this.cCount = 0;
        }

        public async Task<bool> Next(CancellationToken token=default(CancellationToken))
        {

            if (cCount == count)
                return true;

            float cDelay = initMs;
            for(int i=0;i<cCount;i++)
            {
                cDelay *= mult;
            }
            cCount++;
            await Task.Delay(TimeSpan.FromMilliseconds(cDelay), token);
            return false;
        }
        public int Retry  => cCount;
        public int MaxRetries => count;
        public RetryWithBackoff Clone()
        {
            return new RetryWithBackoff(count,initMs,mult);
        }

        public void Reset()
        {
            cCount = 0;
        }
    }
}
