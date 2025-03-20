using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public class TarSinkWriter : IAsyncSink<TarEntry>
    {
        TarWriter tarWriter = null;
        Stream stream;
        readonly Stream streamResolver;
        
        public TarSinkWriter(Stream streamResolver)
        {
            this.streamResolver = streamResolver;
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            await streamResolver.FlushAsync(cancellationToken);
        }
        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            await tarWriter.DisposeAsync();
        }

        public async Task WriteAsync(TarEntry entry, CancellationToken cancellationToken = default)
        {
            if (tarWriter == null)
            {
                tarWriter = new TarWriter(streamResolver, TarEntryFormat.Ustar,leaveOpen: true);
            }

            await tarWriter.WriteEntryAsync(entry, cancellationToken);
        }
    }
}
