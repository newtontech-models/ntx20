using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ntx.api.utils
{
    
    //this code is just a proof of concept

    internal class State : IEquatable<State>
    {
        readonly int reference;
        readonly int result;
        public State(int reference, int result)
        {
            this.reference = reference;
            this.result = result;
        }

        public int Reference { get { return reference; } }
        public int Result { get { return result; } }
        public int Block { get; set; }

        public override int GetHashCode()
        {
            return reference.GetHashCode() ^ result.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            return Equals((State)obj);
        }

        public bool Equals(State other)
        {
            return other.reference.Equals(reference) && other.result.Equals(result);
        }
        public float Fscore { get; set; }
        public float Bscore { get; set; }
        public float Score { get { return Fscore + Bscore; } }
        public BackPtr Bp { get; set; }
    }
    internal class BackPtr
    {
        public int Sub { get; set; }
        public int Del { get; set; }
        public int Ins { get; set; }
        public float Score { get; set; }
        public BackPtr From { get; set; }
        public int RefPos { get; set; }
        public int ResPos { get; set; }
    }
    public enum DTWMatchType
    {
        hit,
        sub,
        ins,
        del
    }
    public class TextItem
    {
        public string Text { get; set; }
        public int Index { get; set; }
        public int Block { get; set; }
        public string Speaker { get; set; }
        public string Value { get; set; }
        public double? Start { get; set; }
        public double? End { get; set; }
        public bool Eval { get; set; }
    }
    public class AlignedItem
    {
        public TextItem Reference { get; set; }
        public TextItem Result { get; set; }
        public DTWMatchType Match { get; set; }
    }
    public static class DTW
    {

        
        static float GetScore(int refDist, int resDist)
        {

            return GetScore(refDist, resDist, out int sub, out int del, out int ins);
        }
        static float GetScore(int refDist, int resDist, out int sub, out int del, out int ins)
        {
            int diag = Math.Min(refDist, resDist);
            sub = diag - 1;
            del = refDist - diag;
            ins = resDist - diag;

            return sub + 0.7f*(del + ins);
        }

        static float LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            if (n + m == 0)
                return 0.0f;

            if(s.StartsWith("#!") ^ t.StartsWith("#!"))
            {
                return 1.0f;
            }


            int[,] d = new int[n + 1, m + 1];

            // Step 1
            if (n == 0)
            {
                return 1.0f;
            }

            if (m == 0)
            {
                return 1.0f;
            }

            // Step 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Step 3
            for (int i = 1; i <= n; i++)
            {
                //Step 4
                for (int j = 1; j <= m; j++)
                {
                    // Step 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Step 6
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            // Step 7
            var dist = d[n, m];
            var l = (float)dist / (float)Math.Max(s.Length, t.Length);
            return l;
        }


        static List<AlignedItem> AlignFull(List<TextItem> reference, List<TextItem> result, bool ignorecase ,int nbest)
        {
            List<State> last = new List<State>() { new State(0,0) { Bp = null, Fscore = 0.0f, Bscore = GetScore(reference.Count + 2, result.Count + 2) } };
            for (int resInd = 0; resInd <= result.Count; resInd++)
            {
                string val = resInd==result.Count ? "" : ignorecase ? result[resInd].Text.ToLowerInvariant() : result[resInd].Text;
              
                List<State> current = new List<State>();
                for (int refInd = 0; refInd <= reference.Count; refInd++)
                {
                    
                    string refVal = refInd == reference.Count ? "" : ignorecase ? reference[refInd].Text.ToLowerInvariant() : reference[refInd].Text;

                    var newstate = new State(refInd + 1, resInd + 1) { Fscore = float.MaxValue, Bp = null};

                    foreach (var c in last.Where(x => x.Reference < newstate.Reference))
                    {
                        
                        int refDist = newstate.Reference - c.Reference;
                        int resDist = newstate.Result - c.Result;
                        
                        var leven = LevenshteinDistance(val, refVal);

                        float fscore = GetScore(refDist, resDist, out int sub, out int del, out int ins) + leven;


                        if (c.Fscore + fscore < newstate.Fscore)
                        {
                            newstate.Fscore = c.Fscore + fscore;
                            newstate.Bp = new BackPtr { Del = del, From = c.Bp, Ins = ins, RefPos = refInd, ResPos = resInd, Score = c.Fscore + fscore, Sub = sub };
                        }
                    }
                    if (newstate.Bp != null)
                    {
                        float bscore = GetScore(reference.Count + 2 - newstate.Reference, result.Count + 2 - newstate.Result);
                        newstate.Bscore = bscore;
                        current.Add(newstate);
                    }
                }
                if (current.Count != 0)
                {
                    last.AddRange(current);
                    last = last.OrderBy(x => x.Score).Take(nbest).ToList();
                }

            }
            var bestbp = last.First().Bp;
            List<BackPtr> _list = new List<BackPtr>();
            while (bestbp != null)
            {
                _list.Add(bestbp);
                bestbp = bestbp.From;
            }
            _list.Reverse();
            List<AlignedItem> ret = new List<AlignedItem>();
            foreach (var i in _list)
            {
                int refpos = i.RefPos - i.Sub - i.Del;
                int respos = i.ResPos - i.Sub - i.Ins;

                for (int z = 0; z < i.Ins; z++)
                {
                    ret.Add(new AlignedItem { Match = DTWMatchType.ins, Result = result[respos], Reference = null });
                    respos++;
                }
                for (int z = 0; z < i.Del; z++)
                {
                    ret.Add(new AlignedItem { Match = DTWMatchType.del, Result = null, Reference = reference[refpos] });
                    refpos++;
                }

                for (int z = 0; z < i.Sub; z++)
                {
                    bool split = reference[refpos].Text.StartsWith("#") ^ result[respos].Text.StartsWith("#");
                    split = split & !reference[refpos].Text.StartsWith("#!") & !result[respos].Text.StartsWith("#!");
                    if (split)
                    {
                        ret.Add(new AlignedItem { Match = DTWMatchType.del, Result = result[respos], Reference = null, });
                        ret.Add(new AlignedItem { Match = DTWMatchType.ins, Result = null, Reference = reference[refpos] });
                    }
                    else
                    {
                        ret.Add(new AlignedItem { Match = DTWMatchType.sub, Result = result[respos], Reference = reference[refpos] });
                    }
                    respos++;
                    refpos++;
                }

                if (respos < result.Count && refpos < reference.Count)
                {
                    bool split = reference[refpos].Text.StartsWith("#") ^ result[respos].Text.StartsWith("#");
                    split = split & !reference[refpos].Text.StartsWith("#!") & !result[respos].Text.StartsWith("#!");
                    if (split)
                    {
                        ret.Add(new AlignedItem { Match = DTWMatchType.del, Result = result[respos], Reference = null });
                        ret.Add(new AlignedItem { Match = DTWMatchType.ins, Result = null, Reference = reference[refpos] });
                    }
                    else
                    {
                        ret.Add(new AlignedItem { Match = DTWMatchType.sub, Result = result[respos], Reference = reference[refpos] });
                    }
                }
            }
            return ret;
        }

        static List<AlignedItem> ProcessBlock(List<AlignedItem> block, bool ignorecase, int nbest)
        {
            int sub = block.Count(x => x.Match == DTWMatchType.sub);
            int nonsub = block.Count(x => x.Match != DTWMatchType.sub);

            if (sub == 0)
                return block;

            var reference = block.Where(x => x.Reference != null).Select(x => x.Reference).ToList();
            var result = block.Where(x => x.Result != null).Select(x => x.Result).ToList();
            return AlignFull(reference, result, ignorecase, nbest);
        }
        public static List<AlignedItem> Tune(this List<AlignedItem> alignment, bool ignorecase = true, int nbest = 100)
        {
            var ret = new List<AlignedItem>();
            List<AlignedItem> block = null;
            foreach(var a in alignment)
            {
                if(a.Match== DTWMatchType.hit)
                {
                    if (block != null)
                    {
                        ret.AddRange(ProcessBlock(block,ignorecase,nbest));
                        block = null;
                    }
                    ret.Add(a);
                }else
                {
                    if (block == null)
                        block = new List<AlignedItem>() { a };
                    else
                        block.Add(a);
                }
            }
            if (block != null)
            {
                ret.AddRange(ProcessBlock(block,ignorecase,nbest));
            }
            return ret;
        }
        public static List<AlignedItem> Tune2(this List<AlignedItem> block, bool ignorecase = true, int nbest = 100)
        {
            var reference = block.Where(x => x.Reference != null).Select(x => x.Reference).ToList();
            var result = block.Where(x => x.Result != null).Select(x => x.Result).ToList();
            return AlignFull(reference, result, ignorecase, nbest);
        }
        public static AlignedItem Inverse(this AlignedItem input)
        {

            var ret = new AlignedItem() {  Match= input.Match};
            switch(input.Match)
            {
                case DTWMatchType.del:
                    ret.Match = DTWMatchType.ins;
                    break;
                case DTWMatchType.ins:
                    ret.Match = DTWMatchType.del;
                    break;
            }

            ret.Reference = input.Result;
            ret.Result = input.Reference;
            return ret;            
        }

      

        public static List<List<AlignedItem>> AlignWith(this List<TextItem> reference, List<TextItem> result, bool ignorecase=true,int nbest=100)
        {
            
            Dictionary<string, List<Tuple<int,int>>> refindex = new Dictionary<string, List<Tuple<int, int>>>();
            var lastBlock = -1;
            for (int i = 0; i < reference.Count; i++)
            {
                string val = ignorecase ? reference[i].Text.ToLowerInvariant() : reference[i].Text;
                if(val.StartsWith("#") && !val.StartsWith("#!") && val.Length > 1)
                {
                    val = val.Substring(0, 2);
                }
               
                lastBlock = reference[i].Block;
                if (refindex.ContainsKey(val))
                    refindex[val].Add(new Tuple<int, int>(i, lastBlock));
                else
                    refindex[val] = new List<Tuple<int, int>>() { new Tuple<int, int>(i, lastBlock) };
            }
            refindex["$END"] = new List<Tuple<int, int>>() { new Tuple<int, int>(reference.Count,lastBlock) };

            List<State> last = new List<State>() { new State(0, 0) { Bp = null, Fscore = 0.0f, Bscore = GetScore(reference.Count + 2, result.Count + 2), Block=0 } };
            for (int resInd = 0; resInd <= result.Count; resInd++)
            {

                string val = resInd == result.Count ? "$END" : ignorecase ? result[resInd].Text.ToLowerInvariant() : result[resInd].Text;
                
                if (val.StartsWith("#") && !val.StartsWith("#!") && val.Length > 1)
                {
                    val = val.Substring(0, 2);
                }
                

                if (!refindex.ContainsKey(val))
                    continue;

                List<State> current = new List<State>();
                foreach (var refTuple in refindex[val])
                {
                    var refInd = refTuple.Item1;

                    var newstate = new State(refInd + 1, resInd + 1) { Fscore = float.MaxValue, Bp = null,Block=refTuple.Item2 };

                    foreach (var c in last.Where(x => x.Reference < newstate.Reference))
                    {
                        int refDist = newstate.Reference - c.Reference;
                        int resDist = newstate.Result - c.Result;
                        int blockDist = newstate.Block - c.Block;
                        float blockScore = (float)blockDist / (float)lastBlock;
                        float fscore = GetScore(refDist, resDist, out int sub, out int del, out int ins);

                        if (c.Fscore + fscore <= newstate.Fscore)
                        {
                            newstate.Fscore = c.Fscore + fscore;
                            newstate.Bp = new BackPtr { Del = del, From = c.Bp, Ins = ins, RefPos = refInd, ResPos = resInd, Score = c.Fscore + fscore, Sub = sub };
                        }
                    }
                    if (newstate.Bp != null)
                    {
                        int blockDist = lastBlock - newstate.Block;
                        float blockScore = (float)blockDist / (float)lastBlock;
                        float bscore = GetScore(reference.Count + 2 - newstate.Reference, result.Count + 2 - newstate.Result);
                        newstate.Bscore = bscore;
                        current.Add(newstate);
                    }
                }
                if (current.Count != 0)
                {
                    last.AddRange(current);
                    last = last.OrderBy(x => x.Score).Take(nbest).ToList();
                }
            }
            
            var bestbp = last.First().Bp;
            if (bestbp == null)
            {
                var alter = last.FirstOrDefault(x => x.Bp != null);
                if (alter != null)
                    bestbp = alter.Bp;
            }
            List<BackPtr> _list = new List<BackPtr>();
            while (bestbp != null)
            {
                _list.Add(bestbp);
                bestbp = bestbp.From;
            }
            _list.Reverse();
            List<AlignedItem> ret = new List<AlignedItem>();
            
            foreach (var i in _list)
            {

                var cb = new List<AlignedItem>();
                int refpos = i.RefPos - i.Sub - i.Del;
                int respos = i.ResPos - i.Sub - i.Ins;


                for (int z = 0; z < i.Del; z++)
                {
                    cb.Add(new AlignedItem { Match = DTWMatchType.del, Result = null, Reference = reference[refpos] });
                    refpos++;
                }

                for (int z = 0; z < i.Sub; z++)
                {
                    cb.Add(new AlignedItem { Match = DTWMatchType.sub, Result = result[respos], Reference = reference[refpos] });
                    respos++;
                    refpos++;
                }
                
                for (int z = 0; z < i.Ins; z++)
                {
                    cb.Add(new AlignedItem { Match = DTWMatchType.ins, Result = result[respos], Reference = null });
                    respos++;
                }

                //get n-best or less substitutions
                
                

                ret.AddRange(cb);

                if (respos < result.Count && refpos < reference.Count)
                    ret.Add(new AlignedItem { Match = DTWMatchType.hit, Result = result[respos], Reference = reference[refpos] });

                
            }

            var ret2 = new List<List<AlignedItem>>();
            var cblock = new List<AlignedItem>();
            foreach (var a in ret)
            {
                if (a.Reference != null && a.Reference.Block > ret2.Count)
                {
                    ret2.Add(cblock.Tune());
                    cblock = new List<AlignedItem>();
                }

                cblock.Add(a);
            }

            ret2.Add(cblock.Tune());
            
            return ret2;
        }
    }
}
