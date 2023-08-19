﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPK_RePack.Core.Editors;
using GPK_RePack.Core.Model;
using GPK_RePack.Core.Model.Composite;
using GPK_RePack.Core.Model.Interfaces;
using NLog;

/*
 * Use powershell to mass dump all gpk names out of the tera folder 
 * ls -r -ea silentlycontinue -fo -inc "*.gpk" | % { $_.fullname } > filelist.txt
 * (Maps use *.gmp!)
 * */

namespace GPK_RePack.Core.IO
{
    public class MassDumper
    {

        private static Logger logger = LogManager.GetLogger("MassDumper");
        public static void DumpMassHeaders(String outfile, String[] gpkFiles)
        {
            try
            {
                if (gpkFiles.Length == 0) return;

                DateTime start = DateTime.Now;
                List<IProgress> runningReaders = new List<IProgress>();
                List<Task> runningTasks = new List<Task>();
                List<GpkPackage> loadedGpkPackages = new List<GpkPackage>();

                if (gpkFiles.Length == 1 && gpkFiles[0].EndsWith(".txt"))
                {
                    var listpath = gpkFiles[0];
                    gpkFiles = File.ReadAllLines(listpath);

                    logger.Info("Read {0} of gpk files from list", gpkFiles.Length);
                }


                logger.Debug("start");
                foreach (var path in gpkFiles)
                {
                    if (File.Exists(path))
                    {
                        Task newTask = new Task(() =>
                        {
                            Reader reader = new Reader();
                            runningReaders.Add(reader);
                            var tmpPack = reader.ReadGpk(path, true);
                            if (tmpPack != null)
                            {
                                loadedGpkPackages.AddRange(tmpPack);
                            }
                        });
                        newTask.Start();
                        runningTasks.Add(newTask);
                    }
                }

                Task.WaitAll(runningTasks.ToArray());

                logger.Debug("loading done");
                using (StreamWriter file = new StreamWriter(outfile))
                {
                    file.WriteLine("Terahelper GPK dump");

                    foreach (var gpk in loadedGpkPackages)
                    {
                        file.WriteLine("### {0} ###", gpk.Path);
                        foreach (var import in gpk.ImportList)
                        {
                            file.WriteLine("{0};{1}", gpk.Filename, import.Value.ToCompactString());
                        }

                        foreach (var export in gpk.ExportList)
                        {
                            file.WriteLine("{0};{1}", gpk.Filename, export.Value.ToCompactString());
                        }

                    }
                }
                logger.Debug("done");

                //filename
                //import
                //exports
            }
            catch (Exception ex)
            {
                logger.Fatal(ex);
            }
        }

