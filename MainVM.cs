using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using MVVMHelper;
using System.Windows.Input;
using ImageMagick;
using System.ComponentModel;
using System.Collections.Generic;
using System.Security.Policy;

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
        private uint quality = 92;
        private string logs = "";
        private string compressedImagesFolderName = "compressed";
        private bool includeSubDirectories = true;
        private bool outputPDFInSameDirectory = false;

        public string SourceDirectoryPath { get=> this.sourceDirectoryPath; set { this.sourceDirectoryPath = value; RaisePropertyChangedEvent("SourceDirectoryPath"); } }
        public string SourceFolderName { get => folderName; set { this.folderName = value; RaisePropertyChangedEvent("SourceFolderName"); } }
        public uint Quality { get => this.quality; set { this.quality = value; RaisePropertyChangedEvent("Quality"); } }
        public string Logs { get => this.logs; private set { this.logs = value; RaisePropertyChangedEvent("Logs"); } }
        public bool IncludeSubDirectories { get => this.includeSubDirectories; set { this.includeSubDirectories = value; RaisePropertyChangedEvent("IncludeSubDirectories"); } }
        public bool OutputPDFInSameDirectory { get => this.outputPDFInSameDirectory; set { this.outputPDFInSameDirectory = value; RaisePropertyChangedEvent("OutputPDFInSameDirectory"); } }
        public ICommand GenerateOutputCommand { get => new RelayCommand(
            (outputType)=> {
                if (CanGenerateOutput(outputType))
                {
                    var outputstr = (string)outputType;
                    var worker = new BackgroundWorker();
                    worker.DoWork += (s, e) =>
                    {
                        if (this.includeSubDirectories)
                            this.GenerateMultiDirectoryOutput(outputstr);
                        else
                            this.GenerateOutput(outputstr);
                    };
                    worker.RunWorkerAsync();
                }
            }
        ); }

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
            IEnumerable<string> imageFiles = GetImageFilesFromDirectory();

            if (!imageFiles.Any())
            {
                this.WriteLog($"No image files found in the directory: {sourceDirectoryPath}", LogType.ERROR);
                return;
            }

            // 2. Create the output PDF document
            var document = new PdfDocument();

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
                            magickimage.Format = MagickFormat.Jpg;
                            magickimage.Quality = this.quality;
                            var imgByteArray = magickimage.ToByteArray();

                            using (var compressedStream = new MemoryStream(imgByteArray, 0, imgByteArray.Length, true, true))
                            {
                                PerformLosslessCompression(compressedStream);

                                if (outputType == "pdf")
                                    AddImageToDocument(document, compressedStream);
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
            }

            // 4. Save the final PDF document
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

        private static void PerformLosslessCompression(MemoryStream compressedStream)
        {
            var optimizer = new ImageOptimizer();
            optimizer.LosslessCompress(compressedStream);
            compressedStream.Seek(0, SeekOrigin.Begin);
        }

        private static void AddImageToDocument(PdfDocument document, MemoryStream compressedStream)
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
            var compresseddir = Path.Combine(this.sourceDirectoryPath, @"..\", $"{folderName}-{compressedImagesFolderName}");
            //create output directory if not exists
            if (!Directory.Exists(compresseddir))
                Directory.CreateDirectory(compresseddir);

            var compressedImg = compressedStream.ToArray();
            var info = new FileInfo(imagePath);
            var outputFileName = info.Name;
            //write compressed image with same name as original file 
            File.WriteAllBytes(Path.Combine(compresseddir, outputFileName), compressedImg);
        }

        private IEnumerable<string> GetImageFilesFromDirectory()
        {
            return Directory.GetFiles(sourceDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(sourceDirectoryPath, "*.jpeg", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles(sourceDirectoryPath, "*.png", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles(sourceDirectoryPath, "*.webp", SearchOption.TopDirectoryOnly));
        }

        private IEnumerable<string> GetSubDirectories()
        {
            return Directory.GetDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories);
        }
    }
}
