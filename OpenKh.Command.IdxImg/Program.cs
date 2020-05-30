﻿using OpenKh.Common;
using OpenKh.Kh2;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xe.IO;

namespace OpenKh.Command.IdxImg
{
    [Command("OpenKh.Command.IdxImg")]
    [VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
    [Subcommand(
        typeof(ExtractCommand),
        typeof(ListCommand),
        typeof(InjectCommand))]
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                return CommandLineApplication.Execute<Program>(args);
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine($"The file {e.FileName} cannot be found. The program will now exit.");
                return 2;
            }
            catch (Exception e)
            {
                Console.WriteLine($"FATAL ERROR: {e.Message}\n{e.StackTrace}");
                return -1;
            }
        }

        protected int OnExecute(CommandLineApplication app)
        {
            app.ShowHelp();
            return 1;
        }

        private static string GetVersion()
            => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        private class ExtractCommand
        {
            private Program Parent { get; set; }

            [Required]
            [FileExists]
            [Argument(0, Description = "Kingdom Hearts II IDX file, paired with a IMG")]
            public string InputIdx { get; set; }

            [FileExists]
            [Option(CommandOptionType.SingleValue, Description = "Custom Kingdom Hearts II IMG file", ShortName = "m", LongName = "img")]
            public string InputImg { get; set; }

            [Option(CommandOptionType.SingleValue, Description = "Path where the content will be extracted", ShortName = "o", LongName = "output")]
            public string OutputDir { get; set; }

            [Option(CommandOptionType.NoValue, Description = "Extract all the sub-IDX recursively", ShortName = "r", LongName = "recursive")]
            public bool Recursive { get; set; }

            [Option(CommandOptionType.NoValue, Description = "Split sub-IDX when extracting recursively", ShortName = "s", LongName = "split")]
            public bool Split { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                var inputImg = InputImg ?? InputIdx.Replace(".idx", ".img", StringComparison.InvariantCultureIgnoreCase);
                var outputDir = OutputDir ?? Path.Combine(Path.GetFullPath(inputImg), "extract");

                var idxEntries = OpenIdx(InputIdx);

                using (var imgStream = File.OpenRead(inputImg))
                {
                    var img = new Img(imgStream, idxEntries, false);
                    var idxName = Path.GetFileNameWithoutExtension(InputIdx);

                    var subIdxPath = ExtractIdx(img, idxEntries, Recursive && Split ? Path.Combine(outputDir, "KH2") : outputDir);
                    if (Recursive)
                    {
                        foreach (var idxFileName in subIdxPath)
                        {
                            idxName = Path.GetFileNameWithoutExtension(idxFileName);
                            ExtractIdx(img, OpenIdx(idxFileName), Split ? Path.Combine(outputDir, idxName) : outputDir);
                        }
                    }
                }

                return 0;
            }

            public static List<string> ExtractIdx(Img img, IEnumerable<Idx.Entry> idxEntries, string basePath)
            {
                var idxs = new List<string>();

                foreach (var entry in idxEntries)
                {
                    var fileName = IdxName.Lookup(entry);
                    if (fileName == null)
                        fileName = $"@noname/{entry.Hash32:X08}-{entry.Hash16:X04}";

                    Console.WriteLine(fileName);

                    var outputFile = Path.Combine(basePath, fileName);
                    var outputDir = Path.GetDirectoryName(outputFile);
                    if (Directory.Exists(outputDir) == false)
                        Directory.CreateDirectory(outputDir);

                    using (var file = File.Create(outputFile))
                    {
                        // TODO handle decompression
                        img.FileOpen(entry).CopyTo(file);
                    }

                    if (Path.GetExtension(fileName) == ".idx")
                        idxs.Add(outputFile);
                }

                return idxs;
            }
        }

        private class ListCommand
        {
            private Program Parent { get; set; }

            [Required]
            [FileExists]
            [Argument(0, Description = "Kingdom Hearts II IDX file, paired with a IMG")]
            public string InputIdx { get; set; }

            [Option(CommandOptionType.NoValue, Description = "Sort file list by their position in the IMG", ShortName = "s", LongName = "sort")]
            public bool Sort { get; set; }

            protected int OnExecute(CommandLineApplication app)
            {
                var entries = OpenIdx(InputIdx);
                if (Sort)
                    entries = entries.OrderBy(x => x.Offset);

                foreach (var entry in entries)
                    Console.WriteLine(IdxName.Lookup(entry) ?? $"@{entry.Hash32:X08}-{entry.Hash16:X04}");

                return 0;
            }
        }

        [Command(Description = "Patch an ISO by injecting a single file in the IMG, without repacking. Useful for quick testing.")]
        private class InjectCommand
        {
            [Required]
            [FileExists]
            [Argument(0, Description = "Kingdom Hearts II ISO file that contains the game files")]
            public string InputIso { get; set; }

            [Required]
            [FileExists]
            [Argument(1, Description = "File to inject")]
            public string InputFile { get; set; }

            [Required]
            [Argument(2, Description = "IDX file path (eg. msg/jp/sys.bar)")]
            public string FilePath { get; set; }

            [Option(CommandOptionType.NoValue, Description = "Do not compress the file to inject", ShortName = "u", LongName = "--uncompressed")]
            public bool Uncompressed { get; set; }

            [Option(CommandOptionType.SingleValue, Description = "ISO block for KH2.IDX. By default is 1417580 for KH2FM", ShortName = "idx", LongName = "--idx-offset")]
            public long IdxIsoBlock { get; set; } = 1417580;

            [Option(CommandOptionType.SingleValue, Description = "ISO block for KH2.IMG. By default is 1841 for KH2FM", ShortName = "img", LongName = "--img-offset")]
            public long ImgIsoBlock { get; set; } = 1841;

            protected int OnExecute(CommandLineApplication app)
            {
                const long EsitmatedMaximumImgFileSize = 4L * 1024 * 1024 * 1024; // 4GB
                const int EsitmatedMaximumIdxFileSize = 600 * 1024; // 600KB
                const int EstimatedMaximumIdxEntryAmountToBeValid = EsitmatedMaximumIdxFileSize / 0x10 - 4;

                using var isoStream = File.Open(InputIso, FileMode.Open, FileAccess.ReadWrite);

                using var idxStream = new SubStream(isoStream, IdxIsoBlock * 0x800, EsitmatedMaximumIdxFileSize);
                var idxEntryCount = idxStream.ReadInt32();
                if (idxEntryCount > EstimatedMaximumIdxEntryAmountToBeValid)
                    throw new Exception("There is a high chance that the IDX block is not valid, therefore the injection will terminate to avoid corruption.");

                var idxEntries = Idx.Read(idxStream.SetPosition(0));
                var entry = idxEntries.FirstOrDefault(x => x.Hash32 == Idx.GetHash32(FilePath) && x.Hash16 == Idx.GetHash16(FilePath));
                if (entry == null)
                    throw new Exception($"The file {FilePath} has not been found inside the KH2.IDX, therefore the injection will terminate.");

                using var imgStream = new SubStream(isoStream, ImgIsoBlock * 0x800, EsitmatedMaximumImgFileSize);

                var inputData = File.ReadAllBytes(InputFile);
                var decompressedLength = inputData.Length;
                if (Uncompressed == false)
                    inputData = Img.Compress(inputData);

                var blockCountRequired = (inputData.Length + 0x7ff) / 0x800 - 1;
                if (blockCountRequired > entry.BlockLength)
                    throw new Exception($"The file to inject is too big: the actual is {inputData.Length} but the maximum allowed is {GetLength(entry.BlockLength)}.");

                imgStream.SetPosition(GetOffset(entry.Offset));
                // Clean completely the content of the previous file to not mess up the decompression
                imgStream.Write(new byte[GetLength(entry.BlockLength)]);

                imgStream.SetPosition(GetOffset(entry.Offset));
                imgStream.Write(inputData);

                entry.IsCompressed = !Uncompressed;
                entry.Length = decompressedLength;
                // we are intentionally not patching entry.BlockLength because it would not allow to insert back bigger files.

                Idx.Write(idxStream.SetPosition(0), idxEntries);

                return 0;
            }

            private static long GetOffset(long blockOffset) => blockOffset * 0x800;
            private static int GetLength(int blockLength) => blockLength * 0x800 + 0x800;
        }

        private static IEnumerable<Idx.Entry> OpenIdx(string fileName)
        {
            using var idxStream = File.OpenRead(fileName);
            return Idx.Read(idxStream);
        }
    }
}
