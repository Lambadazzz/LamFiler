using LamFiler.Models;
using LamFiler.Services;
using static LamFiler.Services.FileActionType;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace LamFiler
{
    // ════════════════════════════════════════════════════════════════════
    //  TAB STATE MODEL
    // ════════════════════════════════════════════════════════════════════
    public class TabState
    {
        public string CurrentPath   { get; set; } = "";
        public Stack<string> Back   { get; set; } = new();
        public Stack<string> Forward{ get; set; } = new();
        public string Header        { get; set; } = "Новая вкладка";
        public Border? Button       { get; set; } // UI-кнопка вкладки
    }

    public partial class MainWindow : Window
    {
        public class FileItem : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void N([CallerMemberName] string? p = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

            public string Modified  { get; set; } = "";
            public string Size      { get; set; } = "";
            public string FullPath  { get; set; } = "";
            public bool   IsDir     { get; set; }

            private string _icon = "";
            public string Icon
            {
                get => _icon;
                set { _icon = value; N(); }
            }

            private string _iconColor = "#858585";
            public string IconColor
            {
                get => _iconColor;
                set { _iconColor = value; N(); }
            }

            private string _typeExt = "";
            public string TypeExt
            {
                get => _typeExt;
                set { _typeExt = value; N(); }
            }

            private string _name = "";
            public string Name
            {
                get => _name;
                set { _name = value; N(); }
            }

            private double _opacity = 1.0;
            public double Opacity
            {
                get => _opacity;
                set { _opacity = value; N(); }
            }

            private bool _isRenaming;
            public bool IsRenaming
            {
                get => _isRenaming;
                set { _isRenaming = value; N(); }
            }

            private string _editName = "";
            public string EditName
            {
                get => _editName;
                set { _editName = value; N(); }
            }

            private bool _isDragTarget;
            public bool IsDragTarget
            {
                get => _isDragTarget;
                set { _isDragTarget = value; N(); }
            }
        }

        public class BreadcrumbPart
        {
            public string Label { get; set; } = "";
            public string Path  { get; set; } = "";
        }

        private string _currentPath = "";
        private readonly Stack<string> _backStack    = new();
        private readonly Stack<string> _forwardStack = new();
        private FileSystemWatcher? _watcher;
        private ObservableCollection<FileItem> _allItems = new();

        public MainWindow()
        {
            InitializeComponent();
            BuildSidebar();
            AddTab(null, activate: true); // стартовая вкладка = Home
        }

        // Конструктор для отрыва вкладки в новое окно
        public MainWindow(string path, Stack<string> back, Stack<string> forward)
        {
            InitializeComponent();
            BuildSidebar();
            var tab = new TabState
            {
                CurrentPath = path,
                Back        = back,
                Forward     = forward,
                Header      = Path.GetFileName(path)
            };
            if (string.IsNullOrEmpty(tab.Header)) tab.Header = path;
            _tabs.Add(tab);
            tab.Button = BuildTabButton(tab);
            TabStrip.Children.Add(tab.Button);
            _activeTab = null;
            // Ручно восстанавливаем состояние без сохранения предыдущей
            _activeTab = tab;
            UpdateTabButtonStyle(tab.Button!, true);
            _backStack.Clear();    foreach (var p in tab.Back.Reverse())    _backStack.Push(p);
            _forwardStack.Clear(); foreach (var p in tab.Forward.Reverse()) _forwardStack.Push(p);
            Navigate(tab.CurrentPath, addToBack: false);
        }

        // ════════════════════════════════════════════════════════════════════
        //  НАВИГАЦИЯ
        // ════════════════════════════════════════════════════════════════════

        private void Navigate(string path, bool addToBack = true)
        {
            if (!Directory.Exists(path)) return;
            if (addToBack && _currentPath != "")
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
            }
            _currentPath = path;
            LoadDirectory(path);
            UpdateBreadcrumb(path);
            UpdateNavButtons();
            StatusRight.Text = path;
            // Обновляем заголовок вкладки
            if (_activeTab != null)
            {
                _activeTab.Header = Path.GetFileName(path);
                if (string.IsNullOrEmpty(_activeTab.Header)) _activeTab.Header = path;
                UpdateTabLabel(_activeTab);
            }
        }

        private void LoadDirectory(string path, bool restoreSelection = false, int selectIndex = 0)
        {
            _allItems.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    try { Directory.GetFileSystemEntries(dir); }
                    catch { continue; }
                    var info = new DirectoryInfo(dir);
                    _allItems.Add(new FileItem
                    {
                        Name = info.Name, Icon = "📁", IconColor = "#FAC775",
                        Modified = info.LastWriteTime.ToString("dd MMM  HH:mm"),
                        Size = "—", TypeExt = "Folder", FullPath = dir, IsDir = true
                    });
                }
                foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                {
                    var info = new FileInfo(file);
                    _allItems.Add(new FileItem
                    {
                        Name = info.Name, Icon = GetFileIcon(info.Extension),
                        IconColor = GetIconColor(info.Extension),
                        Modified = info.LastWriteTime.ToString("dd MMM  HH:mm"),
                        Size = FormatSize(info.Length),
                        TypeExt = info.Extension.ToLowerInvariant(),
                        FullPath = file, IsDir = false
                    });
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                MessageBox.Show("Нет доступа к этой папке.", "Lambada",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ApplyFilter(SearchBox.Text);
            SetupWatcher(path);
            AttachHoverTracking();

            if (restoreSelection && FileList.Items.Count > 0)
            {
                var idx = Math.Min(selectIndex, FileList.Items.Count - 1);
                FileList.SelectedIndex = idx;
                FileList.ScrollIntoView(FileList.Items[idx]);
                Dispatcher.InvokeAsync(() =>
                {
                    var lvi = FileList.ItemContainerGenerator.ContainerFromIndex(idx) as ListViewItem;
                    lvi?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (!restoreSelection && FileList.Items.Count > 0)
            {
                FileList.SelectedIndex = 0;
                FileList.ScrollIntoView(FileList.Items[0]);
                Dispatcher.InvokeAsync(() =>
                {
                    var lvi = FileList.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
                    lvi?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void ApplyFilter(string query)
        {
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allItems.ToList()
                : _allItems.Where(f =>
                    f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.TypeExt.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            var sorted = ApplySort(filtered).ToList();
            FileList.ItemsSource = sorted;
            StatusLeft.Text = $"{sorted.Count} items";
        }

        // ════════════════════════════════════════════════════════════════════
        //  FileSystemWatcher
        // ════════════════════════════════════════════════════════════════════

        private void SetupWatcher(string path)
        {
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFsChanged;
            _watcher.Deleted += OnFsChanged;
            _watcher.Renamed += OnFsChanged;
        }

        private bool _suppressWatcher = false;

        private System.Threading.Timer? _watcherDebounce;

        private void OnFsChanged(object sender, FileSystemEventArgs e)
        {
            _watcherDebounce?.Dispose();
            _watcherDebounce = new System.Threading.Timer(_ =>
                Dispatcher.InvokeAsync(() => { if (!_suppressWatcher) LoadDirectory(_currentPath); }),
                null, 150, System.Threading.Timeout.Infinite);
        }

        // ════════════════════════════════════════════════════════════════════
        //  BREADCRUMB
        // ════════════════════════════════════════════════════════════════════

        private void UpdateBreadcrumb(string path)
        {
            var parts = new List<BreadcrumbPart>();
            var current = path;
            while (true)
            {
                var parent = System.IO.Path.GetDirectoryName(current);
                parts.Insert(0, new BreadcrumbPart
                {
                    Label = parent == null ? current : System.IO.Path.GetFileName(current),
                    Path  = current
                });
                if (parent == null) break;
                current = parent;
            }
            Breadcrumb.ItemsSource = parts;
        }

        private void BreadcrumbPart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string p) Navigate(p);
        }

        private void Breadcrumb_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            // Ищем Button в дереве под курсором
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is Button btn && btn.Tag is string path)
                {
                    OpenInNewTab(path);
                    e.Handled = true;
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  КНОПКИ НАЗАД / ВПЕРЁД
        // ════════════════════════════════════════════════════════════════════

        private void UpdateNavButtons()
        {
            BtnBack.IsEnabled    = _backStack.Count > 0;
            BtnForward.IsEnabled = _forwardStack.Count > 0;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_backStack.Count == 0) return;
            _forwardStack.Push(_currentPath);
            Navigate(_backStack.Pop(), addToBack: false);
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardStack.Count == 0) return;
            _backStack.Push(_currentPath);
            Navigate(_forwardStack.Pop(), addToBack: false);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ДВОЙНОЙ КЛИК
        // ════════════════════════════════════════════════════════════════════

        private void FileList_MouseButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Средняя кнопка (Колёсико) на папке — открыть в новой вкладке
            if (e.ChangedButton != MouseButton.Middle) return;
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi && fi.IsDir)
                {
                    OpenInNewTab(fi.FullPath);
                    e.Handled = true;
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    if (fi.IsDir) Navigate(fi.FullPath);
                    else FileContextMenuService.OpenDefault(fi.FullPath);
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПРЕВЬЮ (правая панель)
        // ════════════════════════════════════════════════════════════════════

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var selected = FileList.SelectedItems.Count;
            StatusLeft.Text = selected > 1
                ? $"{selected} selected"
                : (FileList.ItemsSource is List<FileItem> list ? $"{list.Count} items" : "");
            ShowPreview(item);
        }

        private void ShowPreview(FileItem item)
        {
            PreviewMeta.Children.Clear();
            PreviewImage.Opacity = 0;
            PreviewIcon.Visibility = Visibility.Visible;
            PreviewIcon.Text = item.IsDir ? "📁" : GetFileIcon(System.IO.Path.GetExtension(item.FullPath));

            var imgExt = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            if (!item.IsDir && imgExt.Contains(item.TypeExt.ToLower()))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(item.FullPath);
                    bmp.DecodePixelWidth = 400;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    PreviewImage.Source = bmp;
                    PreviewImage.Opacity = 1;
                    PreviewIcon.Visibility = Visibility.Collapsed;
                }
                catch { }
            }

            AddMeta("Name", item.Name);
            if (!item.IsDir) { AddMeta("Size", item.Size); AddMeta("Type", item.TypeExt); }
            AddMeta("Modified", item.Modified);

            PreviewActions.Visibility = Visibility.Visible;
            BtnPreviewOpenWith.Visibility = item.IsDir ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AddMeta(string key, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var k = new TextBlock { Text = key, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)) };
            var v = new TextBlock { Text = value, FontSize = 12, TextAlignment = TextAlignment.Right,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(k, 0); Grid.SetColumn(v, 1);
            row.Children.Add(k); row.Children.Add(v);
            PreviewMeta.Children.Add(row);
            PreviewMeta.Children.Add(new Border
            {
                Height = 0.5,
                Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        private void BtnPreviewOpen_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            if (item.IsDir) Navigate(item.FullPath);
            else FileContextMenuService.OpenDefault(item.FullPath);
        }

        private void BtnPreviewOpenWith_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var apps = FileContextMenuService.GetOpenWithApps(item.FullPath);
            if (apps.Count == 0) { FileContextMenuService.OpenDefault(item.FullPath); return; }
            var menu = BuildContextMenu();
            foreach (var app in apps)
            {
                var a = app;
                menu.Items.Add(Mni("💻", a.DisplayName,
                    () => FileContextMenuService.OpenWith(a.ExePath, item.FullPath)));
            }
            menu.PlacementTarget = BtnPreviewOpenWith;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void BtnUploadDrive_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();
            var paths    = selected.Count > 0
                ? selected.Select(f => f.FullPath).ToList()
                : new List<string> { _currentPath };
            new UploadWindow(paths) { Owner = this }.ShowDialog();
        }

        private void UploadToDrive(List<string> paths)
        {
            var uploadPaths = new List<string>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var confirm = new UploadConfirmWindow(path) { Owner = this };
                    confirm.ShowDialog();
                    if (!confirm.Confirmed) return;
                    uploadPaths.Add(confirm.UploadPath);
                }
                else uploadPaths.Add(path);
            }
            if (uploadPaths.Count > 0)
                new UploadWindow(uploadPaths) { Owner = this }.ShowDialog();
        }

        // ════════════════════════════════════════════════════════════════════
        //  КОНТЕКСТНОЕ МЕНЮ ПКМ
        // ════════════════════════════════════════════════════════════════════

        private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Up or Key.Down)
                e.Handled = true;
        }

        private void FileList_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    if (FileList.SelectedItems.Contains(fi))
                        e.Handled = true;
                    else
                    {
                        FileList.SelectedItems.Clear();
                        FileList.SelectedItem = fi;
                        e.Handled = true;
                    }
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        private void FileList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();
            var item     = selected.Count == 1 ? selected[0] : null;
            if (selected.Count > 1) item = null;

            var menu = BuildContextMenu();

            if (selected.Count > 1)
            {
                menu.Items.Add(Mni("☁", $"Загрузить все на Google Drive ({selected.Count})",
                    () => UploadToDrive(selected.Select(f => f.FullPath).ToList())));
                menu.Items.Add(new Separator());
                menu.Items.Add(Mni("📋", "Копировать пути",
                    () => FileContextMenuService.CopyPath(
                        string.Join(Environment.NewLine, selected.Select(f => f.FullPath)))));
            }
            else if (item != null)
            {
                // Для .exe/.msi — "Открыть" становится подменю
                if (!item.IsDir && item.TypeExt is ".exe" or ".msi")
                {
                    var subRun = MniSubClickable("▶", "Запустить",
                        () => FileContextMenuService.OpenDefault(item.FullPath));
                    subRun.Items.Add(Mni("🛡", "Запустить от имени администратора",
                        () => RunAsAdmin(item.FullPath)));
                    menu.Items.Add(subRun);
                }
                else if (item.IsDir)
                {
                    // Папка — "Открыть" с подменю
                    var subOpen = MniSubClickable("▶", "Открыть",
                        () => Navigate(item.FullPath));
                    subOpen.Items.Add(Mni("⊞", "Открыть в новой вкладке",
                        () => OpenInNewTab(item.FullPath)));
                    menu.Items.Add(subOpen);
                }
                else
                {
                    menu.Items.Add(Mni("▶", "Открыть", () =>
                        FileContextMenuService.OpenDefault(item.FullPath)));
                }

                menu.Items.Add(Mni("✏", "Переименовать", () => StartRename(item)));

                var apps = FileContextMenuService.GetOpenWithApps(item.FullPath);
                if (apps.Count > 0)
                {
                    var sub = MniSub("🔧", "Открыть с помощью");
                    foreach (var app in apps)
                    {
                        var a = app;
                        sub.Items.Add(Mni("💻", a.DisplayName,
                            () => FileContextMenuService.OpenWith(a.ExePath, item.FullPath)));
                    }
                    menu.Items.Add(sub);
                }

                menu.Items.Add(new Separator());
                menu.Items.Add(Mni("☁", "Загрузить на Google Drive",
                    () => UploadToDrive(new List<string> { item.FullPath })));
                menu.Items.Add(new Separator());

                var scripts = FileContextMenuService.LoadScripts();
                if (scripts.Count > 0)
                {
                    var subS = MniSub("⚡", "Запустить скрипт");
                    foreach (var s in scripts)
                    {
                        var sc = s;
                        subS.Items.Add(sc.Command == "__copy_path__"
                            ? Mni("📋", sc.Label, () => FileContextMenuService.CopyPath(item.FullPath))
                            : Mni("▶", sc.Label, () => FileContextMenuService.RunScript(sc, item.FullPath)));
                    }
                    menu.Items.Add(subS);
                    menu.Items.Add(new Separator());
                }

                menu.Items.Add(Mni("📂", "Показать в Проводнике",
                    () => FileContextMenuService.RevealInExplorer(item.FullPath)));
                menu.Items.Add(Mni("📋", "Копировать путь",
                    () => FileContextMenuService.CopyPath(item.FullPath)));
                menu.Items.Add(new Separator());
                menu.Items.Add(BuildCreateSubmenu());
            }
            else
            {
                menu.Items.Add(Mni("🖥", "Открыть терминал здесь", () =>
                    FileContextMenuService.RunScript(
                        new ScriptEntry { Command = "powershell.exe",
                            Args = $"-NoExit -Command \"Set-Location '{_currentPath}'\"" },
                        _currentPath)));
                menu.Items.Add(Mni("📋", "Копировать путь папки",
                    () => FileContextMenuService.CopyPath(_currentPath)));
                menu.Items.Add(new Separator());
                menu.Items.Add(BuildCreateSubmenu());
            }

            menu.PlacementTarget = FileList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static ContextMenu BuildContextMenu()
        {
            var tpl = new ControlTemplate(typeof(ContextMenu));
            var bd  = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty,         new Thickness(0, 4, 0, 4));
            bd.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            tpl.VisualTree = bd;
            return new ContextMenu { OverridesDefaultStyle = true, Template = tpl };
        }

        private static ControlTemplate BuildMniTemplate()
        {
            var tpl = new ControlTemplate(typeof(MenuItem));
            var bd  = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.PaddingProperty,    new Thickness(12, 6, 24, 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty,    "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var hov = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), "Bd"));
            tpl.Triggers.Add(hov);
            var dis = new Trigger { Property = MenuItem.IsEnabledProperty, Value = false };
            dis.Setters.Add(new Setter(MenuItem.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))));
            tpl.Triggers.Add(dis);
            return tpl;
        }

        private static ControlTemplate BuildMniSubTemplate()
        {
            var tpl  = new ControlTemplate(typeof(MenuItem));
            var bd   = new FrameworkElementFactory(typeof(Border));
            bd.Name  = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.PaddingProperty,    new Thickness(12, 6, 24, 6));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var c0   = new FrameworkElementFactory(typeof(ColumnDefinition));
            c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c1   = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c0); grid.AppendChild(c1);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 0);
            cp.SetValue(ContentPresenter.ContentSourceProperty,    "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(Grid.ColumnProperty, 1);
            arrow.SetValue(TextBlock.TextProperty,       "›");
            arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)));
            arrow.SetValue(TextBlock.FontSizeProperty,   14.0);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            grid.AppendChild(arrow);
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.PlacementProperty,        PlacementMode.Right);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty,        false);
            popup.SetBinding(Popup.IsOpenProperty,
                new Binding("IsSubmenuOpen") { RelativeSource = RelativeSource.TemplatedParent });
            var popBd = new FrameworkElementFactory(typeof(Border));
            popBd.SetValue(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            popBd.SetValue(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
            popBd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popBd.SetValue(Border.PaddingProperty,         new Thickness(0, 4, 0, 4));
            popBd.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popup.AppendChild(popBd);
            grid.AppendChild(popup);
            bd.AppendChild(grid);
            tpl.VisualTree = bd;
            var hov = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), "Bd"));
            tpl.Triggers.Add(hov);
            return tpl;
        }

        private static MenuItem Mni(string icon, string header, Action onClick, bool enabled = true)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 13, Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            var m = new MenuItem
            {
                Header = panel, IsEnabled = enabled,
                OverridesDefaultStyle = true,
                Template = BuildMniTemplate(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            m.Click += (_, _) => onClick();
            return m;
        }

        private static MenuItem MniSubClickable(string icon, string header, Action onClick)
        {
            // Используем стандартный MniSub (с Items и рабочим попапом).
            // Клик по левой зоне (icon+text) перехватываем через PreviewMouseLeftButtonUp
            // на panel, проверяя что клик не по стрелке.
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 13, Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = header,
                VerticalAlignment = VerticalAlignment.Center
            });

            var m = MniSub(icon, header);

            // Перехватываем клик по Header — если клик пришёл не по стрелке (x < ActualWidth - 24), запускаем
            m.PreviewMouseLeftButtonUp += (_, e) =>
            {
                // Определяем позицию клика относительно MenuItem
                var pos = e.GetPosition(m);
                // Правая зона ~24px — стрелка › (задаётся padding в шаблоне)
                if (pos.X < m.ActualWidth - 24)
                {
                    m.IsSubmenuOpen = false;
                    var cm = m.Parent as ContextMenu
                           ?? ItemsControl.ItemsControlFromItemContainer(m) as ContextMenu;
                    if (cm != null) cm.IsOpen = false;
                    onClick();
                    e.Handled = true;
                }
                // Если клик по стрелке — просто открываем подменю
            };

            return m;
        }

        private static MenuItem MniSub(string icon, string header)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 13, Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            var m = new MenuItem
            {
                Header = panel,
                OverridesDefaultStyle = true,
                Template = BuildMniSubTemplate(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            m.MouseEnter += (_, _) => m.IsSubmenuOpen = true;
            m.MouseLeave += (_, _) => m.IsSubmenuOpen = false;
            return m;
        }

        // ════════════════════════════════════════════════════════════════════
        //  DRAG & DROP
        // ════════════════════════════════════════════════════════════════════

        private Point   _dragStart;
        private bool    _dragPending;
        private FileItem? _dragItem;

        // ─── Rubber-band выделение ───────────────────────────────────────────
        private bool    _rubberActive;   // идёт rubber-band (drag из checkbox-зоны)
        private int     _rubberAnchor;   // индекс строки, с которой начали drag

        private bool IsCheckZoneClick(MouseButtonEventArgs e)
        {
            // Ищем ListViewItem в дереве и проверяем X-позицию клика.
            // Левые 28px (ColumnDefinition Width="28") — это зона чекбокса.
            // Такой подход устойчив к прозрачному оверлею ZIndex=99, который
            // перехватывает hit-test и скрывает нижележащие элементы.
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi)
                {
                    var pos = e.GetPosition(lvi);
                    return pos.X >= 0 && pos.X < 28;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            return false;
        }

        private FileItem? HitTestItem(Point posInList)
        {
            var hit = VisualTreeHelper.HitTest(FileList, posInList)?.VisualHit as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi) return fi;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        // Нужно ли при MouseUp применить стандартное выделение (клик без drag по уже выделенному)
        private FileItem? _pendingSelectItem;

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart      = e.GetPosition(null);
            _dragPending    = false;
            _dragItem       = null;
            _rubberActive   = false;
            _pendingSelectItem = null;

            // Клик по checkbox-зоне: toggle-выделение + начало rubber-band
            if (IsCheckZoneClick(e))
            {
                var hit = e.OriginalSource as DependencyObject;
                FileItem? clickedItem = null;
                while (hit != null)
                {
                    if (hit is ListViewItem lvi && lvi.Content is FileItem fi) { clickedItem = fi; break; }
                    hit = VisualTreeHelper.GetParent(hit);
                }

                if (clickedItem != null)
                {
                    // Toggle
                    if (FileList.SelectedItems.Contains(clickedItem))
                        FileList.SelectedItems.Remove(clickedItem);
                    else
                        FileList.SelectedItems.Add(clickedItem);

                    // Индекс для rubber-band anchor
                    _rubberAnchor = (FileList.ItemsSource as List<FileItem>)?.IndexOf(clickedItem) ?? -1;
                    _rubberActive = true;
                    FileList.Focus();
                }
                e.Handled = true;
                return;
            }

            // Обычный клик — ищем FileItem под курсором
            var hitEl = e.OriginalSource as DependencyObject;
            while (hitEl != null)
            {
                if (hitEl is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    _dragItem    = fi;
                    _dragPending = true;

                    // Если кликнули на уже выделенный элемент (множественное выделение) —
                    // блокируем ListView от немедленного сброса выделения.
                    // Настоящее выделение применим в MouseUp если drag не начался.
                    if (FileList.SelectedItems.Count > 1 && FileList.SelectedItems.Contains(fi))
                    {
                        _pendingSelectItem = fi;
                        e.Handled = true;
                    }
                    break;
                }
                hitEl = VisualTreeHelper.GetParent(hitEl);
            }
        }

        private void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Если drag не начался, а клик был по уже выделенному элементу —
            // применяем стандартное одиночное выделение.
            if (_pendingSelectItem != null && !_isDragging)
            {
                FileList.SelectedItems.Clear();
                FileList.SelectedItem = _pendingSelectItem;
            }
            _pendingSelectItem = null;
        }

        private bool _isDragging; // true пока DoDragDrop не вернул управление

        private void  FileList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging) return; // DoDragDrop ещё не завершён — игнорируем все MouseMove

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragPending  = false;
                _rubberActive = false;
                return;
            }

            // ─── Rubber-band: drag из checkbox-зоны ───────────────────────────
            if (_rubberActive && _rubberAnchor >= 0)
            {
                var rbItems = FileList.ItemsSource as List<FileItem>;
                if (rbItems == null) return;

                var posInList = e.GetPosition(FileList);
                var hovered   = HitTestItem(posInList);
                int hoverIdx  = hovered != null ? rbItems.IndexOf(hovered) : -1;

                if (hoverIdx < 0) return;

                int from = Math.Min(_rubberAnchor, hoverIdx);
                int to   = Math.Max(_rubberAnchor, hoverIdx);

                // Обновляем выделение: все в диапазоне выделяем
                FileList.SelectionChanged -= FileList_SelectionChanged;
                FileList.SelectedItems.Clear();
                for (int i = from; i <= to; i++)
                    FileList.SelectedItems.Add(rbItems[i]);
                FileList.SelectionChanged += FileList_SelectionChanged;
                StatusLeft.Text = $"{to - from + 1} selected";

                e.Handled = true;
                return;
            }

            // ─── Обычный drag файлов ─────────────────────────────────
            if (!_dragPending || _dragItem == null) return;
            if (_renamingItem != null) { _dragPending = false; return; }

            var pos  = e.GetPosition(null);
            var diff = pos - _dragStart;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            // Сбрасываем до DoDragDrop, чтобы повторный вход в метод был невозможен
            _dragPending = false;
            var dragItem = _dragItem;
            _dragItem = null;

            var items = FileList.SelectedItems.Contains(dragItem)
                ? FileList.SelectedItems.Cast<FileItem>().Select(f => f.FullPath).ToArray()
                : new[] { dragItem.FullPath };

            var data = new DataObject(DataFormats.FileDrop, items);
            _isDragging = true;
            try
            {
                DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
            }
        }

        private FileItem? _dragTargetItem;
        private ListViewItem? _dragTargetLvi;
        private static readonly SolidColorBrush _brushDragTarget = new(Color.FromArgb(0x40, 0x00, 0x78, 0xD4));

        private void SetDragTarget(FileItem? item)
        {
            // Находим LVI для нового item
            ListViewItem? newLvi = item != null
                ? FileList.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem
                : null;

            // Если LVI не изменился — ничего не делаем (курсор внутри одной строки)
            if (newLvi == _dragTargetLvi) return;

            if (_dragTargetLvi != null) { _dragTargetLvi.Background = Brushes.Transparent; }
            _dragTargetItem = item;
            _dragTargetLvi  = newLvi;
            if (newLvi != null) newLvi.Background = _brushDragTarget;
        }

        private bool _dragOverFileDrop; // кэш: источник содержит FileDrop (проверяем один раз при входе)

        private void FileList_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            // ── Обновляем подсветку только при смене строки ──────────────────
            var hit = e.OriginalSource as DependencyObject;
            ListViewItem? hoveredLvi = null;
            FileItem?     hoveredItem = null;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    hoveredLvi  = lvi;
                    hoveredItem = fi;
                    break;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }

            if (hoveredLvi != _dragTargetLvi)
            {
                if (_dragTargetLvi != null) _dragTargetLvi.Background = Brushes.Transparent;
                _dragTargetLvi  = hoveredLvi;
                _dragTargetItem = hoveredItem;

                if (hoveredLvi != null && hoveredItem != null)
                {
                    var ext = hoveredItem.IsDir ? "" : Path.GetExtension(hoveredItem.FullPath).ToLowerInvariant();
                    if (hoveredItem.IsDir || ext is ".zip" or ".rar" or ".7z")
                        hoveredLvi.Background = _brushDragTarget;
                }

                // Фон списка — меняем только при реальной смене строки
                var newBg = hoveredLvi == null ? _brushListHighlight : _brushListNormal;
                if (!ReferenceEquals(FileList.Background, newBg))
                    FileList.Background = newBg;
            }

            // ── Определяем эффект (без GetData — только по кэшированному hoveredItem) ──
            if (hoveredItem != null && !hoveredItem.IsDir)
            {
                var ext = Path.GetExtension(hoveredItem.FullPath).ToLowerInvariant();
                if (ext is ".zip" or ".rar" or ".7z")
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            // Дроп в текущую папку — проверяем источники только если цель == currentPath
            if (hoveredItem == null)
            {
                // Вызываем GetData только один раз за «сессию» над пустым местом
                if (!_dragOverFileDrop)
                {
                    _dragOverFileDrop = true;
                    var sources = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (sources != null &&
                        sources.All(s => (Path.GetDirectoryName(s) ?? "").Equals(_currentPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        e.Effects = DragDropEffects.None;
                        e.Handled = true;
                        return;
                    }
                }
            }
            else
            {
                _dragOverFileDrop = false; // сбрасываем при наведении на элемент
            }

            e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) != 0
                ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
        }

        private void FileList_DragLeave(object sender, DragEventArgs e)
        {
            SetDragTarget(null);
            FileList.Background = _brushListNormal;
            _dragOverFileDrop = false;
        }

        private void FileList_Drop(object sender, DragEventArgs e)
        {
            SetDragTarget(null);
            FileList.Background = _brushListNormal;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (sources == null || sources.Length == 0) return;

            var targetDir = GetDropTargetDir(e, out var targetItem);

            // Если дроп на архивный файл — добавляем файлы в архив
            if (targetItem != null && !targetItem.IsDir)
            {
                var ext = Path.GetExtension(targetItem.FullPath).ToLowerInvariant();
                if (ext is ".zip" or ".rar" or ".7z")
                {
                    DropIntoArchive(sources, targetItem.FullPath, ext);
                    return;
                }
            }

            var isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0
                      || e.AllowedEffects == DragDropEffects.Copy;

            // Если дроп в папку — передаём её путь для выделения после перезагрузки
            string? selectAfterPath = (targetItem != null && targetItem.IsDir) ? targetItem.FullPath : null;

            DropFiles(sources, targetDir, isCopy, selectAfterPath);
        }

        private async void DropIntoArchive(string[] sources, string archivePath, string ext)
        {
            if (ext == ".zip")
            {
                await Task.Run(() =>
                {
                    using var zip = System.IO.Compression.ZipFile.Open(
                        archivePath, System.IO.Compression.ZipArchiveMode.Update);
                    foreach (var src in sources)
                    {
                        if (File.Exists(src))
                            AddFileToZip(zip, src, Path.GetFileName(src));
                        else if (Directory.Exists(src))
                            AddDirectoryToZip(zip, src, Path.GetFileName(src));
                    }
                });
            }
            else if (ext is ".rar" or ".7z")
            {
                var winrar = FindWinRar();
                if (winrar == null)
                {
                    MessageBox.Show("Не найден WinRAR. Установите WinRAR для работы с RAR/7z архивами.",
                        "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var fileArgs = string.Join(" ", sources.Select(s => $"\"{s}\""));
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = winrar,
                    Arguments       = $"a -ep1 \"{archivePath}\" {fileArgs}",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null) await Task.Run(() => proc.WaitForExit());
            }

            // Обновляем только Size и Modified у архива — без перезагрузки списка
            _suppressWatcher = true;
            var archive = (FileList.ItemsSource as List<FileItem>)
                ?.FirstOrDefault(f => f.FullPath.Equals(archivePath, StringComparison.OrdinalIgnoreCase));
            if (archive != null)
            {
                var info = new FileInfo(archivePath);
                archive.Size     = FormatSize(info.Length);
                archive.Modified = info.LastWriteTime.ToString("dd MMM  HH:mm");
                FileList.SelectedItem = archive;
                // Сбрасываем суппрессию через небольшую задержку (чтобы Watcher успел отстрелять)
                await Task.Delay(300);
            }
            _suppressWatcher = false;
        }

        private static void AddFileToZip(
            System.IO.Compression.ZipArchive zip, string filePath, string entryName)
        {
            var entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream  = File.OpenRead(filePath);
            fileStream.CopyTo(entryStream);
        }

        private static void AddDirectoryToZip(
            System.IO.Compression.ZipArchive zip, string srcDir, string entryPrefix)
        {
            foreach (var file in Directory.GetFiles(srcDir))
                AddFileToZip(zip, file, entryPrefix + "/" + Path.GetFileName(file));
            foreach (var sub in Directory.GetDirectories(srcDir))
                AddDirectoryToZip(zip, sub, entryPrefix + "/" + Path.GetFileName(sub));
        }

        // — Breadcrumb —
        private Button? _highlightedBreadcrumbBtn;

        private Button? GetBreadcrumbButton(DragEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is Button btn && btn.Tag is string) return btn;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private void SetBreadcrumbHighlight(Button? btn)
        {
            if (_highlightedBreadcrumbBtn == btn) return;
            if (_highlightedBreadcrumbBtn != null)
                _highlightedBreadcrumbBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
            _highlightedBreadcrumbBtn = btn;
            if (_highlightedBreadcrumbBtn != null)
                _highlightedBreadcrumbBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        }

        private void Breadcrumb_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            var btn = GetBreadcrumbButton(e);
            SetBreadcrumbHighlight(btn);
            e.Effects = btn != null
                ? ((e.KeyStates & DragDropKeyStates.ControlKey) != 0
                    ? DragDropEffects.Copy : DragDropEffects.Move)
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Breadcrumb_DragLeave(object sender, DragEventArgs e)
            => SetBreadcrumbHighlight(null);

        private void Breadcrumb_Drop(object sender, DragEventArgs e)
        {
            SetBreadcrumbHighlight(null);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var btn = GetBreadcrumbButton(e);
            if (btn?.Tag is not string targetDir) return;
            var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (sources == null || sources.Length == 0) return;
            var isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            DropFiles(sources, targetDir, isCopy);
        }

        private async void DropFiles(string[] sources, string targetDir, bool isCopy, string? selectAfterPath = null)
        {
            sources = sources
                .Where(s => !string.Equals(
                    Path.GetDirectoryName(s), targetDir,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (sources.Length == 0) return;

            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;

            var errors = new List<string>();
            var moved  = new List<string>(); // пути файлов которые успешно перемещены (Move)

            await Task.Run(() =>
            {
                foreach (var src in sources)
                {
                    try
                    {
                        var name = Path.GetFileName(src);
                        var dst  = Path.Combine(targetDir, name);
                        if (File.Exists(dst) || Directory.Exists(dst))
                            dst = GetUniquePath(dst);

                        if (Directory.Exists(src))
                        {
                            CopyDirectory(src, dst);
                            if (!isCopy) { Directory.Delete(src, recursive: true); moved.Add(src); }
                        }
                        else
                        {
                            File.Copy(src, dst);
                            if (!isCopy) { File.Delete(src); moved.Add(src); }
                        }

                        _undo.Push(new FileAction
                        {
                            Type    = isCopy ? FileActionType.Copy : FileActionType.Move,
                            OldPath = src,
                            NewPath = dst
                        });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex.Message);
                    }
                }
            });

            if (errors.Count > 0)
                MessageBox.Show(string.Join("\n", errors), "Lambada",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

            if (!isCopy && moved.Count > 0 && selectAfterPath != null)
            {
                // Move в папку: убираем перемещённые из списка без перезагрузки
                var movedSet = new HashSet<string>(moved, StringComparer.OrdinalIgnoreCase);
                var currentList = FileList.ItemsSource as List<FileItem>;
                if (currentList != null)
                {
                    var newList = currentList.Where(f => !movedSet.Contains(f.FullPath)).ToList();
                    FileList.SelectionChanged -= FileList_SelectionChanged;
                    FileList.ItemsSource = newList;
                    // Выделяем папку-цель
                    var folder = newList.FirstOrDefault(f => f.FullPath.Equals(selectAfterPath, StringComparison.OrdinalIgnoreCase));
                    if (folder != null)
                    {
                        FileList.SelectedItem = folder;
                        FileList.ScrollIntoView(folder);
                    }
                    FileList.SelectionChanged += FileList_SelectionChanged;
                    StatusLeft.Text = $"{newList.Count} items";
                }
                // Даём Watcher отстрелять и подавляем
                await Task.Delay(300);
                _suppressWatcher = false;
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
            }
            else
            {
                // Copy или дроп в пустое место: полная перезагрузка
                _suppressWatcher = false;
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
                var sv = FindChild<ScrollViewer>(FileList, "");
                double scrollOffset = sv?.VerticalOffset ?? 0;
                LoadDirectory(_currentPath);
                Dispatcher.InvokeAsync(() =>
                {
                    FindChild<ScrollViewer>(FileList, "")?.ScrollToVerticalOffset(scrollOffset);
                    if (selectAfterPath != null)
                    {
                        var folder = (FileList.ItemsSource as List<FileItem>)
                            ?.FirstOrDefault(f => f.FullPath.Equals(selectAfterPath, StringComparison.OrdinalIgnoreCase));
                        if (folder != null) { FileList.SelectedItem = folder; FileList.ScrollIntoView(folder); }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // Определяем папку-цель: если курсор над папкой или архивом — этот элемент, иначе — текущая папка
        private string GetDropTargetDir(DragEventArgs e, out FileItem? targetItem)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    // Папка — обычная цель
                    if (fi.IsDir) { targetItem = fi; return fi.FullPath; }
                    // Архив — цель для добавления файлов
                    var ext = Path.GetExtension(fi.FullPath).ToLowerInvariant();
                    if (ext is ".zip" or ".rar" or ".7z") { targetItem = fi; return _currentPath; }
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            targetItem = null;
            return _currentPath;
        }

        // ════════════════════════════════════════════════════════════════════
        //  НОВАЯ ПАПКА (Ctrl+N)
        // ════════════════════════════════════════════════════════════════════

        private void CreateNewFolder()
        {
            // Фиксируем активное переименование если есть
            if (_renamingItem != null) CommitRename();

            // Генерируем уникальное имя
            var baseName = "Новая папка";
            var name     = baseName;
            int i        = 2;
            while (Directory.Exists(Path.Combine(_currentPath, name)))
                name = $"{baseName} ({i++})";

            // Создаём временный FileItem без реальной папки — сразу в режиме редактирования
            var placeholder = new FileItem
            {
                Name     = name,
                EditName = name,
                Icon     = "📁",
                IconColor = "#FAC775",
                Modified = DateTime.Now.ToString("dd MMM  HH:mm"),
                Size     = "—",
                TypeExt  = "Folder",
                FullPath = Path.Combine(_currentPath, name),
                IsDir    = true,
                IsRenaming = true
            };

            // Вставляем в начало списка (папки идут первыми)
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;

            var currentList = (FileList.ItemsSource as List<FileItem>) ?? new List<FileItem>();
            currentList.Insert(0, placeholder);
            FileList.ItemsSource = null;
            FileList.ItemsSource = currentList;

            FileList.SelectedItem = placeholder;
            FileList.ScrollIntoView(placeholder);

            _renamingItem = placeholder;

            // Фокус на TextBox
            Dispatcher.InvokeAsync(() =>
            {
                var lvi = FileList.ItemContainerGenerator.ContainerFromItem(placeholder) as ListViewItem;
                if (lvi == null) return;
                var tb = FindChild<TextBox>(lvi, "NameEditor");
                if (tb == null) return;
                tb.Focus();
                tb.SelectAll();

                // Переопределяем KeyDown — при отмене удаляем плейсхолдер без создания папки
                tb.KeyDown -= RenameBox_KeyDown;
                tb.LostFocus -= RenameBox_LostFocus;
                tb.KeyDown += NewFolderBox_KeyDown;
                tb.LostFocus += NewFolderBox_LostFocus;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void NewFolderBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) { CommitNewFolder(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelNewFolder(); e.Handled = true; }
        }

        private void NewFolderBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_renameCommitting) CommitNewFolder();
        }

        private void CommitNewFolder()
        {
            if (_renamingItem == null || _renameCommitting) return;
            _renameCommitting = true;

            var placeholder = _renamingItem;
            var newName     = placeholder.EditName.Trim();
            _renamingItem   = null;
            placeholder.IsRenaming = false;

            var bad = Path.GetInvalidFileNameChars();
            var valid = !string.IsNullOrEmpty(newName) && !newName.Any(c => bad.Contains(c));

            if (valid)
            {
                var newPath = Path.Combine(_currentPath, newName);
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show($"Папка \"{newName}\" уже существует.",
                        "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                    valid = false;
                }
                else
                {
                    try
                    {
                        Directory.CreateDirectory(newPath);
                        _undo.Push(new FileAction
                        {
                            Type    = FileActionType.CreateFolder,
                            OldPath = "",
                            NewPath = newPath
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка:\n{ex.Message}",
                            "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                        valid = false;
                    }
                }
            }

            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
            _renameCommitting = false;

            // Перезагружаем директорию и выбираем созданную папку (или убираем placeholder)
            LoadDirectory(_currentPath);
            if (valid)
            {
                var created = Path.Combine(_currentPath, newName);
                var items   = FileList.ItemsSource as List<FileItem>;
                var target  = items?.FirstOrDefault(f => f.FullPath == created);
                if (target != null)
                {
                    FileList.SelectedItem = target;
                    FileList.ScrollIntoView(target);
                }
            }
            Dispatcher.InvokeAsync(() => FileList.Focus(),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CancelNewFolder()
        {
            if (_renamingItem == null) return;
            _renamingItem.IsRenaming = false;
            _renamingItem = null;
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
            _renameCommitting = false;

            // Убираем placeholder без создания папки
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() => FileList.Focus(),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПЕРЕИМЕНОВАНИЕ (F2 / ПКМ)
        // ════════════════════════════════════════════════════════════════════

        private FileItem? _renamingItem;
        private bool      _renameCommitting;

        private void StartRename(FileItem item)
        {
            // Если уже что-то переименовывается — фиксируем
            if (_renamingItem != null) CommitRename();

            _renamingItem = item;
            item.EditName  = item.Name;
            item.IsRenaming = true;

            // Фокус на TextBox после рендера
            Dispatcher.InvokeAsync(() =>
            {
                var lvi = FileList.ItemContainerGenerator
                    .ContainerFromItem(item) as ListViewItem;
                if (lvi == null) return;
                var tb = FindChild<TextBox>(lvi, "NameEditor");
                if (tb == null) return;
                tb.Focus();
                // Выделяем имя без расширения
                var dot = item.Name.LastIndexOf('.');
                tb.Select(0, (dot > 0 && !item.IsDir) ? dot : item.Name.Length);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void CommitRename()
        {
            if (_renamingItem == null || _renameCommitting) return;
            _renameCommitting = true;

            var item    = _renamingItem;
            var newName = item.EditName.Trim();
            _renamingItem = null;
            item.IsRenaming = false;

            if (string.IsNullOrEmpty(newName) || newName == item.Name)
            {
                _renameCommitting = false;
                return;
            }

            // Проверка допустимых символов
            var bad = System.IO.Path.GetInvalidFileNameChars();
            if (newName.Any(c => bad.Contains(c)))
            {
                MessageBox.Show("Имя содержит недопустимые символы.",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                _renameCommitting = false;
                return;
            }

            var dir    = System.IO.Path.GetDirectoryName(item.FullPath) ?? _currentPath;
            var newPath = System.IO.Path.Combine(dir, newName);

            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show($"Файл с именем \"{newName}\" уже существует.",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                _renameCommitting = false;
                return;
            }

            // Предупреждение при смене расширения
            if (!item.IsDir && _settings.WarnOnExtensionChange)
            {
                var oldExt = Path.GetExtension(item.Name).ToLowerInvariant();
                var newExt = Path.GetExtension(newName).ToLowerInvariant();
                if (oldExt != newExt)
                {
                    var warn = new RenameExtWarningWindow(item.Name, newName) { Owner = this };
                    warn.ShowDialog();
                    if (warn.DontAskAgain)
                    {
                        _settings.WarnOnExtensionChange = false;
                        _settings.Save();
                    }
                    if (!warn.Confirmed)
                    {
                        _renameCommitting = false;
                        Dispatcher.InvokeAsync(() => FileList.Focus(),
                            System.Windows.Threading.DispatcherPriority.Input);
                        return;
                    }
                }
            }

            try
            {
                _suppressWatcher = true;
                if (_watcher != null) _watcher.EnableRaisingEvents = false;

                if (item.IsDir) Directory.Move(item.FullPath, newPath);
                else            File.Move(item.FullPath, newPath);

                _undo.Push(new FileAction
                {
                    Type    = FileActionType.Rename,
                    OldPath = item.FullPath,
                    NewPath = newPath
                });

                item.Name     = newName;
                item.FullPath = newPath;

                // Обновляем иконку, цвет и расширение если файл
                if (!item.IsDir)
                {
                    var newExt    = Path.GetExtension(newName);
                    item.Icon      = GetFileIcon(newExt);
                    item.IconColor = GetIconColor(newExt);
                    item.TypeExt   = newExt.ToLowerInvariant();
                }

                // Перезагружаем список (чтобы элемент встал на правильное место по сортировке)
                _suppressWatcher = false;
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
                _renameCommitting = false;
                LoadDirectory(_currentPath);
                Dispatcher.InvokeAsync(() =>
                {
                    var found = (FileList.ItemsSource as List<FileItem>)
                        ?.FirstOrDefault(f => f.FullPath.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        FileList.SelectedItem = found;
                        FileList.ScrollIntoView(found);
                        ShowPreview(found);
                    }
                    FileList.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка переименования:\n{ex.Message}",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _suppressWatcher = false;
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
                _renameCommitting = false;
            }
        }

        private void CancelRename()
        {
            if (_renamingItem == null) return;
            _renamingItem.IsRenaming = false;
            _renamingItem = null;
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) { CommitRename(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
        }

        private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // LostFocus срабатывает и при коммите — _renameCommitting защита от двойного вызова
            if (!_renameCommitting) CommitRename();
        }

        // Поиск потомка по имени x:Name
        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && (name == "" || fe.Name == name)) return fe;
                var found = FindChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        //  ФАЙЛОВЫЕ ОПЕРАЦИИ (Ctrl+C / Ctrl+X / Ctrl+V / Delete)
        // ════════════════════════════════════════════════════════════════════

        private List<string>   _clipboard          = new();
        private bool           _clipboardCut       = false;
        private int            _lastArrowDirection = 0;
        private FileItem?      _hoveredItem;
        private PreviewWindow? _previewWindow;
        private AppSettings    _settings           = AppSettings.Load();
        private readonly UndoService _undo         = new();
        private static readonly SolidColorBrush _brushListNormal    = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
        private static readonly SolidColorBrush _brushListHighlight = new(Color.FromArgb(30, 0, 120, 212));

        // ════════════════════════════════════════════════════════════════════
        //  TABS
        // ════════════════════════════════════════════════════════════════════
        private readonly List<TabState> _tabs = new();
        private TabState? _activeTab;

        // ─── Drag‑tab ────────────────────────────────────────────────────────────────────
        private TabState? _draggingTab;
        private Point     _tabDragStart;
        private bool      _tabDragStarted;

        // ─── Создание вкладки ────────────────────────────────────────────────────────────
        private TabState AddTab(string? path, bool activate = true)
        {
            bool isHome = string.IsNullOrEmpty(path);
            var tab = new TabState
            {
                CurrentPath = path ?? "",
                Header      = isHome ? "🏠  Дом" : (Path.GetFileName(path!) is var h && string.IsNullOrEmpty(h) ? path! : h)
            };
            _tabs.Add(tab);
            tab.Button = BuildTabButton(tab);
            TabStrip.Children.Add(tab.Button);
            if (activate) ActivateTab(tab);
            return tab;
        }

        private Border BuildTabButton(TabState tab)
        {
            // Label
            var lbl = new TextBlock
            {
                Text = tab.Header, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)),
                Margin = new Thickness(8, 0, 4, 0)
            };

            // Close button
            var close = new TextBlock
            {
                Text = "×", FontSize = 14, Width = 18, Height = 18,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Arrow,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 4, 0)
            };
            close.MouseEnter += (_, _) => close.Foreground = Brushes.White;
            close.MouseLeave += (_, _) => close.Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
            close.MouseLeftButtonDown += (s, e) => e.Handled = true; // блокируем добуббинг до bd
            close.MouseLeftButtonUp   += (s, e) => { e.Handled = true; CloseTab(tab); };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(lbl);
            row.Children.Add(close);

            var bd = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Margin = new Thickness(1, 4, 0, 0),
                Padding = new Thickness(0),
                Height = 27,
                MinWidth = 80,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Tag = tab
            };

            // Клик по вкладке — активируем
            bd.MouseLeftButtonDown += (s, e) =>
            {
                _tabDragStart   = e.GetPosition(null);
                _tabDragStarted = false;
                _draggingTab    = tab;
                bd.CaptureMouse();
                e.Handled = true;
            };

            bd.MouseLeftButtonUp += (s, e) =>
            {
                bd.ReleaseMouseCapture();
                var wasDragging = _tabDragStarted;
                _draggingTab    = null;
                _tabDragStarted = false;
                if (!wasDragging) ActivateTab(tab);
                e.Handled = true;
            };

            bd.MouseMove += (s, e) =>
            {
                if (_draggingTab == null || e.LeftButton != MouseButtonState.Pressed) return;
                var diff = e.GetPosition(null) - _tabDragStart;
                if (!_tabDragStarted &&
                    (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
                    _tabDragStarted = true;
                // Перемещение внутри TabStrip — обрабатывается в TabBar_MouseMove
            };

            // Дроп файлов на вкладку
            bd.DragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
                ActivateTab(tab); // переключаемся при наведении
                e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) != 0
                    ? DragDropEffects.Copy : DragDropEffects.Move;
                e.Handled = true;
            };

            bd.Drop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (sources?.Length > 0)
                {
                    var isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                    DropFiles(sources, tab.CurrentPath, isCopy);
                }
                e.Handled = true;
            };

            UpdateTabButtonStyle(bd, false);
            return bd;
        }

        // Обновляем надпись кнопки вкладки
        private static void UpdateTabLabel(TabState tab)
        {
            if (tab.Button?.Child is not StackPanel sp) return;
            if (sp.Children[0] is TextBlock lbl) lbl.Text = tab.Header;
        }

        private static void UpdateTabButtonStyle(Border bd, bool active)
        {
            bd.Background = new SolidColorBrush(active
                ? Color.FromRgb(0x1E, 0x1E, 0x1E)
                : Color.FromRgb(0x1A, 0x1A, 0x1A));
            bd.BorderThickness = new Thickness(active ? 1 : 0);
            bd.BorderBrush = active
                ? new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C))
                : Brushes.Transparent;

            // Яркость текста
            if (bd.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock lbl)
                lbl.Foreground = new SolidColorBrush(active
                    ? Color.FromRgb(0xCC, 0xCC, 0xCC)
                    : Color.FromRgb(0x85, 0x85, 0x85));
        }

        private void ActivateTab(TabState tab)
        {
            if (_activeTab == tab) return;

            // Сохраняем состояние текущей вкладки
            if (_activeTab != null)
            {
                _activeTab.CurrentPath = _currentPath;
                _activeTab.Back   = new Stack<string>(_backStack.Reverse());
                _activeTab.Forward= new Stack<string>(_forwardStack.Reverse());
                if (_activeTab.Button != null) UpdateTabButtonStyle(_activeTab.Button, false);
            }

            _activeTab = tab;
            if (_activeTab.Button != null) UpdateTabButtonStyle(_activeTab.Button, true);

            // Восстанавливаем состояние новой вкладки
            _backStack.Clear();    foreach (var p in tab.Back.Reverse())    _backStack.Push(p);
            _forwardStack.Clear(); foreach (var p in tab.Forward.Reverse()) _forwardStack.Push(p);

            bool isHome = string.IsNullOrEmpty(tab.CurrentPath);
            if (isHome)
            {
                ShowHomePage();
            }
            else
            {
                HomePanel.Visibility = Visibility.Collapsed;
                Navigate(tab.CurrentPath, addToBack: false);
            }
        }

        private void CloseTab(TabState tab)
        {
            var idx = _tabs.IndexOf(tab);
            if (idx < 0) return;

            bool wasActive = (_activeTab == tab);

            _tabs.Remove(tab);
            if (tab.Button != null) TabStrip.Children.Remove(tab.Button);

            if (_tabs.Count == 0)
            {
                AddTab(null, activate: true);
                return;
            }

            if (wasActive)
            {
                // Закрываем активную — открываем левую (или первую)
                _activeTab = null;
                var next = _tabs[Math.Max(0, idx - 1)];
                ActivateTab(next);
            }
            // Если закрываем неактивную — ничего не меняем, активная вкладка остаётся активной
        }

        // ─── Вытягивание вкладки в новое окно ──────────────────────────────────────────────
        private void TearOutTab(TabState tab, Point screenPos)
        {
            if (_tabs.Count <= 1) return;

            _tabs.Remove(tab);
            if (tab.Button != null) TabStrip.Children.Remove(tab.Button);

            if (_activeTab == tab)
            {
                _activeTab = null;
                ActivateTab(_tabs[0]);
            }

            var newWin = new MainWindow(tab.CurrentPath,
                new Stack<string>(tab.Back.Reverse()),
                new Stack<string>(tab.Forward.Reverse()));

            // Позиционируем так чтобы курсор оказался примерно над таббаром нового окна
            newWin.Left = screenPos.X - 100;
            newWin.Top  = screenPos.Y - 16;
            newWin.Show();

            // Сразу после Show начинаем DragMove — окно будет следовать за мышью
            newWin.Dispatcher.InvokeAsync(() =>
            {
                try { newWin.DragMove(); } catch { }
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        // ─── Tab bar handlers ──────────────────────────────────────────────────────────────────
        private void TabBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingTab == null || !_tabDragStarted || e.LeftButton != MouseButtonState.Pressed) return;

            var posOnScreen = PointToScreen(e.GetPosition(this));
            var tabBarPosInScreen = TabBarBorder.PointToScreen(new Point(0, 0));
            var dy = posOnScreen.Y - (tabBarPosInScreen.Y + TabBarBorder.ActualHeight / 2);

            // Вытягивание в новое окно — курсор далеко ушёл по вертикали
            if (Math.Abs(dy) > 60)
            {
                var dt = _draggingTab;
                _draggingTab    = null;
                _tabDragStarted = false;
                if (dt.Button != null) dt.Button.ReleaseMouseCapture();
                TearOutTab(dt, new Point(posOnScreen.X - 100, posOnScreen.Y - 10));
                return;
            }

            // Горизонтальная перестановка — определяем над какой вкладкой находится курсор
            if (_draggingTab.Button == null) return;
            var posInStrip = e.GetPosition(TabStrip);

            int targetIdx = -1;
            double accX = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var btn = _tabs[i].Button;
                if (btn == null) continue;
                double w = btn.ActualWidth + btn.Margin.Left + btn.Margin.Right;
                if (posInStrip.X < accX + w / 2) { targetIdx = i; break; }
                accX += w;
            }
            if (targetIdx < 0) targetIdx = _tabs.Count - 1;

            var srcIdx = _tabs.IndexOf(_draggingTab);
            if (srcIdx == targetIdx || targetIdx < 0) return;

            // Переставляем в списке и в UI
            _tabs.RemoveAt(srcIdx);
            _tabs.Insert(targetIdx, _draggingTab);

            TabStrip.Children.Remove(_draggingTab.Button);
            TabStrip.Children.Insert(targetIdx, _draggingTab.Button);
        }

        private void TabBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggingTab    = null;
            _tabDragStarted = false;
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
            => AddTab(null, activate: true);

        private void TabScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Перенаправляем колёсико в горизонтальный скролл
            TabScroll.ScrollToHorizontalOffset(
                TabScroll.HorizontalOffset - e.Delta * 0.5);
            e.Handled = true;
        }

        private void TabStrip_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            { e.Effects = DragDropEffects.None; e.Handled = true; }
        }

        private void TabStrip_Drop(object sender, DragEventArgs e)
        {
            // Дроп на пустую часть TabStrip (не на вкладку) — перемещаем в текущую папку
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (sources?.Length > 0)
                DropFiles(sources, _currentPath, (e.KeyStates & DragDropKeyStates.ControlKey) != 0);
            e.Handled = true;
        }

        // Открыть путь в новой вкладке
        public void OpenInNewTab(string path)
        {
            // Если такая вкладка уже есть — просто активируем её
            var existing = _tabs.FirstOrDefault(t =>
                string.Equals(t.CurrentPath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActivateTab(existing);
                return;
            }
            AddTab(path, activate: true);
        }

        // ─── Home page ──────────────────────────────────────────────────────────────────
        private bool _homePanelBuilt = false;

        private void ShowHomePage()
        {
            // Строим карточки один раз
            if (!_homePanelBuilt)
            {
                _homePanelBuilt = true;

                var quickItems = new[]
                {
                    ("🖥", "Desktop",   Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                    ("⬇", "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                    ("📄", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    ("🖼", "Pictures",  Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                    ("🎵", "Music",     Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                    ("🎬", "Videos",    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                };
                foreach (var (icon, label, path) in quickItems)
                    HomeQuickPanel.Children.Add(BuildHomeCard(icon, label, path));

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var label = $"{drive.Name.TrimEnd('\\')}";
                    if (drive.VolumeLabel.Length > 0) label += $"  {drive.VolumeLabel}";
                    HomeDrivesPanel.Children.Add(BuildHomeCard("💾", label, drive.RootDirectory.FullName));
                }
            }

            HomePanel.Visibility    = Visibility.Visible;
            FileList.ItemsSource    = null;
            Breadcrumb.ItemsSource  = null;
            StatusLeft.Text         = "";
            StatusRight.Text        = "";
            BtnBack.IsEnabled       = false;
            BtnForward.IsEnabled    = false;
        }

        private Border BuildHomeCard(string icon, string label, string path)
        {
            var iconTb = new TextBlock
            {
                Text = icon, FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var labelTb = new TextBlock
            {
                Text = label, FontSize = 12, TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 90,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(iconTb);
            stack.Children.Add(labelTb);

            var card = new Border
            {
                Child = stack,
                Width = 110, Height = 82,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };

            // hover
            card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2E));
            card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));

            // ЛКМ — открыть в текущей вкладке
            var p = path;
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    HomePanel.Visibility = Visibility.Collapsed;
                    if (_activeTab != null) _activeTab.CurrentPath = p;
                    Navigate(p);
                }
            };
            // СКМ — открыть в новой вкладке
            card.MouseUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                    OpenInNewTab(p);
            };

            return card;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;
            if (_renamingItem != null) return;

            if (e.Key == Key.Down)
            {
                _lastArrowDirection = 1;
                var i = FileList.SelectedIndex;
                if (i < FileList.Items.Count - 1) { FileList.SelectedIndex = i + 1; ScrollIfNeeded(i + 1); }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _lastArrowDirection = -1;
                var i = FileList.SelectedIndex;
                if (i > 0) { FileList.SelectedIndex = i - 1; ScrollIfNeeded(i - 1); }
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                var target = (_hoveredItem != null && !_hoveredItem.IsDir)
                    ? _hoveredItem
                    : FileList.SelectedItems.Cast<FileItem>().FirstOrDefault(f => !f.IsDir);
                if (target != null && _previewWindow == null)
                    OpenPreview(target.FullPath);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _previewWindow != null)
            {
                _previewWindow.Close();
                e.Handled = true;
            }
        }

        private void ScrollIfNeeded(int index)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var count = FileList.Items.Count;
                if (index < 0 || index >= count) return;
                var anchor = _lastArrowDirection == 1
                    ? Math.Min(index + 1, count - 1)
                    : Math.Max(index - 1, 0);
                FileList.ScrollIntoView(FileList.Items[anchor]);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OpenPreview(string path)
        {
            _previewWindow = new PreviewWindow(path) { Owner = this };
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.PreviewKeyUp += (_, e) =>
            {
                if (e.Key == Key.Space) { _previewWindow?.Close(); e.Handled = true; }
            };
            _previewWindow.Show();
        }

        private void AttachHoverTracking()
        {
            FileList.MouseMove  -= FileList_MouseMove;
            FileList.MouseMove  += FileList_MouseMove;
            FileList.MouseLeave -= FileList_MouseLeave;
            FileList.MouseLeave += FileList_MouseLeave;
        }

        private void FileList_MouseMove(object sender, MouseEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    _hoveredItem = fi;
                    if (_previewWindow != null && !fi.IsDir)
                        _previewWindow.NavigateTo(fi.FullPath);
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            _hoveredItem = null;
        }

        private void FileList_MouseLeave(object sender, MouseEventArgs e)
            => _hoveredItem = null;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;
            if (_renamingItem != null) return;

            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && selected.Count > 0)
            {
                ResetCutMarks();
                _clipboard    = selected.Select(f => f.FullPath).ToList();
                _clipboardCut = false;
                e.Handled = true;
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control && selected.Count > 0)
            {
                ResetCutMarks();
                _clipboard    = selected.Select(f => f.FullPath).ToList();
                _clipboardCut = true;
                foreach (var f in selected) f.Opacity = 0.35;
                FileList.Items.Refresh();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && _clipboard.Count > 0)
            {
                PasteFiles();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && selected.Count > 0)
            {
                DeleteFiles(selected);
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && selected.Count == 1)
            {
                StartRename(selected[0]);
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CreateNewFolder();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PerformUndo();
                e.Handled = true;
            }
            else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
            {
                AddTab(null, activate: true); // дом по умолчанию
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null) CloseTab(_activeTab);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && selected.Count == 1)
            {
                var item = selected[0];
                if (item.IsDir) Navigate(item.FullPath);
                else FileContextMenuService.OpenDefault(item.FullPath);
                e.Handled = true;
            }
        }

        private void PerformUndo()
        {
            if (!_undo.CanUndo) return;

            var sv = FindChild<ScrollViewer>(FileList, "");
            double scrollOffset = sv?.VerticalOffset ?? 0;
            var selectedPath = (FileList.SelectedItem as FileItem)?.FullPath;

            // Если отменяем rename и выделен именно переименованный — цель выделения будет старый путь
            var action = _undo.Peek();
            if (action?.Type == FileActionType.Rename &&
                string.Equals(selectedPath, action.NewPath, StringComparison.OrdinalIgnoreCase))
                selectedPath = action.OldPath;

            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            var err = _undo.Undo();
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;

            if (err != null)
                MessageBox.Show($"Не удалось отменить:\n{err}",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);

            LoadDirectory(_currentPath);

            Dispatcher.InvokeAsync(() =>
            {
                var sv2 = FindChild<ScrollViewer>(FileList, "");
                sv2?.ScrollToVerticalOffset(scrollOffset);

                if (selectedPath != null)
                {
                    var item = (FileList.ItemsSource as List<FileItem>)
                        ?.FirstOrDefault(f => f.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
                    if (item != null) FileList.SelectedItem = item;
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PasteFiles()
        {
            foreach (var src in _clipboard)
            {
                try
                {
                    var name = System.IO.Path.GetFileName(src);
                    var dst  = System.IO.Path.Combine(_currentPath, name);
                    if (File.Exists(dst) || Directory.Exists(dst))
                        dst = GetUniquePath(dst);
                    if (Directory.Exists(src))
                    {
                        CopyDirectory(src, dst);
                        if (_clipboardCut) Directory.Delete(src, recursive: true);
                    }
                    else
                    {
                        File.Copy(src, dst);
                        if (_clipboardCut) File.Delete(src);
                    }
                    _undo.Push(new FileAction
                    {
                        Type    = _clipboardCut ? FileActionType.Move : FileActionType.Copy,
                        OldPath = src,
                        NewPath = dst
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка:\n{ex.Message}", "Lambada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            if (_clipboardCut) _clipboard.Clear();
            ResetCutMarks();
            LoadDirectory(_currentPath);
        }

        private void DeleteFiles(List<FileItem> items)
        {
            var names = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} файлов";
            var dlg   = new DeleteConfirmWindow($"Удалить {names}?") { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var firstIndex = FileList.SelectedIndex;
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;

            Directory.CreateDirectory(UndoService.TrashDir);

            foreach (var item in items)
            {
                try
                {
                    var trashPath = Path.Combine(UndoService.TrashDir,
                        Guid.NewGuid().ToString("N") + "_" + item.Name);

                    if (item.IsDir) Directory.Move(item.FullPath, trashPath);
                    else            File.Move(item.FullPath, trashPath);

                    _undo.Push(new FileAction
                    {
                        Type    = FileActionType.Delete,
                        OldPath = item.FullPath,
                        NewPath = trashPath
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления:\n{ex.Message}", "Lambada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;

            var targetIndex = firstIndex;
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() =>
            {
                if (FileList.Items.Count > 0)
                {
                    var idx = Math.Min(targetIndex, FileList.Items.Count - 1);
                    FileList.SelectedIndex = idx;
                    FileList.ScrollIntoView(FileList.Items[idx]);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ResetCutMarks()
        {
            foreach (var item in _allItems) item.Opacity = 1.0;
            FileList.Items.Refresh();
        }

        private static void  CopyDirectory(string src, string dst)
        {
            // Защита от копирования папки в саму себя или свою подпапку
            var srcFull = System.IO.Path.GetFullPath(src).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            var dstFull = System.IO.Path.GetFullPath(dst).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (dstFull.StartsWith(srcFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Нельзя копировать папку в саму себя: {src}");

            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(dir)));
        }

        private static string GetUniquePath(string path)
        {
            var dir  = System.IO.Path.GetDirectoryName(path) ?? "";
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var ext  = System.IO.Path.GetExtension(path);
            int i    = 1;
            string result;
            do { result = System.IO.Path.Combine(dir, $"{name} ({i++}){ext}"); }
            while (File.Exists(result) || Directory.Exists(result));
            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        //  СОРТИРОВКА ПО СТОЛБЦАМ
        // ════════════════════════════════════════════════════════════════════

        private string _sortColumn    = "Name";
        private bool   _sortAscending = true;

        private void ColHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string col) return;
            if (_sortColumn == col)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn    = col;
                _sortAscending = true;
            }
            UpdateSortArrows();
            ApplyFilter(SearchBox.Text);
        }

        private void UpdateSortArrows()
        {
            var asc  = "↑";
            var desc = "↓";
            ArrowName.Text     = _sortColumn == "Name"     ? (_sortAscending ? asc : desc) : "";
            ArrowModified.Text = _sortColumn == "Modified" ? (_sortAscending ? asc : desc) : "";
            ArrowSize.Text     = _sortColumn == "Size"     ? (_sortAscending ? asc : desc) : "";
            ArrowType.Text     = _sortColumn == "Type"     ? (_sortAscending ? asc : desc) : "";
        }

        private IEnumerable<FileItem> ApplySort(IEnumerable<FileItem> items)
        {
            // Папки всегда сверху
            var dirs  = items.Where(f =>  f.IsDir);
            var files = items.Where(f => !f.IsDir);

            dirs  = SortGroup(dirs);
            files = SortGroup(files);

            return dirs.Concat(files);
        }

        private IEnumerable<FileItem> SortGroup(IEnumerable<FileItem> group)
        {
            IOrderedEnumerable<FileItem> ordered = _sortColumn switch
            {
                "Modified" => _sortAscending
                    ? group.OrderBy(f => f.Modified)
                    : group.OrderByDescending(f => f.Modified),
                "Size" => _sortAscending
                    ? group.OrderBy(f => ParseSize(f.Size))
                    : group.OrderByDescending(f => ParseSize(f.Size)),
                "Type" => _sortAscending
                    ? group.OrderBy(f => f.TypeExt, StringComparer.OrdinalIgnoreCase)
                    : group.OrderByDescending(f => f.TypeExt, StringComparer.OrdinalIgnoreCase),
                _ => _sortAscending  // Name
                    ? group.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    : group.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
            };
            return ordered;
        }

        private static long ParseSize(string size)
        {
            // Переводим отображаемый размер обратно в байты для сравнения
            if (size == "—" || string.IsNullOrWhiteSpace(size)) return -1;
            var parts = size.Split(' ');
            if (parts.Length < 2 || !double.TryParse(parts[0],
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val)) return 0;
            return parts[1] switch
            {
                "KB" => (long)(val * 1024),
                "MB" => (long)(val * 1024 * 1024),
                "GB" => (long)(val * 1024L * 1024 * 1024),
                _    => (long)val
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПОИСК
        // ════════════════════════════════════════════════════════════════════

        private void SearchArea_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateSearch(true);
            SearchBox.Focus();
        }

        private void SearchArea_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;
            AnimateSearch(false);
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;
            AnimateSearch(false);
        }

        private void AnimateSearch(bool open)
        {
            var wa = new DoubleAnimation(open ? 170 : 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            SearchBorder.BeginAnimation(WidthProperty, wa);
            SearchBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(150)));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(SearchBox.Text);

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SearchBox.Text = ""; AnimateSearch(false); }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SIDEBAR
        // ════════════════════════════════════════════════════════════════════

        private void BuildSidebar()
        {
            AddSidebarSection("Quick access");
            AddSidebarItem("🖥", "Desktop",   Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddSidebarItem("⬇", "Downloads", System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddSidebarItem("📄", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddSidebarItem("🖼", "Pictures",  Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddSidebarSection("Drives");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                AddSidebarItem("💾", $"{drive.Name} ({drive.DriveType})", drive.RootDirectory.FullName);
        }

        private void AddSidebarSection(string title)
        {
            SidebarPanel.Children.Add(new TextBlock
            {
                Text = title.ToUpper(), FontSize = 10, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)),
                Margin = new Thickness(12, 10, 12, 2)
            });
        }

        private void AddSidebarItem(string icon, string label, string path)
        {
            var btn = new Button
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 5, 12, 5), Tag = path
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) });
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85))
            });
            btn.Content    = panel;
            btn.Click      += (_, _) => { if (btn.Tag is string p) Navigate(p); };
            btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
            SidebarPanel.Children.Add(btn);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ХЕЛПЕРЫ
        // ════════════════════════════════════════════════════════════════════

        private static string GetFileIcon(string ext) => ext.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "🖼",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"             => "🎬",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a"            => "🎵",
            ".pdf"                                                       => "📕",
            ".doc" or ".docx"                                            => "📝",
            ".xls" or ".xlsx"                                            => "📊",
            ".ppt" or ".pptx"                                            => "📋",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz"               => "📦",
            ".exe" or ".msi"                                             => "⚙",
            ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c"         => "💻",
            ".txt" or ".md" or ".log"                                    => "📄",
            _                                                            => "📄"
        };

        private static string GetIconColor(string ext) => ext.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "#5DCAA5",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"             => "#AFA9EC",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a"            => "#F0997B",
            ".pdf"                                                       => "#E24B4A",
            ".doc" or ".docx"                                            => "#85B7EB",
            ".zip" or ".rar" or ".7z"                                   => "#FAC775",
            ".cs" or ".py" or ".js" or ".ts"                            => "#62C554",
            _                                                            => "#858585"
        };

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)                return $"{bytes} B";
            if (bytes < 1024 * 1024)         return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static void RunAsAdmin(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = path,
                    UseShellExecute = true,
                    Verb            = "runas"
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Пользователь нажал Отмена в UAC-диалоге — молча игнорируем
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить:\n{ex.Message}",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _watcher?.Dispose();
            _watcherDebounce?.Dispose();
            base.OnClosed(e);
        }

        // ════════════════════════════════════════════════════════════════════
        //  СОЗДАТЬ (подменю ПКМ)
        // ════════════════════════════════════════════════════════════════════

        private static string? _winRarPath;
        private static bool    _winRarChecked;

        private static string? FindWinRar()
        {
            if (_winRarChecked) return _winRarPath;
            _winRarChecked = true;
            var candidates = new[]
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),  "WinRAR", "WinRAR.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "WinRAR.exe"),
            };
            _winRarPath = candidates.FirstOrDefault(File.Exists);
            return _winRarPath;
        }

        private MenuItem BuildCreateSubmenu()
        {
            var sub = MniSub("➕", "Создать");

            // 1. Папка
            sub.Items.Add(Mni("📁", "Папку", () => CreateNewFolder()));

            // 2. Текстовый файл
            sub.Items.Add(Mni("📄", "Текстовый файл", () => CreateNewTextFile()));

            // 3. Архив — RAR если есть WinRAR, иначе ZIP
            var winrar = FindWinRar();
            if (winrar != null)
                sub.Items.Add(Mni("📦", "Архив RAR", () => CreateNewRar(winrar)));
            else
                sub.Items.Add(Mni("📦", "Архив ZIP", () => CreateNewZip()));

            return sub;
        }

        private void CreateNewTextFile()
        {
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            var path = GetUniquePath(System.IO.Path.Combine(_currentPath, "New File.txt"));
            File.WriteAllText(path, "");
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() =>
            {
                var item = (FileList.ItemsSource as List<FileItem>)
                    ?.FirstOrDefault(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    FileList.SelectedItem = item;
                    FileList.ScrollIntoView(item);
                    StartRename(item);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void CreateNewZip()
        {
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            var path = GetUniquePath(System.IO.Path.Combine(_currentPath, "New Archive.zip"));
            // Пустой валидный zip: записываем End-of-central-directory запись вручную
            File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0,0, 0,0, 0,0, 0,0, 0,0,0,0, 0,0,0,0, 0,0 });
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() =>
            {
                var item = (FileList.ItemsSource as List<FileItem>)
                    ?.FirstOrDefault(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    FileList.SelectedItem = item;
                    FileList.ScrollIntoView(item);
                    StartRename(item);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void CreateNewRar(string winrarExe)
        {
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            var path = GetUniquePath(System.IO.Path.Combine(_currentPath, "New Archive.rar"));
            try
            {
                // Пустой валидный RAR5: signature + archive header + end-of-archive block (CRC верифицирован)
                File.WriteAllBytes(path, new byte[]
                {
                    0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00, // RAR5 signature
                    0x3D, 0x3C, 0xDE, 0xE5, 0x02, 0x01, 0x00,       // archive header block
                    0x39, 0xF9, 0xB2, 0x81, 0x02, 0x05, 0x00        // end-of-archive block
                });
            }
            catch (Exception ex)
            {
                _suppressWatcher = false;
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
                MessageBox.Show($"Ошибка создания RAR:\n{ex.Message}",
                    "Lambada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() =>
            {
                var item = (FileList.ItemsSource as List<FileItem>)
                    ?.FirstOrDefault(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    FileList.SelectedItem = item;
                    FileList.ScrollIntoView(item);
                    StartRename(item);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}

