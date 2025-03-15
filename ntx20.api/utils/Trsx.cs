using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
//using System.Numerics.Tensors;
//using Microsoft.Extensions.VectorData;
using System.Xml.Linq;
namespace ntx20.api.utils
{
    public static class Trsx
    {
        static Regex firstAlpha = new Regex(@"^(\s*)(\S)(.*)$");
        static Regex lastPunct = new Regex(@"(,|([^.!?]))\s*$");

        internal static IEnumerable<Tuple<double, double, string>> PostProcess(this IEnumerable<Tuple<double, double, string>> items)
        {
            int cnt = 0;
            foreach (var item in items)
            {
                var s = item.Item3;
                if (cnt == 0)
                {
                    s = firstAlpha.Replace(s, m =>
                       m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                       );
                }
                if(cnt== items.Count()-1)
                {
                    s = lastPunct.Replace(s, m =>
                       m.Groups[2].Value +"."
                       );
                }
                yield return Tuple.Create(item.Item1,item.Item2,s);
                cnt++;
            }
        }
        internal static IEnumerable<Tuple<double, double, string>> ToWords(this IEnumerable<api.proto.Item> items)
        {
            var bStart = -1.0;
            var bEnd = -1.0;
            string cWord = null;
            foreach (var item in items)
            {
                if (item.Key == "ts")
                {
                    if (cWord == null)
                    {
                        bStart = item.D;
                        continue;
                    }
                    if (cWord.Trim().Length == 0)
                    {
                        bStart = item.D;
                        continue;
                    }
                    bEnd = item.D;
                    yield return Tuple.Create(bStart, bEnd, cWord);
                    cWord = null;
                    bStart=item.D;

                    continue;
                }
                if (item.Key == "txt")
                {
                    var s = item.S;
                    if (item.Tags.Contains("sos"))
                    {
                        s = firstAlpha.Replace(s, m =>
                        m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                        );
                    }
                    if (item.Tags.Contains("noise"))
                    {
                        s = "";
                    }

                    if (cWord == null)
                    {
                        cWord = s;
                    }
                    else
                    {
                        cWord += s;
                    }
                }
            }

        }

        internal static IEnumerable<Tuple<double,double,string>> ToBlocks(this IEnumerable<api.proto.Item> items)
        {
            var bStart = -1.0;
            var bEnd = -1.0;
            string cSpk = null;
            foreach (var item in items)
            {
                if (item.Key == "ts")
                {
                    if (cSpk == null)
                    {
                        bStart = item.D;
                    }
                    bEnd = item.D;
                    continue;
                }
                if (item.Key == "txt")
                {
                    if (cSpk != item.S)
                    {
                        if(cSpk != null)
                            yield return Tuple.Create(bStart, bEnd, cSpk);
                        bStart = bEnd;
                        cSpk = item.S;
                    }
                }
            }
            if (cSpk != null)
            {
                yield return Tuple.Create(bStart, bEnd, cSpk);
            }
        }
        public static XDocument Create(IEnumerable<api.proto.Item> diarTrack, IEnumerable<api.proto.Item> atranTrack, string mediaUri)
        {
            var speakers = new Dictionary<string, string>();
            var root = new XElement("transcription",
                     new XAttribute("mediauri", mediaUri),
                     new XAttribute("version", "3.0")
                     );
            var channel = new XElement("se", new XAttribute("name", "transcription"));
            root.Add(new XElement("ch", new XAttribute("name", mediaUri), channel));
            var speaker = new XElement("sp");
            root.Add(speaker);

            
            foreach (var block in diarTrack.ToBlocks())
            {
                var ts = atranTrack.Where(x => x.Key == "ts" && (x.D > block.Item1 || x.D < block.Item2)).Select(x => x.D);
                var startTime = ts
               .OrderBy(n => Math.Abs(n - block.Item1))
               .First();
                var stopTime = ts
                .OrderBy(n => Math.Abs(n - block.Item2))
                .First();

                if (!speakers.ContainsKey(block.Item3))
                {
                    speakers[block.Item3] = speakers.Count().ToString();
                    speaker.Add(
                        new XElement("s",
                            new XAttribute("id", speakers[block.Item3]),
                            new XAttribute("surname", block.Item3),
                            new XAttribute("firstname", ""),
                            new XAttribute("lang", ""),
                            new XAttribute("sex", "")
                            )
                       );
                }




                var cut = atranTrack.SkipWhile(x => x.Key != "ts" || x.Key=="ts" && x.D != startTime)
                    .TakeWhile(x => x.Key != "ts" || x.Key == "ts" && x.D != stopTime)
                    .Append(new proto.Item { Key ="ts", D= stopTime});
                
                List<XElement> words = new List<XElement>();
                foreach (var word in cut.ToWords().PostProcess())
                {
                   
                    words.Add(
                   new XElement("p",
                   new XAttribute("b", TimeSpan.FromMilliseconds(word.Item1)),
                   new XAttribute("e", TimeSpan.FromMilliseconds(word.Item2)),
                   word.Item3));
                }



                channel.Add(new XElement("pa",
                                               new XAttribute("a", ""),
                                               new XAttribute("b", TimeSpan.FromMilliseconds(startTime)),
                                               new XAttribute("e", TimeSpan.FromMilliseconds(stopTime)),
                                               new XAttribute("s", speakers[block.Item3]),
                                               words

                                           ));
                
            }


            var doc = new XDocument(root) { Declaration = new XDeclaration("1.0", "UTF-8", "yes") };
            return doc;
        }
    }
}
