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
        private readonly string _dataPath;

        private readonly Microsoft.Office.Interop.Word.Application _wordApplication;
        private Document _wordDocument;

        public StatisticChecker(string testName)
        {
            // set paths
            _resultPath = testName + "_stats_result.txt";
            _dataPath = testName + "_stats_data.docx";

            // initialize word
            if (!Directory.Exists(_dataPath))
            {
                File.Create(_dataPath);
            }
            var absDataPath = Path.Combine(Assembly.GetExecutingAssembly().Location.Replace("\\Kollector.exe",""), _dataPath);
            _wordApplication = new Microsoft.Office.Interop.Word.Application {Visible = true};
            _wordDocument = _wordApplication.Documents.Open(FileName: absDataPath, ReadOnly: false);
            _wordApplication.Selection.EndKey(WdUnits.wdStory);
        }

        public void Log(double confidence, string ocrText, Bitmap image, long ocrDuration, DateTime timestamp)
        {
            // ---- log to word ---- //

            // log image
            _wordApplication.Selection.InsertParagraphAfter();
            Clipboard.SetImage(image);
            _wordApplication.Selection.Paste();
            _wordApplication.Selection.InsertParagraphAfter();

            // log ocr text
            var paragraph = _wordDocument.Content.Paragraphs.Add();
            paragraph.Range.Text = ocrText;
            _wordDocument.Save();

            // ---- log to data ---- //
            using (var fileStream = new FileStream(_dataPath, FileMode.OpenOrCreate))
            {
                using (var writer = new StreamWriter(fileStream, Encoding.UTF8))
                {
                    writer.WriteLine("timestamp;image_size;confidence;ocr_length;ocr_duration;");
                    writer.Write(timestamp.ToLongTimeString()+";");
                    writer.Write($"{image.Width}*{image.Height};");
                    writer.Write(confidence + ";");
                    writer.Write(ocrText.Length + ";");
                    writer.Write(ocrDuration+ ";"+Environment.NewLine);
                }
            }
        }
    }
}
