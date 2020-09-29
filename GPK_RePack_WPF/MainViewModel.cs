﻿using GPK_RePack.Core;
using GPK_RePack.Core.Editors;
using GPK_RePack.Core.IO;
using GPK_RePack.Core.Model;
using GPK_RePack.Core.Model.Interfaces;
using GPK_RePack.Core.Model.Payload;
using GPK_RePack.Core.Model.Prop;
using GPK_RePack.Core.Updater;
using NAudio.Vorbis;
using NAudio.Wave;
using NLog;
using Nostrum;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GPK_RePack.Core.Model.Composite;
using GPK_RePack_WPF.Windows;
using UpkManager.Dds;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.Forms.DataFormats;

namespace GPK_RePack_WPF
{
    public enum Tab : int
    {
        Info = 0,
        Properties = 1,
        Texture = 2
    }
    public class MainViewModel : TSPropertyChanged, IUpdaterCheckCallback, IDisposable
    {
        // not actually a singleton, just a reference for contextmenu command binding
        public static MainViewModel Instance { get; private set; }

        public static List<string> PropertyTypes = new List<string>
        {
            "ArrayProperty",
            "BoolProperty",
            "ByteProperty",
            "FloatProperty",
            "IntProperty",
            "NameProperty",
            "ObjectProperty",
            "StrProperty",
            "StructProperty"
        }; //todo: turn this into an enum
        private readonly DataFormats.Format exportFormat = DataFormats.GetFormat(typeof(GpkExport).FullName);

        private readonly Logger logger;
        private readonly GpkStore gpkStore;
        private readonly WaveOut waveOut;
        private List<GpkExport>[] changedExports;
        private GpkPackage selectedPackage;
        private GpkExport selectedExport;
        private string selectedClass = "";
        private VorbisWaveReader waveReader;

        private bool _isTextureTabVisible;
        private string _logText;
        private string _statusBarText;
        private string _infoText;
        private string _oggPreviewButtonText;
        private bool _generalButtonsEnabled;
        private bool _dataButtonsEnabled;
        private bool _propertyButtonsEnabled;
        private int _progressValue;
        private readonly LogBuffer _logBuffer;
        private Tab _selectedTab = 0;
        private GpkTreeNode _selectedNode;
        private PropertyViewModel _selectedProperty;


        public PropertyViewModel SelectedProperty
        {
            get => _selectedProperty;
            set
            {
                if (_selectedProperty == value) return;
                _selectedProperty = value;
                N();
            }
        }



        public bool LogToUI
        {
            get => CoreSettings.Default.LogToUI;
            set
            {
                if (CoreSettings.Default.LogToUI == value) return;
                CoreSettings.Default.LogToUI = value;
                N();
                if (value)
                {
                    NLogConfig.EnableFormLogging();
                }
                else
                {
                    NLogConfig.DisableFormLogging();
                }
            }
        }
        public bool GeneralButtonsEnabled
        {
            get => _generalButtonsEnabled;
            set
            {
                if (_generalButtonsEnabled == value) return;
                _generalButtonsEnabled = value;
                N();
            }
        }
        public bool DataButtonsEnabled
        {
            get => _dataButtonsEnabled;
            set
            {
                if (_dataButtonsEnabled == value) return;
                _dataButtonsEnabled = value;
                N();
            }
        }

        public bool PropertyButtonsEnabled => selectedExport != null && _selectedTab == Tab.Properties;
        public bool ImageButtonsEnabled => selectedExport != null && _selectedTab == Tab.Texture && IsTextureTabVisible;
        public bool IsTextureTabVisible
        {
            get => _isTextureTabVisible;
            set
            {
                if (_isTextureTabVisible == value) return;
                _isTextureTabVisible = value;
                N();
                if (value == false && _selectedTab == Tab.Texture) SelectedTabIndex = (int)Tab.Info;
            }
        }
        public string LogText
        {
            get => _logText;
            set
            {
                if (_logText == value) return;
                _logText = value;
                N();
            }
        }
        public string StatusBarText
        {
            get => _statusBarText;
            set
            {
                if (_statusBarText == value) return;
                _statusBarText = value;
                N();
            }
        }
        public string InfoText
        {
            get => _infoText;
            set
            {
                if (_infoText == value) return;
                _infoText = value;
                N();
            }
        }
        public string OggPreviewButtonText
        {
            get => _oggPreviewButtonText;
            set
            {
                if (_oggPreviewButtonText == value) return;
                _oggPreviewButtonText = value;
                N();
            }
        }

        public int SelectedTabIndex
        {
            get => (int)_selectedTab;
            set
            {
                if (_selectedTab == (Tab)value) return;
                _selectedTab = (Tab)value;
                N();
                N(nameof(PropertyButtonsEnabled));
                N(nameof(ImageButtonsEnabled));

            }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (_progressValue == value) return;
                _progressValue = value;
                Debug.WriteLine(_progressValue);

                N();
            }
        }

        //todo: move preview image stuff to its own class
        #region PreviewImage
        private ImageSource _previewImage;
        private string _previewImageFormat;
        private string _previewImageName;

        public ImageSource PreviewImage
        {
            get => _previewImage;
            set
            {
                if (_previewImage == value) return;
                _previewImage = value;
                N();
            }
        }
        public string PreviewImageFormat
        {
            get => _previewImageFormat;
            set
            {
                if (_previewImageFormat == value) return;
                _previewImageFormat = value;
                N();
            }
        }
        public string PreviewImageName
        {
            get => _previewImageName;
            set
            {
                if (_previewImageName == value) return;
                _previewImageName = value;
                N();
            }
        }

        #endregion

        public ICommand OpenCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RefreshCommand { get; }

        public ICommand SwitchTabCommand { get; }

        public ICommand SavePropertiesCommand { get; }
        public ICommand ClearPropertiesCommand { get; }
        public ICommand ExportPropertiesToCsvCommand { get; }

        public ICommand ExportRawDataCommand { get; }
        public ICommand ImportRawDataCommand { get; }
        public ICommand RemoveObjectCommand { get; }
        public ICommand CopyObjectCommand { get; }
        public ICommand PasteObjectCommand { get; }
        public ICommand InsertObjectCommand { get; }
        public ICommand DeleteDataCommand { get; }
        public ICommand SaveSingleGpkCommand { get; }
        public ICommand PatchObjectMapperForSelectedPackageCommand { get; }

        public ICommand ExportDDSCommand { get; }
        public ICommand ImportDDSCommand { get; }
        public ICommand ExportOGGCommand { get; }
        public ICommand ImportOGGCommand { get; }

        public ICommand DecryptDatCommand { get; }
        public ICommand EncryptDatCommand { get; }
        public ICommand LoadMappingCommand { get; }
        public ICommand WriteMappingCommand { get; }
        public ICommand DumpAllTexturesCommand { get; }
        public ICommand DumpIconsCommand { get; }
        public ICommand DumpGPKObjectsCommand { get; }
        public ICommand MinimizeGpkCommand { get; }

        public ICommand SetPropsCustomCommand { get; }
        public ICommand SetFileSizeCommand { get; }
        public ICommand SetVolumeMultipliersCommand { get; }
        public ICommand AddNameCommand { get; }
        public ICommand BigBytePropImportCommand { get; }
        public ICommand BigBytePropExportCommand { get; }

