using ntx20.api.io;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.utils
{
    public static class CmdUtils
    {
        public static proto.Item LexiconFromUrl(string url)
        {
            if (url == "none")
            {
                return new proto.Item {Key = "lexicon" };
            }

            var ret = Google.Protobuf.JsonParser.Default.Parse<proto.Item>(LazyStream.Input(url).GetText());
            ret.Key = "lexicon";
            return ret;
        }
    }
    public class CmdFormat
    {
        public string Format { get; private set; }
        public string Param { get; private set; }

        public string Origin { get; private set; }

        public static CmdFormat FromSuffix(string iFormat, string path)
        {
            if (path == "none")
            {
                return new CmdFormat { Format = "none", Param = "none", Origin = iFormat };
            }

            var origin = iFormat;
            if (iFormat == "auto")
            {
                iFormat = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            }

            var r = iFormat.Split([':', '-'], 2);
            switch (r.Length)
            {
                case 1:
                    return new CmdFormat { Format = r[0], Param = "none", Origin = origin };
                default:
                    return new CmdFormat { Format = r[0], Param = r[1], Origin = origin };
            }
        }

        public CmdFormat ForPath(string path)
        {
            return FromSuffix(Origin, path);
        }
        public override string ToString()
        {
            return $"{Format}:{Param}";
        }
    }

    public class CmdProcessingMode
    {
        public string Mode { get; set; }
        public int Parallelism { get; set; }
        public static CmdProcessingMode Parse(string cmd)
        {
            if (cmd == "one")
            {
                return new CmdProcessingMode { Mode = cmd, Parallelism = 1 };
            }
            var parts = cmd.Trim().Split(':');
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid processing mode {cmd}");
            }
            return new CmdProcessingMode { Mode = parts[0], Parallelism = (int)uint.Parse(parts[1]) };
        }
    }



    public class CmdRetryWithBackoff
    {
        int count;
        int initMs;
        float mult;
        int cCount;

        public static CmdRetryWithBackoff ParseFromCmd(string cmd)
        {
            int count;
            int initMs = 250;
            float mult = 1.5f;
            var parsed = cmd.Split(new char[] { ':' }, 3);
            count = (int)uint.Parse(parsed[0]);
            if (parsed.Length > 1)
                initMs = (int)uint.Parse(parsed[1]);
            if (parsed.Length > 2)
                mult = float.Parse(parsed[2]);

            return new CmdRetryWithBackoff(count, initMs, mult);
        }

        public CmdRetryWithBackoff(int count, int initMs = 250, float mult = 1.5f)
        {
            this.count = count;
            this.initMs = initMs;
            this.mult = mult;
            this.cCount = 0;
        }

        public async Task<bool> Next(CancellationToken token = default(CancellationToken))
        {

            if (cCount == count)
                return true;

            float cDelay = initMs;
            for (int i = 0; i < cCount; i++)
            {
                cDelay *= mult;
            }
            cCount++;
            await Task.Delay(TimeSpan.FromMilliseconds(cDelay), token);
            return false;
        }
        public int Retry => cCount;
        public int MaxRetries => count;
        public CmdRetryWithBackoff Clone()
        {
            return new CmdRetryWithBackoff(count, initMs, mult);
        }

        public void Reset()
        {
            cCount = 0;
        }
    }
}