        //in thread
        public static void DumpMassTextures(GpkStore store, String outdir, Dictionary<String, List<CompositeMapEntry>> filterList)
        {
            logger.Info("Started dumping textures to " + outdir);
            Directory.CreateDirectory(outdir);

            SynchronizedCollection<Task> runningTasks = new SynchronizedCollection<Task>();

            int MAX_TASKS = 100;

            foreach (var file in filterList)
            {

                //throttle thread creation
                while (runningTasks.Count > MAX_TASKS)
                {
                    Thread.Sleep(1000);
                }


                //create out dir
                var fileOutPath = string.Format("{0}\\{1}.gpk\\", outdir, file.Key);
                Directory.CreateDirectory(fileOutPath);

                //limit to 5 threads by default
                foreach (var entry in file.Value)
                {

                    Task newTask = null;
                    newTask = new Task(() =>
                    {
                        string path = string.Format("{0}\\{1}.gpk", store.BaseSearchPath, entry.SubGPKName);



                        if (!File.Exists(path))
                        {
                            logger.Warn("GPK to load not found. Searched for: " + path);
                            return;
                        }

                        Reader r = new Reader();
                        var package = r.ReadSubGpkFromComposite(path, entry.UID, entry.FileOffset, entry.FileLength);
                        package.LowMemMode = true;

                        //extract
                        var exports = package.GetExportsByClass("Core.Texture2D");

                        foreach (var export in exports)
                        {
                            //UID->Composite UID
                            //S1UI_Chat2.Chat2,c7a706fb_6a349a6f_1d212.Chat2_dup |
                            //we use this uid from pkgmapper
                            //var imagePath = string.Format("{0}{1}_{2}.dds", fileOutPath, entry.UID, export.UID);

                            var imagePath = string.Format("{0}{1}---{2}.dds", fileOutPath, entry.UID, export.UID);
                            TextureTools.exportTexture(export, imagePath);

                            logger.Info("Extracted texture {0} to {1}", entry.UID, imagePath);
                        }

                        //remove ref to ease gc
                        exports.Clear();
                        package = null;

                        runningTasks.Remove(newTask);
                    });


                    newTask.Start();
                    runningTasks.Add(newTask);


                }
            }

            Task.WaitAll(runningTasks.ToArray());


            NLogConfig.EnableFormLogging();
            logger.Info("Dumping done");
        }
        public static void DumpMassIcons(GpkStore store, String outdir, Dictionary<String, List<CompositeMapEntry>> filterList, List<GpkPackage> rawPackagesList)
        {
            logger.Info("Started dumping textures to " + outdir);
            Directory.CreateDirectory(outdir);

            SynchronizedCollection<Task> runningTasks = new SynchronizedCollection<Task>();

            int MAX_TASKS = 100;
            Task rawTask = null;
            rawTask = new Task(() =>
            {
                foreach (var package in rawPackagesList)
                {

                    package.LowMemMode = true;
                    //create out dir
                    var fileOutPath = string.Format("{0}\\{1}\\", outdir, package.GetNormalizedFilename());
                    Directory.CreateDirectory(fileOutPath);


                    //extract
                    var exports = package.GetExportsByClass("Core.Texture2D");

                    foreach (var export in exports)
                    {
                        //UID->Composite UID
                        //S1UI_Chat2.Chat2,c7a706fb_6a349a6f_1d212.Chat2_dup |
                        //we use this uid from pkgmapper
                        //var imagePath = string.Format("{0}{1}_{2}.dds", fileOutPath, entry.UID, export.UID);

                        var imagePath = string.Format("{0}{1}.dds", fileOutPath, export.ObjectName);
                        TextureTools.exportTexture(export, imagePath);

                        logger.Info("Extracted texture {0} to {1}", /*entry.UID*/ "", imagePath);
                    }

                    //remove ref to ease gc
                    exports.Clear();
                    //package = null;

                    runningTasks.Remove(rawTask);
                }

            });

            rawTask.Start();
            runningTasks.Add(rawTask);

            foreach (var file in filterList)
            {

                if (!file.Value.Any(x => x.UID.StartsWith("Icon_"))) continue;
                //throttle thread creation
                while (runningTasks.Count > MAX_TASKS)
                {
                    Thread.Sleep(1000);
                }

                //limit to 5 threads by default
                foreach (var entry in file.Value)
                {
                    if (!entry.UID.StartsWith("Icon_")) continue;

                    Task newTask = null;
                    newTask = new Task(() =>
                    {

                        string path = string.Format("{0}\\{1}.gpk", store.BaseSearchPath, entry.SubGPKName);

                        var fullName = entry.UID.Split('.');
                        //create out dir
                        var fileOutPath = string.Format("{0}\\{1}\\", outdir, fullName[0]);
                        Directory.CreateDirectory(fileOutPath);

                        if (!File.Exists(path))
                        {
                            logger.Warn("GPK to load not found. Searched for: " + path);
                            return;
                        }

                        Reader r = new Reader();
                        var package = r.ReadSubGpkFromComposite(path, entry.UID, entry.FileOffset, entry.FileLength);
                        package.LowMemMode = true;

                        //extract
                        var exports = package.GetExportsByClass("Core.Texture2D");

                        foreach (var export in exports)
                        {
                            //UID->Composite UID
                            //S1UI_Chat2.Chat2,c7a706fb_6a349a6f_1d212.Chat2_dup |
                            //we use this uid from pkgmapper
                            //var imagePath = string.Format("{0}{1}_{2}.dds", fileOutPath, entry.UID, export.UID);

                            var imagePath = string.Format("{0}{1}.dds", fileOutPath, fullName[1]);
                            TextureTools.exportTexture(export, imagePath);

                            logger.Info("Extracted texture {0} to {1}", entry.UID, imagePath);
                        }

                        //remove ref to ease gc
                        exports.Clear();
                        package = null;

                        runningTasks.Remove(newTask);
                    });


                    newTask.Start();
                    runningTasks.Add(newTask);


                }
            }

            Task.WaitAll(runningTasks.ToArray());


            NLogConfig.EnableFormLogging();
            logger.Info("Dumping done");
        }
    }
}
