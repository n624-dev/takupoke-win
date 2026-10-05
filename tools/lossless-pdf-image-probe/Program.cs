using LosslessImageResearch;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length==0)throw new ArgumentException("Use --controls [optional fictional PDF], or a PDF path to inspect.");
if(args[0]=="--controls"){Console.WriteLine(JsonSerializer.Serialize(Controls.Run(args.Length>1?args[1]:null),new JsonSerializerOptions{WriteIndented=true}));return;}
var a=LosslessImage.Extract(File.ReadAllBytes(args[0]));
Console.WriteLine(JsonSerializer.Serialize(new{a.PdfSha256,a.Width,a.Height,rgbBytes=a.Rgb.Length,rgbSha256=Convert.ToHexStringLower(SHA256.HashData(a.Rgb)),a.ContentSha256,a.RawImageSha256,a.TypedOperations,scope="Stage0 lossless source sample acquisition only. No OCR/formal/gold used in extraction.",OCRCalls=0}));
