﻿using GPK_RePack.Core;
using GPK_RePack.Core.Editors;
using GPK_RePack.Core.IO;
using GPK_RePack.Core.Model;
using GPK_RePack.Core.Model.Composite;
using GPK_RePack.Core.Model.Interfaces;
using GPK_RePack.Core.Model.Payload;
using GPK_RePack.Core.Model.Prop;
using GPK_RePack.Core.Updater;
using GPK_RePack_WPF.Windows;
using NAudio.Vorbis;
using NAudio.Wave;
using NLog;
using Nostrum;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpkManager.Dds;
using WaveFormRendererLib;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.Forms.DataFormats;
using Image = System.Drawing.Image;
using Size = System.Windows.Size;

namespace GPK_RePack_WPF
{

    public static class ColorExtensions
    {
        public static System.Windows.Media.Color ToMediaColor(this System.Drawing.Color color)
        {
            return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        public static System.Drawing.Color ToDrawingColor(this System.Windows.Media.Color color)
        {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }

    public class MainViewModel : TSPropertyChanged, IUpdaterCheckCallback, IDisposable
    {
        // not actually a singleton, just a reference for contextmenu command binding
        public static MainViewModel Instance { get; private set; }

        //todo: turn this into an enum
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
        };
        private readonly DataFormats.Format exportFormat = DataFormats.GetFormat(typeof(GpkExport).FullName);

        private readonly Logger _logger;
        private readonly GpkStore _gpkStore;
        private List<GpkExport>[] _changedExports;
        private GpkPackage _selectedPackage;
        private GpkExport _selectedExport;
        private string _selectedClass = "";

        private bool _isTextureTabVisible;
        private bool _isSoundTabVisible;
        private string _logText;
        private string _statusBarText;
        private string _infoText;
        private string _oggPreviewButtonText;
        private bool _generalButtonsEnabled;
        private bool _dataButtonsEnabled;
        private int _progressValue;
        private readonly LogBuffer _logBuffer;
        private Tab _selectedTab = 0;
        private PropertyViewModel _selectedProperty;
        public SoundPreviewManager SoundPreviewManager { get; }


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
        public bool PropertyButtonsEnabled => _selectedExport != null && _selectedTab == Tab.Properties;
        public bool ImageButtonsEnabled => _selectedExport != null && _selectedTab == Tab.Texture && IsTextureTabVisible;
        public bool SoundButtonsEnabled => _selectedExport != null && _selectedTab == Tab.Sound && IsSoundTabVisible;
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
        public bool IsSoundTabVisible
        {
            get => _isSoundTabVisible;
            set
            {
                if (_isSoundTabVisible == value) return;
                _isSoundTabVisible = value;
                N();
                if (value == false && _selectedTab == Tab.Sound) SelectedTabIndex = (int)Tab.Info;
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
                N(nameof(SoundButtonsEnabled));

            }
        }
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (_progressValue == value) return;
                _progressValue = value;
                N();
            }
        }
        public GpkTreeNode TreeMain { get; }
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

        //todo: move preview image stuff to its own class ?
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

        #region Commands
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
        public ICommand AddEmptyOGGCommand { get; }

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

        public ICommand SearchCommand { get; }
        public ICommand SearchNextCommand { get; }

        public ICommand PlayStopSoundCommand { get; }

        #endregion


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
            _logger = LogManager.GetLogger("GUI");
            _logger.Info("Startup");

            //
            UpdateCheck.checkForUpdate(this);
            _gpkStore = new GpkStore();
            _gpkStore.PackagesChanged += OnPackagesChanged;
            TreeMain = new GpkTreeNode("");
            Properties = new TSObservableCollection<PropertyViewModel>();
            _searchResultNodes = new List<GpkTreeNode>();
            SoundPreviewManager = new SoundPreviewManager();
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
            AddEmptyOGGCommand = new RelayCommand(AddEmptyOGG);

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

            SearchCommand = new RelayCommand(Search);
            SearchNextCommand = new RelayCommand(prev => SelectNextSearchResult());

            PlayStopSoundCommand = new RelayCommand(PlayStopSound);
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


            if (_gpkStore.LoadedGpkPackages.Count == 0)
                return;

            //do it
            try
            {
                this._gpkStore.SaveGpkListToFiles(_gpkStore.LoadedGpkPackages, usePadding, patchComposite, addComposite, runningSavers, runningTasks);
                //display info while loading
                while (!Task.WaitAll(runningTasks.ToArray(), 50))
                {
                    DisplayStatus(runningSavers, "Saving", start);
                }

                //Diplay end info
                DisplayStatus(runningSavers, "Saving", start);

                _logger.Info("Saving done!");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Save failure!");
            }