        public GpkTreeNode TreeMain { get; }

        #region WindowParameters

        public GridLength LogSize
        {
            get => CoreSettings.Default.LogSize;
            set => CoreSettings.Default.LogSize = value;
        }
        public GridLength TreeViewSize
        {
            get => CoreSettings.Default.TreeViewSize;
            set => CoreSettings.Default.TreeViewSize = value;
        }
        public GridLength PropViewSize
        {
            get => CoreSettings.Default.PropViewSize;
            set => CoreSettings.Default.PropViewSize = value;
        }
        public GridLength TopSize
        {
            get => CoreSettings.Default.TopSize;
            set => CoreSettings.Default.TopSize = value;
        }
        public double WindowHeight
        {
            get => CoreSettings.Default.WindowSize.Height;
            set
            {
                if (CoreSettings.Default.WindowSize.Height == value) return;
                var oldSize = CoreSettings.Default.WindowSize;
                var newSize = new Size(oldSize.Width, value);
                CoreSettings.Default.WindowSize = newSize;
                N();
            }
        }
        public double WindowWidth
        {
            get => CoreSettings.Default.WindowSize.Width;
            set
            {
                if (CoreSettings.Default.WindowSize.Width == value) return;
                var oldSize = CoreSettings.Default.WindowSize;
                var newSize = new Size(value, oldSize.Height);
                CoreSettings.Default.WindowSize = newSize;
                N();
            }
        }
        public WindowState WindowState
        {
            get => CoreSettings.Default.WindowState;
            set
            {
                if (CoreSettings.Default.WindowState == value) return;
                CoreSettings.Default.WindowState = value;
                N();
            }
        }

        #endregion
        public MainViewModel()
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            Instance = this;

            //nlog
            NLogConfig.SetDefaultConfig(NLogConfig.NlogFormConfig.WPF);
            _logBuffer = new LogBuffer();
            _logBuffer.LinesFlushed += LogMessages;
            CustomEventTarget.LogMessageWritten += _logBuffer.AppendLine;
            logger = LogManager.GetLogger("GUI");
            logger.Info("Startup");

            //
            UpdateCheck.checkForUpdate(this);
            gpkStore = new GpkStore();
            gpkStore.PackagesChanged += OnPackagesChanged;
            TreeMain = new GpkTreeNode("");
            Properties = new TSObservableCollection<PropertyViewModel>();
            // audio
            waveOut = new WaveOut();
            waveOut.PlaybackStopped += OnPlaybackStopped;

            if (CoreSettings.Default.SaveDir == "")
                CoreSettings.Default.SaveDir = Directory.GetCurrentDirectory();

            if (CoreSettings.Default.OpenDir == "")
                CoreSettings.Default.OpenDir = Directory.GetCurrentDirectory();

            if (CoreSettings.Default.WorkingDir == "")
                CoreSettings.Default.WorkingDir = Directory.GetCurrentDirectory();

            //mappings
            if (CoreSettings.Default.LoadMappingOnStart && CoreSettings.Default.CookedPCPath != "")
            {
                new Task(() =>
                {
                    LoadAndParseMapping(CoreSettings.Default.CookedPCPath);
                }).Start();
            }

            //commands
            OpenCommand = new RelayCommand<string[]>(Open, checkParamType: false);
            SaveCommand = new RelayCommand<SaveMode>(Save, checkParamType: false);

            PatchObjectMapperForSelectedPackageCommand = new RelayCommand(PatchObjectMapperForSelectedPackage);

            RefreshCommand = new RelayCommand(Refresh);
            ClearCommand = new RelayCommand(Clear);

            SavePropertiesCommand = new RelayCommand(SaveProperties);
            ClearPropertiesCommand = new RelayCommand(ClearProperties);
            ExportPropertiesToCsvCommand = new RelayCommand(ExportPropertiesToCsv);

            ExportRawDataCommand = new RelayCommand(ExportRawData);
            ImportRawDataCommand = new RelayCommand(ImportRawData);
            RemoveObjectCommand = new RelayCommand(RemoveObject);
            CopyObjectCommand = new RelayCommand(CopyObject);
            PasteObjectCommand = new RelayCommand(PasteObject);
            InsertObjectCommand = new RelayCommand(InsertObject);
            DeleteDataCommand = new RelayCommand(DeleteData);

            ImportDDSCommand = new RelayCommand(ImportDDS);
            ExportDDSCommand = new RelayCommand(ExportDDS);
            ImportOGGCommand = new RelayCommand(ImportOGG);
            ExportOGGCommand = new RelayCommand(ExportOGG);

            SaveSingleGpkCommand = new RelayCommand(SaveSingleGpk);

            SwitchTabCommand = new RelayCommand<Tab>(GotoTab);

            DecryptDatCommand = new RelayCommand(DecryptDat);
            EncryptDatCommand = new RelayCommand(EncryptDat);
            LoadMappingCommand = new RelayCommand(LoadMapping);
            WriteMappingCommand = new RelayCommand(WriteMapping);
            DumpAllTexturesCommand = new RelayCommand(DumpAllTextures);
            DumpIconsCommand = new RelayCommand(DumpIcons);
            MinimizeGpkCommand = new RelayCommand(MinimizeGPK);
            DumpGPKObjectsCommand = new RelayCommand(DumpGPKObjects);

            SetPropsCustomCommand = new RelayCommand(SetPropsCustom);
            SetFileSizeCommand = new RelayCommand(SetFileSize);
            SetVolumeMultipliersCommand = new RelayCommand(SetVolumeMultipliers);
            AddNameCommand = new RelayCommand(AddName);

