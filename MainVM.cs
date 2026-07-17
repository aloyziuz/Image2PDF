using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using MVVMHelper;
using System.Windows.Input;
using ImageMagick;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Shell;

namespace Images2PDF
{
    public enum LogType
    {
        NOTICE, ERROR
    }

    public class MainVM: ObservableObject
    {
        private string sourceDirectoryPath = "";
        private string folderName = "";
        private uint maxDimension = 0;
        private string logs = "";
        private string compressedImagesFolderName = "compressed";
        private bool includeSubDirectories = true;
        private bool outputPDFInSameDirectory = false;
        private double _taskbarProgressValue;
        private TaskbarItemProgressState _taskbarProgressState = TaskbarItemProgressState.None;
        private int _totalImageCount;
        private int _processedImageCount;

        public string SourceDirectoryPath { get=> this.sourceDirectoryPath; set { this.sourceDirectoryPath = value; RaisePropertyChangedEvent("SourceDirectoryPath"); } }
        public string SourceFolderName { get => folderName; set { this.folderName = value; RaisePropertyChangedEvent("SourceFolderName"); } }
        public uint MaxDimension { get => this.maxDimension; set { this.maxDimension = value; RaisePropertyChangedEvent("MaxDimension"); } }
        public string Logs { get => this.logs; private set { this.logs = value; RaisePropertyChangedEvent("Logs"); } }
        public bool IncludeSubDirectories { get => this.includeSubDirectories; set { this.includeSubDirectories = value; RaisePropertyChangedEvent("IncludeSubDirectories"); } }
        public bool OutputPDFInSameDirectory { get => this.outputPDFInSameDirectory; set { this.outputPDFInSameDirectory = value; RaisePropertyChangedEvent("OutputPDFInSameDirectory"); } }
        public bool OutputImageInCompressedDirectory { get; set; }
        public double TaskbarProgressValue
        {
            get => _taskbarProgressValue;
            set { _taskbarProgressValue = value; RaisePropertyChangedEvent("TaskbarProgressValue"); }
        }
        public TaskbarItemProgressState TaskbarProgressState
        {
            get => _taskbarProgressState;
            set { _taskbarProgressState = value; RaisePropertyChangedEvent("TaskbarProgressState"); }
        }
        public bool ConvertMP4ToWEBP { get; set; } = true;
        public int WEBPWidth { get; set; } = -1;
        public uint WEBPFPS { get; set; } = 30;
        public uint WEBPQuality { get; set; } = 75;
        public uint WEBPLoop { get; set; } = 0;
        public uint WEBPCompression { get; set; } = 6;

