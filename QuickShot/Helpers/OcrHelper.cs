using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickShot.Helpers
{
    public static class OcrHelper
    {
        private static Task<T> ToTask<T>(IAsyncOperation<T> asyncOp)
        {
            var tcs = new TaskCompletionSource<T>();
            asyncOp.Completed = new AsyncOperationCompletedHandler<T>((info, status) =>
            {
                if (status == AsyncStatus.Completed)
                    tcs.SetResult(info.GetResults());
                else if (status == AsyncStatus.Error)
                    tcs.SetException(info.ErrorCode);
                else if (status == AsyncStatus.Canceled)
                    tcs.SetCanceled();
            });
            return tcs.Task;
        }

        private static Task ToTask(IAsyncAction asyncAction)
        {
            var tcs = new TaskCompletionSource<bool>();
            asyncAction.Completed = new AsyncActionCompletedHandler((info, status) =>
            {
                if (status == AsyncStatus.Completed)
                    tcs.SetResult(true);
                else if (status == AsyncStatus.Error)
                    tcs.SetException(info.ErrorCode);
                else if (status == AsyncStatus.Canceled)
                    tcs.SetCanceled();
            });
            return tcs.Task;
        }

        private static bool IsCjk(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || // CJK Unified Ideographs
                   (c >= 0x3400 && c <= 0x4DBF) || // CJK Extension A
                   (c >= 0xF900 && c <= 0xFAFF) || // CJK Compatibility
                   (c >= 0x3000 && c <= 0x303F) || // CJK Symbols and Punctuation
                   (c >= 0xFF00 && c <= 0xFFEF);   // Fullwidth Forms
        }

        private static int CountCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsCjk(s[i])) count++;
            }
            return count;
        }

        private static bool IsNoSpaceBefore(char c)
        {
            return c == '.' || c == ',' || c == ':' || c == ';' || c == '!' || c == '?' ||
                   c == ')' || c == ']' || c == '}' || c == '>' || c == '”' || c == '’' ||
                   c == '、' || c == '。' || c == '，' || c == '：' || c == '；' || c == '！' ||
                   c == '？' || c == '）' || c == '】' || c == '》' || c == '％' || c == '%' ||
                   c == '/' || c == '\\';
        }

        private static bool IsNoSpaceAfter(char c)
        {
            return c == '(' || c == '[' || c == '{' || c == '<' || c == '“' || c == '‘' ||
                   c == '（' || c == '【' || c == '《' || c == '/' || c == '\\' || c == '@' ||
                   c == '#' || c == '$' || c == '￥' || c == '~';
        }

        public class MergedLine
        {
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
            public double CenterY { get { return (MinY + MaxY) / 2.0; } }
            public double Height { get { return MaxY - MinY; } }
            public List<OcrWord> Words { get; set; }

            public MergedLine(OcrLine line)
            {
                Words = new List<OcrWord>(line.Words);
                MinX = Words.Min(w => w.BoundingRect.X);
                MaxX = Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                MinY = Words.Min(w => w.BoundingRect.Y);
                MaxY = Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            }

            public void Merge(MergedLine other)
            {
                Words.AddRange(other.Words);
                Words = Words.OrderBy(w => w.BoundingRect.X).ToList();
                MinX = Math.Min(MinX, other.MinX);
                MaxX = Math.Max(MaxX, other.MaxX);
                MinY = Math.Min(MinY, other.MinY);
                MaxY = Math.Max(MaxY, other.MaxY);
            }
        }

        private static List<MergedLine> SortAndMergeLines(IReadOnlyList<OcrLine> rawLines)
        {
            if (rawLines == null || rawLines.Count == 0) return new List<MergedLine>();

            var lines = rawLines.Select(l => new MergedLine(l)).ToList();

            // Merge lines that share the same horizontal row (e.g. superscripts [20], inline buttons, or right-side footnotes)
            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        var a = lines[i];
                        var b = lines[j];

                        // Must NOT overlap horizontally
                        bool horizontalSeparated = (a.MaxX <= b.MinX + 15) || (b.MaxX <= a.MinX + 15);
                        if (!horizontalSeparated) continue;

                        // Vertical overlap test
                        double top = Math.Max(a.MinY, b.MinY);
                        double bottom = Math.Min(a.MaxY, b.MaxY);
                        double overlap = bottom - top;
                        double minH = Math.Min(a.Height, b.Height);

                        if (overlap > 0 && (overlap / minH >= 0.3 || Math.Abs(a.CenterY - b.CenterY) <= 15))
                        {
                            a.Merge(b);
                            lines.RemoveAt(j);
                            mergedAny = true;
                            break;
                        }
                    }
                    if (mergedAny) break;
                }
            }

            return lines.OrderBy(l => l.CenterY).ToList();
        }

        private static string FormatLineWords(List<OcrWord> words, bool isEnglish)
        {
            if (words == null || words.Count == 0) return string.Empty;

            var sorted = words.OrderBy(w => w.BoundingRect.X).ToList();
            var sb = new StringBuilder();

            for (int i = 0; i < sorted.Count; i++)
            {
                var text = sorted[i].Text;
                if (string.IsNullOrEmpty(text)) continue;

                if (sb.Length > 0)
                {
                    char prev = sb[sb.Length - 1];
                    char next = text[0];

                    bool addSpace = true;
                    if (!isEnglish)
                    {
                        bool prevCjk = IsCjk(prev);
                        bool nextCjk = IsCjk(next);

                        if (IsNoSpaceAfter(prev) || IsNoSpaceBefore(next))
                        {
                            addSpace = false;
                        }
                        else if (!prevCjk && !nextCjk)
                        {
                            addSpace = char.IsLetterOrDigit(prev) && char.IsLetterOrDigit(next);
                        }
                        else if (!prevCjk && nextCjk)
                        {
                            addSpace = char.IsLetter(prev);
                        }
                        else if (prevCjk && !nextCjk)
                        {
                            addSpace = char.IsLetter(next);
                        }
                        else
                        {
                            addSpace = false; // CJK + CJK -> no space
                        }
                    }
                    else
                    {
                        if (next == ',' || next == '.' || next == ';' || next == ':' || next == '!' || next == '?' || next == ')' || next == ']' || next == '}')
                            addSpace = false;
                        if (prev == '(' || prev == '[' || prev == '{' || prev == '$' || prev == '~')
                            addSpace = false;
                    }

                    if (addSpace) sb.Append(' ');
                }
                sb.Append(text);
            }
            return sb.ToString();
        }

        private static string PostProcessText(string text, bool isEnglish)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanLines = new List<string>();

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (isEnglish)
                {
                    // Fix citation brackets: "[201" -> "[20]", "[91" -> "[9]", "[241" -> "[24]"
                    line = Regex.Replace(line, @"\[\s*(\d+)[1lI]\s*\]?", "[$1]");
                    line = Regex.Replace(line, @"\[\s*(\d+)\s*\]", "[$1]");
                    line = Regex.Replace(line, @"\[\s*(\d+)\s*\)", "[$1]");

                    // Fix currency "$": "SIOO million" -> "$100 million", "S2 billion" -> "$2 billion", "S250" -> "$250"
                    line = Regex.Replace(line, @"\bS(?=\d)", "$");
                    line = Regex.Replace(line, @"\bSIOO\b", "$100");
                    line = Regex.Replace(line, @"\bSIO\b", "$10");
                    line = Regex.Replace(line, @"(?<=\()([~～\?])?S(?=\d)", "$");

                    // Fix approximate "~$": "(~S322" or "(?S322" or "(S322" or "(0$322" or "(-S322" or "(-$322" -> "(~$322"
                    line = Regex.Replace(line, @"\([~～\?0O—\-]\s*\$?(\d+)", "(~$1");

                    // Fix spacing before punctuation
                    line = Regex.Replace(line, @"\s+([,.:;?!])", "$1");

                    // Fix missing space after punctuation / bracket if followed by letter
                    line = Regex.Replace(line, @"(?<=[a-zA-Z0-9])([,;:])(?=[a-zA-Z])", "$1 ");
                    line = Regex.Replace(line, @"(\])([a-zA-Z])", "$1 $2");
                }
                else
                {
                    // 1. Numbered list bullet normalization: "5，" / "5 ．" / "5、" / "5 " -> "5. "
                    line = Regex.Replace(line, @"^(\s*\d+)[，、．,]?(\s*)", "$1. ");
                    line = Regex.Replace(line, @"^(\s*\d+)\.\s*\.\s*", "$1. ");

                    // 2. Remove inline icon artifacts inside parentheses before English identifiers
                    line = Regex.Replace(line, @"([（\(\[【])\s*[^\w\s]{0,2}[巴日口oO0D]\s+([A-Za-z0-9_]+)\s*([）\)\]】])", "$1$2$3");
                    line = Regex.Replace(line, @"([（\(\[【])\s*[^\w\s]{0,2}[巴日口oO0D]([A-Za-z0-9_]+)\s*([）\)\]】])", "$1$2$3");

                    // 3. Remove isolated inline icon badge artifact right before UI elements
                    line = Regex.Replace(line, @"(新增了|添加了|点击|按下|选中|包含|带有|设置|在|增加)\s*[0oO口日巴回田D]\s*(按钮|图标|选项|功能|菜单|窗口|工具栏)", "$1$2");
                }

                // 4. Disambiguate '0'/'O', '1'/'l'/'I' in identifiers, numbers, and acronyms
                line = DisambiguateCharacters(line);

                cleanLines.Add(line);
            }

            // 5. Heading / Title colon recovery for Chinese documents
            if (!isEnglish)
            {
                for (int i = 0; i < cleanLines.Count; i++)
                {
                    string current = cleanLines[i];
                    bool isHeading = Regex.IsMatch(current, @"^\s*\d+\.");

                    if (isHeading)
                    {
                        if (Regex.IsMatch(current, @"[，,]\s*$"))
                        {
                            cleanLines[i] = Regex.Replace(current, @"[，,]\s*$", "：");
                        }
                        else if (i + 1 < cleanLines.Count && (cleanLines[i + 1].TrimStart().StartsWith("·") || cleanLines[i + 1].TrimStart().StartsWith("-") || cleanLines[i + 1].TrimStart().StartsWith("•")))
                        {
                            if (!Regex.IsMatch(current, @"[：:。！!？?]\s*$"))
                            {
                                cleanLines[i] = current.TrimEnd() + "：";
                            }
                        }
                    }
                }
            }

            var result = new StringBuilder();
            foreach (var l in cleanLines)
            {
                result.AppendLine(l);
            }

            return result.ToString().TrimEnd();
        }

        private static string DisambiguateCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = FixOandZero(text);
            text = FixNumericLAndI(text);
            text = FixAcronyms(text);
            text = FixLeadingIWords(text);
            text = FixLeadingLWords(text);
            text = FixEmbeddedOne(text);
            text = FixEmbeddedCapitalI(text);

            return text;
        }

        private static string FixOandZero(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = Regex.Replace(text, @"(?<=\d)[Oo](?=\d)", "0");
            text = Regex.Replace(text, @"(?<=\d)[Oo](?=[%％年号月日点分秒\.,:;\s]|$)", "0");
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/:])([Oo])(?=\.\d+)", "0");

            text = Regex.Replace(text, @"(?<=(?:^|[^\w]))0(?=[a-zA-Z])", m =>
            {
                int idx = m.Index;
                if (idx + 2 <= text.Length)
                {
                    char next = text[idx + 1];
                    if ((next == 'x' || next == 'X') && idx + 2 < text.Length && "0123456789abcdefABCDEF".IndexOf(text[idx + 2]) >= 0)
                    {
                        return "0";
                    }
                }
                return "O";
            });

            text = Regex.Replace(text, @"(?<=[a-zA-Z])0(?=[a-zA-Z])", m =>
            {
                int idx = m.Index;
                char prev = text[idx - 1];
                char next = text[idx + 1];
                if (char.IsUpper(prev) && char.IsUpper(next))
                {
                    return "O";
                }
                return "o";
            });

            text = Regex.Replace(text, @"(?<=[a-zA-Z]{2,})0(?=[^a-zA-Z0-9]|$)", m =>
            {
                int idx = m.Index;
                char prev = text[idx - 1];
                if (char.IsUpper(prev))
                {
                    return "O";
                }
                return "o";
            });

            return text;
        }

        private static string FixNumericLAndI(string text)
        {
            text = Regex.Replace(text, @"(?<=\d)[lI|](?=\d)", "1");
            text = Regex.Replace(text, @"(?<=\d\.)[lI|](?=\.\d|\d|\b)", "1");
            text = Regex.Replace(text, @"(?<=\b\d+)[lI|](?=\.\d+)", "1");
            text = Regex.Replace(text, @"(?<=\d)[lI|](?=[%％年号月日点分秒\.,:;\s]|$)", "1");
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/第])[lI|](?=\d+)", "1");
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/第])[lI|](?=[%％])", "1");
            text = Regex.Replace(text, @"(?<=第\s*)[lI|](?=\s*步|\s*个|\s*章|\s*节|\s*条|\s*项|\s*次|\s*页)", "1");

            return text;
        }

        private static string FixAcronyms(string text)
        {
            text = Regex.Replace(text, @"\bU[l1]\b", "UI");
            text = Regex.Replace(text, @"\b[l1]D\b", "ID");
            text = Regex.Replace(text, @"\b[l1]P\b", "IP");
            text = Regex.Replace(text, @"\b[l1]O\b", "IO");
            text = Regex.Replace(text, @"\bAP[l1]\b", "API");
            text = Regex.Replace(text, @"\bGU[l1]\b", "GUI");
            text = Regex.Replace(text, @"\bCU[l1]\b", "CUI");
            text = Regex.Replace(text, @"\bCL[l1]\b", "CLI");
            text = Regex.Replace(text, @"\bA[l1]\s+(模型|技术|算法|时代|应用|智能|助手|工具|生成|芯片|算力|Prompt|Agent|model|tool|app)", "AI $1", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(生成式|通用|强|弱)\s*A[l1]\b", "$1 AI");

            text = Regex.Replace(text, @"\bHTM[Il1]\b", "HTML");
            text = Regex.Replace(text, @"\bXM[Il1]\b", "XML");
            text = Regex.Replace(text, @"\bUR[Il1]\b", "URL");

            return text;
        }

        private static string FixLeadingIWords(string text)
        {
            text = Regex.Replace(text, @"\b[l1]([mnctdsg][a-zA-Z]*)", "I$1");
            return text;
        }

        private static string FixLeadingLWords(string text)
        {
            text = Regex.Replace(text, @"\b1([aAeEiIoOuUyYrR][a-zA-Z]*)", "l$1");
            return text;
        }

        private static string FixEmbeddedOne(string text)
        {
            text = Regex.Replace(text, @"(?<=[a-zA-Z])1(?=[a-zA-Z])", "l");
            return text;
        }

        private static string FixEmbeddedCapitalI(string text)
        {
            text = Regex.Replace(text, @"(?<=[a-z])I+(?=[a-z])", m => new string('l', m.Length));
            text = Regex.Replace(text, @"(?<=[a-z]{2,})I+(?=[^a-zA-Z0-9]|$)", m => new string('l', m.Length));
            return text;
        }

        private static async Task<SoftwareBitmap> PreprocessToSoftwareBitmapAsync(Bitmap src, float contrast = 1.35f)
        {
            float scale = 2.0f;
            if (src.Width >= 2000 || src.Height >= 2000) scale = 1.0f;
            else if (src.Width >= 1200 || src.Height >= 1200) scale = 1.5f;

            int newW = (int)(src.Width * scale);
            int newH = (int)(src.Height * scale);

            using (var dest = new Bitmap(newW, newH, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(dest))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.White);

                    using (var ia = new ImageAttributes())
                    {
                        float c = contrast;
                        float t = (1.0f - c) / 2.0f;
                        ColorMatrix cm = new ColorMatrix(new float[][]
                        {
                            new float[] {c, 0, 0, 0, 0},
                            new float[] {0, c, 0, 0, 0},
                            new float[] {0, 0, c, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {t, t, t, 0, 1}
                        });
                        ia.SetColorMatrix(cm);
                        g.DrawImage(src, new Rectangle(0, 0, newW, newH), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                    }
                }

                using (var ms = new MemoryStream())
                {
                    dest.Save(ms, ImageFormat.Bmp);
                    byte[] bytes = ms.ToArray();
                    var ras = new InMemoryRandomAccessStream();
                    using (var dw = new DataWriter(ras))
                    {
                        dw.WriteBytes(bytes);
                        await ToTask(dw.StoreAsync());
                        await ToTask(dw.FlushAsync());
                        dw.DetachStream();
                    }
                    ras.Seek(0);
                    var decoder = await ToTask(BitmapDecoder.CreateAsync(ras));
                    return await ToTask(decoder.GetSoftwareBitmapAsync());
                }
            }
        }

        public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return string.Empty;

            try
            {
                var softwareBmp = await PreprocessToSoftwareBitmapAsync(bitmap, 1.35f);
                if (softwareBmp == null) return string.Empty;

                // Load Chinese Engine
                OcrEngine zhEngine = null;
                string[] zhLangs = new string[] { "zh-Hans-CN", "zh-CN", "zh-Hans", "zh-Hant", "zh-HK", "zh-TW" };
                foreach (var tag in zhLangs)
                {
                    try
                    {
                        var lang = new Language(tag);
                        if (OcrEngine.IsLanguageSupported(lang))
                        {
                            zhEngine = OcrEngine.TryCreateFromLanguage(lang);
                            if (zhEngine != null) break;
                        }
                    }
                    catch { }
                }

                // Load English Engine
                OcrEngine engEngine = null;
                string[] engLangs = new string[] { "en-US", "en-GB", "en-CA", "en-AU", "en" };
                foreach (var tag in engLangs)
                {
                    try
                    {
                        var lang = new Language(tag);
                        if (OcrEngine.IsLanguageSupported(lang))
                        {
                            engEngine = OcrEngine.TryCreateFromLanguage(lang);
                            if (engEngine != null) break;
                        }
                    }
                    catch { }
                }

                // Fallback engines
                if (zhEngine == null && engEngine == null)
                {
                    try { zhEngine = OcrEngine.TryCreateFromUserProfileLanguages(); } catch { }
                }
                if (zhEngine == null && engEngine == null)
                {
                    var available = OcrEngine.AvailableRecognizerLanguages;
                    if (available != null && available.Count > 0)
                    {
                        zhEngine = OcrEngine.TryCreateFromLanguage(available[0]);
                    }
                }

                if (zhEngine == null && engEngine == null)
                {
                    throw new InvalidOperationException("当前系统未安装或未启用 OCR 识别语言包。");
                }

                // Run preliminary recognition to determine primary language
                OcrResult primaryResult = null;
                bool isEnglish = false;

                if (zhEngine != null)
                {
                    var zhResult = await ToTask(zhEngine.RecognizeAsync(softwareBmp));
                    int totalChars = 0;
                    int cjkCount = 0;

                    if (zhResult != null && zhResult.Lines != null)
                    {
                        foreach (var l in zhResult.Lines)
                        {
                            string t = l.Text;
                            totalChars += t.Length;
                            cjkCount += CountCjk(t);
                        }
                    }

                    double cjkRatio = totalChars > 0 ? (double)cjkCount / totalChars : 0;

                    // If text contains virtually no Chinese characters and English engine is available,
                    // switch to English OCR engine for maximum dictionary & tokenizer accuracy
                    if (cjkRatio < 0.04 && engEngine != null)
                    {
                        primaryResult = await ToTask(engEngine.RecognizeAsync(softwareBmp));
                        isEnglish = true;
                    }
                    else
                    {
                        primaryResult = zhResult;
                        isEnglish = false;
                    }
                }
                else if (engEngine != null)
                {
                    primaryResult = await ToTask(engEngine.RecognizeAsync(softwareBmp));
                    isEnglish = true;
                }

                if (primaryResult == null || primaryResult.Lines == null || primaryResult.Lines.Count == 0)
                {
                    return string.Empty;
                }

                // Merge same-row split words and superscripts (e.g. [20], [21], footnote citations)
                var mergedLines = SortAndMergeLines(primaryResult.Lines);

                var outSb = new StringBuilder();
                double lastLineCenterY = -1;
                double lastLineHeight = 20;

                for (int i = 0; i < mergedLines.Count; i++)
                {
                    var line = mergedLines[i];
                    double curCenterY = line.CenterY;
                    double curHeight = line.Height;

                    if (lastLineCenterY > 0)
                    {
                        double gap = curCenterY - lastLineCenterY;
                        // Add empty line for distinct paragraphs
                        if (gap > Math.Max(lastLineHeight, curHeight) * 1.4)
                        {
                            outSb.AppendLine();
                        }
                    }

                    string formatted = FormatLineWords(line.Words, isEnglish);
                    if (!string.IsNullOrWhiteSpace(formatted))
                    {
                        outSb.AppendLine(formatted);
                    }

                    lastLineCenterY = curCenterY;
                    lastLineHeight = curHeight;
                }

                string rawResult = outSb.ToString().TrimEnd();
                return PostProcessText(rawResult, isEnglish);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OCR Recognition Error: " + ex);
                throw;
            }
            finally
            {
                MemoryHelper.TrimWorkingSet();
            }
        }
    }
}
