using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.utils
{
    public static class Tensor
    {
        public static List<proto.Tensor> Chunk(this proto.Tensor t, int size)
        {
            var ret = new List<proto.Tensor>();
            int x = 0;
            var b = t.Data.ToArray();
            while (x < t.Data.Length)
            {
                int cnt = Math.Min(x + size, t.Data.Length)-x;


                ret.Add(new proto.Tensor
                {
                    Data = Google.Protobuf.ByteString.CopyFrom(b, x, cnt)
                });
                
                x += size;
            }
            return ret;
        }

        public static List<proto.Tensor> Split(this proto.Tensor t)
        {
            

            if (t.Shape == null || t.Shape.Shape.Count < 2)
            {
                return  new List<proto.Tensor> {t};
            }

            return t.Chunk(t.Data.Length / (int)t.Shape.Shape[0]);
        }

        public static float[] ToF4(this proto.Tensor t)
        {
            if(t.Data.Length % sizeof(float) > 0)
            {
                throw new Exception("Can't convert tensor to float");
            }

            var b = t.Data.ToByteArray();
            var ret = new float[t.Data.Length / sizeof(float)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static byte[] ToU1(this proto.Tensor t)
        {
            return t.Data.ToArray();
        }

        public static bool[] ToB1(this proto.Tensor t)
        {
            return t.Data.ToArray().Select(x => x != 0).ToArray();
        }

        public static sbyte[] ToI1(this proto.Tensor t)
        {
            var b =  t.Data.ToArray();
            var ret = new sbyte[t.Data.Length / sizeof(sbyte)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static short[] ToI2(this proto.Tensor t)
        {
            var b = t.Data.ToArray();
            var ret = new short[t.Data.Length / sizeof(short)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static int[] ToI4(this proto.Tensor t)
        {
            var b = t.Data.ToArray();
            var ret = new int[t.Data.Length / sizeof(int)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static long[] ToI8(this proto.Tensor t)
        {
            var b = t.Data.ToArray();
            var ret = new long[t.Data.Length / sizeof(long)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static double[] ToF8(this proto.Tensor t)
        {
            var b = t.Data.ToArray();
            var ret = new double[t.Data.Length / sizeof(double)];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public static string Print(this proto.Tensor t)
        {

            var str = (t.Dtype switch  {
                "" => t.Data.ToArray().Select(x => x.ToString()),
                "u1" => t.Data.ToArray().Select(x => x.ToString()),
                "i1" => t.ToI1().Select(x => x.ToString()),
                "i2" => t.ToI2().Select(x => x.ToString()),
                "i4" => t.ToI4().Select(x => x.ToString()),
                "i8" => t.ToI1().Select(x => x.ToString()),
                "f4" => t.ToF4().Select(x => x.ToString()),
                "f8" => t.ToF8().Select(x => x.ToString()),
                _ => throw new NotImplementedException($"unsuported sample format { t.Dtype}"),
            });
            return "[" + string.Join(",",str)+ "]";
        }
    }
            
}