        public ICommand GenerateOutputCommand { get => new RelayCommand(
            (outputType)=> {
                if (CanGenerateOutput(outputType))
                {
                    var outputstr = (string)outputType;
                    var worker = new BackgroundWorker();
                    worker.DoWork += (s, e) =>
                    {
                        _processedImageCount = 0;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TaskbarProgressValue = 0;
                            TaskbarProgressState = TaskbarItemProgressState.Normal;
                        });

                        if (this.includeSubDirectories)
                        {
                            _totalImageCount = CountAllImages();
                            WriteLog($"Found {_totalImageCount} images across all directories", LogType.NOTICE);
                            this.GenerateMultiDirectoryOutput(outputstr);
                        }
                        else
                        {
                            _totalImageCount = GetFilesFromDirectory("*.jpg", "*.jpeg", "*.png").Count();
                            WriteLog($"Found {_totalImageCount} images", LogType.NOTICE);
                            this.GenerateOutput(outputstr);
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TaskbarProgressValue = 1.0;
                            TaskbarProgressState = TaskbarItemProgressState.None;
                        });
                    };
                    worker.RunWorkerAsync();
                }
            }
        ); }
        public ICommand RenameCompressedFolderNamesCommand { get => new RelayCommand(o => this.RemoveCompressedFolderNames()); }

        public MainVM() { }

        private void WriteLog(string msg, LogType type)
        {
            string typeappend = "";
            if (type == LogType.ERROR)
                typeappend = "ERROR: ";
            else if (type == LogType.NOTICE)
                typeappend = "\t NOTICE: ";

            this.Logs = this.Logs + typeappend + msg + "\n";
        }

        public bool CanGenerateOutput(object outputType)
        {
            if (string.IsNullOrWhiteSpace(this.sourceDirectoryPath) && string.IsNullOrWhiteSpace(folderName))
            {
                this.WriteLog($"Please select folder first", LogType.ERROR);
                return false;
            }
            //output type should be pdf (combined all images) or image (lossless compressed images)
            if(!(outputType is string outputTypeStr && 
                (outputTypeStr.ToLower() == "pdf" || outputTypeStr.ToLower() == "image")
                ))
            {
                this.WriteLog($"Output type not supported: {outputType.ToString()}", LogType.ERROR);
                return false;
            }
            return true;
        }

        public void RemoveCompressedFolderNames()
        {
            var subdirectories = this.GetSubDirectories();
            WriteLog($"Found {subdirectories.Count()} subfolders", LogType.NOTICE);
            var renameTargets = subdirectories.Where(d => {
                return Path.GetFileName(d).Contains($"-{this.compressedImagesFolderName}");
            });
            foreach(var target in renameTargets)
            {
                var oldName = Path.GetFileName(target);
                var newName = oldName.Replace($"-{this.compressedImagesFolderName}", "");
                Directory.Move(target, Path.Combine(this.sourceDirectoryPath, newName));
                this.WriteLog($"{oldName} => {newName}", LogType.NOTICE);
            }
        }

        public void GenerateMultiDirectoryOutput(string outputType)
        {
            var subdirectories = this.GetSubDirectories();
            WriteLog($"Found {subdirectories.Count()} subfolders", LogType.NOTICE);
            subdirectories = subdirectories.Append(this.sourceDirectoryPath);

            foreach (var subdir in subdirectories)
            {
                WriteLog($"Moving to: {subdir}", LogType.NOTICE);
                var info = new FileInfo(subdir);
                this.sourceDirectoryPath = info.FullName;
                this.folderName = info.Name;
                GenerateOutput(outputType);
            }
        }

        public void GenerateOutput(string outputType)
        {
            IEnumerable<string> imageFiles = GetFilesFromDirectory("*.jpg", "*.jpeg", "*.png");

            // 2. Create the output PDF document
            var document = new PdfDocument();

            if (!imageFiles.Any())
            {
                this.WriteLog($"No image files found in the directory: {sourceDirectoryPath}", LogType.ERROR);
            }
            else
            {
                // 3. Loop through each image and add it to a new page in the PDF
                var sortedImages = imageFiles.OrderBy(s => s, new WindowsFileNameComparer()).ToList();
                foreach (var imagePath in sortedImages)
                {
                    this.WriteLog(imagePath, LogType.NOTICE);

                    try
                    {
                            using (var magickimage = new MagickImage(imagePath))
                            {
                                if (magickimage.Format != MagickFormat.Unknown)
                                {
                                    magickimage.Strip();

                                    if (this.MaxDimension > 0)
                                    {
                                        var maxSide = Math.Max(magickimage.Width, magickimage.Height);
                                        if (maxSide > this.MaxDimension)
                                        {
                                            magickimage.Resize(new MagickGeometry(this.MaxDimension) { IgnoreAspectRatio = false, Greater = true });
                                        }
                                    }

                                    if (magickimage.Format == MagickFormat.Bmp || magickimage.Format == MagickFormat.Tiff || magickimage.Format == MagickFormat.Tif)
                                        magickimage.Format = MagickFormat.Png;

                                    var imgByteArray = magickimage.ToByteArray();

                                    using (var compressedStream = new MemoryStream(imgByteArray, 0, imgByteArray.Length, true, true))
                                    {
                                        PerformLosslessCompression(compressedStream);

                                        //3a. if output is pdf, add it to the pdf file in memory
                                        if (outputType == "pdf")
                                            AddImageToDocument(document, compressedStream);
                                        //3b. if output is image, save it to the output directory
                                        else if (outputType == "image")
                                            SaveImage(imagePath, compressedStream);
                                    }
                                }
                                else
                                {
                                    this.WriteLog($"Image format unknown: '{imagePath}'", LogType.ERROR);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            this.WriteLog($"Could not process image '{imagePath}': {ex.Message}", LogType.ERROR);
                        }

                    _processedImageCount++;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_totalImageCount > 0)
                            TaskbarProgressValue = (double)_processedImageCount / _totalImageCount;
                    });
                }
            }



            // 4. convert mp4 to animated webp via ffmpeg
            if (this.ConvertMP4ToWEBP)
            {
                var videos = GetFilesFromDirectory("*.mp4");
                if (!videos.Any())
                {
                    this.WriteLog($"No video files found in the directory: {sourceDirectoryPath}", LogType.ERROR);
                }
                else
                {
                    this.WriteLog($"Found {videos.Count()} videos.", LogType.NOTICE);
                    var outputPath = CreateOutputFolder();
                    foreach (var videoPath in videos)
                    {
                        var finfo = new FileInfo(videoPath);
                        var outputFilePath = Path.Join(outputPath, finfo.Name.Replace(finfo.Extension, ""));
                        var ffmpegArg = $"-i \"{videoPath}\" " +
                            $"-vcodec libwebp " +
                            $"-filter:v \"scale = {WEBPWidth}:-1:flags = lanczos\" " +
                            $"-loop {WEBPLoop} -lossless 0 -compression_level {WEBPCompression} -q:v {WEBPQuality} " +
                            $"-an -vsync 0 \"{outputFilePath}.webp\"";

                        var ffmpegProcessInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = ffmpegArg,
                            UseShellExecute = false,
                            CreateNoWindow = false, // Prevents the black command window from popping up
                            RedirectStandardOutput = false,
                            RedirectStandardError = false // FFmpeg often outputs progress/errors to StandardError
                        };

                        try
                        {
                            using (Process process = Process.Start(ffmpegProcessInfo))
                            {
                                process.WaitForExit();

                                if (process.ExitCode == 0)
                                {
                                    Application.Current.Dispatcher.Invoke(() => this.WriteLog($"Conversion complete! Output saved to:\n{outputPath}", LogType.NOTICE));
                                    if (outputType == "pdf")
                                    {
                                        using (var s = new StreamReader(outputFilePath))
                                        {
                                            AddImageToDocument(document, s.BaseStream);
                                        }
                                    }
                                }
                                else
                                {
                                    string errorOutput = process.StandardError.ReadToEnd();
                                    Application.Current.Dispatcher.Invoke(() => this.WriteLog($"Conversion failed! Error code: {process.ExitCode}\nFFmpeg Output:\n{errorOutput}", LogType.ERROR));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() => this.WriteLog($"An unexpected error occurred: {ex.Message}", LogType.ERROR));
                        }
                    }
                }
            }

            

            // 5. Save the final PDF document
            if (outputType == "pdf")
            {
                try
                {
                    var outputFilePath = Path.Combine(this.sourceDirectoryPath, $"{this.folderName}.pdf");
                    if (!this.OutputPDFInSameDirectory)
                    {
                        var d = new SaveFileDialog();
                        d.Title = "Select Output PDF Directory";
                        d.InitialDirectory = this.sourceDirectoryPath;
                        d.DefaultExt = ".pdf";
                        d.FileName = this.folderName;
                        var dresult = d.ShowDialog();
                        if (dresult == true)
                        {
                            outputFilePath = d.FileName;
                        }
                    }
                    document.Save(outputFilePath);
                    WriteLog($"PDF created successfully at: {outputFilePath}", LogType.NOTICE);
                }
                catch (Exception ex)
                {
                    this.WriteLog($"An error occurred while saving the PDF: {ex.Message}", LogType.ERROR);
                }
            }
            else if(outputType == "image")
            {
                this.WriteLog("DONE!", LogType.NOTICE);
            }
        }

        public void ResetLogs()
        {
            this.Logs = "";
        }

        private static void PerformLosslessCompression(Stream compressedStream)
        {
            var optimizer = new ImageOptimizer();
            optimizer.LosslessCompress(compressedStream);
            compressedStream.Seek(0, SeekOrigin.Begin);
        }

        private static void AddImageToDocument(PdfDocument document, Stream compressedStream)
        {
            XImage xImage = XImage.FromStream(compressedStream);

            // Create a new PDF page
            PdfPage page = document.AddPage();
            page.Width = new XUnit(xImage.PixelWidth);
            page.Height = new XUnit(xImage.PixelHeight);
            XGraphics gfx = XGraphics.FromPdfPage(page);

            // Draw the image onto the PDF page
            gfx.DrawImage(xImage, 0, 0, xImage.PixelWidth, xImage.PixelHeight);
        }

        private void SaveImage(string imagePath, MemoryStream compressedStream)
        {
            string compresseddir = CreateOutputFolder();

            var compressedImg = compressedStream.ToArray();
            var info = new FileInfo(imagePath);
            var outputFileName = info.Name;
            //write compressed image with same name as original file 
            File.WriteAllBytes(Path.Combine(compresseddir, outputFileName), compressedImg);
        }

        private string CreateOutputFolder()
        {
            var compresseddir = Path.Combine(this.sourceDirectoryPath, @"..\", $"{folderName}-{compressedImagesFolderName}");
            //create output directory if not exists
            if (!Directory.Exists(compresseddir))
                Directory.CreateDirectory(compresseddir);
            return compresseddir;
        }

        private int CountAllImages()
        {
            var dirs = GetSubDirectories().Append(this.sourceDirectoryPath);
            int total = 0;
            foreach (var dir in dirs)
            {
                total += Directory.GetFiles(dir, "*.jpg", SearchOption.TopDirectoryOnly).Length
                    + Directory.GetFiles(dir, "*.jpeg", SearchOption.TopDirectoryOnly).Length
                    + Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly).Length;
            }
            return total;
        }

        private IEnumerable<string> GetFilesFromDirectory(params string[] fileformats)
        {
            var result = new List<string>();
            foreach(var format in fileformats)
            {
                result.AddRange(Directory.GetFiles(sourceDirectoryPath, format, SearchOption.TopDirectoryOnly));
            }
            return result;
        }

        private IEnumerable<string> GetSubDirectories()
        {
            return Directory.GetDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories);
        }
    }
}
