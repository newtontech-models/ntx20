using ntx20.api.io;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
}
