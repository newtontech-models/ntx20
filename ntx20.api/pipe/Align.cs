using ntx.api.utils;
using ntx20.api.proto;
using ntx20.api.proto.legacy.v2t.engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public static partial class Pipe
    {
        
        internal static async IAsyncEnumerable<proto.Payload> ViaReLabelAndSplit(this IAsyncEnumerable<proto.Payload> source, Regex split, Regex plus)
        {

            await foreach (var item in source)
            {
                var ret = new proto.Payload { Track = item.Track };
                foreach (var i in item.Chunk)
                {
                    if (i.Key != "txt")
                    {
                        ret.Chunk.Add(i);
                        continue;
                    }

                    
                    foreach (var s in split.Split(i.S))
                    {
                        if (s == "")
                            continue;
                        if (plus.IsMatch(s))
                        {
                            ret.Chunk.Add(
                                new proto.Item { Key = "txt", S = s, Tags = { "+"} }
                                );
                        }
                        else
                        {
                            ret.Chunk.Add(
                                new proto.Item { Key = "txt", S = s }
                                );
                        }
                    }
                }
                yield return ret;
            }
        }
        internal static async IAsyncEnumerable<proto.Payload> ViaLabelMerge(this IAsyncEnumerable<proto.Payload> source)
        {
            
            await foreach (var item in source)
            {
                var ret = new proto.Payload { Track = item.Track };
                var buff = new List<proto.Item>();
                var pos = item.Chunk.GetEnumerator();
                bool? isPlus = null;
                foreach (var i in item.Chunk)
                {
                    if (i.Key != "txt")
                    {
                        if (isPlus == null)
                        {
                            ret.Chunk.Add(i);
                        }
                        else
                        {
                            buff.Add(i);
                        }
                        continue;
                    }
                    if(isPlus == null)
                    {
                        isPlus = i.Tags.Contains("+");
                        buff.Add(i);
                        continue;
                    }
                    if (isPlus != i.Tags.Contains("+"))
                    {
                        ret.Chunk.AddRange(buff);
                        buff.Clear();
                        buff.Add(i);
                        isPlus=i.Tags.Contains("+");
                        continue;
                    }
                    /*
                    if (isPlus == true)
                    {
                        ret.Chunk.AddRange(buff);
                        buff.Clear();
                        buff.Add(i);
                    }
                    else
                    */
                    {
                        i.S = buff[0].S + i.S;
                        buff.Clear();
                        buff.Add(i);
                    }
                    
                }
                ret.Chunk.AddRange(buff);
                yield return ret;
            }
        }

        internal static IEnumerable<TextItem> ToTextItemRef(this IEnumerable<proto.Payload> source)
        {
            var toNotEval = new string[] { "noise", "+" };
            int block = 0;
            int idg = 0;
            var cSpeaker = "nobody";
            foreach (var item in source)
            {
                int id = 0;
                foreach ( var i in item.Chunk)
                {
                    if (i.Key == "spk")
                    {
                        cSpeaker = i.S;
                    }
                        if (i.Key == "txt")
                    {
                        bool eval = i.Tags.Intersect(toNotEval).Count() == 0;
                        if (i.Tags.Contains("+"))
                        {
                            yield return new TextItem { Block = block,Value= i.S ,Index = idg,
                                Text = "#!ref" +  idg.ToString("000000000") +  i.S, Speaker=cSpeaker,
                                Eval = eval
                            };
                        }
                        else
                        {
                            yield return new TextItem { Block = block,Value =i.S ,Index = idg, Text = i.S, Speaker=cSpeaker, Eval = eval };
                        }
                    }
                    id++;
                    idg++;
                }
                block++;
            }
        }
        internal static IEnumerable<TextItem> ToTextItemRes(this IEnumerable<proto.Payload> source)
        {
            var toNotEval = new string[] { "noise", "+" };
            int block = 0;
            int idg = 0;
            var cSpeaker = "nobody";
            TextItem cItem = null;
            var lastTs = -1.0;
            foreach (var item in source)
            {
                int id = 0;
                foreach (var i in item.Chunk)
                {
                    var lastText = "";
                    if (i.Key == "ts")
                    {
                        lastTs = i.D;
                        
                    }
                    if (i.Key == "spk")
                    {
                        cSpeaker = i.S;
                    }
                    if (i.Key == "txt")
                    {
                        bool eval = i.Tags.Intersect(toNotEval).Count() == 0;
                        if (i.Tags.Contains("+"))
                        {
                            cItem = new TextItem { Block = block, Value = i.S, Index = idg, 
                                Text = "#!res" + idg.ToString("000000000") + i.S , Speaker=cSpeaker, Eval =eval, Start=lastTs};
                        }
                        else
                        {
                            cItem =  new TextItem { Block = block, Value = i.S, Index = idg, Text = i.S , Speaker = cSpeaker, Eval = eval, Start=lastTs };
                        }
                    }
                    id++;
                    idg++;
                }
                block++;
            }
        }
        public static List<AlignBlock> ToAlignBlocks(this List<List<AlignedItem>> alignBlocks, Item[] refList, Item[] resList)
        {
            
            return null;
        }



        public static async IAsyncEnumerable<proto.Payload> AlignWith(this IAsyncEnumerable<proto.Payload> source, 
            IAsyncEnumerable<proto.Payload> reference, string SplitOption, string PlusOption)
        {
            Regex split = new Regex(SplitOption);
            Regex plus = new Regex(PlusOption);

            //Expecting ordered in blocks
            var rfr = await reference.ViaReLabelAndSplit(split, plus).ViaLabelMerge().ToArrayAsync();
            var src = await source.ViaReLabelAndSplit(split, plus).ViaLabelMerge().ToArrayAsync();

            var aligment = rfr.ToTextItemRef().ToList().AlignWith(src.ToTextItemRes().ToList(), true, 1000);
                           //.ToAlignBlocks(rfr.SelectMany(x=>x.Chunk).ToArray(), rfr.SelectMany(x=>x.Chunk).ToArray());

            

            yield return new proto.Payload();

        }
    }
}
