using Google.Protobuf;
using ntx.api.utils;
using ntx20.api.proto;
using ntx20.api.proto.legacy.v2t.engine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public static partial class Pipe
    {
        

        public static async IAsyncEnumerable<proto.Payload> ToTranStream(this IAsyncEnumerable<string> source)
        {
            Regex split = new Regex(Constants.DefaultTextSplit);
            Regex plus = new Regex(Constants.DefaultPlusMatch);
            Regex spk = new Regex(@"^\s*@(\S+):");
            var cspk = "Nobody";
            await foreach (var x in source)
            {
                var line = x.Trim();
                var match = spk.Match(line);
                if (match.Success)
                {
                    cspk= match.Groups[1].Value;
                    line = spk.Replace(line, "");
                }
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var ret = new proto.Payload { Track = "tran", Chunk = { new Item { Key = "spk", S = cspk } } };
                foreach (var s in split.Split(line))
                {
                    if (s == "")
                        continue;
                    if (plus.IsMatch(s))
                    {
                        ret.Chunk.Add(
                            new proto.Item { Key = "txt", S = s, Tags = { "+" } }
                            );
                    }
                    else
                    {
                        ret.Chunk.Add(
                            new proto.Item { Key = "txt", S = s }
                            );
                    }
                }
                yield return ret;
            }
        }

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

        internal static IEnumerable<TextItem> ToTextItem(this IEnumerable<proto.Payload> source, string dummyPrefix)
        {
            var toNotEval = new string[] { "noise", "+" };
            int block = 0;
            var cSpeaker = "nobody";
            
            foreach (var item in source)
            {
                var speaker = item.Chunk.FirstOrDefault(x => x.Key == "spk", new Item { S = cSpeaker }).S;
                var cItem = new TextItem { Block =block, Speaker=speaker, Text = "#!" + dummyPrefix + block.ToString("000000000") };
                foreach ( var i in item.Chunk)
                {
                    
                    if (i.Key == "spk")
                    {
                        cSpeaker = i.S;
                        cItem.Items.Add(i);
                        continue;
                    }
                    if (i.Key == "txt" && i.Tags.Intersect(toNotEval).Count()==0)
                    {
                        if (cItem.Text.StartsWith("#!"))
                        {
                            cItem.Text = i.S;
                            cItem.Speaker = cSpeaker;
                            cItem.Items.Add(i);
                        }
                        else
                        {
                            yield return cItem;
                            cItem = new TextItem { Block = block, Text = i.S, Speaker = cSpeaker };
                            cItem.Items.Add(i);
                        }
                        continue;
                    }
                    cItem.Items.Add(i);
                }
                if (!cItem.Text.StartsWith("#!"))
                {
                    yield return cItem;
                }
                block++;
            }
        }
        

        

        internal static IEnumerable<Alignment> ToAlignment(this IEnumerable<AlignedTextItem> src)
        {
            foreach (var item in src)
            {
                switch (item.Match)
                {
                    case DTWMatchType.hit:
                    case DTWMatchType.sub:
                        yield return new Alignment { Match = item.MatchString, 
                            Ref = item.Reference.ItemText, Res = item.Result.ItemText,
                            Sref= item.Reference.Speaker, Sres = item.Result.Speaker
                        };
                        break;
                    case DTWMatchType.ins:
                        yield return new Alignment { Match = item.MatchString, Ref = "", Res = item.Result.ItemText,
                            Sref = "", Sres = item.Result.Speaker
                        };
                        break;
                    case DTWMatchType.del:
                        yield return new Alignment { Match = item.MatchString, Ref = item.Reference.ItemText, Res = "" ,
                            Sref = item.Reference.Speaker, Sres = ""};
                        break;
                }
            }
        }

        private static void GetResults(this WordScore score)
        {
            if (score.Count == 0)
            {
                score.Accuracy = 0.0f;
                score.Correctness = 0.0f;
                return;
            }


            if (score.Hits < score.Insertions)
                score.Accuracy = 0.0f;
            else
                score.Accuracy = (float)(((double)(score.Hits - score.Insertions)) / (double)(score.Count));

            score.Correctness = (float)(((double)(score.Hits)) / (double)(score.Count));
        }

        internal static IEnumerable<AlignBlock> WithEvaluation(this IEnumerable<AlignBlock> alignBlocks)
        {
            foreach (var alignBlock in alignBlocks)
            {
                var score = new WordScore
                {
                    Count = 0,
                    Deletions = 0,
                    Insertions = 0,
                    Hits = 0,
                    Substitutions = 0
                };

                foreach (var item in alignBlock.Alignment)
                {
                    switch (item.Match)
                    {
                        case "hit":
                            score.Count++;
                            score.Hits++;
                            break;
                        case "ins":
                            score.Insertions++;
                            break;
                        case "del":
                            score.Deletions++;
                            score.Count++;
                            break;
                        case "sub":
                            score.Substitutions++;
                            score.Count++;
                            break;
                    }
                }
                score.GetResults();
                alignBlock.Wscore = score;
                yield return alignBlock;
            }
        }

        internal static void GetResults(this DiarScore score)
        {
            if (score.Count == 0)
            {
                score.Recall = 0.0f;
                score.Precision = 0.0f;
                score.Frate = 0.0f;
            }
            else
            {
                score.Recall = (float)((double)(score.Hits) / (double)score.Count);
                if ((score.Hits + score.Insertions) == 0)
                {
                    score.Precision = 0.0f;
                }
                else
                {
                    score.Precision = (float)((double)(score.Hits) / (double)((score.Hits) + score.Insertions));
                }
                if ((score.Recall + score.Precision) == 0)
                {
                    score.Frate = 0.0f;
                }
                else
                {
                    score.Frate = (2 * score.Recall * score.Precision) / (score.Recall + score.Precision);
                }
            }
            if (score.ClusterCount == 0)
            {
                score.ClusterPurity = 0.0f;
            }
            else
            {
                score.ClusterPurity = (float)((double)(score.ClusterMatch) / (double)score.ClusterCount);
            }


        }
        internal static IEnumerable<AlignBlock> ToAlignBlocks(this List<List<AlignedTextItem>> alignBlocks)
        {
            foreach(var ab in alignBlocks)
            {
                var ret = new AlignBlock();
                foreach (var a in ab.ToAlignment())
                {
                    ret.Alignment.Add(a);
                }
                yield return ret;
            }
            
        }

        public static Evaluation Evaluate(this IEnumerable<AlignBlock> alignBlocks)
        {

            var ds = new DiarScore();
            var ws = new WordScore();
            var ret = new Evaluation {  Wscore = ws, Dscore = ds};

            var speakerMap = new Dictionary<string, string>();
            var ref2res = new Dictionary<string, Dictionary<string, long>>();

            string cRef = null;
            string cRes = null;

            foreach (var alignBlock in alignBlocks.WithEvaluation())
            {
                alignBlock.Dscore = new DiarScore();
                ws.Count += alignBlock.Wscore.Count;
                ws.Hits += alignBlock.Wscore.Hits;
                ws.Substitutions += alignBlock.Wscore.Substitutions;
                ws.Deletions += alignBlock.Wscore.Deletions;
                ws.Insertions+= alignBlock.Wscore.Insertions;
                var refS = alignBlock.Alignment.Where(x => x.Sref != "").Select(x => x.Sref).Distinct().ToArray();
                
                if (refS.Length != 1)
                {
                    throw new Exception("Something wrong, bad no ref speakers");
                }
                alignBlock.Speaker = refS[0];

                string pSpeaker = null;
                foreach(var speaker in alignBlock.Alignment.Where(x => x.Sres != "").Select(x => x.Sres))
                {
                    if(speaker!=pSpeaker)
                        alignBlock.Cluster.Add(speaker);
                    pSpeaker = speaker;
                }
                foreach (var a in alignBlock.Alignment)
                {
                    if(a.Sres!="")
                        cRes= a.Sres;
                    if (a.Sref != "")
                        cRef = a.Sref;
                    if (cRef == null || cRes == null)
                        continue;
                    if (a.Match == "ins")
                        continue;
                    
                    if (ref2res.ContainsKey(cRef))
                        if (ref2res[cRef].ContainsKey(cRes))
                            ref2res[cRef][cRes]++;
                        else
                            ref2res[cRef][cRes] = 1;
                    else
                        ref2res[cRef] = new Dictionary<string, long>() { { cRes, 1 } };
                }
                ret.Blocks.Add(alignBlock);
            }
            ws.GetResults();

            var res2ref = new Dictionary<string, string>();
            var resCount = new Dictionary<string, long>();
            foreach (var kv in ref2res)
            {
                var refL = kv.Key;
                var resL = kv.Value.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
                var resLCount = kv.Value.Aggregate((l, r) => l.Value > r.Value ? l : r).Value;

                if (!resCount.ContainsKey(resL))
                {
                    resCount[resL] = resLCount;
                    res2ref[resL] = refL;
                }
                else
                {
                    if (resLCount > resCount[resL])
                    {
                        resCount[resL] = resLCount;
                        res2ref[resL] = refL;
                    }
                }

            }

            
            foreach (var kv in res2ref)
            {
                //ds.ClusterCount += (UInt32)ref2res[kv.Value].Values.Sum();
                //ds.ClusterMatch += (UInt32)ref2res[kv.Value][kv.Key];
                ds.SpeakerMap.Add(kv.Key, kv.Value);
            }
            

            //Add per block results
            var pRefSpeaker = "";
            var pResSpeaker = "";
            foreach(var alignBlock in ret.Blocks)
            {
                alignBlock.Dscore = new DiarScore();
                bool refSpkChange = alignBlock.Speaker != pRefSpeaker;
                pRefSpeaker = alignBlock.Speaker;
                bool resSpkChange = false;
                if (alignBlock.Cluster.Count > 0)
                {
                    resSpkChange = pResSpeaker != alignBlock.Cluster[0];
                    pResSpeaker = alignBlock.Cluster[^1];
                    alignBlock.Dscore.Insertions +=(uint)alignBlock.Cluster.Count()-1;
                }

                if(refSpkChange && resSpkChange)
                {
                    alignBlock.Dscore.Count++;
                    alignBlock.Dscore.Hits++;
                }
                else
                {
                    if (refSpkChange)
                    {
                        alignBlock.Dscore.Count++;
                        alignBlock.Dscore.Deletions++;
                    }
                    if (resSpkChange)
                    {
                        alignBlock.Dscore.Insertions++;
                    }
                }

                foreach (var a in alignBlock.Alignment)
                {
                    if (a.Sres == "")
                        continue;
                    alignBlock.Dscore.ClusterCount++;
                    if (alignBlock.Speaker == res2ref.GetValueOrDefault(a.Sres, a.Sres))
                    {
                        alignBlock.Dscore.ClusterMatch++;
                    }
                }
                ds.Count += alignBlock.Dscore.Count;
                ds.Hits+= alignBlock.Dscore.Hits;
                ds.Deletions += alignBlock.Dscore.Deletions;
                ds.Insertions+= alignBlock.Dscore.Insertions;
                ds.ClusterCount+= alignBlock.Dscore.ClusterCount;
                ds.ClusterMatch += alignBlock.Dscore.ClusterMatch;
                alignBlock.Dscore.GetResults();
            }

            ds.GetResults();



            return ret;


        } 

        public static async Task<Evaluation> AlignWith(this IAsyncEnumerable<proto.Payload> source, 
            IAsyncEnumerable<proto.Payload> reference, string SplitOption, string PlusOption)
        {
            Regex split = new Regex(SplitOption);
            Regex plus = new Regex(PlusOption);

            //Expecting ordered in blocks
            var rfr = await reference.ViaReLabelAndSplit(split, plus).ViaLabelMerge().ToArrayAsync();
            var src = await source.ViaReLabelAndSplit(split, plus).ViaLabelMerge().ToArrayAsync();

            var aligment = rfr.ToTextItem("ref").ToList().AlignWith(src.ToTextItem("res").ToList(), true, 1000)
                .ToAlignBlocks().Evaluate();
            return aligment;

        }
        internal static IEnumerable<string> ToHtmlStrings(this AlignBlock block, Dictionary<string,string> speakerMap)
        {
            
            
            if (block.Wscore != null)
            {
                yield return $"<BR>Score: {string.Format(CultureInfo.InvariantCulture, "{0:0.00} ({1:0.00}) [H={2}, D={3}, S={4}, I={5}, N={6}]", 
                    100 * block.Wscore.Correctness, 100 * block.Wscore.Accuracy, block.Wscore.Hits, 
                    block.Wscore.Deletions, block.Wscore.Substitutions, block.Wscore.Insertions, block.Wscore.Count)}";
            }
            if (block.Speaker != "")
            {
                yield return $"<BR>Speaker: {block.Speaker}";
            }
            if (block.Cluster.Count != 0)
            {
                var spk = block.Cluster.AsEnumerable();

                if (speakerMap != null) {
                    spk = spk.Select(x => speakerMap.GetValueOrDefault(x,x));
                }
                yield return $"<BR>Cluster: {string.Join(",",spk)}";
                
            }
            if (block.Dscore != null)
            {
                yield return "<BR>Diar: " + string.Format(CultureInfo.InvariantCulture, "R = {0:0.00} P = {1:0.00} F = {2:0.00} C= {3:0.00} [H={4}, D={5}, I={6}, N={7}, M={8}, C={9}]",
                    100 * block.Dscore.Recall,
                    100 * block.Dscore.Precision,
                    100 * block.Dscore.Frate,
                    100 * block.Dscore.ClusterPurity,
                    block.Dscore.Hits, block.Dscore.Deletions, block.Dscore.Insertions, block.Dscore.Count, block.Dscore.ClusterMatch, block.Dscore.ClusterCount);
            }
            
            string table1 = "<TD align=\"left\"><B>REF:</B></TD>"; ;
            string table2 = "<TD align=\"left\"><B>RES:</B></TD>"; ;
            foreach (var a in block.Alignment)
            {
                var bgcolor = "style='";
                if (speakerMap != null && speakerMap.GetValueOrDefault(a.Sres,a.Sres)!=block.Speaker)
                {
                    bgcolor = "style='background-color:#ccffcc;";
                }
                
                switch (a.Match)
                {
                    case "hit":
                        table1 += $"<TD align=\"left\"><B>" + WebUtility.HtmlEncode(a.Ref) + "</B></TD>";
                        table2 += $"<TD align=\"left\"><B {bgcolor}'>" + WebUtility.HtmlEncode(a.Res) + "</B></TD>";
                        break;
                    case "sub":
                        table1 += $"<TD align=\"left\"><B style='color:red;'>" + WebUtility.HtmlEncode(a.Ref) + "</B></TD>";
                        table2 += $"<TD align=\"left\"><B {bgcolor}color:red;'>" + WebUtility.HtmlEncode(a.Res) + "</B></TD>";
                        break;
                    case "ins":
                          table1 += $"<TD align=\"left\"><B style='color:blue;'>" + " " + "</B></TD>";
                          table2 += $"<TD align=\"left\"><B {bgcolor}color:blue;'>" + WebUtility.HtmlEncode(a.Res) + "</B></TD>";
                        break;
                    case "del":
                        table1 += $"<TD align=\"left\"><B style='color:blue;'>" + WebUtility.HtmlEncode(a.Ref) + "</B></TD>";
                        table2 += $"<TD align=\"left\"><B {bgcolor}color:blue;'>" + " " + "</B></TD>";
                        break;
                }
            }
            yield return  "<TABLE BORDER=0>";
            if (block.Alignment.Count > 0)
            {
                yield return  $"<TR>{table1}";
                yield return  $"<TR>{table2}";
            }
            yield return "</TABLE>";

        }
        public static IEnumerable<string> ToHtmlStrings(this Evaluation eval)
        {
            yield return $"<HTML><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"><TITLE>ASR results {eval.Id}</TITLE>";
            yield return  $"<BR>ID: {eval.Id}<BR>";
            if (eval.Speed != null)
            {
                yield return  $"<BR>Speed: {String.Format("{0:0.00}", eval.Speed.Factor)}";
                yield return  $"<BR>Duration: {TimeSpan.FromTicks((long)eval.Speed.StreamDuration)}";
            }

            yield return "<BR>Score: " + string.Format(CultureInfo.InvariantCulture, "{0:0.00} ({1:0.00}) [H={2}, D={3}, S={4}, I={5}, N={6}]", 
                100 * eval.Wscore.Correctness, 100 * eval.Wscore.Accuracy, eval.Wscore.Hits, 
                eval.Wscore.Deletions, eval.Wscore.Substitutions, eval.Wscore.Insertions, eval.Wscore.Count);

            if (eval.Dscore != null)
            {
                yield return  "<BR>Diar: " + string.Format(CultureInfo.InvariantCulture, "R = {0:0.00} P = {1:0.00} F = {2:0.00} C= {3:0.00} [H={4}, D={5}, I={6}, N={7}, M={8}, C={9}]",
                    100 * eval.Dscore.Recall,
                    100 * eval.Dscore.Precision,
                    100 * eval.Dscore.Frate,
                    100 * eval.Dscore.ClusterPurity,
                    eval.Dscore.Hits, eval.Dscore.Deletions, eval.Dscore.Insertions, eval.Dscore.Count, eval.Dscore.ClusterMatch, eval.Dscore.ClusterCount);

                if (eval.Dscore.SpeakerMap != null)
                {
                    var spk1 = "<TD align=\"left\"><B>REF:</B></TD>";
                    var spk2 = "<TD align=\"left\"><B>RES:</B></TD>";
                    foreach (var s in eval.Dscore.SpeakerMap)
                    {
                        spk1 += $"<TD align=\"center\"><B>{s.Value}</B></TD>";
                        spk2 += $"<TD align=\"center\"><B>{s.Key}</B></TD>";
                    }
                    yield return "<TABLE BORDER=1>";
                    yield return $"<TR>{spk1}";
                    yield return $"<TR>{spk2}";
                    yield return "</TABLE>";
                }
                yield return  "<HR>";
                foreach (var block in eval.Blocks)
                {
                    foreach(var chunk in block.ToHtmlStrings(eval.Dscore?.SpeakerMap.ToDictionary()))
                    {
                        yield return chunk;
                    }
                    yield return "<HR>";
                }
                yield return  "</HTML>";
            }

        }
    }
}
