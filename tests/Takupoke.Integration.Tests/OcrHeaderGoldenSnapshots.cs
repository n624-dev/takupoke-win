using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Infrastructure.Parsing;
namespace Takupoke.Integration.Tests;

// Only independently generated fictional fixture outputs. Captured from the
// unchanged production OcrLineLabels, including exact source IDs/bounds/order.
// Embedded in test source so ordinary repository tests need no external DLL.
internal static class OcrHeaderGoldenSnapshots
{
    private sealed record Snapshot(string[] Labels, long Work);
    private static readonly Dictionary<string, Snapshot> Values = JsonSerializer.Deserialize<Dictionary<string, Snapshot>>(Json)!;
    internal static (string[] Labels, long Work) For(PdfPageLayout page)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)));
        var snapshot = Values[key]; // Unknown/mutated fixtures must fail loudly.
        return (snapshot.Labels, snapshot.Work);
    }
    private const string Json = """
{
  "17406da569ed5ed44ee58c9d6a5cf5549e669cae4dc29b0823bd3d65836a024b": {
    "Labels": [
      "\u6708\u66DC\u65E5|p1s0,p1s1,p1s2|{\u0022X\u0022:180,\u0022Y\u0022:12,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 16876651
  },
  "a842097d7ff240a656ccac212482acbc914ffbc84c7a6b0ec5830dc10df852b3": {
    "Labels": [
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 14205
  },
  "ec6b4be93a0fc58b94b77e0d142c498f6add09049ad19d1e2209b2eb20689834": {
    "Labels": [
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 13926
  },
  "2492dcbdf040cf5b7b57b0d7de36194b651b9e98da9fe651e7a2855e831f2a83": {
    "Labels": [
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 13914
  },
  "2fddb1d0ee9176e5e021b8a2e3e97719edc021a1eb08053aa3855dfdfe1fdb1a": {
    "Labels": [
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 13597
  },
  "06bee3965f100417d3cb92a100ca733fb57aa79b6e510a003bf49b7d78cad395": {
    "Labels": [
      "\u6708\u66DC\u65E5|p1s0,p1s1,p1s2|{\u0022X\u0022:180,\u0022Y\u0022:12,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u706B\u66DC|p1s3,p1s4|{\u0022X\u0022:180,\u0022Y\u0022:18,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u91D1|p1s5|{\u0022X\u0022:180,\u0022Y\u0022:24,\u0022Width\u0022:4,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s6,p1s7,p1s8,p1s9,p1s10,p1s11,p1s12,p1s13,p1s14,p1s15|{\u0022X\u0022:180,\u0022Y\u0022:30,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9\u670812\u65E5|p1s16,p1s17,p1s18,p1s19,p1s20|{\u0022X\u0022:180,\u0022Y\u0022:36,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034/9/12|p1s21,p1s22,p1s23,p1s24,p1s25,p1s26,p1s27,p1s28,p1s29|{\u0022X\u0022:180,\u0022Y\u0022:42,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "9-12|p1s30,p1s31,p1s32,p1s33|{\u0022X\u0022:180,\u0022Y\u0022:48,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "1-1|p1s34,p1s35,p1s36|{\u0022X\u0022:180,\u0022Y\u0022:54,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "5_ES|p1s37,p1s38,p1s39,p1s40|{\u0022X\u0022:180,\u0022Y\u0022:60,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_2|p1s41,p1s42,p1s43,p1s44|{\u0022X\u0022:180,\u0022Y\u0022:66,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2\u5E74|p1s45,p1s46|{\u0022X\u0022:180,\u0022Y\u0022:72,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "2034\u5E749\u670812\u65E5|p1s47,p1s48,p1s49,p1s50,p1s51,p1s52,p1s53,p1s54,p1s55,p1s56|{\u0022X\u0022:180,\u0022Y\u0022:78,\u0022Width\u0022:49,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "AI_1|p1s57,p1s58,p1s59,p1s60,p1s61,p1s62|{\u0022X\u0022:180,\u0022Y\u0022:84,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|True",
      "\u524D\u671F|p1s63,p1s64|{\u0022X\u0022:180,\u0022Y\u0022:90,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5F8C\u671F|p1s65,p1s66|{\u0022X\u0022:180,\u0022Y\u0022:96,\u0022Width\u0022:9,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s67,p1s68,p1s69,p1s70,p1s71,p1s72|{\u0022X\u0022:180,\u0022Y\u0022:102,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "2034\u5E74\u5EA6|p1s73,p1s74,p1s75,p1s76,p1s77,p1s78|{\u0022X\u0022:180,\u0022Y\u0022:108,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u8A66\u9A13\u8FD4\u5374\u6642\u9593\u5272|p1s79,p1s80,p1s81,p1s82,p1s83,p1s84,p1s85|{\u0022X\u0022:180,\u0022Y\u0022:114,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u5B9A\u671F\u8A66\u9A13\u6642\u9593\u5272|p1s86,p1s87,p1s88,p1s89,p1s90,p1s91,p1s92|{\u0022X\u0022:180,\u0022Y\u0022:120,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u901A\u5E38\u6642\u9593\u5272|p1s93,p1s94,p1s95,p1s96,p1s97|{\u0022X\u0022:180,\u0022Y\u0022:126,\u0022Width\u0022:24,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u6642\u9650\u76EE|p1s98,p1s99,p1s100,p1s101|{\u0022X\u0022:180,\u0022Y\u0022:132,\u0022Width\u0022:19,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "1\u30FB2\u6642\u9650\u9023\u7D9A|p1s102,p1s103,p1s104,p1s105,p1s106,p1s107,p1s108|{\u0022X\u0022:180,\u0022Y\u0022:138,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "7\u301C8\u6642\u9650\u9023\u7D9A|p1s109,p1s110,p1s111,p1s112,p1s113,p1s114,p1s115|{\u0022X\u0022:180,\u0022Y\u0022:144,\u0022Width\u0022:34,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s116,p1s117,p1s118,p1s119,p1s120,p1s121,p1s122,p1s123,p1s124|{\u0022X\u0022:180,\u0022Y\u0022:150,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\u301C9:20|p1s125,p1s126,p1s127,p1s128,p1s129,p1s130,p1s131,p1s132,p1s133|{\u0022X\u0022:180,\u0022Y\u0022:156,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30\uFF5E9:20|p1s134,p1s135,p1s136,p1s137,p1s138,p1s139,p1s140,p1s141,p1s142|{\u0022X\u0022:180,\u0022Y\u0022:162,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u79D1\u76EE|p1s143,p1s144,p1s145|{\u0022X\u0022:180,\u0022Y\u0022:168,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u54E1|p1s148,p1s149,p1s150|{\u0022X\u0022:180,\u0022Y\u0022:174,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u6559\u5BA4|p1s153,p1s154,p1s155|{\u0022X\u0022:180,\u0022Y\u0022:180,\u0022Width\u0022:14,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "8:30~9:20|p1s162,p1s163,p1s164,p1s165,p1s166,p1s167,p1s168,p1s169,p1s170|{\u0022X\u0022:200,\u0022Y\u0022:186,\u0022Width\u0022:44,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False",
      "\u4EE4\u548C16\u5E74\u5EA6|p1s175,p1s176,p1s177,p1s178,p1s179,p1s180|{\u0022X\u0022:190,\u0022Y\u0022:192,\u0022Width\u0022:29,\u0022Height\u0022:3,\u0022Valid\u0022:true}|False"
    ],
    "Work": 14219
  },
  "0919331259cf83856c862c84aebdbf43f4153418e3dd1fcf551a198afa5eb088": {
    "Labels": [],
    "Work": 912
  }
}
""";
}
