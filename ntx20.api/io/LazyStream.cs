using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.io
{
    public class LazyStream : Stream
    {
        Stream _stream;
        readonly Func<Task<Stream>> fetcher;
        readonly bool input;
        long position = 0;
        public LazyStream(Func<Task<Stream>> fetcher, bool input)
        {
            this.fetcher = fetcher;
            this.input = input;
        }

        private Stream Stream()
        {
            if(_stream==null)
            {
                _stream = fetcher().Result;
            }

            return _stream;
        }

        public static LazyStream Input(string url, CancellationToken token = default)
        {
            return new LazyStream(StreamProvider.Input(url),true);
        }
        public static LazyStream Output(string url, string contentType, CancellationToken token = default)
        {
            return new LazyStream(StreamProvider.Output(url,contentType),false);
        }

        public override bool CanRead => input;

        public override bool CanSeek => Stream().CanSeek;

        public override bool CanWrite => !input;

        public override long Length => Stream().Length;

        public override long Position { get => position; set => throw new NotImplementedException(); }

        public override void Flush()
        {
            Stream().Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int br= Stream().Read(buffer, offset, count);
            if (br > 0)
                position+=br;
            return br;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            position = Stream().Seek(offset, origin);
            return position;
        }

        public override void SetLength(long value)
        {
            Stream().SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream().Write(buffer, offset, count);
            position += count;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
            base.Dispose(disposing);
        }
    }
}
