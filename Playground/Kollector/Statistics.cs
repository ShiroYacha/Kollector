//#define LOGWORD

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Office.Interop.Word;
using Task = System.Threading.Tasks.Task;

namespace Kollector
{
    public class Statistic
    {

    }

    public class StatisticChecker
    {
        private List<Statistic> _statistics = new List<Statistic>();
        private readonly string _resultPath;

#if LOGWORD
        private readonly string _dataPath;

        private readonly Microsoft.Office.Interop.Word.Application _wordApplication;
        private readonly Document _wordDocument;
#endif

        public StatisticChecker(string testName)
        {
            // set paths
            _resultPath = testName + "_stats_result.txt";

#if LOGWORD
            // initialize word
            _dataPath = testName + "_stats_data.docx";
            if (!File.Exists(_dataPath))
            {
                var fs = File.Create(_dataPath);
                fs.Close();
            }
            var absDataPath = Path.Combine(Assembly.GetExecutingAssembly().Location.Replace("\\Kollector.exe",""), _dataPath);
            _wordApplication = new Microsoft.Office.Interop.Word.Application {Visible = true};
            _wordDocument = _wordApplication.Documents.Open(FileName: absDataPath, ReadOnly: false);
            _wordApplication.Selection.EndKey(WdUnits.wdStory);
#endif

            if (!File.Exists(_resultPath))
            {
                var fs = File.Create(_resultPath);
                fs.Close();

                using (var writer = File.AppendText(_resultPath))
                {
                    writer.WriteLine("timestamp;image_size;confidence;ocr_length;ocr_duration;");
                }
            }
        }

        public void Log(double confidence, string ocrText, Bitmap image, long ocrDuration, DateTime timestamp)
        {
            // ---- log to word ---- //
#if LOGWORD

            // log image
            _wordApplication.Selection.InsertParagraphAfter();
            Clipboard.SetImage(image);
            _wordApplication.Selection.Paste();
            _wordApplication.Selection.InsertParagraphAfter();

            // log ocr text
            var paragraph = _wordDocument.Content.Paragraphs.Add();
            paragraph.Range.Text = ocrText;
            _wordDocument.Save();
#endif

            // ---- log to data ---- //

            using (var writer = File.AppendText(_resultPath))
            {
                writer.Write(timestamp.ToLongTimeString() + ";");
                writer.Write($"{image.Width}*{image.Height};");
                writer.Write(confidence + ";");
                writer.Write(ocrText.Length + ";");
                writer.Write(ocrDuration + ";" + Environment.NewLine);
            }
        }
    }
}

