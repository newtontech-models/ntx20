# NTX20 GRPC communication protocol
NTX20 services are implemented as a bidirectional flow of  [Protobuf 3](https://protobuf.dev/programming-guides/proto3/) Payload messages defined in [engine.proto](https://gitlab.com/nanotrix-public/ntx20/-/blob/0.2.0/ntx20.api.proto/ntx/core20/engine.proto).

## Authorization
Only basic authorization is supported, i.e. set the grpc header:
```
Authorization: Basic base64_encode(username:password)
```
## Task selection
Set the grpc header "service" e.g.:
```
service: atran-cz-openlex
```
or with specific version:
```
service: atran-cz-openlex:2023.11.1-spc
```
## General message flow
Client starts by sending configuration message and continues with sending payload. At the same time client receives configuration confirmation message followed by the stream of payload messages. Both the input/output grpc streams are multiplexed, i.e. they are composed of several track streams and every track consists of several chunks of data specified by Item message.  Every application type (atran, vad, ppc, pnc, diar, etc.) has defined specific message flow.
```
message Payload {
    repeated Item chunk = 1;
    string track = 2;
}
```
```
message Item {
  string key = 1;

  //oneof
  string type = 2;
  repeated Item m = 3;
  string s = 4;
  int64 i = 5;
  float f = 6;
  double d = 7;
  Tensor t = 8;
  bytes b = 9;
  repeated string tags = 10;
  map<string,string> labels = 11;
} 
```

# ATRAN application
ATRAN application consumes audio signal (aud track) and produces voice activity detection (vad), speech to text conversion (v2t), postprocessing of text (ppc) and automatic punctuation (pnc) tracks. 
## Configuration message
Configuration message is a Payload message with following items/keys: 
### audio-format
is one of folowing string values (type="s"):
- auto:0 - automatic audio format/codec detection, zero means automatic probe size in bytes
- pcm:pcmFormat:sampleFormat:sampleRate:channelLayout, where 
    - pcmFormats={"s16le", "alaw", "mulaw"}
    - sampleFormats={ "i2" }
    - sampleRates = { "8000", "16000", "32000", "48000", "96000", "11025", "22050", "44100" }
    - channelLayouts = { "mono", "stereo" }
### audio-channel:
is one of folowing string values (type="s"): 
- downmix - merge stereo channel to mono
- left - select left
- right - select right
### features:
change default pipeline settings - string value (type="s"), coma separated, e.g. novad,lookahead:
- lookahead: enable lookahead, i.e. partial hypotheses
- latency: enable latency mode
- novad: disable voice activity detector
- noppc: disable postprocessing
- nopnc: disable punctuation
### lexicon:
custom lexicon is of type="m", i.e. array of strings (output symbols), optionally with pronunciation hints encoded as labels.
```
{
    "key": "lexicon",
    "type": "m",
    "m": [
        {
            "type": "s",
            "s": "B2",
            "labels": {
                "hint": "b two"
            }
        },
        {
            "type": "s",
            "s": "B2",
            "labels": {
                "hint": "be two"
            }
        },
        {
            "type": "s",
            "s": "B2 bomber"
        }
    ]
}

```
## Input stream
Atran accepts only single audio track on the input side, i.e. stream of Payload messages where track="aud". Every Payload message should contain one chunk of raw audio data, i.e. item of type="b". Recommended size is 4kB.
## Output stream
consist of several tracks:
### VAD
voice activity detection track is a stream of timestamps (key="ts", values in miliseconds, type="d") and text (key="txt", type="s", values are on/off)
### V2T
voice to text track is a stream of timestamps (key="ts", values in miliseconds, type="d") and text (key="txt", type="s", values are words)
### PPC
postprocessing track is a stream of timestamps (key="ts", values in miliseconds, type="d") and text (key="txt", type="s", values are words)
### PNC
postprocessing track is a stream of timestamps (key="ts", values in miliseconds, type="d") and text (key="txt", type="s", values are words)

## Item tags
output items should contain following tags:
- *noise* marks non-speech txt item
- *la* item belongs to partial lookahed hypothesis
- *sos* is start of sentence txt item
- *\+* is txt item not pronunciated by speaker, e.g. space, comma, dot.



