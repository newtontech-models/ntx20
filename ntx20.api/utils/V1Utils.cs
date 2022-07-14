using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading.Tasks;

namespace ntx20.api.utils
{
    public static class V1Utils
    {

     
        

        public static proto.Payload ToV2(this proto.legacy.v2t.engine.Events events, string track)
        {
            var ret = new proto.Payload { Track = track };

            foreach (var e in events.Events_)
            {
                var v2 = e.ToV2();
                if (v2 != null)
                {
                    ret.Chunk.Add(e.ToV2());
                }
            }
            return ret;
        }

        private static proto.Item ToV2(this proto.legacy.v2t.engine.Event.Types.Label _label)
        {
            return _label.LabelCase switch
            {
                proto.legacy.v2t.engine.Event.Types.Label.LabelOneofCase.Item => new proto.Item { Key = "txt", S = _label.Item },
                proto.legacy.v2t.engine.Event.Types.Label.LabelOneofCase.Plus => new proto.Item { Key = "txt", S = _label.Plus, Tags = { "+" } },
                proto.legacy.v2t.engine.Event.Types.Label.LabelOneofCase.Noise => new proto.Item { Key = "txt", S = _label.Plus, Tags = { "noise" } },
                _ => throw new NotImplementedException()
            };
        }

        private static proto.Item ToV2(this proto.legacy.v2t.engine.Event.Types.Meta _meta)
        {
            return _meta.BodyCase switch
            {
                proto.legacy.v2t.engine.Event.Types.Meta.BodyOneofCase.Confidence => new proto.Item { Key = "acnf", F = (float)_meta.Confidence.Value },
                _ => throw new NotImplementedException()
            };
        }

        private static proto.Item ToV2(this proto.legacy.v2t.engine.Event.Types.Timestamp _ts)
        {
            return _ts.ValueCase switch
            {
                proto.legacy.v2t.engine.Event.Types.Timestamp.ValueOneofCase.Timestamp_ => new proto.Item { Key = "ts", D = TimeSpan.FromTicks((long)_ts.Timestamp_).TotalMilliseconds },
                proto.legacy.v2t.engine.Event.Types.Timestamp.ValueOneofCase.Recovery => null,
                _ => throw new NotImplementedException()
            };
        }

        private static proto.Item ToV2(this proto.legacy.v2t.engine.Event _event)
        {
            return _event.BodyCase switch
            {
                proto.legacy.v2t.engine.Event.BodyOneofCase.Audio => new proto.Item {B = _event.Audio.Body },
                proto.legacy.v2t.engine.Event.BodyOneofCase.Label => _event.Label.ToV2(),
                proto.legacy.v2t.engine.Event.BodyOneofCase.Meta => _event.Meta.ToV2(),
                proto.legacy.v2t.engine.Event.BodyOneofCase.Timestamp => _event.Timestamp.ToV2(),
                _ => throw new NotImplementedException()

            };
        }

        public static proto.legacy.v2t.engine.Events ToV1(this proto.Payload payload)
        {
            var ret = new proto.legacy.v2t.engine.Events();
            foreach(var item in payload.Chunk)
            {
                var i = item.ToV1();
                if (i != null)
                {
                    ret.Events_.Add(i);
                }
            }
            return ret;
        }

        private static proto.legacy.v2t.engine.Event ToV1(this proto.Item item)
        {

            return item.Key switch
            {
                "txt" =>
                    item.Tags.Contains("noise")
                    ? new proto.legacy.v2t.engine.Event { Label = new proto.legacy.v2t.engine.Event.Types.Label { Noise = item.S } } :
                    item.Tags.Contains("+")
                    ? new proto.legacy.v2t.engine.Event { Label = new proto.legacy.v2t.engine.Event.Types.Label { Plus = item.S } } :
                    new proto.legacy.v2t.engine.Event { Label = new proto.legacy.v2t.engine.Event.Types.Label { Item = item.S } },
                "ts" =>
                    new proto.legacy.v2t.engine.Event { Timestamp = new proto.legacy.v2t.engine.Event.Types.Timestamp { Timestamp_ = (ulong)TimeSpan.FromMilliseconds(item.D).Ticks } },
                "acnf" =>
                    new proto.legacy.v2t.engine.Event
                    {
                        Meta = new proto.legacy.v2t.engine.Event.Types.Meta
                        {
                            Confidence = new proto.legacy.v2t.engine.Event.Types.Meta.Types.Confidence { Value = item.F }
                        }
                    },
                _ => null
            };
        }

        
    }
}
