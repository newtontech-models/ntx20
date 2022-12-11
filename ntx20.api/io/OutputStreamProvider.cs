using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.io
{
    public static partial class StreamProvider
    {
        public static Func<Task<Stream>> Output(string uri, string contentType, CancellationToken token = default, bool createDir = false)
        {
            //preprocess
            if (uri.StartsWith("file://"))
            {
                uri = uri[7..];
            }

            if (uri == "-")
            {
                return () => Task.FromResult(Console.OpenStandardOutput());
            }

            return () => Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(uri);
                if(dir.Length>0 && createDir)
                {
                    Directory.CreateDirectory(dir);
                }
                return new PartFileStream(uri) as Stream;
            }
                );
            
            
            
            
        }
    }
}
