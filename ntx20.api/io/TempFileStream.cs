using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.io
{
    public class TempFileStream : FileStream
    {
        private readonly string finalpath;
        bool closed = false;
        bool disposed = false;
        public TempFileStream(string path) : base(Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".part"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)
        {
            this.finalpath = path;
            //this.closed = closed;
        }
        public void Confirm()
        {
            closed = true;
        }
        public override void Close()
        {

            if (!disposed)
            {
                closed = true;
                base.Flush();
                base.Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                try
                {
                    if (closed)
                        File.Move(this.Name, this.finalpath);
                    else
                        File.Delete(this.Name);
                }
                catch 
                {

                    if (File.Exists(this.Name))
                        File.Delete(this.Name);
                }

                disposed = true;
            }
        }
    }

}
