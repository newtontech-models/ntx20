using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.io
{
    public class PartFileStream : FileStream, ICompleteStream,IDisposable  
    {
        private readonly string finalpath;
        bool disposed = false;
        public PartFileStream(string path) : base(path+ ".part", FileMode.Create, FileAccess.ReadWrite, FileShare.Read)
        {
            this.finalpath = path;
        }
        public void Complete()
        {
            if (!disposed)
            {
                base.Flush();
                base.Close();
                File.Move(this.Name, this.finalpath,true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
               // if (File.Exists(this.Name))
                 //   File.Delete(this.Name);
                disposed = true;
            }
        }
    }

}
