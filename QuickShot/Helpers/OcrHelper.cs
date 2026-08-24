using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
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

                return sb.ToString().TrimEnd();
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
