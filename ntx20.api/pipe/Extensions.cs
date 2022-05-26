using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public static class Extensions
    {
        public static proto.Item GetSingleParamOrThrow(this proto.Payload start, string key)
        {
            var f = start.Chunk.FirstOrDefault(x => x.Key == key);
            if (f == null)
                throw new Exception($"Missing {key} param");
            return f;
        }

        public static proto.Item GetSingleParamOrDefault(this proto.Payload start, string key, proto.Item def)
        {
            var f = start.Chunk.FirstOrDefault(x => x.Key == key);
            if (f == null)
                return def;
            return f;
        }
        public static proto.Item GetSingleParamOrNull(this proto.Payload start, string key)
        {
            var f = start.Chunk.FirstOrDefault(x => x.Key == key);
            if (f == null)
                return null;
            return f;
        }
    }
}
