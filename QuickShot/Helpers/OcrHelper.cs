using System;
using System.Drawing;
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

        public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return string.Empty;

            try
            {
                SoftwareBitmap softwareBmp = null;
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Bmp);
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
                    if (line != null && !string.IsNullOrWhiteSpace(line.Text))
                    {
                        sb.AppendLine(line.Text.Trim());
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
