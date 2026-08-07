using System.Runtime.CompilerServices;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace FluxDB.Models
{
    public partial class FileEntry : ObservableObject
    {
        private static readonly Dictionary<string, (SymbolRegular Symbol, string Color)> IconLookup = new()
        {
            [".jpg"] = (SymbolRegular.Image24, "#9B59B6"), [".jpeg"] = (SymbolRegular.Image24, "#9B59B6"), [".png"] = (SymbolRegular.Image24, "#9B59B6"), [".gif"] = (SymbolRegular.Image24, "#9B59B6"),
            [".bmp"] = (SymbolRegular.Image24, "#9B59B6"), [".webp"] = (SymbolRegular.Image24, "#9B59B6"), [".ico"] = (SymbolRegular.Image24, "#9B59B6"),
            [".mp3"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".wav"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".flac"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".aac"] = (SymbolRegular.MusicNote224, "#2ECC71"),
            [".ogg"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".wma"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".m4a"] = (SymbolRegular.MusicNote224, "#2ECC71"),
            [".mp4"] = (SymbolRegular.Video24, "#E74C3C"), [".avi"] = (SymbolRegular.Video24, "#E74C3C"), [".mkv"] = (SymbolRegular.Video24, "#E74C3C"), [".mov"] = (SymbolRegular.Video24, "#E74C3C"),
            [".wmv"] = (SymbolRegular.Video24, "#E74C3C"), [".flv"] = (SymbolRegular.Video24, "#E74C3C"), [".webm"] = (SymbolRegular.Video24, "#E74C3C"),
            [".pdf"] = (SymbolRegular.DocumentPdf24, "#E74C3C"),
            [".doc"] = (SymbolRegular.Document24, "#3498DB"), [".docx"] = (SymbolRegular.Document24, "#3498DB"), [".rtf"] = (SymbolRegular.Document24, "#3498DB"), [".odt"] = (SymbolRegular.Document24, "#3498DB"),
            [".txt"] = (SymbolRegular.Document24, "#3498DB"), [".md"] = (SymbolRegular.Document24, "#3498DB"),
            [".xls"] = (SymbolRegular.DocumentData24, "#27AE60"), [".xlsx"] = (SymbolRegular.DocumentData24, "#27AE60"), [".csv"] = (SymbolRegular.DocumentData24, "#27AE60"), [".ods"] = (SymbolRegular.DocumentData24, "#27AE60"),
            [".zip"] = (SymbolRegular.Box24, "#E67E22"), [".rar"] = (SymbolRegular.Box24, "#E67E22"), [".7z"] = (SymbolRegular.Box24, "#E67E22"), [".tar"] = (SymbolRegular.Box24, "#E67E22"), [".gz"] = (SymbolRegular.Box24, "#E67E22"),
            [".exe"] = (SymbolRegular.WindowConsole20, "#95A5A6"), [".msi"] = (SymbolRegular.WindowConsole20, "#95A5A6"),
            [".cs"] = (SymbolRegular.Code24, "#00CED1"), [".js"] = (SymbolRegular.Code24, "#00CED1"), [".ts"] = (SymbolRegular.Code24, "#00CED1"), [".py"] = (SymbolRegular.Code24, "#00CED1"),
            [".java"] = (SymbolRegular.Code24, "#00CED1"), [".cpp"] = (SymbolRegular.Code24, "#00CED1"), [".c"] = (SymbolRegular.Code24, "#00CED1"), [".h"] = (SymbolRegular.Code24, "#00CED1"),
            [".html"] = (SymbolRegular.Code24, "#00CED1"), [".css"] = (SymbolRegular.Code24, "#00CED1"), [".xaml"] = (SymbolRegular.Code24, "#00CED1"), [".xml"] = (SymbolRegular.Code24, "#00CED1"),
            [".json"] = (SymbolRegular.Code24, "#00CED1"), [".sql"] = (SymbolRegular.Code24, "#00CED1"), [".php"] = (SymbolRegular.Code24, "#00CED1"),
        };

        [ObservableProperty]
        private int _id;

        [ObservableProperty]
        private string _path;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _extension;

        [ObservableProperty]
        private long _size;

        [ObservableProperty]
        private DateTime _createdAt;

        [ObservableProperty]
        private DateTime _modifiedAt;

        [ObservableProperty]
        private bool _deleted;

        [ObservableProperty]
        private DateTime _lastIndexedAt;

        [ObservableProperty]
        private string _tagsText;

        [ObservableProperty]
        private string _note;

        [ObservableProperty]
        private bool _isFolder;

        private SymbolRegular _cachedIconSymbol;
        private string _cachedSizeDisplay;
        private bool _cacheValid;

        public List<string> Tags { get; set; } = new List<string>();

        public string SizeDisplay
        {
            get
            {
                if (_cacheValid && _cachedSizeDisplay != null) return _cachedSizeDisplay;
                if (_isFolder) { _cachedSizeDisplay = ""; return _cachedSizeDisplay; }
                if (_size < 1024) _cachedSizeDisplay = _size + " B";
                else if (_size < 1024 * 1024) _cachedSizeDisplay = (_size / 1024.0).ToString("F1") + " KB";
                else if (_size < 1024 * 1024 * 1024) _cachedSizeDisplay = (_size / (1024.0 * 1024.0)).ToString("F1") + " MB";
                else _cachedSizeDisplay = (_size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
                return _cachedSizeDisplay;
            }
        }

        public SymbolRegular IconSymbol
        {
            get
            {
                if (_cacheValid && _cachedIconSymbol != default) return _cachedIconSymbol;
                return _cachedIconSymbol = _isFolder ? SymbolRegular.Folder24 : GetExtLookup().Symbol;
            }
        }

        public Brush IconColorBrush
        {
            get
            {
                var color = _isFolder ? "#DCB67A" : GetExtLookup().Color;
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }
        }

        public string TypeDisplay
        {
            get
            {
                if (_isFolder) return "Folder";
                return string.IsNullOrEmpty(_extension) ? "File" : _extension.TrimStart('.').ToUpper();
            }
        }

        partial void OnExtensionChanged(string value)
        {
            _cacheValid = false;
            OnPropertyChanged(nameof(IconSymbol));
            OnPropertyChanged(nameof(IconColorBrush));
            OnPropertyChanged(nameof(TypeDisplay));
        }

        partial void OnSizeChanged(long value)
        {
            _cacheValid = false;
            OnPropertyChanged(nameof(SizeDisplay));
        }

        partial void OnIsFolderChanged(bool value)
        {
            _cacheValid = false;
            OnPropertyChanged(nameof(IconSymbol));
            OnPropertyChanged(nameof(IconColorBrush));
            OnPropertyChanged(nameof(SizeDisplay));
            OnPropertyChanged(nameof(TypeDisplay));
        }

        private (SymbolRegular Symbol, string Color) GetExtLookup()
        {
            var ext = (_extension ?? "").ToLower();
            return IconLookup.TryGetValue(ext, out var value) ? value : (SymbolRegular.Document24, "#BDC3C7");
        }
    }
}