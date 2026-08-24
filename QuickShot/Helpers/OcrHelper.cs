using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
                   (c >= 0x3000 && c <= 0x303F) || // CJK Symbols and Punctuation (，。！？等)
                   (c >= 0xFF00 && c <= 0xFFEF);   // Halfwidth and Fullwidth Forms（中文括号等）
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
                   c == '#' || c == '$' || c == '￥';
        }

        private static string CleanAndFormatLine(OcrLine line)
        {
            if (line == null || line.Words == null || line.Words.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < line.Words.Count; i++)
            {
                var word = line.Words[i].Text;
                if (string.IsNullOrEmpty(word)) continue;

                if (i > 0 && sb.Length > 0)
                {
                    char prevChar = sb[sb.Length - 1];
                    char nextChar = word[0];

                    bool prevCjk = IsCjk(prevChar);
                    bool nextCjk = IsCjk(nextChar);

                    bool shouldAddSpace = false;

                    if (IsNoSpaceAfter(prevChar) || IsNoSpaceBefore(nextChar))
                    {
                        shouldAddSpace = false;
                    }
                    else if (!prevCjk && !nextCjk)
                    {
                        // Both Latin/digits (e.g. "Hello" and "World") -> keep space
                        if (char.IsLetterOrDigit(prevChar) && char.IsLetterOrDigit(nextChar))
                        {
                            shouldAddSpace = true;
                        }
                    }
                    else if (!prevCjk && nextCjk)
                    {
                        // English word followed by Chinese -> keep 1 space (e.g. "Fluent 结果")
                        if (char.IsLetter(prevChar))
                        {
                            shouldAddSpace = true;
                        }
                        else if (char.IsDigit(prevChar))
                        {
                            shouldAddSpace = false; // "8秒", "10个"
                        }
                    }
                    else if (prevCjk && !nextCjk)
                    {
                        // Chinese followed by English word -> keep 1 space (e.g. "右下角 Fluent")
                        if (char.IsLetter(nextChar))
                        {
                            shouldAddSpace = true;
                        }
                        else if (char.IsDigit(nextChar))
                        {
                            shouldAddSpace = false;
                        }
                    }
                    // CJK followed by CJK -> shouldAddSpace = false (no spaces between Chinese characters)

                    if (shouldAddSpace)
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(word);
            }
            return sb.ToString();
        }

        private static string PostProcessText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanLines = new System.Collections.Generic.List<string>();

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 1. Numbered list bullet normalization:
                // "5，" / "5 ．" / "5、" / "5 " / "5. " -> "5. "
                // Ensures the dot '.' after leading digits is never omitted
                line = Regex.Replace(line, @"^(\s*\d+)[，、．,]?(\s*)", "$1. ");
                // Avoid double dots like "5.. "
                line = Regex.Replace(line, @"^(\s*\d+)\.\s*\.\s*", "$1. ");

                // 2. Remove inline icon artifacts inside parentheses before English identifiers:
                // e.g. "（巴 EditorWindow）" or "(巴 EditorWindow)" -> "（EditorWindow）"
                line = Regex.Replace(line, @"([（\(\[【])\s*[^\w\s]{0,2}[巴日口oO0D]\s+([A-Za-z0-9_]+)\s*([）\)\]】])", "$1$2$3");
                line = Regex.Replace(line, @"([（\(\[【])\s*[^\w\s]{0,2}[巴日口oO0D]([A-Za-z0-9_]+)\s*([）\)\]】])", "$1$2$3");

                // 3. Remove isolated inline icon badge artifact right before UI elements / buttons / icons / menus:
                // e.g. "新增了 0 按钮" / "新增了0按钮" -> "新增了按钮"
                line = Regex.Replace(line, @"(新增了|添加了|点击|按下|选中|包含|带有|设置|在|增加)\s*[0oO口日巴回田D]\s*(按钮|图标|选项|功能|菜单|窗口|工具栏)", "$1$2");

                // 4. Disambiguate '0' (zero) and 'O' (letter O), '1' (one), 'l' (el), 'I' (eye) in words, acronyms, and numbers
                line = DisambiguateCharacters(line);

                cleanLines.Add(line);
            }

            // 5. Heading / Title colon recovery:
            // If a line is a numbered heading (e.g. "5. 右下角 Fluent 结果预览窗" or "6. 编辑器工具栏联动")
            // and ends with comma/fullwidth comma or has no trailing punctuation followed by a sub-item,
            // recover the colon "："
            for (int i = 0; i < cleanLines.Count; i++)
            {
                string current = cleanLines[i];
                bool isHeading = Regex.IsMatch(current, @"^\s*\d+\.");

                if (isHeading)
                {
                    // If it ends with comma or fullwidth comma, replace with colon
                    if (Regex.IsMatch(current, @"[，,]\s*$"))
                    {
                        cleanLines[i] = Regex.Replace(current, @"[，,]\s*$", "：");
                    }
                    // If it doesn't end with punctuation and next line is a bullet/sub-item, restore trailing colon
                    else if (i + 1 < cleanLines.Count && (cleanLines[i + 1].TrimStart().StartsWith("·") || cleanLines[i + 1].TrimStart().StartsWith("-") || cleanLines[i + 1].TrimStart().StartsWith("•")))
                    {
                        if (!Regex.IsMatch(current, @"[：:。！!？?]\s*$"))
                        {
                            cleanLines[i] = current.TrimEnd() + "：";
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

            // 1. Fix letter 'O'/'o' inside numbers and '0' in words
            text = FixOandZero(text);

            // 2. Fix '1', 'l', 'I' inside numeric contexts
            text = FixNumericLAndI(text);

            // 3. Fix acronyms (e.g. "Ul" -> "UI", "lD" -> "ID", "lP" -> "IP", "HTMI" -> "HTML", "APl" -> "API")
            text = FixAcronyms(text);

            // 4. Fix capitalized English words starting with 'l' or '1' where phonotactics require 'I' (Image, Index, Info, Item, Icon, Idea, Is, It, In)
            text = FixLeadingIWords(text);

            // 5. Fix words starting with '1' where 'l' or 'L' is required (e.g. "1ook" -> "look", "1ine" -> "line", "1ocal" -> "local")
            text = FixLeadingLWords(text);

            // 6. Fix digit '1' embedded inside lowercase English words (e.g. "c1ass" -> "class", "c1ick" -> "click", "defau1t" -> "default")
            text = FixEmbeddedOne(text);

            // 7. Fix capital 'I' embedded inside lowercase English words (e.g. "cIass" -> "class", "faiI" -> "fail", "bIur" -> "blur", "heIIo" -> "hello")
            text = FixEmbeddedCapitalI(text);

            return text;
        }

        private static string FixOandZero(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 1. Fix letter 'O'/'o' inside numbers (e.g. "1O0" -> "100", "2O26" -> "2026", "1O%" -> "10%", "1O:30" -> "10:30")
            text = Regex.Replace(text, @"(?<=\d)[Oo](?=\d)", "0");
            text = Regex.Replace(text, @"(?<=\d)[Oo](?=[%％年号月日点分秒\.,:;\s]|$)", "0");
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/:])([Oo])(?=\.\d+)", "0");

            // 2. Fix digit '0' inside/starting English words (e.g. "0cr" -> "Ocr", "0crEngine" -> "OcrEngine", "0pen" -> "Open")
            // Exclude hex literals like "0x" or "0X"
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

            // Digit '0' embedded inside letters (e.g. "Micr0soft" -> "Microsoft", "JS0N" -> "JSON", "Hell0" -> "Hello")
            text = Regex.Replace(text, @"(?<=[a-zA-Z])0(?=[a-zA-Z])", m =>
            {
                int idx = m.Index;
                char prev = text[idx - 1];
                char next = text[idx + 1];
                if (char.IsUpper(prev) && char.IsUpper(next))
                {
                    return "O"; // JS0N -> JSON
                }
                return "o"; // Micr0soft -> Microsoft
            });

            // Digit '0' at end of an English word (e.g. "Hell0" -> "Hello", "inf0" -> "info")
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
            // 'l' or 'I' flanked by digits (e.g. "2l0" -> "210")
            text = Regex.Replace(text, @"(?<=\d)[lI|](?=\d)", "1");
            // In IP addresses or decimals: "192.168.l.1" -> "192.168.1.1", "3.l4" -> "3.14"
            text = Regex.Replace(text, @"(?<=\d\.)[lI|](?=\.\d|\d|\b)", "1");
            text = Regex.Replace(text, @"(?<=\b\d+)[lI|](?=\.\d+)", "1");
            // Flanked by digit on left and units/symbols on right (e.g. "202l年", "202l-", "l00%")
            text = Regex.Replace(text, @"(?<=\d)[lI|](?=[%％年号月日点分秒\.,:;\s]|$)", "1");
            // Flanked by start/punctuation and digit (e.g. "l00%", "l2:30", "第 l 步")
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/第])[lI|](?=\d+)", "1");
            text = Regex.Replace(text, @"(?<=^|[\s\(\[\{=+\-*/第])[lI|](?=[%％])", "1");
            text = Regex.Replace(text, @"(?<=第\s*)[lI|](?=\s*步|\s*个|\s*章|\s*节|\s*条|\s*项|\s*次|\s*页)", "1");

            return text;
        }

        private static string FixAcronyms(string text)
        {
            // Acronyms where 'l' or '1' is mistaken for 'I':
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
            // In English phonotactics, no English word begins with lowercase 'l' followed by consonants:
            // "lm..." -> "Im..." (Image, Import, Implicit, Impact, Immense, Immunity, ...)
            // "ln..." -> "In..." (Index, Info, Input, Include, Inside, Install, Init, Instance, ...)
            // "lt..." -> "It..." (Item, Iterate, Italic, ...)
            // "lc..." -> "Ic..." (Icon, Ice, ...)
            // "ld..." -> "Id..." (Idea, Identify, Idle, Idol, ...)
            // "ls..." -> "Is..." (Is, Issue, Island, ...)
            // "lg..." -> "Ig..." (Ignore, Ignite, ...)
            text = Regex.Replace(text, @"\b[l1]([mnctdsg][a-zA-Z]*)", "I$1");
            return text;
        }

        private static string FixLeadingLWords(string text)
        {
            // Words starting with '1' followed by vowels / common L-initial consonants:
            // "1ook" -> "look", "1ine" -> "line", "1evel" -> "level", "1ist" -> "list",
            // "1og" -> "log", "1oad" -> "load", "1ink" -> "link", "1ayout" -> "layout",
            // "1ight" -> "light", "1ast" -> "last", "1ocal" -> "local", "1ock" -> "lock"
            text = Regex.Replace(text, @"\b1([aAeEiIoOuUyYrR][a-zA-Z]*)", "l$1");
            return text;
        }

        private static string FixEmbeddedOne(string text)
        {
            // Digit '1' inside a word flanked by letters:
            // e.g. "c1ass" -> "class", "c1ick" -> "click", "defau1t" -> "default", "uti1s" -> "utils"
            text = Regex.Replace(text, @"(?<=[a-zA-Z])1(?=[a-zA-Z])", "l");
            return text;
        }

        private static string FixEmbeddedCapitalI(string text)
        {
            // Capital 'I' inside lowercase word flanked by lowercase letters:
            // e.g. "cIass" -> "class", "cIick" -> "click", "faiI" -> "fail", "utiIs" -> "utils", "bIur" -> "blur"
            text = Regex.Replace(text, @"(?<=[a-z])I+(?=[a-z])", m => new string('l', m.Length));
            text = Regex.Replace(text, @"(?<=[a-z]{2,})I+(?=[^a-zA-Z0-9]|$)", m => new string('l', m.Length));
            return text;
        }

        private static Bitmap PreprocessBitmap(Bitmap src)
        {
            // Dynamic scale factor: small screen fonts (12-14px) are upscaled 2x using HighQualityBicubic
            // to provide optimal stroke rendering for Windows OCR neural recognizer.
            float scale = 2.0f;
            if (src.Width >= 2000 || src.Height >= 2000)
            {
                scale = 1.0f;
            }
            else if (src.Width >= 1200 || src.Height >= 1200)
            {
                scale = 1.5f;
            }

            int newW = (int)(src.Width * scale);
            int newH = (int)(src.Height * scale);
            var dest = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(src, new Rectangle(0, 0, newW, newH), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }

            return dest;
        }

        public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return string.Empty;

            try
            {
                SoftwareBitmap softwareBmp = null;

                // Preprocess and upscale image for maximum recognition accuracy
                using (var processed = PreprocessBitmap(bitmap))
                using (var ms = new MemoryStream())
                {
                    processed.Save(ms, ImageFormat.Bmp);
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
                    softwareBmp = await ToTask(decoder.GetSoftwareBitmapAsync());
                }

                if (softwareBmp == null) return string.Empty;

                OcrEngine engine = null;

                // Priority 1: Simplified Chinese (covers Chinese + English seamlessly)
                string[] preferredLangs = new string[] { "zh-Hans-CN", "zh-CN", "zh-Hans", "zh-Hant", "zh-HK", "zh-TW" };
                foreach (var tag in preferredLangs)
                {
                    try
                    {
                        var lang = new Language(tag);
                        if (OcrEngine.IsLanguageSupported(lang))
                        {
                            engine = OcrEngine.TryCreateFromLanguage(lang);
                            if (engine != null) break;
                        }
                    }
                    catch { }
                }

                // Priority 2: System user profile language
                if (engine == null)
                {
                    try
                    {
                        engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    }
                    catch { }
                }

                // Priority 3: Any available recognizer
                if (engine == null)
                {
                    var available = OcrEngine.AvailableRecognizerLanguages;
                    if (available != null && available.Count > 0)
                    {
                        engine = OcrEngine.TryCreateFromLanguage(available[0]);
                    }
                }

                if (engine == null)
                {
                    throw new InvalidOperationException("当前系统未安装或未启用 OCR 识别语言包。");
                }

                var ocrResult = await ToTask(engine.RecognizeAsync(softwareBmp));
                if (ocrResult == null || ocrResult.Lines == null || ocrResult.Lines.Count == 0)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < ocrResult.Lines.Count; i++)
                {
                    var line = ocrResult.Lines[i];
                    if (line != null)
                    {
                        string formattedLine = CleanAndFormatLine(line);
                        if (!string.IsNullOrWhiteSpace(formattedLine))
                        {
                            sb.AppendLine(formattedLine);
                        }
                    }
                }

                string rawResult = sb.ToString().TrimEnd();
                return PostProcessText(rawResult);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OCR Recognition Error: " + ex);
                throw;
            }
            finally
            {
                // Trim memory immediately after OCR execution to keep background memory minimal
                MemoryHelper.TrimWorkingSet();
            }
        }
    }
}
