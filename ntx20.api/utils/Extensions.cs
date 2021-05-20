using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.utils
{
    public static class Extensions
    {
        public static async Task<string> GetTextAsync(this System.IO.Stream stream)
        {
            using var v = new System.IO.StreamReader(stream);
            return await v.ReadToEndAsync();
        }
        public static string GetText(this System.IO.Stream stream)
        {
            using var v = new System.IO.StreamReader(stream);
            return v.ReadToEnd();
        }

    }
}