            BigBytePropImportCommand = new RelayCommand(BigBytePropImport);
            BigBytePropExportCommand = new RelayCommand(BigBytePropExport);

        }


        private void GotoTab(Tab tab)
        {
            if (!IsTextureTabVisible && tab == Tab.Texture) return;
            SelectedTabIndex = (int)tab;
        }

        private void Refresh()
        {
            Reset();
            DrawPackages();
        }

        private void Save(SaveMode mode)
        {
            var usePadding = false;
            var patchComposite = false;
            var addComposite = false;
            switch (mode)
            {
                case SaveMode.Rebuild: break;
                case SaveMode.RebuildPadding:
                    usePadding = true;
                    break;
                case SaveMode.Patched:
                    SavePatched();
                    return;
                case SaveMode.PatchedComposite:
                    usePadding = true;
                    patchComposite = true;
                    break;
                case SaveMode.AddedComposite:
                    addComposite = true;
                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            var start = DateTime.Now;
            var runningSavers = new List<IProgress>();
            var runningTasks = new List<Task>();


            if (gpkStore.LoadedGpkPackages.Count == 0)
                return;

            //do it
            try
            {
                this.gpkStore.SaveGpkListToFiles(gpkStore.LoadedGpkPackages, usePadding, patchComposite, addComposite, runningSavers, runningTasks);
                //display info while loading
                while (!Task.WaitAll(runningTasks.ToArray(), 50))
                {
                    DisplayStatus(runningSavers, "Saving", start);
                }

                //Diplay end info
                DisplayStatus(runningSavers, "Saving", start);

                logger.Info("Saving done!");
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "Save failure!");
            }

            void SavePatched()
            {
                var save = false;
                if (changedExports != null)
                {
                    for (var i = 0; i < changedExports.Length; i++)
                    {
                        var list = changedExports[i];
                        if (list.Count <= 0) continue;
                        try
                        {
                            var tmpS = new Writer();
                            var package = gpkStore.LoadedGpkPackages[i];
                            var savepath = package.Path + "_patched";
                            tmpS.SaveReplacedExport(package, savepath, list);
                            logger.Info($"Saved the changed data of package '{package.Filename} to {savepath}'!");
                            save = true;
                        }
                        catch (Exception ex)
                        {
                            logger.Fatal(ex, "Save failure! ");
                        }
                    }
                }

                if (!save)
                {
                    logger.Info("Nothing to save in PatchMode!");
                }
            }
        }
        private void Clear()
        {
            Reset();
            changedExports = null;
            ResetOggPreview();
            gpkStore.clearGpkList();
        }
        private void Open(string[] providedFiles = null)
        {
            var files = providedFiles ?? MiscFuncs.GenerateOpenDialog(true, false);

            if (files.Length == 0) return;

            var start = DateTime.Now;
            var runningReaders = new List<IProgress>();
            var runningTasks = new List<Task>();


            foreach (var path in files)
            {
                if (!File.Exists(path)) continue;
                var newTask = new Task(() =>
                {
                    var reader = new Reader();
                    runningReaders.Add(reader);
                    gpkStore.loadGpk(path, reader, false);
                });
                runningTasks.Add(newTask);
                newTask.Start();
            }

            Task.Run(() =>
            {
                //display info while loading
                while (!Task.WaitAll(runningTasks.ToArray(), 10))
                {
                    DisplayStatus(runningReaders, "Loading", start);
                }


            }).ContinueWith(t =>
            {
                //for patchmode
                Array.Resize(ref changedExports, gpkStore.LoadedGpkPackages.Count);
                for (var i = 0; i < changedExports.Length; i++)
                {
                    changedExports[i] = new List<GpkExport>();
                }
                //Diplay end info
                DisplayStatus(runningReaders, "Loading", start);

                DrawPackages();
            });
        }
        private void DisplayStatus(List<IProgress> list, string tag, DateTime start)
        {
            if (list.Count == 0)
            {
                Debug.WriteLine(DateTime.Now + " DisplayStatus list is empty");
                return;
            }

            long actual = 0, total = 0, finished = 0;
            foreach (var p in list)
            {
                if (p == null) continue;
                var stat = p.GetStatus();


                if (stat.subGpkCount > 1)
                {
                    //dont show actual objects, just the sub-file count
                    actual += stat.subGpkDone;
                    total += stat.subGpkCount;
                    if (actual == total) finished++;
                }
                else
                {
                    //normal gpk 
                    actual += stat.progress;
                    total += stat.totalobjects;
                    if (stat.finished) finished++;
                }
            }

            if (finished < list.Count)
            {
                if (total > 0) ProgressValue = (int)(((double)actual / (double)total) * 100);
                StatusBarText = $"[{tag}] Finished {finished}/{list.Count}";
            }
            else
            {
                total = 0;
                var builder = new StringBuilder();
                builder.AppendLine($"[{tag} Task Info]");
                foreach (var p in list)
                {
                    var stat = p.GetStatus();
                    total += stat.time;
                    builder.AppendLine($"Task {stat.name}: {stat.time}ms");
                }
                builder.AppendLine($"Avg Worktime: {total / list.Count}ms");
                builder.AppendLine($"Total elapsed Time: {(int)DateTime.Now.Subtract(start).TotalMilliseconds}ms");

                InfoText = builder.ToString();
                ProgressValue = 0;
                StatusBarText = "Ready";
            }
        }
        private void OnPackagesChanged()
        {

        }
        public void PostUpdateResult(bool updateAvailable)
        {
            if (updateAvailable)
            {
                logger.Info("A newer version is available. Download it at https://github.com/GoneUp/GPK_RePack/releases");
            }
        }
        private void LogMessages(string msg)
        {
            LogText += msg;
        }
        private void Reset()
        {
            selectedExport = null;
            selectedPackage = null;
            _selectedNode = null;
            selectedClass = "";
            InfoText = "";
            StatusBarText = "Ready";
            GeneralButtonsEnabled = false;
            DataButtonsEnabled = false;
            IsTextureTabVisible = false;
            N(nameof(PropertyButtonsEnabled));
            ProgressValue = 0;
            PreviewImage = null;
            TreeMain.Children.Clear();
            Properties.Clear();
        }
        public void Dispose()
        {
            waveOut?.Dispose();
            waveReader?.Dispose();
        }

        private void DrawPackages()
        {
            //we may get calls out of gpkStore
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(DrawPackages);
                return;
            }

            TreeMain.Children.Clear();

            if (CoreSettings.Default.EnableSortTreeNodes)
            {
                TreeMain.TreeViewNodeSorter = new MiscFuncs.NodeSorter();
            }

            //var total = gpkStore.LoadedGpkPackages.Count;
            //var done = 0;
            ProgressValue = 0;
            foreach (var package in gpkStore.LoadedGpkPackages)
            {
                //Task.Run(() =>
                //{
                //    Dispatcher.InvokeAsync(() =>
                //    {

                var packageNode = new GpkTreeNode(package.Filename, package)
                {
                    IsPackage = true,
                    Index = gpkStore.LoadedGpkPackages.IndexOf(package)
                };

                var classNodes = new Dictionary<string, GpkTreeNode>();
                GpkTreeNode importsNode = null;
                GpkTreeNode exportsNode = null;

                if (CoreSettings.Default.ShowImports)
                {
                    foreach (var tmp in package.ImportList.OrderByDescending(pair => pair.Value.ObjectName)
                        .Reverse())
                    {
                        var key = tmp.Value.UID;
                        var value = tmp.Value.ObjectName;
                        if (CoreSettings.Default.UseUID) value = key;

                        switch (CoreSettings.Default.ViewMode)
                        {
                            case ViewMode.Normal:
                                if (importsNode == null)
                                {
                                    importsNode = new GpkTreeNode("Imports");
                                    packageNode.AddNode(importsNode);
                                }

                                importsNode.AddNode(new GpkTreeNode(value, key) { IsImport = true });
                                break;
                            case ViewMode.Class:
                                GpkTreeNode.CheckClassNode(tmp.Value.ClassName, classNodes, packageNode);
                                var n = new GpkTreeNode( /*key,*/ value, key) { Class = tmp.Value.ClassName };
                                classNodes[tmp.Value.ClassName].AddNode(n);
                                break;
                        }

                    }
                }

                //Exports
                foreach (var pair in package.ExportList.OrderByDescending(pair => pair.Value.ObjectName).Reverse())
                {
                    var export = pair.Value;
                    var key = export.UID;
                    var value = export.ObjectName;
                    if (CoreSettings.Default.UseUID) value = key;

                    switch (CoreSettings.Default.ViewMode)
                    {
                        case ViewMode.Normal:
                            if (exportsNode == null)
                            {
                                exportsNode = new GpkTreeNode("Exports");
                                packageNode.AddNode(exportsNode);
                            }

                            exportsNode.AddNode(new GpkTreeNode( /*key,*/ value, key));
                            break;
                        case ViewMode.Class:
                            GpkTreeNode.CheckClassNode(export.ClassName, classNodes, packageNode);
                            var cn = new GpkTreeNode( /*key,*/ value, key) { Class = export.ClassName };
                            classNodes[export.ClassName].AddNode(cn);
                            break;

                        case ViewMode.Package:
                            GpkTreeNode.CheckClassNode(pair.Value.PackageName, classNodes, packageNode);
                            var pn = new GpkTreeNode( /*key,*/ value, key) { Class = export.ClassName };
                            classNodes[export.PackageName].AddNode(pn);
                            break;

                    }

                }

                TreeMain.AddNode(packageNode);
                //        Interlocked.Increment(ref done);
                //        ProgressValue = done *100 / total;
                //        if (ProgressValue == 100) ProgressValue = 0;
                //    }, DispatcherPriority.Background);
                //});
            }
        }
        public void SelectNode(GpkTreeNode node)
        {
            if (node.IsPackage) //level = 1 (rootlevel = 0)
            {
                GeneralButtonsEnabled = true;
                DataButtonsEnabled = true;
                selectedPackage = gpkStore.LoadedGpkPackages[Convert.ToInt32(node.Index)];
                try
                {
                    InfoText = selectedPackage.ToString();
                }
                catch (Exception ex)
                {
                    logger.Fatal(ex, "Failed to show package info.");
                    InfoText = $"Failed to show package info: \n{ex}";
                }
            }
            else if (node.Level == 2 && CoreSettings.Default.ViewMode == ViewMode.Class)
            {
                selectedPackage = gpkStore.LoadedGpkPackages[Convert.ToInt32(node.FindPackageNode().Index)];
                selectedClass = node.Key;

                DataButtonsEnabled = true;
            }
            else if (node.Level == 3 && CoreSettings.Default.ViewMode == ViewMode.Normal || node.IsLeaf)
            {
                var package = gpkStore.LoadedGpkPackages[Convert.ToInt32(node.FindPackageNode().Index)];
                var selected = package.GetObjectByUID(node.Key);

                if (selected is GpkImport imp)
                {
                    InfoText = imp.ToString();
                }
                else if (selected is GpkExport exp)
                {
                    //buttons
                    GeneralButtonsEnabled = true;
                    DataButtonsEnabled = true;
                    selectedExport = exp;
                    selectedPackage = package;

                    N(nameof(PropertyButtonsEnabled));
                    RefreshExportInfo();
                }
            }

            _selectedNode = node;
        }
        private void RefreshExportInfo()
        {
            //tabs
            InfoText = selectedExport.ToString();
            DrawGrid(selectedPackage, selectedExport);

            PreviewImage = null;
            if (selectedExport.Payload != null && selectedExport.Payload is Texture2D)
            {
                IsTextureTabVisible = true;
                var image = (Texture2D)selectedExport.Payload;
                var ddsFile = new DdsFile();
                var imageStream = image.GetObjectStream();
                if (imageStream != null)
                {
                    ddsFile.Load(image.GetObjectStream());

                    PreviewImage = ddsFile.BitmapSource; //= TextureTools.BitmapFromSource(ddsFile.BitmapSource);
                    PreviewImageFormat = image.parsedImageFormat.ToString();
                    PreviewImageName = selectedExport.ObjectName;
                    //boxImagePreview.BackColor = CoreSettings.Default.PreviewColor; //TODO add bg in xaml

                    //workaround for a shrinking window
                    //scaleFont(); //todo?
                    //resizePiutureBox(); //todo?
                }
            }
            else
            {
                IsTextureTabVisible = false;
            }
        }
        public TSObservableCollection<PropertyViewModel> Properties { get; }
        private void SaveSingleGpk()
        {
            if (selectedPackage == null || _selectedNode == null || !_selectedNode.IsPackage) return;
            var packages = new List<GpkPackage> { selectedPackage };
            var writerList = new List<IProgress>();
            var taskList = new List<Task>();

            this.gpkStore.SaveGpkListToFiles(packages, false, false, false, writerList, taskList);

            //wait
            while (!Task.WaitAll(taskList.ToArray(), 50))
            {

            }
            logger.Info("Single export done!");
        }


        private void DrawGrid(GpkPackage package, GpkExport export)
        {
            Properties.Clear();


            foreach (var iProp in export.Properties)
            {
                var prop = (GpkBaseProperty)iProp;
                var row = new PropertyViewModel
                {
                    Name = prop.name,
                    PropertyType = prop.type,
                    Size = iProp.RecalculateSize(),
                    ArrayIndex = prop.arrayIndex,
                    InnerTypes = package.InnerTypes,
                    InnerType = "None"
                };

                // could be converted to two switch expression if moving to netcore
                switch (prop)
                {
                    case GpkArrayProperty tmpArray:
                        row.Value = tmpArray.GetValueHex();
                        //todo: context menu
                        #region ContextMenu
                        //row.ContextMenuStrip = new ContextMenuStrip();
                        //row.ContextMenuStrip.Items.Add(
                        //    new ToolStripButton("Export", null,
                        //        (sender, args) =>
                        //        {
                        //            BigBytePropExport(tmpArray);
                        //        }));
                        //row.ContextMenuStrip.Items.Add(
                        //    new ToolStripButton("Import", null,
                        //       (sender, args) =>
                        //       {
                        //           BigBytePropImport(tmpArray);
                        //       })
                        //    ); 
                        #endregion
                        break;
                    case GpkStructProperty tmpStruct:
                        row.InnerType = tmpStruct.innerType;
                        row.Value = tmpStruct.GetValueHex();
                        break;
                    case GpkNameProperty tmpName:
                        row.Value = tmpName.value; 
                        row.EditAsEnum = true;
                        break;
                    case GpkObjectProperty tmpObj:
                        row.Value = tmpObj.objectName; 
                        row.EditAsEnum = true;
                        break;
                    case GpkByteProperty tmpByte when tmpByte.size == 8 || tmpByte.size == 16:
                        row.InnerType = tmpByte.enumType;
                        row.Value = tmpByte.nameValue; 
                        row.EditAsEnum = true;
                        break;
                    case GpkByteProperty tmpByte:
                        row.InnerType = tmpByte.enumType;
                        row.Value = tmpByte.byteValue;
                        break;
                    case GpkFloatProperty tmpFloat:
                        row.Value = tmpFloat.value;
                        break;
                    case GpkIntProperty tmpInt:
                        row.Value = tmpInt.value;
                        break;
                    case GpkStringProperty tmpString:
                        row.Value = tmpString.value;
                        break;
                    case GpkBoolProperty tmpBool:
                        row.Value = tmpBool.value;
                        break;
                    default:
                        logger.Info("Unk Prop?!?");
                        break;
                }

                //todo: button with expanded view
                //const int maxInputLength = 32;//32767; //from winforms
                //if (row.Value != null && row.Value.ToString().Length > maxInputLength)
                //{
                //    row.Value = "[##TOO_LONG##]";
                //}

                Properties.Add(row);
            }
        }
        private void SaveProperties()
        {
            //1. compare and alter
            //or 2. read and rebuild  -- this. we skip to the next in case of user input error.

            if (selectedExport == null || selectedPackage == null)
            {
                logger.Info("save failed");
                return;
            }

            var list = new List<IProperty>();
            foreach (var prop in Properties)
            {
                try
                {
                    list.Add(prop.GetIProperty(selectedPackage));
                }
                catch (Exception ex)
                {

                    logger.Info("Failed to save row {0}, {1}!", Properties.IndexOf(prop), ex);
                }

            }

            selectedExport.Properties = list;
            logger.Info("Saved properties of export {0}.", selectedExport.UID);
        }
        private void ClearProperties()
        {
            if (selectedExport == null || selectedPackage == null)
            {
                logger.Info("save failed");
                return;
            }

            selectedExport.Properties.Clear();
            DrawGrid(selectedPackage, selectedExport);
            logger.Info("Cleared!");
        }
        private void ExportPropertiesToCsv()
        {
            //JSON?
            //CSV?
            //XML?
            //Name;Type;Size;ArrayIndex;InnerType;Value

            var builder = new StringBuilder();
            builder.AppendLine("Name;Type;Size;ArrayIndex;InnerType;Value");

            foreach (var row in Properties)
            {
                if (/*row.IsNewRow ||*/ row.Name == null)
                    continue;

                var csvRow = string.Format("{0};{1};{2};{3};{4};{5}",
                    row.Name,
                    row.PropertyType,
                    row.Size,
                    row.ArrayIndex,
                    row.InnerType,
                    row.Value.ToString()
                );
                builder.AppendLine(csvRow);
            }


            var path = MiscFuncs.GenerateSaveDialog(selectedExport.ObjectName, ".csv");
            if (path == "") return;

            Task.Factory.StartNew(() => File.WriteAllText(path, builder.ToString(), Encoding.UTF8));
        }

        // main - done
        // load/save - done
        // displaygpk - done
        // editgpk - done
        // image - done

        private void ExportRawData()
        {
            if (selectedExport != null)
            {
                if (selectedExport.Data == null)
                {
                    logger.Info("Length is zero. Nothing to export");
                    return;
                }

                var path = MiscFuncs.GenerateSaveDialog(selectedExport.ObjectName, "");
                if (path == "") return;
                DataTools.WriteExportDataFile(path, selectedExport);
            }
            else if (selectedPackage != null && selectedClass != "")
            {
                var exports = selectedPackage.GetExportsByClass(selectedClass);

                if (exports.Count == 0)
                {
                    logger.Info("No exports found for class {0}.", selectedClass);
                    return;
                }


                var dialog = new FolderBrowserDialog { SelectedPath = CoreSettings.Default.SaveDir };
                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    CoreSettings.Default.SaveDir = dialog.SelectedPath;

                    foreach (var exp in exports)
                    {
                        if (exp.Data == null) continue;
                        DataTools.WriteExportDataFile($"{dialog.SelectedPath}\\{exp.ObjectName}", exp);
                        logger.Trace("save for " + exp.UID);
                    }
                }
            }
            else if (selectedPackage != null)
            {
                var dialog = new FolderBrowserDialog { SelectedPath = CoreSettings.Default.SaveDir };
                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    CoreSettings.Default.SaveDir = dialog.SelectedPath;

                    foreach (var exp in selectedPackage.ExportList.Values)
                    {
                        if (exp.Data == null) continue;
                        DataTools.WriteExportDataFile($"{dialog.SelectedPath}\\{exp.ClassName}\\{exp.ObjectName}", exp);
                        logger.Trace("save for " + exp.UID);
                    }
                }
            }

            logger.Info("Data was saved!");
        }
        private void ImportRawData()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }
            if (selectedExport.Data == null)
            {
                logger.Trace("no export data");
                return;
            }

            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;
            var path = files[0];

            if (!File.Exists(path)) return;

            var buffer = File.ReadAllBytes(path);

            if (CoreSettings.Default.PatchMode)
            {
                //if (treeMain.SelectedNode.Parent.Parent == null) return;
                //var packageIndex = Convert.ToInt32(treeMain.SelectedNode.Parent.Parent.Name);
                var package = _selectedNode.FindPackageNode();
                var packageIndex = package.Index;
                if (buffer.Length > selectedExport.Data.Length)
                {
                    //Too long, not possible without rebuiling the gpk
                    logger.Info("File size too big for PatchMode. Size: " + buffer.Length + " Maximum Size: " +
                                selectedExport.Data.Length);
                    return;
                }

                //selectedExport.data = buffer;
                Array.Copy(buffer, selectedExport.Data, buffer.Length);

                changedExports[packageIndex].Add(selectedExport);

            }
            else
            {
                //Rebuild Mode
                //We force the rebuilder to recalculate the size. (atm we dont know how big the propertys are)
                logger.Trace($"rebuild mode old size {selectedExport.Data.Length} new size {buffer.Length}");

                selectedExport.Data = buffer;
                selectedExport.GetDataSize();
                selectedPackage.Changes = true;
            }


            logger.Info($"Replaced the data of {selectedExport.ObjectName} successfully! Dont forget to save.");



        }
        private void RemoveObject()
        {
            if (selectedPackage != null && selectedExport == null)
            {
                gpkStore.DeleteGpk(selectedPackage);

                logger.Info("Removed package {0}...", selectedPackage.Filename);

                TreeMain.Children.Remove(_selectedNode);
                selectedPackage = null;
                GeneralButtonsEnabled = false;
                RefreshIndexes();
                //GC.Collect(); //memory 
            }
            else if (selectedPackage != null && selectedExport != null)
            {
                selectedPackage.ExportList.Remove(selectedPackage.GetObjectKeyByUID(selectedExport.UID));

                logger.Info("Removed object {0}...", selectedExport.UID);

                selectedExport = null;

                _selectedNode.Remove();
                _selectedNode = null;
            }
        }

        private void RefreshIndexes()
        {
            Task.Run(() =>
            {
                foreach (var pkgNode in TreeMain.Children)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        pkgNode.Index = gpkStore.LoadedGpkPackages.IndexOf(pkgNode.SourcePackage);
                    }, DispatcherPriority.Background);
                }
            });
        }

        private void CopyObject()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }

            try
            {
                var memorystream = new MemoryStream();
                var bf = new BinaryFormatter();
                bf.Serialize(memorystream, selectedExport);
            }
            catch (Exception ex)
            {
                logger.Debug(ex);
                logger.Info("Serialize failed, check debug log");
                return;
            }

            Clipboard.SetData(exportFormat.Name, selectedExport);
            logger.Info("Made a copy of {0}...", selectedExport.UID);
        }
        private void PasteObject()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }
            var copyExport = (GpkExport)Clipboard.GetData(exportFormat.Name);

            if (copyExport == null)
            {
                logger.Info("copy paste fail");
                return;
            }

            logger.Trace(CoreSettings.Default.CopyMode);
            var option = "";

            switch (CoreSettings.Default.CopyMode)
            {
                case CopyMode.All:
                    DataTools.ReplaceAll(copyExport, selectedExport);
                    option = "everything";
                    break;
                case CopyMode.DataProps:
                    DataTools.ReplaceProperties(copyExport, selectedExport);
                    DataTools.ReplaceData(copyExport, selectedExport);
                    option = "data and properties";
                    break;
                case CopyMode.Data:
                    DataTools.ReplaceData(copyExport, selectedExport);
                    option = "data";
                    break;
                case CopyMode.Props:
                    DataTools.ReplaceProperties(copyExport, selectedExport);
                    option = "properties";
                    break;
                default:
                    logger.Info("Your setting file is broken. Go to settings windows and select a copymode.");
                    break;

            }

            copyExport.GetDataSize();
            SelectNode(_selectedNode);
            logger.Info("Pasted the {0} of {1} to {2}", option, copyExport.UID, selectedExport.UID);
        }
        private void InsertObject()
        {
            if (selectedPackage == null)
            {
                logger.Trace("no selected package to insert");
                return;
            }
            var copyExport = (GpkExport)Clipboard.GetData(exportFormat.Name);

            if (copyExport == null)
            {
                logger.Info("copy paste fail");
                return;
            }

            selectedPackage.CopyObjectFromPackage(copyExport.UID, copyExport.motherPackage, true);

            RedrawPackage(TreeMain.Children.FirstOrDefault(p => p.SourcePackage == selectedPackage));
            logger.Info("Insert done");
        }

        private void RedrawPackage(GpkTreeNode packageNode)
        {
            var package = packageNode.SourcePackage;
            packageNode.Children.Clear();
            packageNode.Index = gpkStore.LoadedGpkPackages.IndexOf(package);
            //var packageNode = new GpkTreeNode(package.Filename, package)
            //{
            //    IsPackage = true,
            //    Index = gpkStore.LoadedGpkPackages.IndexOf(package)
            //};



            var classNodes = new Dictionary<string, GpkTreeNode>();
            GpkTreeNode importsNode = null;
            GpkTreeNode exportsNode = null;

            if (CoreSettings.Default.ShowImports)
            {
                foreach (var tmp in package.ImportList.OrderByDescending(pair => pair.Value.ObjectName)
                    .Reverse())
                {
                    var key = tmp.Value.UID;
                    var value = tmp.Value.ObjectName;
                    if (CoreSettings.Default.UseUID) value = key;

                    switch (CoreSettings.Default.ViewMode)
                    {
                        case ViewMode.Normal:
                            if (importsNode == null)
                            {
                                importsNode = new GpkTreeNode("Imports");
                                packageNode.AddNode(importsNode);
                            }

                            importsNode.AddNode(new GpkTreeNode(value, key) { IsImport = true });
                            break;
                        case ViewMode.Class:
                            GpkTreeNode.CheckClassNode(tmp.Value.ClassName, classNodes, packageNode);
                            var n = new GpkTreeNode( /*key,*/ value, key) { Class = tmp.Value.ClassName };
                            classNodes[tmp.Value.ClassName].AddNode(n);
                            break;
                    }

                }
            }

            //Exports
            foreach (var pair in package.ExportList.OrderByDescending(pair => pair.Value.ObjectName).Reverse())
            {
                var export = pair.Value;
                var key = export.UID;
                var value = export.ObjectName;
                if (CoreSettings.Default.UseUID) value = key;

                switch (CoreSettings.Default.ViewMode)
                {
                    case ViewMode.Normal:
                        if (exportsNode == null)
                        {
                            exportsNode = new GpkTreeNode("Exports");
                            packageNode.AddNode(exportsNode);
                        }

                        exportsNode.AddNode(new GpkTreeNode( /*key,*/ value, key));
                        break;
                    case ViewMode.Class:
                        GpkTreeNode.CheckClassNode(export.ClassName, classNodes, packageNode);
                        var cn = new GpkTreeNode( /*key,*/ value, key) { Class = export.ClassName };
                        classNodes[export.ClassName].AddNode(cn);
                        break;

                    case ViewMode.Package:
                        GpkTreeNode.CheckClassNode(pair.Value.PackageName, classNodes, packageNode);
                        var pn = new GpkTreeNode( /*key,*/ value, key) { Class = export.ClassName };
                        classNodes[export.PackageName].AddNode(pn);
                        break;

                }

            }
        }

        private void DeleteData()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }

            if (selectedExport.Data == null)
            {
                logger.Trace("no export data");
                return;
            }

            selectedExport.Loader = null;
            selectedExport.Data = null;
            selectedExport.DataPadding = null;
            selectedExport.Payload = null;
            selectedExport.GetDataSize();

            SelectNode(_selectedNode);
        }

        private void ImportDDS()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }

            if (selectedExport.Payload == null || !(selectedExport.Payload is Texture2D))
            {
                logger.Info("Not a Texture2D object");
                return;
            }

            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;

            if (files[0] != "" && File.Exists(files[0]))
            {
                TextureTools.importTexture(selectedExport, files[0]);
                RefreshExportInfo();
            }
        }
        private void ExportDDS()
        {
            if (selectedExport == null)
            {
                logger.Trace("no selected export");
                return;
            }

            if (selectedExport.Payload == null || !(selectedExport.Payload is Texture2D))
            {
                logger.Info("Not a Texture2D object");
                return;
            }

            var path = MiscFuncs.GenerateSaveDialog(selectedExport.ObjectName, ".dds");
            if (path != "")
            {
                new Task(() => TextureTools.exportTexture(selectedExport, path)).Start();
            }


        }

        private void ImportOGG()
        {
            try
            {
                if (selectedExport != null)
                {
                    var files = MiscFuncs.GenerateOpenDialog(false);
                    if (files.Length == 0) return;

                    if (File.Exists(files[0]))
                    {
                        SoundwaveTools.ImportOgg(selectedExport, files[0]);
                        SelectNode(_selectedNode);
                        logger.Info("Import successful.");
                    }
                    else
                    {
                        logger.Info("File not found.");
                    }

                }
                else if (selectedPackage != null && selectedClass == "Core.SoundNodeWave")
                {
                    var exports = selectedPackage.GetExportsByClass(selectedClass);

                    var dialog = new FolderBrowserDialog();
                    dialog.SelectedPath = Path.GetDirectoryName(CoreSettings.Default.SaveDir);
                    var result = dialog.ShowDialog();

                    if (result == DialogResult.OK)
                    {
                        CoreSettings.Default.SaveDir = dialog.SelectedPath;

                        var files = Directory.GetFiles(dialog.SelectedPath);

                        foreach (var file in files)
                        {
                            var filename = Path.GetFileName(file); //AttackL_02.ogg
                            var oggname = filename.Remove(filename.Length - 4);

                            if (oggname == "") continue;

                            foreach (var exp in exports)
                            {
                                if (exp.ObjectName == oggname)
                                {
                                    SoundwaveTools.ImportOgg(exp, file);
                                    logger.Trace("Matched file {0} to export {1}!", filename, exp.ObjectName);
                                    break;
                                }
                            }


                        }


                        logger.Info("Mass import to {0} was successful.", dialog.SelectedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "Import failure!");
            }
        }
        private void ExportOGG()
        {

            if (selectedExport != null && selectedExport.ClassName == "Core.SoundNodeWave")
            {
                var path = MiscFuncs.GenerateSaveDialog(selectedExport.ObjectName, ".ogg");
                if (path != "")
                    SoundwaveTools.ExportOgg(selectedExport, path);
            }
            else if (selectedPackage != null && selectedClass == "Core.SoundNodeWave")
            {
                var exports = selectedPackage.GetExportsByClass(selectedClass);

                if (exports.Count == 0)
                {
                    logger.Info("No oggs found for class {0}.", selectedClass);
                    return;
                }


                var dialog = new FolderBrowserDialog();
                dialog.SelectedPath = Path.GetDirectoryName(CoreSettings.Default.SaveDir);
                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    CoreSettings.Default.SaveDir = dialog.SelectedPath;

                    foreach (var exp in exports)
                    {
                        SoundwaveTools.ExportOgg(exp, string.Format("{0}\\{1}.ogg", dialog.SelectedPath, exp.ObjectName));
                        logger.Trace("ogg save for " + exp.UID);
                    }

                    logger.Info("Mass export to {0} was successful.", dialog.SelectedPath);
                }
            }
        }
        private void AddEmptyOGG()
        {
            if (selectedExport != null)
            {
                SoundwaveTools.ImportOgg(selectedExport, "fake");
                SelectNode(_selectedNode);
            }
        }

        //todo: wrap ogg preview stuff in its own class and add seeking support
        private void btnOggPreview_Click(object sender, EventArgs e)
        {
            try
            {
                if (selectedExport != null && selectedExport.Payload is Soundwave && waveOut.PlaybackState == PlaybackState.Stopped)
                {
                    var wave = (Soundwave)selectedExport.Payload;
                    waveReader = new VorbisWaveReader(new MemoryStream(wave.oggdata));
                    waveOut.Init(waveReader);
                    waveOut.Play();
                    OggPreviewButtonText = "Stop Preview";
                }
                else if (waveOut != null && waveOut.PlaybackState == PlaybackState.Playing)
                {
                    ResetOggPreview();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Playback Error");
            }
        }
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            ResetOggPreview();
        }
        private void ResetOggPreview()
        {
            if (waveOut != null && waveOut.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Stop();
                waveReader.Close();
                waveReader.Dispose();
            }

            waveReader = null;
            OggPreviewButtonText = "Ogg Preview";
        }

        private void PatchObjectMapperForSelectedPackage()
        {
            if (!IsPackageSelected()) return;

            gpkStore.MultiPatchObjectMapper(selectedPackage, CoreSettings.Default.CookedPCPath);
        }

        #region composite gpk
        private void DecryptDat()
        {
            var files = MiscFuncs.GenerateOpenDialog(false, true);
            if (files.Length == 0) return;

            var outfile = MiscFuncs.GenerateSaveDialog("decrypt", ".txt");

            new Task(() =>
            {
                logger.Info("Decryption is running in the background");

                MapperTools.DecryptAndWriteFile(files[0], outfile);

                logger.Info("Decryption done");

            }).Start();
        }
        private void EncryptDat()
        {
            var files = MiscFuncs.GenerateOpenDialog(false, true);
            if (files.Length == 0) return;

            var outfile = MiscFuncs.GenerateSaveDialog("encrypt", ".txt");

            new Task(() =>
            {
                logger.Info("Encryption is running in the background");

                MapperTools.EncryptAndWriteFile(files[0], outfile);

                logger.Info("Encryption done");

            }).Start();
        }
        private void LoadMapping()
        {
            if (gpkStore.CompositeMap.Count > 0)
            {
                new MapperWindow(gpkStore).Show();
                return;
            }

            var dialog = new FolderBrowserDialog();
            if (CoreSettings.Default.CookedPCPath != "")
                dialog.SelectedPath = CoreSettings.Default.CookedPCPath;
            dialog.Description = "Select a folder with PkgMapper.dat and CompositePackageMapper.dat in it. Normally your CookedPC folder.";
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;

            var path = dialog.SelectedPath + "\\";
            CoreSettings.Default.CookedPCPath = path;

            LoadAndParseMapping(path);

            new MapperWindow(gpkStore).Show();
        }
        private void WriteMapping()
        {
            var dialog = new FolderBrowserDialog();
            if (CoreSettings.Default.WorkingDir != "")
                dialog.SelectedPath = CoreSettings.Default.WorkingDir;
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;

            var path = dialog.SelectedPath + "\\";

            MapperTools.WriteMappings(path, gpkStore, true, true);
        }
        private void DumpAllTextures()
        {
            //cookedpc path, outdir path
            var dialog = new FolderBrowserDialog();
            if (CoreSettings.Default.CookedPCPath != "")
                dialog.SelectedPath = CoreSettings.Default.CookedPCPath + "\\";
            dialog.Description = "Select a folder with PkgMapper.dat and CompositePackageMapper.dat in it. Normally your CookedPC folder.";
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;


            var path = dialog.SelectedPath + "\\";
            gpkStore.BaseSearchPath = path;

            CoreSettings.Default.CookedPCPath = path;
            MapperTools.ParseMappings(path, gpkStore);

            var subCount = gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", gpkStore.CompositeMap.Count, subCount);

            //selection
            var text = "";//todo Microsoft.VisualBasic.Interaction.InputBox("Select range of composite gpks to load. Format: n-n [e.g. 1-5] or empty for all.", "Selection", "");
            var filterList = FilterCompositeList(text);

            //save dir
            dialog = new FolderBrowserDialog
            {
                SelectedPath = CoreSettings.Default.WorkingDir,
                Description = "Select your output dir"
            };
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;

            logger.Warn("Warning: This function can be ultra long running (hours) and unstable. Monitor logfile and output folder for progress.");
            logger.Warn("Disabling logging, dump is running in the background. Consider setting file logging to only info.");

            NLogConfig.DisableFormLogging();
            var outDir = dialog.SelectedPath;
            new Task(() => MassDumper.DumpMassTextures(gpkStore, outDir, filterList)).Start();
        }
        private void DumpIcons()
        {
            //cookedpc path, outdir path
            var dialog = new FolderBrowserDialog();
            if (CoreSettings.Default.CookedPCPath != "")
                dialog.SelectedPath = CoreSettings.Default.CookedPCPath;
            dialog.Description = "Select a folder with PkgMapper.dat and CompositePackageMapper.dat in it. Normally your CookedPC folder.";
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;

            var path = dialog.SelectedPath;
            gpkStore.BaseSearchPath = path;
            CoreSettings.Default.CookedPCPath = path;
            MapperTools.ParseMappings(path, gpkStore);

            var subCount = gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", gpkStore.CompositeMap.Count, subCount);
            var list = FilterCompositeList("");
            //save dir
            dialog = new FolderBrowserDialog
            {
                SelectedPath = CoreSettings.Default.WorkingDir,
                Description = "Select your output dir"
            };
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;
            logger.Warn("Warning: This function can be ultra long running (hours) and unstable. Monitor logfile and output folder for progress.");
            logger.Warn("Disabling logging, dump is running in the background. Consider setting file logging to only info.");

            NLogConfig.DisableFormLogging();
            var outDir = dialog.SelectedPath;
            new Task(() => MassDumper.DumpMassIcons(gpkStore, outDir, list)).Start();

        }
        private Dictionary<string, List<CompositeMapEntry>> FilterCompositeList(string text)
        {
            try
            {
                if (text != "" && text.Split('-').Length > 0)
                {
                    var start = Convert.ToInt32(text.Split('-')[0]) - 1;
                    var end = Convert.ToInt32(text.Split('-')[1]) - 1;
                    var filterCompositeList = gpkStore.CompositeMap.Skip(start).Take(end - start + 1).ToDictionary(k => k.Key, v => v.Value);
                    logger.Info("Filterd down to {0} GPKs.", end - start + 1);
                    return filterCompositeList;
                }
                else
                {
                    return gpkStore.CompositeMap;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "filter fail");
                return gpkStore.CompositeMap;
            }
        }
        private void MinimizeGPK()
        {
            if (!IsPackageSelected()) return;

            DataTools.RemoveObjectRedirects(selectedPackage);
            if (_selectedNode?.SourcePackage == selectedPackage)
                RedrawPackage(_selectedNode);
            else
                DrawPackages(); //shouldn't happen, just in case
        }
        private void LoadAndParseMapping(string path)
        {
            gpkStore.BaseSearchPath = path;
            MapperTools.ParseMappings(path, gpkStore);

            var subCount = gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", gpkStore.CompositeMap.Count, subCount);
        }

        #endregion

        #region misc
        private void SetFileSize()
        {
            if (!IsPackageSelected()) return;

            var input =  new InputBoxWindow($"New filesize for {selectedPackage.Filename}? Old: {selectedPackage.OrginalSize}").ShowDialog();

            if (input == "" || !int.TryParse(input, out var num))
            {
                logger.Info("No/Invalid input");
            }
            else
            {
                logger.Trace(num);
                selectedPackage.OrginalSize = num;
                logger.Info("Set filesize for {0} to {1}", selectedPackage.Filename, selectedPackage.OrginalSize);
            }

        }

        private void SetPropsCustom()
        {
            if (!IsPackageSelected()) return;

            try
            {
                var className = new InputBoxWindow("Classname UID?\nWrite #all to select every object.\nSupported types: Int, Float (x,xx), Bool, String").ShowDialog();
                var propName = new InputBoxWindow("Proprty Name to edit?").ShowDialog();
                var propValue = new InputBoxWindow("Proprty Value:").ShowDialog();

                var exports = selectedPackage.GetExportsByClass(className);

                SoundwaveTools.SetPropertyDetails(exports, propName, propValue);

                logger.Info("Custom set success for {0} Objects.", exports.Count);
            }
            catch (Exception ex)
            {
                logger.Fatal("Custom update fail. Ex " + ex);
            }


        }

        private void SetVolumeMultipliers()
        {
            if (!IsPackageSelected()) return;

            var input = new InputBoxWindow($"New VolumeMultiplier for all SoundCues in {selectedPackage.Filename}: \nFormat: x,xx").ShowDialog();

            if (input == "" || !float.TryParse(input, out var num))
            {
                logger.Info("No/Invalid input");
            }
            else
            {
                logger.Trace(num);
                SoundwaveTools.SetAllVolumes(selectedPackage, num);
                logger.Info("Set Volumes for {0} to {1}.", selectedPackage.Filename, num);
            }
        }

        private void AddName()
        {
            if (!IsPackageSelected()) return;

            var input = new InputBoxWindow("Add a new name to the package:").ShowDialog();
            if (input == "") return;
            selectedPackage.AddString(input);
            if (selectedExport != null)
                DrawGrid(selectedPackage, selectedExport);
        }


        private bool IsPackageSelected()
        {
            if (selectedPackage != null) return true;
            logger.Info("Select a package!");
            return false;

        }

        private void DumpGPKObjects()
        {
            NLogConfig.DisableFormLogging();

            var files = MiscFuncs.GenerateOpenDialog(true, true, "GPK (*.gpk;*.upk;*.gpk_rebuild)|*.gpk;*.upk;*.gpk_rebuild|All files (*.*)|*.*");
            if (files.Length == 0) return;

            var outfile = MiscFuncs.GenerateSaveDialog("dump", ".txt");

            new Task(() =>
            {
                logger.Info("Dump is running in the background");
                MassDumper.DumpMassHeaders(outfile, files);
                logger.Info("Dump done");

                NLogConfig.EnableFormLogging();
            }).Start();
        }

        private void BigBytePropExport()
        {
            var arrayProp = CheckArrayRow();

            if (arrayProp == null || arrayProp.value == null) return;
            var data = new byte[arrayProp.value.Length - 4];
            Array.Copy(arrayProp.value, 4, data, 0, arrayProp.value.Length - 4); //remove count bytes

            var path = MiscFuncs.GenerateSaveDialog(arrayProp.name, "");
            if (path == "") return;

            DataTools.WriteExportDataFile(path, data);
        }

        private void BigBytePropImport()
        {
            var arrayProp = CheckArrayRow();

            if (arrayProp == null) return;

            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;
            var path = files[0];
            if (!File.Exists(path)) return;

            var data = File.ReadAllBytes(path);
            //readd count bytes 
            arrayProp.value = new byte[data.Length + 4];
            Array.Copy(BitConverter.GetBytes(data.Length), arrayProp.value, 4);
            Array.Copy(data, 0, arrayProp.value, 4, data.Length);

            DrawGrid(selectedPackage, selectedExport);
        }

        private GpkArrayProperty CheckArrayRow()
        {
            if (selectedExport == null) return null;
            if (SelectedProperty == null)//(gridProps.SelectedRows.Count != 1)
            {
                logger.Info("select a complete row (click the arrow in front of it)");
                return null;
            }

            //var row = gridProps.SelectedRows[0];
            if (/*row.Cells["type"].Value.ToString() */ SelectedProperty.PropertyType != "ArrayProperty")
            {
                logger.Info("select a arrayproperty row");
                return null;
            }

            return (GpkArrayProperty)SelectedProperty.GetIProperty(selectedPackage);
        }

        //#region search
        //private void searchForObjectToolStripMenuItem_Click(object sender, EventArgs e)
        //{
        //    string input = Microsoft.VisualBasic.Interaction.InputBox("String to search?", "Search");

        //    if (string.IsNullOrEmpty(input))
        //        return;

        //    searchResultNodes.Clear();
        //    searchResultIndex = 0;

        //    foreach (var node in Collect(treeMain.Nodes))
        //    {
        //        if (node.Text.ToLowerInvariant().Contains(input.ToLowerInvariant().Trim()))
        //        {
        //            searchResultNodes.Add(node);
        //        }
        //    }

        //    if (searchResultNodes.Count > 0)
        //    {
        //        tryToSelectNextSearchResult();
        //    }
        //    else
        //    {
        //        logger.Info(string.Format("Nothing found for '{0}'!", input));
        //    }


        //}

        //IEnumerable<TreeNode> Collect(TreeNodeCollection nodes)
        //{
        //    foreach (TreeNode node in nodes)
        //    {
        //        yield return node;

        //        foreach (var child in Collect(node.Nodes))
        //            yield return child;
        //    }
        //}

        //private void nextToolStripMenuItem_Click(object sender, EventArgs e)
        //{
        //    tryToSelectNextSearchResult();
        //}

        //private void tryToSelectNextSearchResult()
        //{
        //    if (searchResultNodes.Count == 0 || searchResultIndex >= searchResultNodes.Count)
        //    {
        //        SystemSounds.Asterisk.Play();
        //        searchResultIndex = 0;
        //        tempStatusLabel.StartTimer("Ready", string.Format("End reached, searching from start.", searchResultIndex, searchResultNodes.Count), 2000);
        //        return;

        //    }

        //    treeMain.SelectedNode = searchResultNodes[searchResultIndex];
        //    treeMain_AfterSelect(this, new TreeViewEventArgs(searchResultNodes[searchResultIndex]));
        //    tempStatusLabel.StartTimer("Ready", string.Format("Result {0}/{1}", searchResultIndex + 1, searchResultNodes.Count), 2000);

        //    searchResultIndex++;
        //}


        //#endregion //search
        #endregion //misc

    }
}