            void SavePatched()
            {
                var save = false;
                if (_changedExports != null)
                {
                    for (var i = 0; i < _changedExports.Length; i++)
                    {
                        var list = _changedExports[i];
                        if (list.Count <= 0) continue;
                        try
                        {
                            var tmpS = new Writer();
                            var package = _gpkStore.LoadedGpkPackages[i];
                            var savepath = package.Path + "_patched";
                            tmpS.SaveReplacedExport(package, savepath, list);
                            _logger.Info($"Saved the changed data of package '{package.Filename} to {savepath}'!");
                            save = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.Fatal(ex, "Save failure! ");
                        }
                    }
                }

                if (!save)
                {
                    _logger.Info("Nothing to save in PatchMode!");
                }
            }
        }
        private void Clear()
        {
            Reset();
            _changedExports = null;
            SoundPreviewManager.ResetOggPreview();
            _gpkStore.clearGpkList();
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
                    _gpkStore.loadGpk(path, reader, false);
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
                Array.Resize(ref _changedExports, _gpkStore.LoadedGpkPackages.Count);
                for (var i = 0; i < _changedExports.Length; i++)
                {
                    _changedExports[i] = new List<GpkExport>();
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
                if (total > 0) ProgressValue = (int)((actual / (double)total) * 100);
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
                _logger.Info("A newer version is available. Download it at https://github.com/GoneUp/GPK_RePack/releases");
            }
        }
        private void LogMessages(string msg)
        {
            LogText += msg;
        }
        private void Reset()
        {
            _selectedExport = null;
            _selectedPackage = null;
            SelectedNode = null;
            _selectedClass = "";
            InfoText = "";
            StatusBarText = "Ready";
            GeneralButtonsEnabled = false;
            DataButtonsEnabled = false;
            IsTextureTabVisible = false;
            N(nameof(PropertyButtonsEnabled));
            N(nameof(ImageButtonsEnabled));
            N(nameof(SoundButtonsEnabled));
            ProgressValue = 0;
            PreviewImage = null;
            TreeMain.Children.Clear();
            Properties.Clear();
        }
        public void Dispose()
        {
            SoundPreviewManager.Dispose();

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
            foreach (var package in _gpkStore.LoadedGpkPackages)
            {
                //Task.Run(() =>
                //{
                //    Dispatcher.InvokeAsync(() =>
                //    {

                var packageNode = new GpkTreeNode(package.Filename, package)
                {
                    IsPackage = true,
                    Index = _gpkStore.LoadedGpkPackages.IndexOf(package)
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
                _selectedPackage = _gpkStore.LoadedGpkPackages[Convert.ToInt32(node.Index)];
                try
                {
                    InfoText = _selectedPackage.ToString();
                }
                catch (Exception ex)
                {
                    _logger.Fatal(ex, "Failed to show package info.");
                    InfoText = $"Failed to show package info: \n{ex}";
                }
            }
            else if (node.Level == 2 && CoreSettings.Default.ViewMode == ViewMode.Class)
            {
                _selectedPackage = _gpkStore.LoadedGpkPackages[Convert.ToInt32(node.FindPackageNode().Index)];
                _selectedClass = node.Key;

                DataButtonsEnabled = true;
            }
            else if (node.Level == 3 && CoreSettings.Default.ViewMode == ViewMode.Normal || node.IsLeaf)
            {
                var package = _gpkStore.LoadedGpkPackages[Convert.ToInt32(node.FindPackageNode().Index)];
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
                    var exportChanged = _selectedExport == exp;
                    _selectedExport = exp;
                    _selectedPackage = package;

                    N(nameof(PropertyButtonsEnabled));
                    RefreshExportInfo(exportChanged);
                }
            }
            SelectedNode = node;
        }
        private void RefreshExportInfo(bool exportChanged)
        {
            //tabs
            InfoText = _selectedExport.ToString();
            DrawGrid(_selectedPackage, _selectedExport);

            PreviewImage = null;
            switch (_selectedExport.Payload)
            {
                case Texture2D texture:
                {
                    IsTextureTabVisible = true;
                    IsSoundTabVisible = false;
                    var image = texture;
                    var ddsFile = new DdsFile();
                    var imageStream = image.GetObjectStream();
                    if (imageStream != null)
                    {
                        ddsFile.Load(image.GetObjectStream());

                        PreviewImage = ddsFile.BitmapSource;
                        PreviewImageFormat = image.parsedImageFormat.ToString();
                        PreviewImageName = _selectedExport.ObjectName;
                    }

                    break;
                }
                case Soundwave sw:
                    SoundPreviewManager.Setup(sw.oggdata);
                    IsSoundTabVisible = true;
                    IsTextureTabVisible = false;
                    break;
                default:
                    IsTextureTabVisible = false;
                    IsSoundTabVisible = false;

                    break;
            }

            if (exportChanged)
            {
                SoundPreviewManager.ResetOggPreview();
            }
        }
        public TSObservableCollection<PropertyViewModel> Properties { get; }
        public GpkTreeNode SelectedNode { get; set; }


        private void SaveSingleGpk()
        {
            if (_selectedPackage == null || SelectedNode == null || !SelectedNode.IsPackage) return;
            var packages = new List<GpkPackage> { _selectedPackage };
            var writerList = new List<IProgress>();
            var taskList = new List<Task>();

            this._gpkStore.SaveGpkListToFiles(packages, false, false, false, writerList, taskList);

            //wait
            while (!Task.WaitAll(taskList.ToArray(), 50))
            {

            }
            _logger.Info("Single export done!");
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
                        _logger.Info("Unk Prop?!?");
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

            if (_selectedExport == null || _selectedPackage == null)
            {
                _logger.Info("save failed");
                return;
            }

            var list = new List<IProperty>();
            foreach (var prop in Properties)
            {
                try
                {
                    list.Add(prop.GetIProperty(_selectedPackage));
                }
                catch (Exception ex)
                {

                    _logger.Info("Failed to save row {0}, {1}!", Properties.IndexOf(prop), ex);
                }

            }

            _selectedExport.Properties = list;
            _logger.Info("Saved properties of export {0}.", _selectedExport.UID);
        }
        private void ClearProperties()
        {
            if (_selectedExport == null || _selectedPackage == null)
            {
                _logger.Info("save failed");
                return;
            }

            _selectedExport.Properties.Clear();
            DrawGrid(_selectedPackage, _selectedExport);
            _logger.Info("Cleared!");
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

                var csvRow =
                    $"{row.Name};{row.PropertyType};{row.Size};{row.ArrayIndex};{row.InnerType};{row.Value}";
                builder.AppendLine(csvRow);
            }


            var path = MiscFuncs.GenerateSaveDialog(_selectedExport.ObjectName, ".csv");
            if (path == "") return;

            Task.Factory.StartNew(() => File.WriteAllText(path, builder.ToString(), Encoding.UTF8));
        }

        private void ExportRawData()
        {
            if (_selectedExport != null)
            {
                if (_selectedExport.Data == null)
                {
                    _logger.Info("Length is zero. Nothing to export");
                    return;
                }

                var path = MiscFuncs.GenerateSaveDialog(_selectedExport.ObjectName, "");
                if (path == "") return;
                DataTools.WriteExportDataFile(path, _selectedExport);
            }
            else if (_selectedPackage != null && _selectedClass != "")
            {
                var exports = _selectedPackage.GetExportsByClass(_selectedClass);

                if (exports.Count == 0)
                {
                    _logger.Info("No exports found for class {0}.", _selectedClass);
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
                        _logger.Trace("save for " + exp.UID);
                    }
                }
            }
            else if (_selectedPackage != null)
            {
                var dialog = new FolderBrowserDialog { SelectedPath = CoreSettings.Default.SaveDir };
                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    CoreSettings.Default.SaveDir = dialog.SelectedPath;

                    foreach (var exp in _selectedPackage.ExportList.Values)
                    {
                        if (exp.Data == null) continue;
                        DataTools.WriteExportDataFile($"{dialog.SelectedPath}\\{exp.ClassName}\\{exp.ObjectName}", exp);
                        _logger.Trace("save for " + exp.UID);
                    }
                }
            }

            _logger.Info("Data was saved!");
        }
        private void ImportRawData()
        {
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }
            if (_selectedExport.Data == null)
            {
                _logger.Trace("no export data");
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
                var package = SelectedNode.FindPackageNode();
                var packageIndex = package.Index;
                if (buffer.Length > _selectedExport.Data.Length)
                {
                    //Too long, not possible without rebuiling the gpk
                    _logger.Info("File size too big for PatchMode. Size: " + buffer.Length + " Maximum Size: " +
                                _selectedExport.Data.Length);
                    return;
                }

                //selectedExport.data = buffer;
                Array.Copy(buffer, _selectedExport.Data, buffer.Length);

                _changedExports[packageIndex].Add(_selectedExport);

            }
            else
            {
                //Rebuild Mode
                //We force the rebuilder to recalculate the size. (atm we dont know how big the propertys are)
                _logger.Trace($"rebuild mode old size {_selectedExport.Data.Length} new size {buffer.Length}");

                _selectedExport.Data = buffer;
                _selectedExport.GetDataSize();
                _selectedPackage.Changes = true;
            }


            _logger.Info($"Replaced the data of {_selectedExport.ObjectName} successfully! Dont forget to save.");



        }
        private void RemoveObject()
        {
            if (_selectedPackage != null && _selectedExport == null)
            {
                _gpkStore.DeleteGpk(_selectedPackage);

                _logger.Info("Removed package {0}...", _selectedPackage.Filename);

                TreeMain.Children.Remove(SelectedNode);
                _selectedPackage = null;
                GeneralButtonsEnabled = false;
                RefreshIndexes();
                //GC.Collect(); //memory 
            }
            else if (_selectedPackage != null && _selectedExport != null)
            {
                _selectedPackage.ExportList.Remove(_selectedPackage.GetObjectKeyByUID(_selectedExport.UID));

                _logger.Info("Removed object {0}...", _selectedExport.UID);

                _selectedExport = null;

                SelectedNode.Remove();
                SelectedNode = null;
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
                        pkgNode.Index = _gpkStore.LoadedGpkPackages.IndexOf(pkgNode.SourcePackage);
                    }, DispatcherPriority.Background);
                }
            });
        }

        private void CopyObject()
        {
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }

            try
            {
                var memorystream = new MemoryStream();
                var bf = new BinaryFormatter();
                bf.Serialize(memorystream, _selectedExport);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex);
                _logger.Info("Serialize failed, check debug log");
                return;
            }

            Clipboard.SetData(exportFormat.Name, _selectedExport);
            _logger.Info("Made a copy of {0}...", _selectedExport.UID);
        }
        private void PasteObject()
        {
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }
            var copyExport = (GpkExport)Clipboard.GetData(exportFormat.Name);

            if (copyExport == null)
            {
                _logger.Info("copy paste fail");
                return;
            }

            _logger.Trace(CoreSettings.Default.CopyMode);
            var option = "";

            switch (CoreSettings.Default.CopyMode)
            {
                case CopyMode.All:
                    DataTools.ReplaceAll(copyExport, _selectedExport);
                    option = "everything";
                    break;
                case CopyMode.DataProps:
                    DataTools.ReplaceProperties(copyExport, _selectedExport);
                    DataTools.ReplaceData(copyExport, _selectedExport);
                    option = "data and properties";
                    break;
                case CopyMode.Data:
                    DataTools.ReplaceData(copyExport, _selectedExport);
                    option = "data";
                    break;
                case CopyMode.Props:
                    DataTools.ReplaceProperties(copyExport, _selectedExport);
                    option = "properties";
                    break;
                default:
                    _logger.Info("Your setting file is broken. Go to settings windows and select a copymode.");
                    break;

            }

            copyExport.GetDataSize();
            SelectNode(SelectedNode);
            _logger.Info("Pasted the {0} of {1} to {2}", option, copyExport.UID, _selectedExport.UID);
        }
        private void InsertObject()
        {
            if (_selectedPackage == null)
            {
                _logger.Trace("no selected package to insert");
                return;
            }
            var copyExport = (GpkExport)Clipboard.GetData(exportFormat.Name);

            if (copyExport == null)
            {
                _logger.Info("copy paste fail");
                return;
            }

            _selectedPackage.CopyObjectFromPackage(copyExport.UID, copyExport.motherPackage, true);

            RedrawPackage(TreeMain.Children.FirstOrDefault(p => p.SourcePackage == _selectedPackage));
            _logger.Info("Insert done");
        }

        private void RedrawPackage(GpkTreeNode packageNode)
        {
            var package = packageNode.SourcePackage;
            packageNode.Children.Clear();
            packageNode.Index = _gpkStore.LoadedGpkPackages.IndexOf(package);
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
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }

            if (_selectedExport.Data == null)
            {
                _logger.Trace("no export data");
                return;
            }

            _selectedExport.Loader = null;
            _selectedExport.Data = null;
            _selectedExport.DataPadding = null;
            _selectedExport.Payload = null;
            _selectedExport.GetDataSize();

            SelectNode(SelectedNode);
        }

        private void ImportDDS()
        {
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }

            if (!(_selectedExport.Payload is Texture2D))
            {
                _logger.Info("Not a Texture2D object");
                return;
            }

            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;

            if (files[0] != "" && File.Exists(files[0]))
            {
                TextureTools.importTexture(_selectedExport, files[0]);
                RefreshExportInfo(false);
            }
        }
        private void ExportDDS()
        {
            if (_selectedExport == null)
            {
                _logger.Trace("no selected export");
                return;
            }

            if (!(_selectedExport.Payload is Texture2D))
            {
                _logger.Info("Not a Texture2D object");
                return;
            }

            var path = MiscFuncs.GenerateSaveDialog(_selectedExport.ObjectName, ".dds");
            if (path != "")
            {
                new Task(() => TextureTools.exportTexture(_selectedExport, path)).Start();
            }


        }

        private void ImportOGG()
        {
            try
            {
                if (_selectedExport != null)
                {
                    var files = MiscFuncs.GenerateOpenDialog(false);
                    if (files.Length == 0) return;

                    if (File.Exists(files[0]))
                    {
                        SoundwaveTools.ImportOgg(_selectedExport, files[0]);
                        SelectNode(SelectedNode);
                        _logger.Info("Import successful.");
                    }
                    else
                    {
                        _logger.Info("File not found.");
                    }

                }
                else if (_selectedPackage != null && _selectedClass == "Core.SoundNodeWave")
                {
                    var exports = _selectedPackage.GetExportsByClass(_selectedClass);

                    var dialog = new FolderBrowserDialog
                    {
                        SelectedPath = Path.GetDirectoryName(CoreSettings.Default.SaveDir)
                    };
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
                                    _logger.Trace("Matched file {0} to export {1}!", filename, exp.ObjectName);
                                    break;
                                }
                            }


                        }


                        _logger.Info("Mass import to {0} was successful.", dialog.SelectedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Import failure!");
            }
        }
        private void ExportOGG()
        {

            if (_selectedExport != null && _selectedExport.ClassName == "Core.SoundNodeWave")
            {
                var path = MiscFuncs.GenerateSaveDialog(_selectedExport.ObjectName, ".ogg");
                if (path != "")
                    SoundwaveTools.ExportOgg(_selectedExport, path);
            }
            else if (_selectedPackage != null && _selectedClass == "Core.SoundNodeWave")
            {
                var exports = _selectedPackage.GetExportsByClass(_selectedClass);

                if (exports.Count == 0)
                {
                    _logger.Info("No oggs found for class {0}.", _selectedClass);
                    return;
                }


                var dialog = new FolderBrowserDialog
                {
                    SelectedPath = Path.GetDirectoryName(CoreSettings.Default.SaveDir)
                };
                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    CoreSettings.Default.SaveDir = dialog.SelectedPath;

                    foreach (var exp in exports)
                    {
                        SoundwaveTools.ExportOgg(exp, $"{dialog.SelectedPath}\\{exp.ObjectName}.ogg");
                        _logger.Trace("ogg save for " + exp.UID);
                    }

                    _logger.Info("Mass export to {0} was successful.", dialog.SelectedPath);
                }
            }
        }
        private void AddEmptyOGG()
        {
            if (_selectedExport != null)
            {
                SoundwaveTools.ImportOgg(_selectedExport, "fake");
                SelectNode(SelectedNode);
            }
        }


        private void PatchObjectMapperForSelectedPackage()
        {
            if (!IsPackageSelected()) return;

            _gpkStore.MultiPatchObjectMapper(_selectedPackage, CoreSettings.Default.CookedPCPath);
        }


        private void PlayStopSound()
        {
            try
            {
                if (SoundPreviewManager.PlaybackState == PlaybackState.Stopped || SoundPreviewManager.PlaybackState == PlaybackState.Paused)
                {
                    SoundPreviewManager.PlaySound();
                }
                else if (SoundPreviewManager.PlaybackState == PlaybackState.Playing)
                {
                    SoundPreviewManager.PauseSound();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Playback Error");
            }
        }


        #region composite gpk
        private void DecryptDat()
        {
            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;

            var outfile = MiscFuncs.GenerateSaveDialog("decrypt", ".txt");

            new Task(() =>
            {
                _logger.Info("Decryption is running in the background");

                MapperTools.DecryptAndWriteFile(files[0], outfile);

                _logger.Info("Decryption done");

            }).Start();
        }
        private void EncryptDat()
        {
            var files = MiscFuncs.GenerateOpenDialog(false);
            if (files.Length == 0) return;

            var outfile = MiscFuncs.GenerateSaveDialog("encrypt", ".txt");

            new Task(() =>
            {
                _logger.Info("Encryption is running in the background");

                MapperTools.EncryptAndWriteFile(files[0], outfile);

                _logger.Info("Encryption done");

            }).Start();
        }
        private void LoadMapping()
        {
            if (_gpkStore.CompositeMap.Count > 0)
            {
                new MapperWindow(_gpkStore).Show();
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

            new MapperWindow(_gpkStore).Show();
        }
        private void WriteMapping()
        {
            var dialog = new FolderBrowserDialog();
            if (CoreSettings.Default.WorkingDir != "")
                dialog.SelectedPath = CoreSettings.Default.WorkingDir;
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;

            var path = dialog.SelectedPath + "\\";

            MapperTools.WriteMappings(path, _gpkStore, true, true);
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
            _gpkStore.BaseSearchPath = path;

            CoreSettings.Default.CookedPCPath = path;
            MapperTools.ParseMappings(path, _gpkStore);

            var subCount = _gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            _logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", _gpkStore.CompositeMap.Count, subCount);

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

            _logger.Warn("Warning: This function can be ultra long running (hours) and unstable. Monitor logfile and output folder for progress.");
            _logger.Warn("Disabling logging, dump is running in the background. Consider setting file logging to only info.");

            NLogConfig.DisableFormLogging();
            var outDir = dialog.SelectedPath;
            new Task(() => MassDumper.DumpMassTextures(_gpkStore, outDir, filterList)).Start();
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
            _gpkStore.BaseSearchPath = path;
            CoreSettings.Default.CookedPCPath = path;
            MapperTools.ParseMappings(path, _gpkStore);

            var subCount = _gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            _logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", _gpkStore.CompositeMap.Count, subCount);
            var list = FilterCompositeList("");
            //save dir
            dialog = new FolderBrowserDialog
            {
                SelectedPath = CoreSettings.Default.WorkingDir,
                Description = "Select your output dir"
            };
            if (dialog.ShowDialog() == DialogResult.Cancel)
                return;
            _logger.Warn("Warning: This function can be ultra long running (hours) and unstable. Monitor logfile and output folder for progress.");
            _logger.Warn("Disabling logging, dump is running in the background. Consider setting file logging to only info.");

            NLogConfig.DisableFormLogging();
            var outDir = dialog.SelectedPath;
            new Task(() => MassDumper.DumpMassIcons(_gpkStore, outDir, list)).Start();

        }
        private Dictionary<string, List<CompositeMapEntry>> FilterCompositeList(string text)
        {
            try
            {
                if (text != "" && text.Split('-').Length > 0)
                {
                    var start = Convert.ToInt32(text.Split('-')[0]) - 1;
                    var end = Convert.ToInt32(text.Split('-')[1]) - 1;
                    var filterCompositeList = _gpkStore.CompositeMap.Skip(start).Take(end - start + 1).ToDictionary(k => k.Key, v => v.Value);
                    _logger.Info("Filterd down to {0} GPKs.", end - start + 1);
                    return filterCompositeList;
                }
                else
                {
                    return _gpkStore.CompositeMap;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "filter fail");
                return _gpkStore.CompositeMap;
            }
        }
        private void MinimizeGPK()
        {
            if (!IsPackageSelected()) return;

            DataTools.RemoveObjectRedirects(_selectedPackage);
            if (SelectedNode?.SourcePackage == _selectedPackage)
                RedrawPackage(SelectedNode);
            else
                DrawPackages(); //shouldn't happen, just in case
        }
        private void LoadAndParseMapping(string path)
        {
            _gpkStore.BaseSearchPath = path;
            MapperTools.ParseMappings(path, _gpkStore);

            var subCount = _gpkStore.CompositeMap.Sum(entry => entry.Value.Count);
            _logger.Info("Parsed mappings, we have {0} composite GPKs and {1} sub-gpks!", _gpkStore.CompositeMap.Count, subCount);
        }

        #endregion

        #region misc
        private void SetFileSize()
        {
            if (!IsPackageSelected()) return;

            var input = new InputBoxWindow($"New filesize for {_selectedPackage.Filename}? Old: {_selectedPackage.OrginalSize}").ShowDialog();

            if (input == "" || !int.TryParse(input, out var num))
            {
                _logger.Info("No/Invalid input");
            }
            else
            {
                _logger.Trace(num);
                _selectedPackage.OrginalSize = num;
                _logger.Info("Set filesize for {0} to {1}", _selectedPackage.Filename, _selectedPackage.OrginalSize);
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

                var exports = _selectedPackage.GetExportsByClass(className);

                SoundwaveTools.SetPropertyDetails(exports, propName, propValue);

                _logger.Info("Custom set success for {0} Objects.", exports.Count);
            }
            catch (Exception ex)
            {
                _logger.Fatal("Custom update fail. Ex " + ex);
            }


        }

        private void SetVolumeMultipliers()
        {
            if (!IsPackageSelected()) return;

            var input = new InputBoxWindow($"New VolumeMultiplier for all SoundCues in {_selectedPackage.Filename}: \nFormat: x,xx").ShowDialog();

            if (input == "" || !float.TryParse(input, out var num))
            {
                _logger.Info("No/Invalid input");
            }
            else
            {
                _logger.Trace(num);
                SoundwaveTools.SetAllVolumes(_selectedPackage, num);
                _logger.Info("Set Volumes for {0} to {1}.", _selectedPackage.Filename, num);
            }
        }

        private void AddName()
        {
            if (!IsPackageSelected()) return;

            var input = new InputBoxWindow("Add a new name to the package:").ShowDialog();
            if (input == "") return;
            _selectedPackage.AddString(input);
            if (_selectedExport != null)
                DrawGrid(_selectedPackage, _selectedExport);
        }


        private bool IsPackageSelected()
        {
            if (_selectedPackage != null) return true;
            _logger.Info("Select a package!");
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
                _logger.Info("Dump is running in the background");
                MassDumper.DumpMassHeaders(outfile, files);
                _logger.Info("Dump done");

                NLogConfig.EnableFormLogging();
            }).Start();
        }

        private void BigBytePropExport()
        {
            var arrayProp = CheckArrayRow();

            if (arrayProp?.value == null) return;
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

            DrawGrid(_selectedPackage, _selectedExport);
        }

        private GpkArrayProperty CheckArrayRow()
        {
            if (_selectedExport == null) return null;
            if (SelectedProperty == null)//(gridProps.SelectedRows.Count != 1)
            {
                _logger.Info("select a complete row (click the arrow in front of it)");
                return null;
            }

            //var row = gridProps.SelectedRows[0];
            if (/*row.Cells["type"].Value.ToString() */ SelectedProperty.PropertyType != "ArrayProperty")
            {
                _logger.Info("select a arrayproperty row");
                return null;
            }

            return (GpkArrayProperty)SelectedProperty.GetIProperty(_selectedPackage);
        }

        private readonly List<GpkTreeNode> _searchResultNodes;
        private int searchResultIndex;

        #region search
        private void Search()
        {
            var input = new InputBoxWindow("String to search?").ShowDialog();

            if (string.IsNullOrEmpty(input))
                return;

            _searchResultNodes.Clear();
            searchResultIndex = 0;

            foreach (var node in TreeMain.Collect())
            {
                if (node.Key.ToLowerInvariant().Contains(input.ToLowerInvariant().Trim()))
                {
                    _searchResultNodes.Add(node);
                }
            }

            if (_searchResultNodes.Count > 0)
            {
                SelectNextSearchResult();
            }
            else
            {
                _logger.Info($"Nothing found for '{input}'!");
            }


        }


        private void SelectNextSearchResult(bool previous = false)
        {
            if (!CheckSearch()) return;

            SelectNode(_searchResultNodes[searchResultIndex]);
            _searchResultNodes[searchResultIndex].ParentsToPackage.ForEach(p => p.IsExpanded = true);
            _searchResultNodes[searchResultIndex].IsSelected = true;
            if (!previous)
            {

                StatusBarText = $"Result {searchResultIndex + 1}/{_searchResultNodes.Count}";
                searchResultIndex++;
            }
            else
            {
                StatusBarText = $"Result {searchResultIndex - 1}/{_searchResultNodes.Count}";
                searchResultIndex--;
            }


            bool CheckSearch()
            {
                var found = true;
                var msg = "";
                if (_searchResultNodes.Count == 0)
                {
                    searchResultIndex = 0;
                    msg = "No items found.";
                    found = false;
                }

                if (!previous && searchResultIndex >= _searchResultNodes.Count)
                {
                    searchResultIndex = 0;
                    msg = "End reached, searching from start.";
                    found = false;
                }

                if (previous && searchResultIndex == 0)
                {
                    searchResultIndex = _searchResultNodes.Count - 1;
                    msg = "Start reached, searching from end.";
                    found = false;
                }

                if (found) return true;

                SystemSounds.Asterisk.Play();
                StatusBarText = msg;

                return false;
            }

        }


        #endregion //search
        #endregion //misc

    }

    public class SoundPreviewManager : TSPropertyChanged, IDisposable
    {
        private VorbisWaveReader _waveReader;
        private readonly WaveOut _waveOut;
        private readonly Timer _timer;
        private readonly WaveFormRenderer _renderer;
        private Image _bmp;
        private ImageSource _waveForm;
        private DateTime _startTime;

        public ImageSource WaveForm
        {
            get => _waveForm;
            set
            {
                if (_waveForm == value) return;
                _waveForm = value;
                N();
            }
        }

        public double CurrentPosition
        {
            get
            {
                if (_waveReader == null) return 0;
                //if (_waveReader != null)
                //    return _waveReader.CurrentTime.TotalMilliseconds / _waveReader.TotalTime.TotalMilliseconds;
                return (DateTime.Now - _startTime).TotalMilliseconds*1000 / _waveReader.TotalTime.TotalMilliseconds;
                //return 0;
            }
            set
            {
                if (_waveReader == null) return;
                var totalMs = _waveReader.TotalTime.TotalMilliseconds;
                var setMs = (value/1000D) * totalMs;
                if (setMs < totalMs)
                {
                    _waveOut.Pause();
                    _waveReader.CurrentTime = TimeSpan.FromMilliseconds(setMs);
                    _waveOut.Play();
                }
                else
                {
                    _waveOut.Stop();
                }
                N();
                N(nameof(CurrentTime));
                _startTime = DateTime.Now - _waveReader.CurrentTime;
            }
        }

        public string CurrentTime
        {
            get
            {
                if (_waveReader == null) return TimeSpan.Zero.ToString(@"mm\:ss\.ff");
                return (DateTime.Now - _startTime).ToString(@"mm\:ss\.ff");
            }
        }

        public PlaybackState PlaybackState => _waveOut.PlaybackState;

        public SoundPreviewManager()
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            _waveOut = new WaveOut();
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _timer = new Timer { Interval = 10 };
            _timer.Tick += OnTimerTick;
            //https://github.com/naudio/NAudio.WaveFormRenderer
            _renderer = new WaveFormRenderer();

        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                N(nameof(CurrentPosition));
                N(nameof(CurrentTime));

            }, DispatcherPriority.DataBind);
            if((DateTime.Now - _startTime).TotalMilliseconds >= _waveReader.TotalTime.TotalMilliseconds) _waveOut.Stop();
            if (PlaybackState == PlaybackState.Playing) return;
            _timer.Stop();

        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            _timer.Stop();
            _startTime = DateTime.Now;
            N(nameof(CurrentPosition));
            N(nameof(CurrentTime));

            //ResetOggPreview();
        }

        public void Setup(byte[] soundwave)
        {
            ResetOggPreview();
            _waveReader = new VorbisWaveReader(new MemoryStream(soundwave));
            RenderWaveForm();
            _waveOut.Init(_waveReader);
        }

        private void RenderWaveForm()
        {
            Task.Run(() =>
            {
                var col = ((System.Windows.Media.Color)App.Current.FindResource("SelectionColor")).ToDrawingColor();
                var rendererSettings = new StandardWaveFormRendererSettings
                {
                    Width = 1200,
                    TopHeight = 128,
                    BottomHeight = 128,
                    BackgroundColor = System.Drawing.Color.Transparent,
                    TopPeakPen = new System.Drawing.Pen(col),
                    BottomPeakPen = new System.Drawing.Pen(col)
                };
                var maxPeakProvider = new RmsPeakProvider(200);
                this._bmp = _renderer.Render(_waveReader, maxPeakProvider, rendererSettings);

                Dispatcher.InvokeAsync(() =>
                {
                    using (var ms = new MemoryStream())
                    {
                        _bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;

                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        WaveForm = bi;
                    }
                    _waveReader.Position = 0;
                });

            });
        }

        public void PlaySound()
        {
            if (PlaybackState == PlaybackState.Paused)
            {
                _startTime = DateTime.Now - _waveReader.CurrentTime;
                _waveOut.Resume();
            }
            else
            {
                _startTime = DateTime.Now;
                _waveReader.Position = 0;
                _waveOut.Play();
            }
            _timer.Start();
            //OggPreviewButtonText = "Stop Preview";
        }

        public void ResetOggPreview()
        {
            if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
            {
                _waveOut.Stop();
                _waveReader?.Close();
                _waveReader?.Dispose();
                WaveForm = null;
            }

            _waveReader = null;
            _bmp?.Dispose();
            //OggPreviewButtonText = "Ogg Preview";
        }

        public void Dispose()
        {
            _waveReader?.Dispose();
            _waveOut?.Dispose();
            _bmp?.Dispose();
        }

        public void PauseSound()
        {
            if (PlaybackState == PlaybackState.Playing)
            {
                _waveOut?.Pause();
                _timer.Stop();

            }
        }
    }
}